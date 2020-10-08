using System;
namespace Warden.Networking.Udp
{
    public class UdpServerConfiguration : UdpPeerConfiguration
    {
        public int MaximumConnections { get => maximumConnections; set { CheckLocked(); maximumConnections = value; } }

        int maximumConnections;

        public UdpServerConfiguration()
        {
            maximumConnections = int.MaxValue;
            autoMtuExpand = false;
        }
    }
}
