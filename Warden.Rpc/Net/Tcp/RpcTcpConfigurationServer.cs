using System;
using System.Threading;
using System.Threading.Tasks;
using Warden.Networking.Tcp;
using Warden.Rpc;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfigurationServer : RpcTcpConfiguration
    {
        public TcpConfigurationServer TcpConfiguration { get => tcpConfiguration; set { CheckLocked(); CheckNull(value); tcpConfiguration = value; } }
        
        TcpConfigurationServer tcpConfiguration;

        public override void CaptureSynchronizationContext()
        { 
            base.CaptureSynchronizationContext();
            tcpConfiguration.CaptureSynchronizationContext();
        }

        public RpcTcpConfigurationServer() : base()
        {
            tcpConfiguration = new TcpConfigurationServer();
        }
    }
}
