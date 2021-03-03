using Warden.Networking.Tcp;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfigurationServer : RpcConfiguration
    {
        public int MaxStashedConnections { get => maxStashedConnections; set { CheckLocked(); maxStashedConnections = value; } }
        public TcpConfigurationServer TcpConfiguration => tcpConfiguration;

        TcpConfigurationServer tcpConfiguration;
        int maxStashedConnections;

        public override void CaptureSynchronizationContext()
        { 
            base.CaptureSynchronizationContext();
            tcpConfiguration.CaptureSynchronizationContext();
        }

        public RpcTcpConfigurationServer() : base()
        {
            tcpConfiguration = new TcpConfigurationServer();
            maxStashedConnections = 128;
        }
    }
}
