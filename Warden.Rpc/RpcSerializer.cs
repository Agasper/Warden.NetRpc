using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Warden.Networking.IO;

namespace Warden.Rpc
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

    public class RpcSerializer
    {
        const int PROTOBUF_INTERNAL_BUFFER_SIZE = 4096;
        
        public Encoding Encoding { get; set; }

        protected Dictionary<string, MessageInfo> messageMap;
        protected Dictionary<Type, MessageInfo> messageMapReverse;

        protected HashSet<Assembly> assemblies;
        protected ArrayPool<byte> arrayPool;

        public RpcSerializer(params Assembly[] assemblies)
            : this()
        {
            AddAssemblyTypesToRegistry(assemblies);
        }

        public RpcSerializer(ArrayPool<byte> arrayPool)
        {
            this.arrayPool = arrayPool;
            this.Encoding = new UTF8Encoding(false);
            this.assemblies = new HashSet<Assembly>();
            this.messageMap = new Dictionary<string, MessageInfo>();
            this.messageMapReverse = new Dictionary<Type, MessageInfo>();
        }

        public RpcSerializer() : this(ArrayPool<byte>.Shared)
        {
            
        }

        public void AddAssemblyTypesToRegistry(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));
            
            if (this.assemblies.Add(assembly))
                InitializeRegistry();
        }

        public void AddAssemblyTypesToRegistry(params Assembly[] assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));
            
            foreach (var assembly in assemblies)
                if (assembly == null)
                    throw new ArgumentNullException(nameof(assemblies), "One of assemblies is null");
            
            bool atLeastOneAdded = false;
            foreach (var assembly in assemblies)
                if (this.assemblies.Add(assembly))
                    atLeastOneAdded = true;

            if (atLeastOneAdded)
                InitializeRegistry();
        }

        protected virtual void InitializeRegistry()
        {
            var newMessageMap = new Dictionary<string, MessageInfo>();
            var newMessageMapReverse = new Dictionary<Type, MessageInfo>();

            foreach (var assembly in assemblies)
            {
                foreach (var messageType in assembly.GetTypes())
                {
                    if (!messageType.GetInterfaces().Contains(typeof(IMessage)))
                        continue;
                    
                    MessageDescriptor descriptor = messageType.GetProperty("Descriptor",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as MessageDescriptor;

                    if (descriptor == null)
                        throw new NullReferenceException(
                            $"{nameof(MessageDescriptor)} not found for message {messageType.FullName}");

                    MessageParser parser = messageType.GetProperty("Parser",
                        BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as MessageParser;
                    
                    if (parser == null)
                        throw new NullReferenceException(
                            $"{nameof(MessageParser)} not found for message {messageType.FullName}");

                    MessageInfo wardenMessageInfo = new MessageInfo(descriptor, parser);

                    if (newMessageMap.ContainsKey(wardenMessageInfo.MessageType))
                        throw new ArgumentException($"Message {wardenMessageInfo.MessageType} defined at least twice: {messageType.Name}, {newMessageMap[wardenMessageInfo.MessageType].Descriptor.ClrType.Name}.");
                    newMessageMap.Add(wardenMessageInfo.MessageType, wardenMessageInfo);
                    newMessageMapReverse.Add(messageType, wardenMessageInfo);
                }
            }

            this.messageMap = newMessageMap;
            this.messageMapReverse = newMessageMapReverse;
        }

        public object ParseBinary(IReader reader)
        {
            int length = reader.ReadVarInt32();
            if (length < 0)
                return null;
            string messageType = reader.ReadString();
            if (messageMap.ContainsKey(messageType))
            {
                var messageInfo = messageMap[messageType];
                using (LimitedReadStream limitedReadStream = new LimitedReadStream(reader.BaseStream, length, true))
                {
                    byte[] rentedArray = arrayPool.Rent(PROTOBUF_INTERNAL_BUFFER_SIZE);
                    try
                    {
                        using(CodedInputStream cis = new CodedInputStream(limitedReadStream, rentedArray, true))
                            return messageInfo.Parser.ParseFrom(cis);
                    }
                    finally
                    {
                        arrayPool.Return(rentedArray);
                    }
                }
            }
            else if (messageType.StartsWith("\0"))
            {
                return ReadPrimitive(reader, messageType, length);
            }

            throw new InvalidOperationException($"Message type {messageType} not found");
        }

        public void WriteBinary(IWriter writer, object value)
        {
            if (value == null)
            {
                writer.WriteVarInt(-1);
                return;
            }

            if (value is IMessage iMessage)
            {
                var type = value.GetType();
                if (!messageMapReverse.ContainsKey(type))
                {
                    throw new ArgumentException($"IMessage with clr type {value.GetType().Name} not found in registry. Add assembly {type.Assembly.FullName} to the serializer.");
                }
                var messageInfo = messageMapReverse[type];

                int len = iMessage.CalculateSize();
                writer.WriteVarInt(len);
                writer.Write(messageInfo.MessageType);

                if (len > 0)
                {
                    byte[] rentedArray = arrayPool.Rent(PROTOBUF_INTERNAL_BUFFER_SIZE);
                    try
                    {
                        using (CodedOutputStream cos = new CodedOutputStream(writer.BaseStream, rentedArray, true))
                        {
                            iMessage.WriteTo(cos);
                        }
                    }
                    finally
                    {
                        arrayPool.Return(rentedArray);
                    }
                }
            }
            else if (value is ICustomMessage customMessageValue)
            {
                throw new NotImplementedException();
                
                
                //int origPosition = (int)writer.BaseStream.Position;


                //customMessageValue.WriteTo(new WriteFormatterInfo(writer, this));
            }
            else if (IsPrimitive(value.GetType()))
            {
                WritePrimitive(writer, value);
            }
            else
                throw new ArgumentException($"{nameof(value)} should be IMessage, ICustomMessage or primitive type, but got {value.GetType().Name}");
        }

        public static bool IsPrimitive(Type type)
        {
            if (type == typeof(byte) ||
                type == typeof(sbyte) ||
                type == typeof(bool) ||
                type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(short) ||
                type == typeof(ushort) ||
                type == typeof(int) ||
                type == typeof(uint) ||
                type == typeof(long) ||
                type == typeof(ulong) ||
                type == typeof(string) ||
                type == typeof(DateTime) ||
                type == typeof(TimeSpan) ||
                type == typeof(Guid))
                return true;
            return false;
        }

        void WritePrimitive(IWriter writer, object value)
        {
            switch (value)
            {
                case bool v: writer.WriteVarInt(1); writer.Write("\0o"); writer.Write(v); break;
                case float v: writer.WriteVarInt(4); writer.Write("\0f"); writer.Write(v); break;
                case double v: writer.WriteVarInt(8); writer.Write("\0d"); writer.Write(v); break;
                case byte v: writer.WriteVarInt(1); writer.Write("\0b"); writer.Write(v); break;
                case sbyte v: writer.WriteVarInt(1); writer.Write("\0B"); writer.Write(v); break;
                case short v: writer.WriteVarInt(2); writer.Write("\0S"); writer.Write(v); break;
                case ushort v: writer.WriteVarInt(2); writer.Write("\0s"); writer.Write(v); break;
                case int v: writer.WriteVarInt(4); writer.Write("\0I"); writer.Write(v); break;
                case uint v: writer.WriteVarInt(4); writer.Write("\0i"); writer.Write(v); break;
                case long v: writer.WriteVarInt(8); writer.Write("\0L"); writer.Write(v); break;
                case ulong v: writer.WriteVarInt(8); writer.Write("\0l"); writer.Write(v); break;
                case string v:
                    {
                        int bytesLen = this.Encoding.GetByteCount(v);
                        writer.WriteVarInt(bytesLen);
                        writer.Write("\0$");
                        writer.Write(this.Encoding.GetBytes(v));
                        break;
                    }
                case DateTime v: writer.WriteVarInt(8); writer.Write("\0:"); writer.Write(v.Ticks); break;
                case TimeSpan v: writer.WriteVarInt(8); writer.Write("\0."); writer.Write(v.Ticks); break;
                case Guid v: writer.WriteVarInt(16); writer.Write("\0g"); writer.Write(v.ToByteArray()); break;
                default: throw new ArgumentException($"Invalid primitive type `{value.GetType().FullName}`");
            }
        }

        object ReadPrimitive(IReader reader, string type, int length)
        {
            switch (type)
            {
                case "\0o": return reader.ReadBoolean();
                case "\0f": return reader.ReadSingle();
                case "\0d": return reader.ReadDouble();
                case "\0b": return reader.ReadByte();
                case "\0B": return reader.ReadSByte();
                case "\0S": return reader.ReadInt16();
                case "\0s": return reader.ReadUInt16();
                case "\0I": return reader.ReadInt32();
                case "\0i": return reader.ReadUInt32();
                case "\0L": return reader.ReadInt64();
                case "\0l": return reader.ReadUInt64();
                case "\0$": return Encoding.UTF8.GetString(reader.ReadBytes(length));
                case "\0:": return new DateTime(reader.ReadInt64());
                case "\0.": return new TimeSpan(reader.ReadInt64());
                case "\0g": return new Guid(reader.ReadBytes(length));
                default: throw new ArgumentException($"Message type `{type}` not found in registry");
            }
        }
    }
}
