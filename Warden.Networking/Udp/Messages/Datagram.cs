using System;
using System.IO;
using Warden.Networking.IO;
using Warden.Networking.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Messages
{
    public class Datagram : RawMessage
    {
        public class FragmentInfo
        {
            public ushort Frame { get; private set; } = 1;
            public ushort Frames { get; private set; } = 1;
            public ushort FragmentationGroupId { get; private set; }

            public FragmentInfo(ushort groupId, ushort frame, ushort frames)
            {
                this.FragmentationGroupId = groupId;
                this.Frame = frame;
                this.Frames = frames;
            }
        }

        public bool IsDisposed { get; set; }
        public bool DontDisposeOnSend { get; set; }
        public MessageType Type { get; set; }
        public ushort ConnectionKey { get; set; }
        public ushort Sequence { get; set; }
        //public int Flag { get; set; }
        public bool IsFragmented => FragmentationInfo != null;
        public FragmentInfo FragmentationInfo { get; set; }
        public DeliveryType DeliveryType { get; set; }
        public int Channel
        {
            get => channel;
            set
            {
                ChannelDescriptor.CheckChannelValue(value);
                channel = value;
            }
        }

        int channel;

        internal static Datagram CreateFromRaw(MemoryStreamPool memoryStreamPool, ArraySegment<byte> data)
        {
            Datagram datagram = new Datagram(memoryStreamPool, data.Count);

            ByteArrayReader reader = new ByteArrayReader(data);
            byte serviceByte = reader.ReadByte();
            byte serviceByte2 = reader.ReadByte();
            int deliveryMethod = (serviceByte & 0b_1110_0000) >> 5;
            int channel = (byte)((serviceByte & 0b_0001_1100) >> 2);
            bool fragmented = (serviceByte & 0b_0000_0010) >> 1 == 1;
            //free bit serviceByte 0b_0000_0001
            int datagramType = ((serviceByte2 & 0b_1111_0000) >> 4);

            datagram.DeliveryType = (DeliveryType)deliveryMethod;
            datagram.Channel = channel;
            datagram.Type = (MessageType)datagramType;

            datagram.ConnectionKey = reader.ReadUInt16();
            datagram.Sequence = reader.ReadUInt16();
            if (fragmented)
            {
                datagram.FragmentationInfo = new FragmentInfo(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
            }

            if (reader.EOF)
                return datagram;

            var payloadSegment = reader.ReadArraySegment(reader.Count - reader.Position);
            datagram.stream.Write(payloadSegment.Array, payloadSegment.Offset, payloadSegment.Count);
            datagram.stream.Position = 0;

            return datagram;
        }

        internal static Datagram CreateNew(MemoryStreamPool memoryStreamPool, int payloadSize)
        {
            return new Datagram(memoryStreamPool, payloadSize);
        }

        internal static Datagram CreateNew(MemoryStreamPool memoryStreamPool)
        {
            return new Datagram(memoryStreamPool);
        }

        internal static Datagram CreateEmpty(MemoryStreamPool memoryStreamPool)
        {
            return new Datagram(memoryStreamPool, new ArraySegment<byte>(RawMessage.emptyArray, 0, 0), false);
        }

        private Datagram(MemoryStreamPool memoryStreamPool, int payloadSize)
            : base(memoryStreamPool, payloadSize)
        {

        }

        private Datagram(MemoryStreamPool memoryStreamPool)
            : base(memoryStreamPool)
        {

        }

        private Datagram(MemoryStreamPool memoryStreamPool, ArraySegment<byte> segment, bool copy)
            : base(memoryStreamPool, segment, copy)
        {
            
        }

        public int GetTotalSize()
        {
            int size = GetHeaderSize();
            if (stream != null)
                size += (int)stream.Length;
            return size;
        }

        public static int GetHeaderSize(bool fragmented)
        {
            int result = 1 + 1 + 2 + 2;
            if (fragmented)
                result += 2 + 2 + 2;
            return result;
        }

        public int GetHeaderSize()
        {
            return GetHeaderSize(this.IsFragmented);
        }

        public ChannelDescriptor GetChannelDescriptor()
        {
            return new ChannelDescriptor(Channel, DeliveryType);
        }

        public Datagram CreateAck()
        {
            Datagram ack = Datagram.CreateEmpty(memoryStreamPool);
            ack.Type = MessageType.DeliveryAck;
            ack.Sequence = this.Sequence;
            ack.channel = this.channel;
            ack.DeliveryType = this.DeliveryType;
            ack.ConnectionKey = this.ConnectionKey;
            return ack;
        }

        public UdpRawMessage ConvertToMessage()
        {
            UdpRawMessage message;
            if (this.Length == 0)
                message = UdpRawMessage.CreateEmpty(memoryStreamPool);
            else
                message = new UdpRawMessage(this.memoryStreamPool, this.Length);
            message.DeliveryType = this.DeliveryType;
            message.Channel = this.channel;
            if (this.Length == 0)
                return message;
            this.Position = 0;
            this.CopyTo(message.BaseStream);
            message.Position = 0;
            return message;
        }

        public int WriteTo(ArraySegment<byte> segment)
        {
            ChannelDescriptor.CheckChannelValue(Channel);

            ByteArrayWriter writer = new ByteArrayWriter(segment);
            int serviceByte = 0;

            serviceByte |= (byte)DeliveryType << 5;
            serviceByte |= Channel << 2;
            if (IsFragmented)
                serviceByte |= 0b_0000_0010;
            writer.Write((byte)serviceByte);

            serviceByte = 0;
            serviceByte |= (byte)Type << 4;
            writer.Write((byte)serviceByte);

            writer.Write(ConnectionKey);
            writer.Write(Sequence);

            if (IsFragmented)
            {
                writer.Write(this.FragmentationInfo.FragmentationGroupId);
                writer.Write(this.FragmentationInfo.Frame);
                writer.Write(this.FragmentationInfo.Frames);
            }

            if (stream != null)
            {
                stream.Position = 0;
                stream.Read(segment.Array, segment.Offset + writer.Position, (int)stream.Length);
                writer.Position += (int)stream.Length;
            }

            return writer.Position;
        }

        public override string ToString()
        {
            string fragInfo = "null";
            if (IsFragmented)
                fragInfo = $"{FragmentationInfo.Frame}/{FragmentationInfo.Frames}(g{FragmentationInfo.FragmentationGroupId})";
            string len = $"0 ({GetTotalSize()})";
            if (stream != null)
                len = $"{stream.Length} ({GetTotalSize()})";
            return $"{nameof(Datagram)}[type={Type},connKey={ConnectionKey},dtype={DeliveryType},channel={Channel},seq={Sequence},len={len},frag={fragInfo}]";
        }

        public override void Dispose()
        {
            IsDisposed = true;
            base.Dispose();
        }
    }
}
