using System;
using Warden.Rpc.Net;

namespace Warden.Rpc
{
    public delegate RpcSession DCreateSession(RpcSessionContext context);
    
    public class DefaultSessionFactory: ISessionFactory
    {
        DCreateSession generator;
        
        public DefaultSessionFactory(DCreateSession generator)
        {
            if (generator == null)
                throw new ArgumentNullException(nameof(generator));
            this.generator = generator;
        }
        
        public RpcSession CreateSession(RpcSessionContext context)
        {
            return generator(context);
        }
    }
}