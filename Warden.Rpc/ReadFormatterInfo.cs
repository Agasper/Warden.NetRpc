

using Warden.Networking.IO;
using Warden.Rpc.Serialization;

namespace Warden.Rpc
{
    public struct ReadFormatterInfo
    {
        public RpcSerializer Serializer { get; private set; }
        public IReader Reader { get; private set; }

        public ReadFormatterInfo(IReader reader, RpcSerializer serializer)
        {
            this.Serializer = serializer;
            this.Reader = reader;
        }
    }
}
