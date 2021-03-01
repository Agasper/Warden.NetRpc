using System.Net;
using Warden.Networking.Udp.Messages;

namespace Warden.Networking.Udp.Events
{
    public class OnAcceptConnectionEventArgs
    {
        public EndPoint Endpoint { get; private set; }
        public UdpRawMessage Payload { get; private set; }

        public OnAcceptConnectionEventArgs(EndPoint endpoint, UdpRawMessage payload)
        {
            this.Endpoint = endpoint;
            this.Payload = payload;
        }
    }
}
