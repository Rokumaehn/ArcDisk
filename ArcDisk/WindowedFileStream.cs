using System;
using System.IO;
using System.IO.Hashing;

namespace ArcDisk
{
    public class WindowedFileStream : FileStream
    {
        long Offset;
        long WindowLen;
        public WindowedFileStream(string path, FileMode mode, long offset, long windowLen) : base(path, mode)
        {
            base.Seek(offset, SeekOrigin.Begin);
            WindowLen = windowLen;
            Offset = offset;
        }
        public WindowedFileStream(string path, FileMode mode, FileAccess access, long offset, long windowLen) : base(path, mode, access)
        {
            base.Seek(offset, SeekOrigin.Begin);
            WindowLen = windowLen;
            Offset = offset;
        }
        public WindowedFileStream(string path, FileMode mode, FileAccess access, FileShare share, long offset, long windowLen) : base(path, mode, access, share)
        {
            base.Seek(offset, SeekOrigin.Begin);
            WindowLen = windowLen;
            Offset = offset;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long pos = base.Seek(offset, origin);
            if(pos < Offset)
            {
                return base.Seek(Offset, SeekOrigin.Begin);
            }
            else if(pos - Offset >= WindowLen)
            {
                return base.Seek(Offset + WindowLen, SeekOrigin.Begin);
            }
            return pos;
        }

        public override int Read(byte[] array, int offset, int count)
        {
            if(Position - Offset + count >= WindowLen)
            {
                count = (int)(WindowLen - (Position - Offset));
            }

            int bytesRead = base.Read(array, offset, count);
            return bytesRead;
        }

        public override int ReadByte()
        {
            if(Position - Offset + 1 >= WindowLen)
            {
                return -1;
            }
            return base.ReadByte();
        }

        public override void Write(byte[] array, int offset, int count)
        {
            base.Write(array, offset, count);
        }

        public override void WriteByte(byte value)
        {
            base.WriteByte(value);
        }

        
    }
}