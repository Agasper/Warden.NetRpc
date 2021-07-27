using Warden.Networking.IO;
using Warden.Rpc.Serialization;

namespace Warden.Rpc
{
    public struct WriteFormatterInfo
    {
        public RpcSerializer Serializer { get; private set; }
        public IWriter Writer { get; private set; }

        public WriteFormatterInfo(IWriter writer, RpcSerializer serializer)
        {
            this.Serializer = serializer;
            this.Writer = writer;
        }
    }
}
