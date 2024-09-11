using System.IO;

namespace ArcDisk;

public class WrappedPhysicalDiskStream : Stream
{
    protected PhysicalDiskStream DiskStream;
    protected long MaxLen;
    protected long Offset;

    public WrappedPhysicalDiskStream(PhysicalDiskStream diskStream, long maxLen)
    {
        DiskStream = diskStream;
        MaxLen = maxLen;
        Offset = 0;
    }

    public WrappedPhysicalDiskStream(PhysicalDiskStream diskStream, long offset, long maxLen)
    {
        DiskStream = diskStream;
        MaxLen = maxLen;
        Offset = offset;
        DiskStream.Seek(offset, SeekOrigin.Begin);
    }

    public override bool CanRead => DiskStream.CanRead;

    public override bool CanSeek => DiskStream.CanSeek;

    public override bool CanWrite => DiskStream.CanWrite;

    public override long Length => MaxLen;

    long _position = 0;

    public override long Position { get => _position; set => DiskStream.Position = value; }

    public override void Flush()
    {
        DiskStream.Flush();
    }

    public System.IO.Hashing.Crc32 Crc { get; set; } = new System.IO.Hashing.Crc32();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int cntRead = 0;

        if (_position + count >= MaxLen)
        {
            count = (int)(MaxLen - _position);
        }

        var actual = DiskStream.Read(buffer, offset, count);
        Crc.Append(buffer.AsSpan(offset, count));
        cntRead += count;

        _position += count;
        return cntRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = DiskStream.Seek(Offset + offset, origin);
        return _position;
    }

    public override void SetLength(long value)
    {
        DiskStream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        DiskStream.Write(buffer, offset, count);
    }
}
