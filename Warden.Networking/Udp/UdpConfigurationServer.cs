namespace Warden.Networking.Udp
{
    public class UdpConfigurationServer : UdpConfigurationPeer
    {
        public int MaximumConnections { get => maximumConnections; set { CheckLocked(); maximumConnections = value; } }

        int maximumConnections;

        public UdpConfigurationServer()
        {
            maximumConnections = int.MaxValue;
            autoMtuExpand = false;
        }
    }
}
