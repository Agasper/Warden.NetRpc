using System;
using System.IO;
using System.Text;

namespace Warden.Networking.IO
{
    public class WardenStreamReader : IReader, IByteReader, IDisposable
    {
        readonly BinaryReader reader;
        readonly Stream stream;

        public Stream BaseStream => stream;

        public int Position
        {
            get
            {
                return (int)reader.BaseStream.Position;
            }
            set
            {
                reader.BaseStream.Position = value;
            }
        }

        readonly bool leaveOpen;

        public WardenStreamReader(Stream stream, bool leaveOpen)
        {
            this.leaveOpen = leaveOpen;
            this.reader = new BinaryReader(stream, Encoding.UTF8, true);
            this.stream = stream;
        }

        public float ReadSingle()
        {
            return reader.ReadSingle();
        }

        public double ReadDouble()
        {
            return reader.ReadDouble();
        }

        public bool ReadBoolean()
        {
            return reader.ReadBoolean();
        }

        public byte ReadByte()
        {
            return reader.ReadByte();
        }

        public short ReadInt16()
        {
            return reader.ReadInt16();
        }

        public int ReadInt32()
        {
            return reader.ReadInt32();
        }

        public long ReadInt64()
        {
            return reader.ReadInt64();
        }

        public sbyte ReadSByte()
        {
            return reader.ReadSByte();
        }

        public string ReadString()
        {
            return reader.ReadString();
        }

        public ushort ReadUInt16()
        {
            return reader.ReadUInt16();
        }

        public uint ReadUInt32()
        {
            return reader.ReadUInt32();
        }

        public ulong ReadUInt64()
        {
            return reader.ReadUInt64();
        }

        public byte[] ReadBytes(int count)
        {
            return reader.ReadBytes(count);
        }

        public void Read(byte[] array, int index, int count)
        {
            reader.Read(array, index, count);
        }

        public int ReadVarInt32()
        {
            return VarintBitConverter.ToInt32(this);
        }

        public long ReadVarInt64()
        {
            return VarintBitConverter.ToInt64(this);
        }

        public uint ReadVarUInt32()
        {
            return VarintBitConverter.ToUInt32(this);
        }

        public ulong ReadVarUInt64()
        {
            return VarintBitConverter.ToUInt64(this);
        }

        public void Dispose()
        {
            if (reader != null)
                reader.Dispose();

            if (!leaveOpen && stream != null)
                stream.Dispose();
        }
    }
}
