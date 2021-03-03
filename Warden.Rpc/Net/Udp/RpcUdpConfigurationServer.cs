using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConfigurationServer : RpcConfiguration
    {
        public UdpConfigurationServer UdpConfiguration => udpConfiguration;

        UdpConfigurationServer udpConfiguration;

        public override void CaptureSynchronizationContext()
        { 
            base.CaptureSynchronizationContext();
            udpConfiguration.CaptureSynchronizationContext();
        }

        public RpcUdpConfigurationServer() : base()
        {
            udpConfiguration = new UdpConfigurationServer();
        }
    }
}
