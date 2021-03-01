using Warden.Networking.Tcp;

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
