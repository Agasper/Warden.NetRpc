using Warden.Rpc.Payload;

namespace Warden.Rpc.Events
{
    public struct RemoteExecutionExceptionEventArgs
    {
        public ExecutionRequest Request { get; private set; }
        public RemotingException Exception { get; private set; }

        internal RemoteExecutionExceptionEventArgs(ExecutionRequest remotingRequest, RemotingException exception)
        {
            this.Request = remotingRequest;
            this.Exception = exception;
        }
    }
}