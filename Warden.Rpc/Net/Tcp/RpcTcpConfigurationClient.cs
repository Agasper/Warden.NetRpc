using Warden.Networking.Tcp;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpConfigurationClient : RpcConfiguration
    {
        public TcpConfigurationClient TcpConfiguration => tcpConfiguration;
        
        public bool AutoReconnect { get => autoReconnect; set { CheckLocked(); autoReconnect = value; } }
        public int AutoReconnectDelay { get => autoReconnectDelay; set { CheckLocked(); autoReconnectDelay = value; } }
        
        protected bool autoReconnect;
        protected int autoReconnectDelay;
        
        TcpConfigurationClient tcpConfiguration;

        public RpcTcpConfigurationClient() : base()
        {
            autoReconnect = false;
            autoReconnectDelay = 5000;
            tcpConfiguration = new TcpConfigurationClient();
        }
    }
}
