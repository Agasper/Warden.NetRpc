using System;
namespace Warden.Networking.Tcp.Events
{
    public class ConnectionClosedEventArgs
    {
        public TcpConnection Connection { get; private set; }

        internal ConnectionClosedEventArgs(TcpConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            this.Connection = connection;
        }
    }
}
