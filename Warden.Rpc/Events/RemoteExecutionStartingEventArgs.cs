namespace Warden.Rpc.Events
{
    public struct RemoteExecutionStartingEventArgs
    {
        public ExecutionRequest Request { get; private set; }
        public ExecutionOptions Options { get; private set; }

        internal RemoteExecutionStartingEventArgs(ExecutionRequest remotingRequest, ExecutionOptions options)
        {
            this.Request = remotingRequest;
            this.Options = options;
        }
    }
}