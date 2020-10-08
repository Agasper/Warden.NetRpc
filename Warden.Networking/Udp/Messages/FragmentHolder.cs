using System;
using System.Collections.Generic;
using System.Linq;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Messages
{
    public class FragmentHolder : IDisposable
    {
        public DateTime Created { get; private set; }
        public bool IsCompleted => Frames == receivedFrames;
        public IReadOnlyCollection<Datagram> Datagrams => datagrams;
        public ushort Frames { get; private set; }
        public ushort FragmentationGroupId { get; private set; }

        readonly Datagram[] datagrams;

        int receivedFrames;
        bool disposed;

        public FragmentHolder(ushort groupId, ushort frames)
        {
            FragmentationGroupId = groupId;
            Frames = frames;
            Created = DateTime.UtcNow;
            datagrams = new Datagram[frames];
        }

        public void Dispose()
        {
            for(int i = 0; i < datagrams.Length; i++)
            {
                var d = datagrams[i];
                if (d != null)
                    d.Dispose();
            }
            disposed = true;
        }

        public UdpRawMessage Merge(MemoryStreamPool memoryStreamPool)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(FragmentHolder));

            if (!IsCompleted)
                throw new InvalidOperationException($"{nameof(FragmentHolder)} isn't completed");

            if (datagrams.Length == 0)
                throw new ArgumentException("Datagrams collection is empty");

            var firstDatagram = datagrams.First();
            if (firstDatagram.Length == 0)
                throw new ArgumentException("Head datagram is empty");

            MessageType messageType = (MessageType)firstDatagram.BaseStream.ReadByte();

            int payloadSize = (int)datagrams.Sum(d => d.Length) - 1;
            UdpRawMessage message = new UdpRawMessage(memoryStreamPool, payloadSize, false);

            foreach (var datagram in datagrams)
            {
                datagram.Position = 0;
                datagram.CopyTo(message.BaseStream);
                message.DeliveryType = datagram.DeliveryType;
                message.Channel = datagram.Channel;
            }

            return message;
        }

        public bool SetFrame(Datagram datagram)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(FragmentHolder));
            if (datagram == null)
                throw new ArgumentNullException(nameof(datagram));
            if (!datagram.IsFragmented)
                throw new ArgumentException($"{nameof(FragmentHolder)} accepts only fragmented datagrams");
            if (datagram.FragmentationInfo.FragmentationGroupId != this.FragmentationGroupId)
                throw new ArgumentException($"{nameof(Datagram)} wrong fragmentation group id");

            if ((datagram.FragmentationInfo.Frame-1) > datagrams.Length || datagram.FragmentationInfo.Frame < 1)
                throw new ArgumentOutOfRangeException($"Frame out of range values 1-{datagrams.Length}");

            if (datagrams[datagram.FragmentationInfo.Frame - 1] != null)
                return false;

            datagrams[datagram.FragmentationInfo.Frame - 1] = datagram;
            receivedFrames++;
            return true;
        }
    }
}
