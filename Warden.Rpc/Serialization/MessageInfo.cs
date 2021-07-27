using System;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Warden.Rpc.Serialization
{
    public class MessageInfo
    {
        public string MessageType { get; private set; }
        public MessageDescriptor Descriptor { get; private set; }
        public MessageParser Parser { get; private set; }

        internal MessageInfo(MessageDescriptor messageDescriptor, MessageParser messageParser)
        {
            this.Descriptor = messageDescriptor;
            this.Parser = messageParser;
            this.MessageType = messageDescriptor.File.Package + "/" + messageDescriptor.Name;

            ConstructorInfo constructorInfo = messageDescriptor.ClrType.GetConstructor(Type.EmptyTypes);
            if (constructorInfo == null)
                throw new ArgumentException($"Message {messageDescriptor.ClrType.Name} doesn't have public parameterless constructor");
        }
    }
}