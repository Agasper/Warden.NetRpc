using System;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public class LocalExecutionExceptionArgs
    {
        public RemotingException Exception { get; private set; }
        public ExecutionRequest Request { get; private set; }
        public bool CloseConnection { get; set; }

        internal LocalExecutionExceptionArgs()
        {
            CloseConnection = false;
        }

        internal LocalExecutionExceptionArgs(RemotingException exception, ExecutionRequest request) : this()
        {
            this.Exception = exception;
            this.Request = request;
        }

    }
}
