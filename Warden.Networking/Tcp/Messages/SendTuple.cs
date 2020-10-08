using System;
namespace Warden.Networking.Tcp.Messages
{
    struct SendTuple
    {
        public TcpRawMessage Message { get; private set; }
        public TcpRawMessageOptions Options { get; private set; }
         
        public SendTuple(TcpRawMessage message, TcpRawMessageOptions options)
        {
            this.Message = message;
            this.Options = options;
        }
    }
}
