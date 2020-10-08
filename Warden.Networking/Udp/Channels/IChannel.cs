using System;
using System.Collections.Generic;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp.Channels
{
    interface IChannel : IDisposable
    {
        ChannelDescriptor Descriptor { get; }

        void OnAckReceived(Datagram datagram);
        void OnDatagram(Datagram datagram);
        UdpSendStatus SendDatagram(Datagram datagram);

        void PollEvents();
    }
}
