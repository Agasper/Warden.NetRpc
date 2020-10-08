using System;
namespace Warden.Networking.Tcp
{
    public class TcpServerConfiguration : TcpPeerConfiguration
    {
        public int MaximumConnections { get => maximumConnections; set { CheckLocked(); maximumConnections = value; } }
        public int ListenBacklog { get => listenBackLog; set { CheckLocked(); listenBackLog = value; } }
        public int AcceptThreads { get => acceptThreads; set { CheckLocked(); acceptThreads = value; } }

        int acceptThreads;
        int listenBackLog;
        int maximumConnections;

        public TcpServerConfiguration()
        {
            listenBackLog = 100;
            acceptThreads = 1;
            maximumConnections = int.MaxValue;
        }
    }
}
