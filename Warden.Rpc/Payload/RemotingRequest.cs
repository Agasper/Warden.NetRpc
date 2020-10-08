using System;
using System.Threading.Tasks;
using Warden.Util;

namespace Warden.Rpc.Payload
{
    public class RemotingRequest : RemotingPayload
    {
        public bool ExpectResponse { get; set; }
        public object MethodKey { get; set; }
        public uint RequestId { get; set; }
        public DateTime Created { get; private set; }
        public RemotingResponse Response => response;

        public object Result
        {
            get
            {
                if (response == null)
                    throw new InvalidOperationException("Response is not received yet");
                if (!response.HasArgument)
                    throw new InvalidOperationException("Remote method is void. No result received");
                return response.Argument;
            }
        }

        RemotingResponse response;

        TaskCompletionSource<object> taskCompletionSource;

        public RemotingRequest()
        {
            Created = DateTime.UtcNow;
        }

        public override string ToString()
        {
            string arg = "None";
            if (HasArgument)
                arg = Argument.GetType().Name;
            return $"{nameof(RemotingRequest)}[id={RequestId},method={MethodKey},expectResponse={ExpectResponse},arg={arg}]";
        }

        public override void MergeFrom(ReadFormatterInfo readFormatterInfo)
        {
            base.MergeFrom(readFormatterInfo);
            this.RequestId = readFormatterInfo.Reader.ReadVarUInt32();
            bool keyIsInt = (serviceByte & (1 << 1)) == (1 << 1);
            ExpectResponse = (serviceByte & (1 << 2)) == (1 << 2);
            if (keyIsInt)
                this.MethodKey = readFormatterInfo.Reader.ReadVarInt32();
            else
                this.MethodKey = readFormatterInfo.Reader.ReadString();
        }

        public override void WriteTo(WriteFormatterInfo writeFormatterInfo)
        {
            writeFormatterInfo.Writer.Write((byte)MessageType.RpcRequest);
            if (MethodKey is int)
                serviceByte |= 1 << 1;
            if (ExpectResponse)
                serviceByte |= 1 << 2;
            base.WriteTo(writeFormatterInfo);
            writeFormatterInfo.Writer.WriteVarInt(RequestId);
            if (MethodKey is int)
                writeFormatterInfo.Writer.WriteVarInt((int)MethodKey);
            else
                writeFormatterInfo.Writer.Write(MethodKey.ToString());
        }

        internal void CreateAwaiter()
        {
            taskCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal void SetCancelled()
        {
            if (taskCompletionSource == null)
                return;
            taskCompletionSource.TrySetCanceled();
        }

        internal void SetError(Exception exception)
        {
            if (taskCompletionSource == null)
                return;
            taskCompletionSource.TrySetException(exception);
        }

        internal void SetResult(RemotingResponse response)
        {
            if (taskCompletionSource == null)
                return;
            this.response = response;
            taskCompletionSource.TrySetResult(null);
        }

        internal async Task WaitAsync(int timeout)
        {
            if (taskCompletionSource == null)
                throw new InvalidOperationException("Awaiter isn't created for this request");

            await taskCompletionSource.Task.TimeoutAfter(timeout).ConfigureAwait(false);
        }
    }
}
