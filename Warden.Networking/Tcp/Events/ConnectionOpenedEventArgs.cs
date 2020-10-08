using System;
namespace Warden.Networking.Tcp.Events
{
    public class ConnectionOpenedEventArgs
    {
        public TcpConnection Connection { get; private set; }

        internal ConnectionOpenedEventArgs(TcpConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            this.Connection = connection;
        }

    }
}
