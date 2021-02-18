using System;
using Warden.Networking.Udp;

namespace Warden.Rpc.Net.Udp
{
    public class RpcUdpConnection : UdpConnection
    {
        public RpcUdpConnection(UdpPeer peer) : base(peer)
        {
        }
    }
}
