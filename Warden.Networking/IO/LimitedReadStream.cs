using System;
using System.IO;

namespace Warden.Networking.IO
{
    public class LimitedReadStream : Stream
    {
        Stream stream;
        long offset;
        int length;
        bool leaveOpen;

        public LimitedReadStream(Stream stream, int length, bool leaveOpen)
        {
            if (stream.Length - stream.Position < length)
                throw new ArgumentException($"Provided stream has insufficient length {stream.Length - stream.Position}, requested {length}");
            this.stream = stream;
            this.offset = stream.Position;
            this.length = length;
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead => stream.CanRead;

        public override bool CanSeek => stream.CanSeek;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get
            {
                return stream.Position - offset;
            }
            set
            {
                if (value < 0 || value > length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                stream.Position = value + offset;
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                if (Position + count > Length)
                    count = (int)(Length - Position);
                var result = stream.Read(buffer, offset, count);
                return result;
            }
            catch(ArgumentException ex)
            {
                if (ex.Message == "buffer length must be at least offset + count")
                    throw new ArgumentException($"buffer length must be at least offset + count. buffer len: {buffer.Length}, offset: {offset}, count: {count}, Position: {Position}, Length: {Length}, base position: {stream.Position}, base length: {stream.Length}");
                throw;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            //switch(origin)
            //{
            //    case SeekOrigin.Begin:
            //        {
            //            if (offset < 0 || offset > length)
            //                throw new ArgumentOutOfRangeException(nameof(offset));
            //            return stream.Seek(this.offset + offset, origin);
            //        }
            //    case SeekOrigin.Current:
            //        {
            //            if (offset + Position < 0 || offset + Position > length)
            //                throw new ArgumentOutOfRangeException(nameof(offset));
            //            return stream.Seek(offset, origin);
            //        }
            //    case SeekOrigin.End:
            //        {
            //            if (offset > 0 || offset < -length)
            //                throw new ArgumentOutOfRangeException(nameof(offset));
            //            return stream.Seek(offset, origin);
            //        }
            //    default:
            //        throw new ArgumentException("Invalid type of SeekOrigin");
            //}
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
