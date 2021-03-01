namespace Warden.Rpc.Payload
{
    public class RemotingPayload : ICustomMessage
    {
        public bool HasArgument { get; set; }
        public object Argument { get; set; }

        protected byte serviceByte;

        public RemotingPayload()
        {
        }

        public virtual void MergeFrom(ReadFormatterInfo readFormatterInfo)
        {
            this.serviceByte = readFormatterInfo.Reader.ReadByte();
            HasArgument = (serviceByte & 1) == 1;
            if (HasArgument)
                Argument = readFormatterInfo.Serializer.ParseBinary(readFormatterInfo.Reader);
        }

        public virtual void WriteTo(WriteFormatterInfo writeFormatterInfo)
        {
            if (HasArgument)
                serviceByte |= 1;

            writeFormatterInfo.Writer.Write(serviceByte);
            
            if (HasArgument)
                writeFormatterInfo.Serializer.WriteBinary(writeFormatterInfo.Writer, Argument);
        }
    }
}
