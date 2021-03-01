namespace Warden.Rpc.Payload
{
    public class RemotingResponseError : ICustomMessage
    {
        public uint RequestId { get; set; }
        public object MethodKey { get; set; }
        public ulong ExecutionTime { get; set; }
        public RemotingException Exception { get; set; }

        public RemotingResponseError()
        {
        }

        public override string ToString()
        {
            return $"{nameof(RemotingResponseError)}[id={RequestId},method={MethodKey},exc={Exception.Message}]";
        }

        public RemotingResponseError(uint requestId, object methodKey, RemotingException remotingException)
        {
            this.RequestId = requestId;
            this.MethodKey = methodKey;
            this.Exception = remotingException;
        }

        public void MergeFrom(ReadFormatterInfo readFormatterInfo)
        {
            byte serviceByte = readFormatterInfo.Reader.ReadByte();
            if (serviceByte == 1)
                MethodKey = readFormatterInfo.Reader.ReadVarInt32();
            else
                MethodKey = readFormatterInfo.Reader.ReadString();
            this.RequestId = readFormatterInfo.Reader.ReadVarUInt32();
            this.ExecutionTime = readFormatterInfo.Reader.ReadVarUInt64();
            this.Exception = new RemotingException(readFormatterInfo.Reader);
        }

        public void WriteTo(WriteFormatterInfo writeFormatterInfo)
        {
            writeFormatterInfo.Writer.Write((byte)MessageType.RpcResponseError);
            byte serviceByte = 0;
            if (MethodKey is int)
                serviceByte = 1;
            writeFormatterInfo.Writer.Write(serviceByte);
            if (MethodKey is int)
                writeFormatterInfo.Writer.WriteVarInt((int)MethodKey);
            else
                writeFormatterInfo.Writer.Write(MethodKey.ToString());
            writeFormatterInfo.Writer.WriteVarInt(RequestId);
            writeFormatterInfo.Writer.WriteVarInt(ExecutionTime);
            Exception.WriteTo(writeFormatterInfo.Writer);
        }
    }
}
