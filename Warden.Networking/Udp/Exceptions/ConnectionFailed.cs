using System;
namespace Warden.Networking.Udp.Exceptions
{
    class ConnectionFailed : Exception
    {
        public DisconnectReason Reason { get; private set; }

        public ConnectionFailed(DisconnectReason reason, string message) : base(message)
        {
            this.Reason = reason;
        }

        public ConnectionFailed(DisconnectReason reason) : base()
        {
            this.Reason = reason;
        }
    }
}
