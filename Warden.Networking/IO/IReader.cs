using System.IO;

namespace Warden.Networking.IO
{
    public interface IByteReader
    {
        byte ReadByte();
    }

    public interface IReader
    {
        Stream BaseStream { get; }

        string ReadString();
        bool ReadBoolean();
        float ReadSingle();
        double ReadDouble();

        byte ReadByte();
        sbyte ReadSByte();
        short ReadInt16();
        ushort ReadUInt16();
        int ReadInt32();
        uint ReadUInt32();
        long ReadInt64();
        ulong ReadUInt64();

        int ReadVarInt32();
        uint ReadVarUInt32();
        long ReadVarInt64();
        ulong ReadVarUInt64();

        byte[] ReadBytes(int count);
    }
}
