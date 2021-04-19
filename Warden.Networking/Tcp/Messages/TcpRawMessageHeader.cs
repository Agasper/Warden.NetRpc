using System;
namespace Warden.Networking.Tcp.Messages
{
    [Flags]
    enum MessageHeaderFlags : byte
    {
        None = 0,
        KeepAliveRequest = 1,
        KeepAliveResponse = 2,
        Compressed = 4,
            //8
            //16
            //32
    }

    class TcpRawMessageHeader
    {
        public int MessageSize { get; private set; }
        public TcpRawMessageOptions Options => options;

        int sizeBitsPosition;
        bool flagsRead;
        TcpRawMessageOptions options;

        public TcpRawMessageHeader()
        {
            options = TcpRawMessageOptions.None;
        }

        public TcpRawMessageHeader(int size, TcpRawMessageOptions options) : this()
        {
            this.MessageSize = size;
            this.options = options;
        }

        public override string ToString()
        {
            return $"{nameof(TcpRawMessageHeader)}[{options}]";
        }

        public bool Write(ArraySegment<byte> newData, out int bytesRead)
        {
            bytesRead = 0;
            if (newData.Count == 0)
                return false;

            for (int i = 0; i < newData.Count; i++)
            {
                try
                {
                    if (WriteByte(newData.Array[newData.Offset + i]))
                        return true;
                }
                finally
                {
                    bytesRead = i + 1;
                }
            }

            return false;
        }


        bool WriteByte(byte value)
        {
            if (!flagsRead)
            {
                options.Flags = (MessageHeaderFlags)value;
                flagsRead = true;
                return false;
            }

            uint byteValue = value;

            uint tmp = byteValue & 0x7f;
            MessageSize |= (int)(tmp << sizeBitsPosition);

            if ((byteValue & 0x80) != 0x80)
                return true;

            sizeBitsPosition += 7;

            if (sizeBitsPosition > 32) //int bits
                throw new InvalidOperationException("Message header size exceeded 32 bits value");

            return false;
        }

        public void Reset()
        {
            sizeBitsPosition = 0;
            MessageSize = 0;
            flagsRead = false;
            options = TcpRawMessageOptions.None;
        }

        public ArraySegment<byte> Build()
        {
            byte[] header = new byte[5];
            int headerPos = 1;
            uint value = (uint)MessageSize;
            do
            {
                var byteVal = value & 0x7f;
                value >>= 7;

                if (value != 0)
                {
                    byteVal |= 0x80;
                }

                header[headerPos] = (byte)byteVal;
                headerPos++;

            } while (value != 0);

            header[0] = (byte) options.Flags;

            return new ArraySegment<byte>(header, 0, headerPos);
        }

    }
}
