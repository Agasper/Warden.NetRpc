using Warden.Logging;
using Warden.Networking.Udp.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Channels
{
    class UnreliableChannel : ChannelBase
    {
        public UnreliableChannel(MemoryStreamPool memoryStreamPool, ILogManager logManager,
            ChannelDescriptor descriptor, IChannelConnection connection)
            : base(memoryStreamPool, logManager, descriptor, connection)
        {

        }

        public override UdpSendStatus SendDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);
            datagram.Sequence = GetNextSequenceOut();
            _ = connection.SendDatagramAsync(datagram);
            return UdpSendStatus.Enqueued;
        }

        public override void OnDatagram(Datagram datagram)
        {
            CheckDatagramValid(datagram);
            connection.ReleaseDatagram(datagram);
        }
    }
}
