using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct RemoteExecutionCompletedEventArgs
    {
        public RemotingRequest Request { get; private set; }
        public RemotingResponse Response { get; private set; }
        public ExecutionOptions Options { get; private set; }
        public float ElapsedMilliseconds { get; private set; }
        
        internal RemoteExecutionCompletedEventArgs(RemotingRequest remotingRequest, RemotingResponse response, ExecutionOptions options, float elapsedMilliseconds)
        {
            this.Request = remotingRequest;
            this.Response = response;
            this.Options = options;
            this.ElapsedMilliseconds = elapsedMilliseconds;
        }
    }
}