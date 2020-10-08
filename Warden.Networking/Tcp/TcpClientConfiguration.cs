using System;
namespace Warden.Networking.Tcp
{
    public class TcpClientConfiguration : TcpPeerConfiguration
    {
        public int ConnectTimeout { get => connectTimeout; set { CheckLocked(); connectTimeout = value; } }
        public bool AutoReconnect { get => autoReconnect; set { CheckLocked(); autoReconnect = value; } }
        public int AutoReconnectDelay { get => autoReconnectDelay; set { CheckLocked(); autoReconnectDelay = value; } }

        int connectTimeout;
        bool autoReconnect;
        int autoReconnectDelay;

        public TcpClientConfiguration()
        {
            connectTimeout = 10000;
            autoReconnect = false;
            autoReconnectDelay = 5000;
        }
    }
}
