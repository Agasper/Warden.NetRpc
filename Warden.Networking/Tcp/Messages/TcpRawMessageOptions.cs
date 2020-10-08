using System;
namespace Warden.Networking.Tcp.Messages
{
    struct TcpRawMessageOptions
    {
        public MessageHeaderFlags flags;

        public override string ToString()
        {
            return $"RawMessageOptions[F={flags}]";
        }

        public static TcpRawMessageOptions None
        {
            get
            {
                return new TcpRawMessageOptions() { flags = MessageHeaderFlags.None };
            }
        }
    }
}
