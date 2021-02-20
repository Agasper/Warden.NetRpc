using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct RemoteExecutionStartingEventArgs
    {
        public RemotingRequest Request { get; private set; }
        public ExecutionOptions Options { get; private set; }

        internal RemoteExecutionStartingEventArgs(RemotingRequest remotingRequest, ExecutionOptions options)
        {
            this.Request = remotingRequest;
            this.Options = options;
        }
    }
}