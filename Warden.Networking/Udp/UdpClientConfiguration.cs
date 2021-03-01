namespace Warden.Networking.Udp
{
    public class UdpClientConfiguration : UdpPeerConfiguration
    {
        public UdpClientConfiguration()
        {
            autoMtuExpand = true;
        }
    }
}
