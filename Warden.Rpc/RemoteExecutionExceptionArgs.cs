using System;
using Warden.Rpc.Payload;

namespace Warden.Rpc
{
    public class RemoteExecutionExceptionArgs
    {
        public RemotingException Exception { get; private set; }
        public object MethodIdentity { get; private set; }
        public bool CloseConnection { get; set; }

        internal RemoteExecutionExceptionArgs()
        {
            CloseConnection = false;
        }

        internal RemoteExecutionExceptionArgs(RemotingException exception, object methodIdentity) : this()
        {
            this.Exception = exception;
            this.MethodIdentity = methodIdentity;
        }

    }
}
