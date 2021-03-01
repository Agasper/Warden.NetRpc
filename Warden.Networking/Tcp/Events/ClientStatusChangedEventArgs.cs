using static Warden.Networking.Tcp.TcpClient;

namespace Warden.Networking.Tcp.Events
{
    public class ClientStatusChangedEventArgs
    {
        public TcpConnectionStatus Status { get; private set; }

        internal ClientStatusChangedEventArgs(TcpConnectionStatus newStatus)
        {
            this.Status = newStatus;
        }
    }
}
