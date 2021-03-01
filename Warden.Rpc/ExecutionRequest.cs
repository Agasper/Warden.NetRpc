using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public class ExecutionRequest
    {
        public bool HasArgument { get; private set; }
        public object Argument { get; private set; }
        public object MethodKey { get; private set; }

        internal ExecutionRequest(bool hasArgument, object argument, object methodKey)
        {
            this.HasArgument = hasArgument;
            this.Argument = argument;
            this.MethodKey = methodKey;
        }

        internal ExecutionRequest(RemotingRequest request) : this(request.HasArgument, request.Argument,
            request.MethodKey)
        {
            
        }
    }
}