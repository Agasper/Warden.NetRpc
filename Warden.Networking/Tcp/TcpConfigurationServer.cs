namespace Warden.Networking.Tcp
{
    public class TcpConfigurationServer : TcpConfigurationPeer
    {
        public int MaximumConnections { get => maximumConnections; set { CheckLocked(); maximumConnections = value; } }
        public int ListenBacklog { get => listenBackLog; set { CheckLocked(); listenBackLog = value; } }
        public int AcceptThreads { get => acceptThreads; set { CheckLocked(); acceptThreads = value; } }

        protected int acceptThreads;
        protected int listenBackLog;
        protected int maximumConnections;

        public TcpConfigurationServer()
        {
            listenBackLog = 100;
            acceptThreads = 1;
            maximumConnections = int.MaxValue;
        }
    }
}
