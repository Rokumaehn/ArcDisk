using System;
using System.Diagnostics;
using System.IO;

namespace RawDiskLib
{
    public class RawDiskStream : Stream
    {
        private readonly FileStream _diskStream;
        private readonly int _smallestChunkSize;
        private readonly long _length;

        public byte[] Remainder { get; private set; }
        public long RemainderLength { get; private set; }
        private byte[] _work;

        internal RawDiskStream(FileStream diskStream, int smallestChunkSize, long length)
        {
            _diskStream = diskStream;
            _smallestChunkSize = smallestChunkSize;
            _length = length;
            Remainder = new byte[_smallestChunkSize];
            _work = new byte[_smallestChunkSize];
        }

        public override void Flush()
        {
            _diskStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = Position;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPosition = offset;
                    break;
                case SeekOrigin.Current:
                    newPosition += offset;
                    break;
                case SeekOrigin.End:
                    newPosition = Length - offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            // Is it valid?
            if (0 > newPosition || newPosition > Length)
                throw new ArgumentOutOfRangeException("Out of bounds");

            // Valid
            Position = newPosition;

            // Position disk stream
            long diskOffset = Position - Position % _smallestChunkSize; // Align to a multiple of '_smallestChunkSize'

            Debug.Assert(diskOffset % _smallestChunkSize == 0);

            _diskStream.Seek(diskOffset, SeekOrigin.Begin);

            return Position;
        }

        public override void SetLength(long value)
        {
            _diskStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long chunk = Position / _smallestChunkSize;
            int chunks = count / _smallestChunkSize + (Position % _smallestChunkSize == 0 ? 0 : 1);

            RemainderLength = 0;

            // Seek
            long diskOffset = chunk * _smallestChunkSize;
            if (diskOffset != _diskStream.Position)
                _diskStream.Seek(diskOffset, SeekOrigin.Begin);

            // Read sectors
            int actualRead;
            if (Position % _smallestChunkSize == 0 && count % _smallestChunkSize == 0)
            {
                // Read directly into target buffer
                actualRead = _diskStream.Read(buffer, offset, count);
            }
            else
            {
                // // Do a temporary buffer
                // byte[] data = new byte[(chunks + 1) * _smallestChunkSize];
                // actualRead = _diskStream.Read(data, 0, data.Length);

                // Array.Copy(data, (int) (Position % _smallestChunkSize), buffer, offset, count);

                // if((Position % _smallestChunkSize) + count < data.Length)
                // {
                //     _remainder = new byte[data.Length - (int)(Position % _smallestChunkSize) - count];
                //     Array.Copy(data, (int)(Position % _smallestChunkSize) + count, _remainder, 0, _remainder.Length);
                // }
                // else
                // {
                //     _remainder = null;
                // }


                actualRead = 0;

                if(Position % _smallestChunkSize > 0)
                {
                    // Read is unaligned

                    var discardCnt = Position % _smallestChunkSize;
                    var usableCnt = _smallestChunkSize - discardCnt;
                    _diskStream.Read(_work, 0, _smallestChunkSize);
                    actualRead = _smallestChunkSize;

                    if(count < usableCnt)
                    {
                        Array.Copy(_work, discardCnt, buffer, offset, count);
                        RemainderLength = usableCnt - count;
                        Array.Copy(_work, discardCnt + count, Remainder, 0, RemainderLength);

                        Position += actualRead;
                        return actualRead;
                    }
                    else if(count == usableCnt)
                    {
                        Array.Copy(_work, discardCnt, buffer, offset, count);
                        RemainderLength = 0;

                        Position += actualRead;
                        return actualRead;
                    }
                    else
                    {
                        // count > usableCnt
                        Array.Copy(_work, discardCnt, buffer, offset, usableCnt);
                        offset += (int)usableCnt;
                        count -= (int)usableCnt;

                        // we are now aligned
                    }
                }
                
                // Read is aligned

                // read as many blocks as possible
                var blocks = count / _smallestChunkSize;
                if(blocks > 0)
                {
                    // Read directly into target buffer
                    actualRead += _diskStream.Read(buffer, offset, blocks * _smallestChunkSize);
                    offset += blocks * _smallestChunkSize;
                    count -= blocks * _smallestChunkSize;
                }

                // read the remainder
                var remain = count % _smallestChunkSize;
                if(remain > 0)
                {
                    actualRead += _diskStream.Read(_work, 0, _smallestChunkSize);
                    Array.Copy(_work, 0, buffer, offset, remain);
                    RemainderLength = _smallestChunkSize - remain;
                    Array.Copy(_work, remain, Remainder, 0, RemainderLength);
                }
                else
                {
                    RemainderLength = 0;
                }

                Position += actualRead;
                return actualRead;
                





                // if(Position % _smallestChunkSize > 0)
                // {
                //     // Read is unaligned

                //     var discardCnt = Position % _smallestChunkSize;
                //     var usableCnt = _smallestChunkSize - discardCnt;
                //     byte[] beginBuffer = new byte[_smallestChunkSize];
                //     _diskStream.Read(beginBuffer, 0, beginBuffer.Length);
                //     actualRead = _smallestChunkSize;

                //     if(count < usableCnt)
                //     {
                //         Array.Copy(beginBuffer, discardCnt, buffer, offset, count);
                //         _remainder = new byte[usableCnt - count];
                //         Array.Copy(beginBuffer, discardCnt + count, _remainder, 0, _remainder.Length);

                //         Position += actualRead;
                //         return actualRead;
                //     }
                //     else if(count == usableCnt)
                //     {
                //         Array.Copy(beginBuffer, discardCnt, buffer, offset, count);
                //         _remainder = null;

                //         Position += actualRead;
                //         return actualRead;
                //     }
                //     else
                //     {
                //         // count > usableCnt
                //         Array.Copy(beginBuffer, discardCnt, buffer, offset, usableCnt);
                //         offset += (int)usableCnt;
                //         count -= (int)usableCnt;

                //         // we are now aligned

                //         // read as many blocks as possible
                //         var blocks = count / _smallestChunkSize;
                //         if(blocks > 0)
                //         {
                //             // Read directly into target buffer
                //             actualRead += _diskStream.Read(buffer, offset, blocks * _smallestChunkSize);
                //             offset += blocks * _smallestChunkSize;
                //             count -= blocks * _smallestChunkSize;
                //         }

                //         // read the remainder
                //         var remain = count % _smallestChunkSize;
                //         if(remain > 0)
                //         {
                //             byte[] endBuffer = new byte[_smallestChunkSize];
                //             actualRead += _diskStream.Read(endBuffer, 0, endBuffer.Length);
                //             Array.Copy(endBuffer, 0, buffer, offset, remain);
                //             _remainder = new byte[_smallestChunkSize - remain];
                //             Array.Copy(endBuffer, remain, _remainder, 0, _remainder.Length);
                //         }
                //         else
                //         {
                //             _remainder = null;
                //         }

                //         Position += actualRead;
                //         return actualRead;
                //     }
                // }
                // else
                // {
                //     // Read is aligned

                //     // read as many blocks as possible
                //     var blocks = count / _smallestChunkSize;
                //     actualRead = 0;
                //     if(blocks > 0)
                //     {
                //         // Read directly into target buffer
                //         actualRead += _diskStream.Read(buffer, offset, blocks * _smallestChunkSize);
                //         offset += blocks * _smallestChunkSize;
                //         count -= blocks * _smallestChunkSize;
                //     }

                //     // read the remainder
                //     var remain = count % _smallestChunkSize;
                //     if(remain > 0)
                //     {
                //         byte[] endBuffer = new byte[_smallestChunkSize];
                //         actualRead += _diskStream.Read(endBuffer, 0, endBuffer.Length);
                //         Array.Copy(endBuffer, 0, buffer, offset, remain);
                //         _remainder = new byte[_smallestChunkSize - remain];
                //         Array.Copy(endBuffer, remain, _remainder, 0, _remainder.Length);
                //     }
                //     else
                //     {
                //         _remainder = null;
                //     }

                //     Position += actualRead;
                //     return actualRead;
                // }




            }

            Position += actualRead;
            return actualRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            long chunk = Position / _smallestChunkSize;
            int chunks = count / _smallestChunkSize + (Position % _smallestChunkSize == 0 ? 0 : 1);

            // Write sectors
            if (Position % _smallestChunkSize == 0 && count % _smallestChunkSize == 0)
            {
                // Seek
                long diskOffset = chunk * _smallestChunkSize;
                if (diskOffset != _diskStream.Position)
                    _diskStream.Seek(diskOffset, SeekOrigin.Begin);

                // Write directly into stream
                _diskStream.Write(buffer, offset, count);
            }
            else
            {
                // Do copy-on write
                byte[] tmpBuff = new byte[_smallestChunkSize];

                int firstChunkLength = _smallestChunkSize - (int)(Position % _smallestChunkSize);
                int middleChunksLength = ((count - firstChunkLength) / _smallestChunkSize) * _smallestChunkSize;        // This will ensure that 'middleChunksLength' is a multiple of '_smallestChunkSize'
                int lastChunkLength = count - middleChunksLength - firstChunkLength;

                Debug.Assert(0 <= firstChunkLength && firstChunkLength < _smallestChunkSize);
                Debug.Assert(middleChunksLength % _smallestChunkSize == 0);
                Debug.Assert(0 <= lastChunkLength && lastChunkLength < _smallestChunkSize);

                // Seek
                _diskStream.Seek(chunk * _smallestChunkSize, SeekOrigin.Begin);

                // == First chunk ==
                if (firstChunkLength > 0)
                {
                    // Do copy-on write
                    _diskStream.Read(tmpBuff, 0, tmpBuff.Length);

                    Array.Copy(buffer, offset, tmpBuff, tmpBuff.Length - firstChunkLength, firstChunkLength);

                    _diskStream.Seek(-tmpBuff.Length, SeekOrigin.Current);
                    _diskStream.Write(tmpBuff, 0, tmpBuff.Length);
                }

                // == Middle chunks ==
                if (middleChunksLength > 0)
                {
                    // Write directly
                    _diskStream.Write(buffer, offset + firstChunkLength, middleChunksLength);
                }

                // == Last chunk ==
                if (lastChunkLength > 0)
                {
                    // Do copy-on write
                    _diskStream.Read(tmpBuff, 0, tmpBuff.Length);

                    Array.Copy(buffer, offset + firstChunkLength + middleChunksLength, tmpBuff, 0, lastChunkLength);

                    _diskStream.Seek(-tmpBuff.Length, SeekOrigin.Current);
                    _diskStream.Write(tmpBuff, 0, tmpBuff.Length);
                }
            }

            Position += count;
        }

        public override bool CanRead => _diskStream.CanRead;

        public override bool CanSeek => _diskStream.CanSeek;

        public override bool CanWrite => _diskStream.CanWrite;

        public override long Length => _length;

        public override long Position { get; set; }
    }
}
