using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct LocalExecutionCompletedEventArgs
    {
        public RemotingRequest Request { get; private set; }
        public RemotingResponse Response { get; private set; }
        public float ElapsedMilliseconds { get; private set; }
        
        internal LocalExecutionCompletedEventArgs(RemotingRequest request, RemotingResponse response, float elapsedMilliseconds)
        {
            this.Request = request;
            this.Response = response;
            this.ElapsedMilliseconds = elapsedMilliseconds;
        }
    }
}