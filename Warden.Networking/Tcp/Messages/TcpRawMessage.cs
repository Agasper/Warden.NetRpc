using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Tcp.Messages
{
    public class TcpRawMessage : RawMessage
    {
        
        internal static TcpRawMessage GetEmpty(MemoryStreamPool memoryStreamPool)
        {
            return new TcpRawMessage(memoryStreamPool, new ArraySegment<byte>(RawMessage.emptyArray,0,0), false);
        }

        internal TcpRawMessage(MemoryStreamPool memoryStreamPool) : base(memoryStreamPool)
        {
        }

        internal TcpRawMessage(MemoryStreamPool memoryStreamPool, int size) : base(memoryStreamPool, size)
        {
        }

        internal TcpRawMessage(MemoryStreamPool memoryStreamPool, int size, bool contiguous) : base(memoryStreamPool, size, contiguous)
        {
        }

        internal TcpRawMessage(MemoryStreamPool memoryStreamPool, ArraySegment<byte> segment, bool copy) : base(memoryStreamPool, segment, copy)
        {
        }

        public TcpRawMessage Encrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateEncryptor())
            {
                TcpRawMessage encryptedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(encryptedMessage.BaseStream), encryptor, CryptoStreamMode.Write))
                {
                    this.stream.Position = 0;
                    this.stream.CopyTo(cryptoStream);
                    cryptoStream.FlushFinalBlock();
                    return encryptedMessage;
                }
            }
        }

        public TcpRawMessage Decrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateDecryptor())
            {
                this.stream.Position = 0;
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(this.stream), encryptor, CryptoStreamMode.Read))
                {
                    TcpRawMessage decryptedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                    cryptoStream.CopyTo(decryptedMessage.BaseStream);
                    decryptedMessage.BaseStream.Position = 0;
                    return decryptedMessage;
                }
            }
        }

    }
}
