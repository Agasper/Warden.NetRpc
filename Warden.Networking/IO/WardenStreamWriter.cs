
using System;
using System.IO;
using System.Text;

namespace Warden.Networking.IO
{
    public class WardenStreamWriter : IWriter, IByteWriter, IDisposable
    {
        readonly Stream stream;

        public Stream BaseStream => stream;

        readonly BinaryWriter writer;
        readonly bool leaveOpen;

        public WardenStreamWriter(Stream stream, bool leaveOpen)
        {
            this.leaveOpen = leaveOpen;
            this.writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
            this.stream = stream;
        }

        public void Write(float value)
        {
            this.writer.Write(value);
        }

        public void Write(double value)
        {
            this.writer.Write(value);
        }

        public int Write(string value)
        {
            int pos = (int)this.writer.BaseStream.Position;
            this.writer.Write(value);
            return (int)this.writer.BaseStream.Position - pos;
        }

        public void Write(bool value)
        {
            this.writer.Write(value);
        }

        public void Write(byte value)
        {
            this.writer.Write(value);
        }

        public void Write(sbyte value)
        {
            this.writer.Write(value);
        }

        public void Write(short value)
        {
            this.writer.Write(value);
        }

        public void Write(ushort value)
        {
            this.writer.Write(value);
        }

        public void Write(int value)
        {
            this.writer.Write(value);
        }

        public void Write(uint value)
        {
            this.writer.Write(value);
        }

        public void Write(long value)
        {
            this.writer.Write(value);
        }

        public void Write(ulong value)
        {
            this.writer.Write(value);
        }

        public void Write(byte[] value)
        {
            this.writer.Write(value);
        }

        public void Write(byte[] value, int index, int count)
        {
            this.writer.Write(value, index, count);
        }

        public void WriteVarInt(int value)
        {
            VarintBitConverter.WriteVarintBytes(this, value);
        }

        public void WriteVarInt(uint value)
        {
            VarintBitConverter.WriteVarintBytes(this, value);
        }

        public void WriteVarInt(long value)
        {
            VarintBitConverter.WriteVarintBytes(this, value);
        }

        public void WriteVarInt(ulong value)
        {
            VarintBitConverter.WriteVarintBytes(this, value);
        }

        public void Dispose()
        {
            if (writer != null)
                writer.Dispose();

            if (!leaveOpen && stream != null)
                stream.Dispose();
        }
    }
}
