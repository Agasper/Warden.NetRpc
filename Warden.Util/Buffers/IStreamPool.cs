using System;
using System.IO;

namespace Warden.Util.Buffers
{
    public interface IStreamPool
    {
        RecyclableMemoryStream GetStream(object tag);
        RecyclableMemoryStream GetStream();

        RecyclableMemoryStream GetStream(ArraySegment<byte> segment);
        RecyclableMemoryStream GetStream(object tag, ArraySegment<byte> segment);

        RecyclableMemoryStream GetStream(object tag, int length, bool contiguous = false);
        RecyclableMemoryStream GetStream(int length, bool contiguous = false);
    }
}
