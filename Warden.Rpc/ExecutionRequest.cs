using System;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public struct ExecutionRequest
    {
        public object MethodKey { get; private set; }
        public bool HasArgument { get; private set; }
        public object Argument { get; private set; }


        internal static ExecutionRequest FromRemotingMessage(RemotingRequest request)
        {
            ExecutionRequest result = new ExecutionRequest();
            result.Argument = request.Argument;
            result.HasArgument = request.HasArgument;
            result.MethodKey = request.MethodKey;

            return result;
        }
    }
}
