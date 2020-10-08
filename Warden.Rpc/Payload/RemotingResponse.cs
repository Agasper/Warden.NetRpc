using System;
using Warden.Networking.IO;

namespace Warden.Rpc.Payload
{
    public class RemotingResponse : RemotingPayload
    {
        public uint RequestId { get; set; }
        public ulong ExecutionTime { get; set; }

        public RemotingResponse()
        {
            
        }

        public override string ToString()
        {
            string arg = "None";
            if (HasArgument)
                arg = Argument.GetType().Name;
            return $"{nameof(RemotingResponse)}[id={RequestId},arg={arg}]";
        }

        public override void MergeFrom(ReadFormatterInfo readFormatterInfo)
        {
            this.RequestId = readFormatterInfo.Reader.ReadVarUInt32();
            this.ExecutionTime = readFormatterInfo.Reader.ReadVarUInt64();
            base.MergeFrom(readFormatterInfo);
        }

        public override void WriteTo(WriteFormatterInfo writeFormatterInfo)
        {
            writeFormatterInfo.Writer.Write((byte)MessageType.RpcResponse);
            writeFormatterInfo.Writer.WriteVarInt(RequestId);
            writeFormatterInfo.Writer.WriteVarInt(ExecutionTime);
            base.WriteTo(writeFormatterInfo);
        }
    }
}
