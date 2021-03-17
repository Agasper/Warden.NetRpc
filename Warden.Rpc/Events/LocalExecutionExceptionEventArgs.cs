﻿using Warden.Rpc.Payload;

namespace Warden.Rpc.Events
{
    public struct LocalExecutionExceptionEventArgs
    {
        public RemotingException Exception { get; private set; }
        public ExecutionRequest Request { get; private set; }
        
        public LocalExecutionExceptionEventArgs(RemotingException exception, ExecutionRequest request) : this()
        {
            this.Exception = exception;
            this.Request = request;
        }

    }
}
