using System;
using Warden.Networking.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Messages
{
    public class UdpRawMessage : RawMessage
    {
        public DeliveryType DeliveryType { get; set; } = DeliveryType.ReliableOrdered;
        public int Channel { get; set; } = 0;

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, int payloadSize, bool contiguous)
            : base(memoryStreamPool, payloadSize, contiguous)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, int payloadSize)
            : base(memoryStreamPool, payloadSize)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool)
            : base(memoryStreamPool)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, ArraySegment<byte> segment, bool copy)
            : base(memoryStreamPool, segment, copy)
        {
        }

        internal static UdpRawMessage CreateEmpty(MemoryStreamPool memoryStreamPool)
        {
            return new UdpRawMessage(memoryStreamPool, new ArraySegment<byte>(RawMessage.emptyArray, 0, 0), false);
        }

        public Datagram ConvertToDatagram()
        {
            Datagram datagram;
            if (this.Length == 0)
            {
                datagram = Datagram.CreateEmpty(memoryStreamPool);
                return datagram;
            }

            datagram = Datagram.CreateNew(memoryStreamPool, this.Length);
            this.Position = 0;
            this.CopyTo(datagram.BaseStream);
            return datagram;
        }

    }
}
