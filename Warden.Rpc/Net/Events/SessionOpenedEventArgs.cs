using System;
namespace Warden.Rpc.Net.Events
{
    public class SessionOpenedEventArgs
    {
        public RpcSession Session { get; private set; }
        public IRpcConnection Connection { get; private set; }

        internal SessionOpenedEventArgs(RpcSession session, IRpcConnection connection)
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
