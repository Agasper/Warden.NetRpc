using System.Threading.Tasks;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public class RpcSessionUdp : RpcSession
    {
        public RpcSessionUdp(RpcSessionContext context) : base(context)
        {
            
        }

        protected virtual Task<RemotingResponse> ExecuteRequestAsync(RemotingRequest request)
        {
            throw new System.NotImplementedException();
        }
    }
}