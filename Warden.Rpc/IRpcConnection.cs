using System;
using System.Net;
using System.Threading.Tasks;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public interface IRpcConnection
    {
        object Tag { get; }
        EndPoint RemoteEndpoint { get; }
        void SendMessage(ICustomMessage message, SendingOptions sendingOptions);
        void SendMessage(ICustomMessage message);
        Task FlushSendQueueAndCloseAsync();
        void Close();
    }
}
