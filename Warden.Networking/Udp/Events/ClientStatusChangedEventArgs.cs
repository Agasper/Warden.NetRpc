using System;

namespace Warden.Networking.Udp.Events
{
    public class ConnectionStatusChangedEventArgs
    {
        public UdpConnection Connection { get; private set; }
        public UdpConnectionStatus Status { get; private set; }

        internal ConnectionStatusChangedEventArgs(UdpConnection connection, UdpConnectionStatus status)
        {
            this.Connection = connection;
            this.Status = status;
        }
    }
}
