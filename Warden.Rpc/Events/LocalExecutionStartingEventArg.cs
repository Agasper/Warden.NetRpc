namespace Warden.Rpc.Events
{
    public struct LocalExecutionStartingEventArgs
    {
        public ExecutionRequest Request { get; private set; }

        internal LocalExecutionStartingEventArgs(ExecutionRequest remotingRequest)
        {
            this.Request = remotingRequest;
        }
    }
}