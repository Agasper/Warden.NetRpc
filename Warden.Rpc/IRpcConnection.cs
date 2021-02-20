using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Networking.Udp.Messages;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public interface IRpcConnectionTcp
    {
        object Tag { get; }
        EndPoint RemoteEndpoint { get; }
        bool SendReliable(ICustomMessage message);
        void Close();
    }
    
    public interface IRpcConnectionUdp : IRpcConnectionTcp
    {
        bool SendCustom(ICustomMessage message, DeliveryType deliveryType, int channel);
    }
}
