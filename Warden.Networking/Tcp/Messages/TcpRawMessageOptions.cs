namespace Warden.Networking.Tcp.Messages
{
    struct TcpRawMessageOptions
    {
        public MessageHeaderFlags Flags { get; set; }
        
        public bool HasFlag(MessageHeaderFlags flag)
        {
            return Flags.HasFlag(flag);
        }

        public override string ToString()
        {
            return $"RawMessageOptions[F={Flags}]";
        }

        public TcpRawMessageOptions(MessageHeaderFlags flags)
        {
            this.Flags = flags;
        }

        public static TcpRawMessageOptions None => new TcpRawMessageOptions(MessageHeaderFlags.None);
    }
}
