using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct LocalExecutionStartingEventArgs
    {
        public RemotingRequest Request { get; private set; }

        internal LocalExecutionStartingEventArgs(RemotingRequest remotingRequest)
        {
            this.Request = remotingRequest;
        }
    }
}