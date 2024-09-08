using System.IO;
using RawDiskLib;

namespace ArcDisk;

public class BufferedHashingDiskStreamWriter : Stream
{
    protected RawDiskStream DiskStream;
    protected byte[] Buffer;
    protected int CurrentBufferLength;
    protected System.IO.Hashing.Crc32 InputCrc { get; set; } = new System.IO.Hashing.Crc32();

    public BufferedHashingDiskStreamWriter(RawDiskStream diskStream, int bufferSize)
    {
        DiskStream = diskStream;
        Buffer = new byte[bufferSize];
        CurrentBufferLength = 0;
        InputCrc = new System.IO.Hashing.Crc32();
    }

    public override bool CanRead => false;

    public override bool CanSeek => DiskStream.CanSeek;

    public override bool CanWrite => DiskStream.CanWrite;

    public override long Length => DiskStream.Length;

    long _position = 0;

    public override long Position { get => _position; set => _position = DiskStream.Position = value; }

    public void ResetCrc() => InputCrc.Reset();
    public byte[] GetCrc() => InputCrc.GetCurrentHash();
    public uint GetCrcUint() => InputCrc.GetCurrentHashAsUInt32();

    public override void Flush()
    {
        DiskStream.Write(Buffer, 0, CurrentBufferLength);
        CurrentBufferLength = 0;
        DiskStream.Flush();
    }

    public override void Close()
    {
        Flush();
        DiskStream.Close();
        base.Close();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new System.NotSupportedException();
    }

    public override int ReadByte()
    {
        throw new System.NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        int remLen = (int)(Buffer.Length - CurrentBufferLength);
        int minLen = System.Math.Min(remLen, count);
        Array.Copy(buffer, offset, Buffer, CurrentBufferLength, minLen);
        CurrentBufferLength += minLen;
        _position += minLen;
        if(CurrentBufferLength == Buffer.Length)
        {
            DiskStream.Write(Buffer, 0, Buffer.Length);
            InputCrc.Append(Buffer);
            CurrentBufferLength = 0;
        }
        else
        {
            return;
        }

        // buffer was emptied
        
        offset += minLen;
        count -= minLen;

        while(count >= Buffer.Length)
        {
            DiskStream.Write(buffer, offset, Buffer.Length);
            InputCrc.Append(buffer.AsSpan(offset, Buffer.Length));
            _position += Buffer.Length;
            offset += Buffer.Length;
            count -= Buffer.Length;
        }

        if(count > 0)
        {
            Array.Copy(buffer, offset, Buffer, 0, count);
            CurrentBufferLength = count;
            _position += count;
        }
    }

    public override void WriteByte(byte value)
    {
        Buffer[CurrentBufferLength++] = value;
        _position++;

        if(CurrentBufferLength == Buffer.Length)
        {
            DiskStream.Write(Buffer, 0, Buffer.Length);
            InputCrc.Append(Buffer);
            CurrentBufferLength = 0;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Flush();
        _position = DiskStream.Seek(offset, origin);
        return _position;
    }

    public override void SetLength(long value)
    {
        DiskStream.SetLength(value);
    }

    
}
