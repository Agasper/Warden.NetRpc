using System;
namespace Warden.Rpc.Net.Tcp.Events
{
    public class SessionClosedEventArgs
    {
        public RpcSession Session { get; private set; }
        public RpcTcpConnection Connection { get; private set; }

        internal SessionClosedEventArgs(RpcSession session, RpcTcpConnection connection)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            this.Session = session;
            this.Connection = connection;
        }
    }
}
