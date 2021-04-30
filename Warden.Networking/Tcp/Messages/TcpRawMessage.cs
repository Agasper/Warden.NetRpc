using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using Warden.Networking.Cryptography;
using Warden.Networking.IO;
using Warden.Networking.Messages;
using Warden.Util.Buffers;

namespace Warden.Networking.Tcp.Messages
{
    public class TcpRawMessage : RawMessage
    {
        const int BUFFER_SIZE = 512;
        
        internal MessageHeaderFlags Flags { get; set; }

        public override string ToString()
        {
            if (stream == null || disposed)
                return $"{this.GetType().Name}[size=0,flags={Flags},hc={this.GetHashCode()}]";
            else
                return $"{this.GetType().Name}[size={stream.Length},flags={Flags},hc={this.GetHashCode()}]";
        }
        
        internal static TcpRawMessage GetEmpty(MemoryStreamPool memoryStreamPool, MessageHeaderFlags flags)
        {
            return new TcpRawMessage(memoryStreamPool, new ArraySegment<byte>(RawMessage.emptyArray,0,0), false)
            {
                Flags = flags
            };
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

        public bool HasCompressionMark()
        {
            long prevPos = this.stream.Position;
            this.stream.Position = 0;
            
            if (this.stream.ReadByte() != 0x1F)
            {
                this.stream.Position = prevPos;
                return false;
            }

            if (this.stream.ReadByte() != 0x8B)
            {
                this.stream.Position = prevPos;
                return false;
            }

            this.stream.Position = prevPos;
            return true;
        }

        public TcpRawMessage Compress(CompressionLevel compressionLevel)
        {
            CheckDisposed();
            TcpRawMessage compressedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
            compressedMessage.Flags = this.Flags;
            this.stream.Position = 0;
            
            byte[] buffer = null;
            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
                using (GZipOutputStream gzip = new GZipOutputStream(compressedMessage.stream, BUFFER_SIZE))
                {
                    gzip.SetLevel(6);
                    gzip.IsStreamOwner = false;
                    int readBytes;
                    while ((readBytes = this.stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        gzip.Write(buffer, 0, readBytes);
                    }
                }
            }
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer);
            }
            
            compressedMessage.Position = 0;
            return compressedMessage;
        }
        
        public TcpRawMessage Decompress()
        {
            CheckDisposed();
            if (!HasCompressionMark())
                throw new IOException("Message isn't compressed or compression mark not found");
            TcpRawMessage decompressedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
            decompressedMessage.Flags = this.Flags;
            this.stream.Position = 0;

            byte[] buffer = null;
            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
                using (GZipInputStream gzip = new GZipInputStream(this.stream))
                {
                    gzip.IsStreamOwner = false;
                    int readBytes;
                    while ((readBytes = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        decompressedMessage.stream.Write(buffer, 0, readBytes);
                    }
                }
            }
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer);
            }

            decompressedMessage.Position = 0;
            return decompressedMessage;
        }

        public TcpRawMessage Encrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateEncryptor())
            {
                TcpRawMessage encryptedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                encryptedMessage.Flags = this.Flags;
                this.stream.Position = 0;
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(encryptedMessage.BaseStream), encryptor, CryptoStreamMode.Write))
                {
                    this.stream.CopyTo(cryptoStream);
                    cryptoStream.FlushFinalBlock();
                    encryptedMessage.Position = 0;
                    return encryptedMessage;
                }
            }
        }

        public TcpRawMessage Decrypt(ICipher cipher)
        {
            CheckDisposed();
            using (var encryptor = cipher.CreateDecryptor())
            {
                using (CryptoStream cryptoStream = new CryptoStream(new NonDisposableStream(this.stream), encryptor, CryptoStreamMode.Read))
                {
                    TcpRawMessage decryptedMessage = new TcpRawMessage(this.memoryStreamPool, (int)this.stream.Length);
                    decryptedMessage.Flags = this.Flags;
                    this.stream.Position = 0;
                    cryptoStream.CopyTo(decryptedMessage.BaseStream);
                    decryptedMessage.Position = 0;
                    return decryptedMessage;
                }
            }
        }

    }
}
