// File only kept for reference

// using System.IO;
// using RawDiskLib;

// namespace ArcDisk;

// public class WrappedDiskStream : Stream
// {
//     protected RawDiskStream DiskStream;
//     protected long MaxLen;
//     protected long Offset;

//     public WrappedDiskStream(RawDiskStream diskStream, long maxLen)
//     {
//         DiskStream = diskStream;
//         MaxLen = maxLen;
//         Offset = 0;
//     }

//     public WrappedDiskStream(RawDiskStream diskStream, long offset, long maxLen)
//     {
//         DiskStream = diskStream;
//         MaxLen = maxLen;
//         Offset = offset;
//         DiskStream.Seek(offset, SeekOrigin.Begin);
//     }

//     public override bool CanRead => DiskStream.CanRead;

//     public override bool CanSeek => DiskStream.CanSeek;

//     public override bool CanWrite => DiskStream.CanWrite;

//     public override long Length => MaxLen;

//     long _position = 0;

//     byte[] _buffer;

//     public override long Position { get => _position; set => DiskStream.Position = value; }

//     public override void Flush()
//     {
//         DiskStream.Flush();
//     }

//     // public override int Read(byte[] buffer, int offset, int count)
//     // {
//     //     if(_position + count >= MaxLen)
//     //     {
//     //         count = (int)(MaxLen - _position);
//     //     }

//     //     if(count <= 0)
//     //     {
//     //         return 0;
//     //     }

//     //     var actual = DiskStream.Read(buffer, offset, count);
//     //     if(actual > count)
//     //     {
//     //         var diff = count - actual;
//     //         DiskStream.Seek(diff, SeekOrigin.Current);
//     //         _position += count;
//     //         return count;
//     //     }

//     //     _position += actual;
//     //     return actual;
//     // }

//     public System.IO.Hashing.Crc32 Crc { get; set; } = new System.IO.Hashing.Crc32();

//     public override int Read(byte[] buffer, int offset, int count)
//     {
//         int cntRead = 0;

//         if (_position + count >= MaxLen)
//         {
//             count = (int)(MaxLen - _position);
//         }

//         if (_buffer != null)
//         {
//             var toCopy = Math.Min(count, _buffer.Length);
//             Array.Copy(_buffer, 0, buffer, offset, toCopy);
//             _position += toCopy;
//             if (count < _buffer.Length)
//             {
//                 _buffer = _buffer.AsSpan().Slice(toCopy).ToArray();
//                 Crc.Append(buffer.AsSpan(offset, count));
//                 return toCopy;
//             }
//             else if (count == _buffer.Length)
//             {
//                 _buffer = null;
//                 Crc.Append(buffer.AsSpan(offset, count));
//                 return toCopy;
//             }
//             // at this point, count > _buffer.Length
//             Crc.Append(buffer.AsSpan(offset, toCopy));
//             _buffer = null;
//             cntRead += toCopy;
//             count -= toCopy;
//             offset += toCopy;
//         }

//         var actual = DiskStream.Read(buffer, offset, count);
//         Crc.Append(buffer.AsSpan(offset, count));
//         if (actual > count)
//         {
//             cntRead += count;
//             var diff = actual - count;
//             _buffer = new byte[diff];
//             Array.Copy(DiskStream.Remainder, 0, _buffer, 0, diff);
//             _position += count;
//             return cntRead;
//         }
//         cntRead += count;

//         _position += count;
//         return cntRead;
//     }

//     public override long Seek(long offset, SeekOrigin origin)
//     {
//         _buffer = null;
//         _position = DiskStream.Seek(Offset + offset, origin);
//         return _position;
//     }

//     public override void SetLength(long value)
//     {
//         DiskStream.SetLength(value);
//     }

//     public override void Write(byte[] buffer, int offset, int count)
//     {
//         DiskStream.Write(buffer, offset, count);
//     }
// }
