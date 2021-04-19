using System;
using System.IO.Compression;
using System.Security.Cryptography;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Tcp.Messages
{
    public class TcpRawMessage : RawMessage
    {
        public bool Compressed { get; internal set; }
        
        public override string ToString()
        {
            if (stream == null || disposed)
                return $"{this.GetType().Name}[size=0,compressed={Compressed}]";
            else
                return $"{this.GetType().Name}[size={stream.Length},compressed={Compressed}]";
        }
        
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

        public TcpRawMessage Compress(CompressionLevel compressionLevel)
        {
            CheckDisposed();
            TcpRawMessage compressedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
            compressedMessage.Compressed = true;
            using (GZipStream gzipStream = new GZipStream(compressedMessage.BaseStream, compressionLevel, true))
            {
                this.stream.Position = 0;
                this.stream.CopyTo(gzipStream);
            }

            return compressedMessage;
        }
        
        public TcpRawMessage Decompress()
        {
            CheckDisposed();
            TcpRawMessage decompressedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length); 
            this.stream.Position = 0;
            using (GZipStream gzipStream = new GZipStream(this.stream, CompressionMode.Decompress, true))
            {
                gzipStream.CopyTo(decompressedMessage.BaseStream);
                decompressedMessage.Position = 0;
            }

            return decompressedMessage;
        }

        public TcpRawMessage Encrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateEncryptor())
            {
                TcpRawMessage encryptedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                encryptedMessage.Compressed = this.Compressed;
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
                    decryptedMessage.Compressed = this.Compressed;
                    cryptoStream.CopyTo(decryptedMessage.BaseStream);
                    decryptedMessage.BaseStream.Position = 0;
                    return decryptedMessage;
                }
            }
        }

    }
}
