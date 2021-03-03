using Warden.Networking.Tcp;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfigurationClient : RpcConfiguration
    {
        public TcpConfigurationClient TcpConfiguration => tcpConfiguration;

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
