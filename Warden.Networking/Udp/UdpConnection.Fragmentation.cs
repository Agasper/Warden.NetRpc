using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Warden.Networking.Messages;
using Warden.Networking.Udp.Channels;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp
{
    public partial class UdpConnection
    {
        ConcurrentDictionary<ushort, FragmentHolder> fragments;
        ushort fragmentationGroupOut;
        object fragmentationGroupMutex = new object();


        ushort GetNextFragementationGroupId()
        {
            ushort result = 0;
            lock (fragmentationGroupMutex)
                result = fragmentationGroupOut++;
            return result;
        }


        void ManageFragment(Datagram datagram)
        {
            if (!datagram.IsFragmented)
            {
                datagram.Dispose();
                throw new InvalidOperationException("Datagram isn't fragemented");
            }

            if (datagram.FragmentationInfo.Frame > datagram.FragmentationInfo.Frames)
            {
                logger.Error($"Invalid fragmented datagram frame {datagram.FragmentationInfo.Frame} > frames {datagram.FragmentationInfo.Frames}. Dropping...");
                datagram.Dispose();
                return;
            }

            FragmentHolder fragmentHolder = this.fragments.GetOrAdd(datagram.FragmentationInfo.FragmentationGroupId, (groupId) =>
            {
                return new FragmentHolder(datagram.FragmentationInfo.FragmentationGroupId, datagram.FragmentationInfo.Frames);
            });

            fragmentHolder.SetFrame(datagram);

            if (fragmentHolder.IsCompleted)
            {
                if (this.fragments.TryRemove(datagram.FragmentationInfo.FragmentationGroupId, out FragmentHolder removed))
                {
                    var message = fragmentHolder.Merge(peer.Configuration.MemoryStreamPool);
                    ReleaseMessage(message, datagram.DeliveryType, datagram.Channel);
                    fragmentHolder.Dispose();
                }
            }
        }


        bool CheckCanBeSendUnfragmented(UdpRawMessage datagram)
        {
            if (datagram.Length + Datagram.GetHeaderSize(false) > Mtu)
                return false;
            return true;
        }

        UdpSendStatus SendFragmentedMessage(UdpRawMessage message, IChannel channel)
        {
            message.Position = 0;
            int mtu = this.Mtu;
            ushort frame = 1;
            ushort frames = (ushort)Math.Ceiling(message.Length / (float)(mtu - Datagram.GetHeaderSize(true)));
            ushort groupId = GetNextFragementationGroupId();
            do
            {
                Debug.Assert(frame <= frames, "frame > frames");

                Datagram datagramFrag = Datagram.CreateNew(peer.Configuration.MemoryStreamPool, mtu);
                datagramFrag.Type = MessageType.UserData;
                datagramFrag.Channel = channel.Descriptor.Channel;
                datagramFrag.DeliveryType = channel.Descriptor.DeliveryType;
                datagramFrag.FragmentationInfo = new Datagram.FragmentInfo(groupId, frame, frames);
                datagramFrag.ConnectionKey = this.EndPoint.ConnectionKey;

                int toCopy = mtu - Datagram.GetHeaderSize(true);
                if (toCopy > message.Length - message.Position)
                {
                    toCopy = message.Length - message.Position;
                }
                message.CopyTo(datagramFrag.BaseStream, toCopy);

                channel.SendDatagram(datagramFrag);
                frame++;

            } while (message.Position < message.Length);

            message.Dispose();

            return UdpSendStatus.Enqueued;
        }


    }
}
