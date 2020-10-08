using System;
using System.IO;
using Warden.Util.Buffers.Internal;

namespace Warden.Util.Buffers
{
    public delegate void DOnStreamFinalized(RecyclableMemoryStream stream);

    //https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream
    public class MemoryStreamPool : IStreamPool
    {
        public event DOnStreamFinalized OnStreamFinalized;

        //const int BlockSize = 1024;
        //const int LargeBufferMultiple = 1024;
        //const int MaximumBufferSize = 1024 * 1024;
        public MemoryStreamPoolStatistics Statistics { get; private set; }

        public bool GenerateCallStacks { get => manager.GenerateCallStacks; set => manager.GenerateCallStacks = value; }
        public int BlockSize => manager.BlockSize;
        public int LargeBufferMultiple => manager.LargeBufferMultiple;
        public int MaximumBufferSize => manager.MaximumBufferSize;
        public bool UseExponentialLargeBuffer => manager.UseExponentialLargeBuffer;
        public bool ThrowExceptionOnToArray { get => manager.ThrowExceptionOnToArray; set => manager.ThrowExceptionOnToArray = value; }

        RecyclableMemoryStreamManager manager;

        public static MemoryStreamPool Shared
        {
            get
            {
                lock(sharedPoolLock)
                {
                    if (sharedPool == null)
                        sharedPool = new MemoryStreamPool(1024,
                            1024,
                            1024 * 1024);
                }

                return sharedPool;
            }
        }

        static object sharedPoolLock = new object();
        volatile static MemoryStreamPool sharedPool;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockSize">one block size</param>
        /// <param name="largeBufferMultiple">large pool size = log(maximumBufferSize / largeBufferMultiple, 2)</param>
        /// <param name="maximumBufferSize">maximum size of stream</param>
        public MemoryStreamPool(int blockSize, int largeBufferMultiple, int maximumBufferSize)
        {
            Statistics = new MemoryStreamPoolStatistics();
            manager = new RecyclableMemoryStreamManager(blockSize, largeBufferMultiple, maximumBufferSize, true);
            manager.ThrowExceptionOnToArray = false;
            manager.UsageReport += Report;
            manager.StreamFinalized += Manager_StreamFinalized;
            manager.GenerateCallStacks = false;
        }

        private void Manager_StreamFinalized(RecyclableMemoryStream stream)
        {
            Statistics.StreamsFinalized++;
            OnStreamFinalized?.Invoke(stream);
        }

        void Report(long smallPoolInUseBytes, long smallPoolFreeBytes, long largePoolInUseBytes, long largePoolFreeBytes)
        {
            Statistics.SmallPoolFreeBytes = smallPoolFreeBytes;
            Statistics.SmallPoolInUseBytes = smallPoolInUseBytes;
            Statistics.LargePoolFreeBytes = largePoolFreeBytes;
            Statistics.LargePoolInUseBytes = largePoolInUseBytes;
        }

        public RecyclableMemoryStream GetStream(object tag)
        {
            return manager.GetStream(tag);
        }

        public RecyclableMemoryStream GetStream()
        {
            return this.GetStream(tag: null);
        }

        public RecyclableMemoryStream GetStream(ArraySegment<byte> segment)
        {
            return this.GetStream(null, segment);
        }

        public RecyclableMemoryStream GetStream(object tag, ArraySegment<byte> segment)
        {
            return manager.GetStream(tag, segment.Array, segment.Offset, segment.Count);
        }

        public RecyclableMemoryStream GetStream(int length, bool contiguous = false)
        {
            return this.GetStream(null, length, contiguous);
        }

        public RecyclableMemoryStream GetStream(object tag, int length, bool contiguous = false)
        {
            if (contiguous)
                return manager.GetStream(tag, length, true);
            else
                return manager.GetStream(tag, length);
        }

    }
}
