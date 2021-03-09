using System;
using Warden.Rpc.Net.Tcp;

namespace Warden.Rpc.Net.Events
{
    public class RpcClientStatusChangedEventArgs
    {
        public RpcTcpClient Client { get; private set; }
        public  RpcClientStatus OldStatus { get; private set; }
        public  RpcClientStatus NewStatus { get; private set; }

        internal RpcClientStatusChangedEventArgs(RpcTcpClient client, RpcClientStatus oldStatus, RpcClientStatus newStatus)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            this.Client = client;
            this.OldStatus = oldStatus;
            this.NewStatus = newStatus;
        }
    }
}
