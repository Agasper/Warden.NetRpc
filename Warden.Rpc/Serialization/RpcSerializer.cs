using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Warden.Networking.IO;

namespace Warden.Rpc.Serialization
{
    public class RpcSerializer
    {
        const int PROTOBUF_INTERNAL_BUFFER_SIZE = 4096;

        public enum PayloadType : byte
        {
            TypeCode,
            Protobuf
        }
        
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

            PayloadType payloadType = (PayloadType) reader.ReadByte();
            if (payloadType == PayloadType.Protobuf)
            {
                string messageType = reader.ReadString();
                if (!messageMap.ContainsKey(messageType))
                    throw new InvalidOperationException($"Message type {messageType} not found in the registry");

                var messageInfo = messageMap[messageType];
                using (LimitedReadStream limitedReadStream = new LimitedReadStream(reader.BaseStream, length, true))
                {
                    byte[] rentedArray = arrayPool.Rent(PROTOBUF_INTERNAL_BUFFER_SIZE);
                    try
                    {
                        using (CodedInputStream cis = new CodedInputStream(limitedReadStream, rentedArray, true))
                            return messageInfo.Parser.ParseFrom(cis);
                    }
                    finally
                    {
                        arrayPool.Return(rentedArray);
                    }
                }
            }
            else if (payloadType == PayloadType.TypeCode)
            {
                TypeCode typeCode = (TypeCode)reader.ReadVarInt32();
                return ReadPrimitive(reader, typeCode, length);
            }
            
            throw new InvalidOperationException($"Wrong payload type {(int)payloadType}");
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
                writer.Write((byte)PayloadType.Protobuf);
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
            else if (value is IWardenMessage customMessageValue)
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
                throw new ArgumentException($"{nameof(value)} should be IMessage, IWardenMessage or primitive type, but got {value.GetType().Name}");
        }
        
        public static bool IsPrimitive(Type type)
        {
            TypeCode typeCode = Type.GetTypeCode(type);
            if (typeCode == TypeCode.Empty || 
                typeCode == TypeCode.Object || 
                typeCode == TypeCode.DBNull)
                return false;

            return true;
        }

        void WritePrimitiveSetup(IWriter writer, int length, TypeCode typeCode)
        {
            writer.WriteVarInt(length);
            writer.Write((byte)PayloadType.TypeCode);
            writer.WriteVarInt((int)typeCode);
        }

        void WritePrimitive(IWriter writer, object value)
        {
            TypeCode typeCode = Type.GetTypeCode(value.GetType());
            switch (typeCode)
            {
                case TypeCode.Boolean:
                    WritePrimitiveSetup(writer, 1, typeCode); writer.Write((bool)value); break;
                case TypeCode.Single:
                    WritePrimitiveSetup(writer, 4, typeCode); writer.Write((float)value); break;
                case TypeCode.Double: 
                    WritePrimitiveSetup(writer, 8, typeCode); writer.Write((double)value); break;
                case TypeCode.Byte: 
                    WritePrimitiveSetup(writer, 1, typeCode); writer.Write((byte)value); break;
                case TypeCode.SByte: 
                    WritePrimitiveSetup(writer, 1, typeCode); writer.Write((sbyte)value); break;
                case TypeCode.Int16: 
                    WritePrimitiveSetup(writer, 2, typeCode); writer.Write((short)value); break;
                case TypeCode.UInt16: 
                    WritePrimitiveSetup(writer, 2, typeCode); writer.Write((ushort)value); break;
                case TypeCode.Int32: 
                    WritePrimitiveSetup(writer, 4, typeCode); writer.Write((int)value); break;
                case TypeCode.UInt32: 
                    WritePrimitiveSetup(writer, 4, typeCode); writer.Write((uint)value); break;
                case TypeCode.Int64: 
                    WritePrimitiveSetup(writer, 8, typeCode); writer.Write((long)value); break;
                case TypeCode.UInt64: 
                    WritePrimitiveSetup(writer, 8, typeCode); writer.Write((ulong)value); break;
                case TypeCode.Decimal:
                    WritePrimitiveSetup(writer, 16, typeCode); writer.Write((decimal)value); break;
                case TypeCode.Char:
                    byte[] bytes = BitConverter.GetBytes((char) value);
                    WritePrimitiveSetup(writer, bytes.Length, typeCode); 
                    writer.Write(bytes);
                    break;
                case TypeCode.String:
                    string s = (string) value;
                    int bytesLen = this.Encoding.GetByteCount(s);
                    WritePrimitiveSetup(writer, bytesLen, typeCode); 
                    writer.Write(this.Encoding.GetBytes(s)); 
                    break;
                case TypeCode.DateTime:
                    WritePrimitiveSetup(writer, 8, typeCode); writer.Write(((DateTime)value).Ticks); break;
                default: throw new ArgumentException($"Invalid primitive type `{value.GetType().FullName}`");
            }
        }

        object ReadPrimitive(IReader reader, TypeCode typeCode, int length)
        {
            switch (typeCode)
            {
                case TypeCode.Boolean: return reader.ReadBoolean();
                case TypeCode.Single: return reader.ReadSingle();
                case TypeCode.Double: return reader.ReadDouble();
                case TypeCode.Byte: return reader.ReadByte();
                case TypeCode.SByte: return reader.ReadSByte();
                case TypeCode.Int16: return reader.ReadInt16();
                case TypeCode.UInt16: return reader.ReadUInt16();
                case TypeCode.Int32: return reader.ReadInt32();
                case TypeCode.UInt32: return reader.ReadUInt32();
                case TypeCode.Int64: return reader.ReadInt64();
                case TypeCode.UInt64: return reader.ReadUInt64();
                case TypeCode.String: return Encoding.UTF8.GetString(reader.ReadBytes(length));
                case TypeCode.Char: return BitConverter.ToChar(reader.ReadBytes(length), 0);
                case TypeCode.DateTime: return new DateTime(reader.ReadInt64());
                case TypeCode.Decimal: return reader.ReadDecimal();
                case TypeCode.DBNull: return null;
                default: throw new ArgumentException($"Unsupported type code `{typeCode}`");
            }
        }
    }
}
