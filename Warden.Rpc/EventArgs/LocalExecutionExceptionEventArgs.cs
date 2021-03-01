using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct LocalExecutionExceptionEventArgs
    {
        public RemotingException Exception { get; private set; }
        public ExecutionRequest Request { get; private set; }
        
        internal LocalExecutionExceptionEventArgs(RemotingException exception, ExecutionRequest request) : this()
        {
            this.Exception = exception;
            this.Request = request;
        }

    }
}
