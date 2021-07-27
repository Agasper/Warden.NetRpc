using System.IO;

namespace Warden.Networking.IO
{
    public interface IByteWriter
    {
        void Write(byte value);
    }

    public interface IWriter
    {
        Stream BaseStream { get; }

        int Write(string value);
        void Write(float value);
        void Write(double value);
        void Write(bool value);
        void Write(byte value);
        void Write(sbyte value);
        void Write(short value);
        void Write(ushort value);
        void Write(int value);
        void Write(uint value);
        void Write(long value);
        void Write(ulong value);
        void Write(decimal value);
        void Write(byte[] value);
        void Write(byte[] value, int index, int count);

        void WriteVarInt(int value);
        void WriteVarInt(uint value);
        void WriteVarInt(long value);
        void WriteVarInt(ulong value);

    }
}
