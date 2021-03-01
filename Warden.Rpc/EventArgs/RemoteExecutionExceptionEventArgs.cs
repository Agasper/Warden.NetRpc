using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct RemoteExecutionExceptionEventArgs
    {
        public ExecutionRequest Request { get; private set; }
        public RemotingException Exception { get; private set; }
        public ExecutionOptions Options { get; private set; }

        internal RemoteExecutionExceptionEventArgs(ExecutionRequest remotingRequest, RemotingException exception, ExecutionOptions options)
        {
            this.Request = remotingRequest;
            this.Exception = exception;
            this.Options = options;
        }
    }
}