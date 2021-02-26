using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Networking.Udp.Messages;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public interface IRpcConnection
    {
        object Tag { get; }
        EndPoint RemoteEndpoint { get; }
        bool SendReliable(ICustomMessage message);
        void Close();
    }
    
    public interface IRpcConnectionAdvancedDelivery : IRpcConnection
    {
        bool SendCustom(ICustomMessage message, DeliveryType deliveryType, int channel);
    }
}
