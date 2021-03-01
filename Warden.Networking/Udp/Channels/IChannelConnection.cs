using System.Net.Sockets;
using System.Threading.Tasks;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp.Channels
{
    public interface IChannelConnection
    {
        int GetInitialResendDelay();
        void ReleaseDatagram(Datagram datagram);
        Task<SocketError> SendDatagramAsync(Datagram datagram);
        void UpdateTimeoutDeadline();
        void UpdateLatency(int latency);
    }
}
