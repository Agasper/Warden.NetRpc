using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConfigurationClient : RpcConfiguration
    {
        public UdpConfigurationClient UdpConfiguration => udpConfiguration;

        UdpConfigurationClient udpConfiguration;

        public override void CaptureSynchronizationContext()
        { 
            base.CaptureSynchronizationContext();
            udpConfiguration.CaptureSynchronizationContext();
        }

        public RpcUdpConfigurationClient() : base()
        {
            udpConfiguration = new UdpConfigurationClient();
        }
    }
}
