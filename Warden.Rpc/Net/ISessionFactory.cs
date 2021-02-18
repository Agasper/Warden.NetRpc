using System;
using Warden.Rpc.Net.Tcp;

namespace Warden.Rpc.Net
{
    public interface ISessionFactory
    {
        RpcSession CreateSession(RpcSessionContext context);
    }
}
