using System;

namespace Warden.Rpc.Events
{
    public struct OnCloseEventArgs
    {
        public Exception TransportException { get; private set; }

        public OnCloseEventArgs(Exception transportException)
        {
            this.TransportException = transportException;
        }
    }
}