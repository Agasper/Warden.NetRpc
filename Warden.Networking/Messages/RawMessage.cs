using System;
using System.Collections.Generic;
using System.IO;
using Warden.Util.Buffers;

namespace Warden.Networking.Messages
{
    public abstract class RawMessage : IDisposable
    {
        public virtual Stream BaseStream
        {
            get
            {
                CheckDisposed();
                return stream;
            }
        }
        public int Position { get => (int)stream.Position; set => stream.Position = value; }
        public int Length { get => (int)stream.Length; }

        internal static readonly byte[] emptyArray = new byte[0];
        private protected MemoryStream stream;
        private protected MemoryStreamPool memoryStreamPool;

        private protected bool disposed;

        private protected RawMessage(MemoryStreamPool memoryStreamPool)
        {
            this.memoryStreamPool = memoryStreamPool;
            stream = memoryStreamPool.GetStream(this);
        }

        private protected RawMessage(MemoryStreamPool memoryStreamPool, int size)
        {
            this.memoryStreamPool = memoryStreamPool;
            stream = memoryStreamPool.GetStream(this, size);
        }

        private protected RawMessage(MemoryStreamPool memoryStreamPool, int size, bool contiguous)
        {
            this.memoryStreamPool = memoryStreamPool;
            stream = memoryStreamPool.GetStream(this, size, contiguous);
        }

        private protected RawMessage(MemoryStreamPool memoryStreamPool, ArraySegment<byte> segment, bool copy)
        {
            this.memoryStreamPool = memoryStreamPool;
            if (copy)
                stream = memoryStreamPool.GetStream(this, segment);
            else
                stream = new MemoryStream(segment.Array, segment.Offset, segment.Count);
        }

        public IReadOnlyList<ArraySegment<byte>> GetBuffers()
        {
            CheckDisposed();
            if (stream.Length == 0)
                return new ArraySegment<byte>[] { new ArraySegment<byte>(emptyArray) };

            if (stream is RecyclableMemoryStream rStream)
                return rStream.GetBuffers();
            else
                return new ArraySegment<byte>[] { new ArraySegment<byte>(stream.GetBuffer(), 0, (int)stream.Length) };
        }

        public byte[] GetBuffer()
        {
            CheckDisposed();
            return stream.GetBuffer();
        }

        public void CopyTo(Stream destStream)
        {
            CopyTo(destStream, (int)(stream.Length - stream.Position));
        }

        public void CopyTo(Stream destStream, int bytes)
        {
            if (bytes <= 0)
                return;
            if (bytes > stream.Length - stream.Position)
                throw new ArgumentOutOfRangeException(nameof(bytes), "Stream EOF");

            int bytesLeft = bytes;
            int skipBytes = (int)stream.Position;
            foreach (var segment in GetBuffers())
            {
                int startIndex = 0;
                if (skipBytes > 0)
                {
                    if (skipBytes < segment.Count)
                    {
                        startIndex = skipBytes;
                        skipBytes -= segment.Count;
                    }
                    else
                    {
                        skipBytes -= segment.Count;
                        continue;
                    }
                }

                int toWrite = bytesLeft;
                if (toWrite > segment.Count - (startIndex + segment.Offset))
                    toWrite = segment.Count - (startIndex + segment.Offset);
                destStream.Write(segment.Array, segment.Offset + startIndex, toWrite);
                bytesLeft -= toWrite;

                if (bytesLeft <= 0)
                    break;
            }

            stream.Position += bytes;
        }

        public byte[] ToArray()
        {
            CheckDisposed();
            return stream.ToArray();
        }

        public override string ToString()
        {
            if (stream == null || disposed)
                return $"{this.GetType().Name}[size=0]";
            else
                return $"{this.GetType().Name}[size={stream.Length}]";
        }

        private protected void CheckDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(RawMessage));
        }

        public virtual void Dispose()
        {
            if (stream != null)
                stream.Dispose();
            disposed = true;
        }
    }
}
