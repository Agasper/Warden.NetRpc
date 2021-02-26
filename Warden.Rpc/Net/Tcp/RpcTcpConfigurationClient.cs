using System;
using System.Threading;
using System.Threading.Tasks;
using Warden.Networking.Tcp;
using Warden.Rpc;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfigurationClient : RpcTcpConfiguration
    {
        public TcpConfigurationClient TcpConfiguration { get => tcpConfiguration; set { CheckLocked(); CheckNull(value); tcpConfiguration = value; } }
        
        TcpConfigurationClient tcpConfiguration;

        public override void CaptureSynchronizationContext()
        { 
            base.CaptureSynchronizationContext();
            tcpConfiguration.CaptureSynchronizationContext();
        }

        public RpcTcpConfigurationClient() : base()
        {
            tcpConfiguration = new TcpConfigurationClient();
        }
    }
}
