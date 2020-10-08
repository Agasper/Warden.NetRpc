using System;
namespace Warden.Rpc.Payload
{
    enum MessageType : byte
    {
        RpcRequest = 1,
        RpcResponse = 2,
        RpcResponseError = 3
    }
}
