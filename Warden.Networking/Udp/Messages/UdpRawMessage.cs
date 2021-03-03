using System;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Messages;
using Warden.Networking.Tcp.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Udp.Messages
{
    public class UdpRawMessage : RawMessage
    {
        public DeliveryType DeliveryType { get; set; } = DeliveryType.ReliableOrdered;
        public int Channel { get; set; } = 0;

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, int payloadSize, bool contiguous)
            : base(memoryStreamPool, payloadSize, contiguous)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, int payloadSize)
            : base(memoryStreamPool, payloadSize)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool)
            : base(memoryStreamPool)
        {
        }

        internal UdpRawMessage(MemoryStreamPool memoryStreamPool, ArraySegment<byte> segment, bool copy)
            : base(memoryStreamPool, segment, copy)
        {
        }

        internal static UdpRawMessage CreateEmpty(MemoryStreamPool memoryStreamPool)
        {
            return new UdpRawMessage(memoryStreamPool, new ArraySegment<byte>(RawMessage.emptyArray, 0, 0), false);
        }

        public Datagram ConvertToDatagram()
        {
            Datagram datagram;
            if (this.Length == 0)
            {
                datagram = Datagram.CreateEmpty(memoryStreamPool);
                return datagram;
            }

            datagram = Datagram.CreateNew(memoryStreamPool, this.Length);
            this.Position = 0;
            this.CopyTo(datagram.BaseStream);
            return datagram;
        }
        
        public UdpRawMessage Encrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateEncryptor())
            {
                UdpRawMessage encryptedMessage = new UdpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(encryptedMessage.BaseStream), encryptor, CryptoStreamMode.Write))
                {
                    this.stream.Position = 0;
                    this.stream.CopyTo(cryptoStream);
                    cryptoStream.FlushFinalBlock();
                    return encryptedMessage;
                }
            }
        }

        public UdpRawMessage Decrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateDecryptor())
            {
                this.stream.Position = 0;
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(this.stream), encryptor, CryptoStreamMode.Read))
                {
                    UdpRawMessage decryptedMessage = new UdpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                    cryptoStream.CopyTo(decryptedMessage.BaseStream);
                    decryptedMessage.BaseStream.Position = 0;
                    return decryptedMessage;
                }
            }
        }

    }
}
