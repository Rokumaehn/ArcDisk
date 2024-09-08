using System;
using System.IO;
using System.IO.Hashing;

namespace ArcDisk
{
    public class CrcFileStream : FileStream
    {
        protected Crc32 Crc32 { get; set; }

        public void ResetCrc()
        {
            Crc32.Reset();
        }

        public byte[] GetCrc()
        {
            return Crc32.GetCurrentHash();
        }

        public uint GetCrcUint()
        {
            return Crc32.GetCurrentHashAsUInt32();
        }

        public CrcFileStream(string path, FileMode mode) : base(path, mode)
        {
            Crc32 = new Crc32();
        }
        public CrcFileStream(string path, FileMode mode, FileAccess access) : base(path, mode, access)
        {
            Crc32 = new Crc32();
        }
        public CrcFileStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
        {
            Crc32 = new Crc32();
        }

        public override int Read(byte[] array, int offset, int count)
        {
            int bytesRead = base.Read(array, offset, count);
            Crc32.Append(array.AsSpan(offset, count));
            return bytesRead;
        }

        public override void Write(byte[] array, int offset, int count)
        {
            base.Write(array, offset, count);
            Crc32.Append(array.AsSpan(offset, count));
        }

        public override void WriteByte(byte value)
        {
            base.WriteByte(value);
            Crc32.Append(new ReadOnlySpan<byte>(ref value));
        }
    }
}