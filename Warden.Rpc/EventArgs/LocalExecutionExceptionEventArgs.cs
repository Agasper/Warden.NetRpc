using System;
using Warden.Rpc.Payload;

namespace Warden.Rpc.EventArgs
{
    public struct LocalExecutionExceptionEventArgs
    {
        public RemotingException Exception { get; private set; }
        public RemotingRequest Request { get; private set; }
        
        internal LocalExecutionExceptionEventArgs(RemotingException exception, RemotingRequest request) : this()
        {
            this.Exception = exception;
            this.Request = request;
        }

    }
}
