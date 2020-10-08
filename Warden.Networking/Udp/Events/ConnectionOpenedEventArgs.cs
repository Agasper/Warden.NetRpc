using System;
using Warden.Networking.Udp;

namespace Warden.Networking.Udp.Events
{
    public class ConnectionOpenedEventArgs
    {
        public UdpConnection Connection { get; private set; }

        internal ConnectionOpenedEventArgs(UdpConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            this.Connection = connection;
        }

    }
}
