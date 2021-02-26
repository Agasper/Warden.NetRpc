using System;
using Warden.Networking.Tcp.Messages;

namespace Warden.Networking.Tcp.Events
{
    public class MessageEventArgs
    {
        public TcpConnection Connection { get; private set; }
        public TcpRawMessage Message { get; private set; }

        public MessageEventArgs(TcpConnection connection, TcpRawMessage message)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            this.Connection = connection;
            this.Message = message;
        }
    }
}
