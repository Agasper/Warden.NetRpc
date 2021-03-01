using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp.Events
{
    public class ConnectionClosedEventArgs
    {
        public UdpConnection Connection { get; private set; }
        public DisconnectReason Reason { get; private set; }
        public UdpRawMessage Payload { get; private set; }

        public ConnectionClosedEventArgs(UdpConnection connection, DisconnectReason reason, UdpRawMessage payload)
        {
            this.Connection = connection;
            this.Reason = reason;
            this.Payload = payload;
        }
    }
}
