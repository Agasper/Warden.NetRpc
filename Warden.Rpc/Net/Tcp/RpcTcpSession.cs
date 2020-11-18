using System;
using System.Threading.Tasks;

namespace Warden.Rpc.Net.Tcp
{
    public class RpcTcpSession : RpcSession
    {
        public object Object => obj;

        readonly object obj;

        RemotingObjectScheme remotingObjectScheme;

        public RpcTcpSession(object obj, RpcConfiguration configuration) : base(configuration)
        {
            this.obj = obj;

            RemotingObjectConfiguration remotingObjectConfiguration
                = new RemotingObjectConfiguration(true, true, true);
            remotingObjectScheme = new RemotingObjectScheme(remotingObjectConfiguration,
                obj.GetType());
        }

        protected override async Task<ExecutionResult> ExecuteRequestAsync(ExecutionRequest request)
        {
            var container = remotingObjectScheme.GetInvokationContainer(request.MethodKey);

            object result = null;
            if (request.HasArgument)
                result = await container.InvokeAsync(obj, request.Argument).ConfigureAwait(false);
            else
                result = await container.InvokeAsync(obj).ConfigureAwait(false);

            ExecutionResult executionResult = new ExecutionResult();
            executionResult.HasResult = container.DoesReturnValue;
            executionResult.Result = result;

            return executionResult;
        }
    }
}
