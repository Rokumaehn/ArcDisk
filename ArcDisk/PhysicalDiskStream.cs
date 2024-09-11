using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArcDisk;

public class PhysicalDiskStream : Stream
{
    protected FileAccess _access;
    public override bool CanRead => _access == FileAccess.Read || _access == FileAccess.ReadWrite;

    public override bool CanSeek => CanRead;

    public override bool CanWrite => _access == FileAccess.Write || _access == FileAccess.ReadWrite;

    protected long _length;
    public override long Length => _length;

    protected long _position = 0;
    public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }


    protected FileStream DiskStream { get; set; }

    public int BlockSize {get; protected set;} = 512;
    public int ReadBufferSize  {get; protected set;} = 1024 * 1024 * 128;
    public int WriteBufferSize {get; protected set;} = 1024 * 1024 * 64;


    public PhysicalDiskStream(int diskNumber, int bytesPerSector, long length, FileAccess access, int readBufferSize = 1024 * 1024 * 128, int writeBuffersSize = 0)
    {
        BlockSize = bytesPerSector;
        _access = access;
        FileShare shr = _access switch
        {
            FileAccess.Read => FileShare.ReadWrite,
            FileAccess.Write => FileShare.None,
            FileAccess.ReadWrite => FileShare.None,
            _ => FileShare.None
        };
        _length = length;
        _work = new byte[BlockSize];
        var hFile = CreateDeviceHandle($@"\\.\PhysicalDrive{diskNumber}", access, shr);
        DiskStream = new FileStream(hFile, access);
        //DiskStream = new FileStream($@"C:\amiga\backups\dryrun.img", FileMode.Create, access, shr);
        //DiskStream = new FileStream($@"C:\amiga\backups\Emu68-32GB-Kioxia.img", FileMode.Open, FileAccess.Read, shr);
        if(access == FileAccess.Read | access == FileAccess.ReadWrite)
        {
            asyncBuffer = new byte[ReadBufferSize];
            asyncBufferValid = ReadBufferSize;
        }
        if(access == FileAccess.Write | access == FileAccess.ReadWrite)
        {
            WriteBufferSize = writeBuffersSize;
            if(WriteBufferSize > 0)
            {
                asyncWriteBuffer0 = new byte[WriteBufferSize];
                asyncWriteBuffer1 = new byte[WriteBufferSize];
            }
        }
    }

    

    Task asyncReadTask = null;
    private byte[] asyncBuffer;
    int asyncBufferPosition = -1;
    int asyncBufferValid; // must be set to ReadBufferSize in any constructor!!!
    public override int Read(byte[] buffer, int offset, int count)
    {
        //return InternalRead(buffer, offset, count);

        int actualRead = 0;
        int feedAmount = 0;

        if(_position >= Length && asyncBufferValid == 0)
        {
            return 0;
        }

        if(asyncBufferPosition == 0 && asyncReadTask != null)
        {
            asyncReadTask.Wait();
            int min = Math.Min(Math.Min(asyncBuffer.Length, count), asyncBufferValid);
            Array.Copy(asyncBuffer, asyncBufferPosition, buffer, offset, min);
            actualRead += min;
            asyncBufferPosition += min;
            if(asyncBufferValid < ReadBufferSize)
            {
                asyncBufferValid -= min;
                return actualRead;
            }
            asyncBufferValid -= min;
            count -= min;
            offset += min;
        }

        if(count > 0)
        {
            asyncBufferPosition = 0;
            feedAmount += InternalRead(buffer, offset, count);
            actualRead += feedAmount;

            if(feedAmount < count)
            {
                return actualRead;
            }

            asyncReadTask = Task.Run(() =>
            {
                asyncBufferValid = InternalRead(asyncBuffer, 0, asyncBuffer.Length);
            });

            return actualRead;
        }
        else
        {
            // for(int i = 0; i < asyncBuffer.Length - asyncBufferPosition; i++)
            // {
            //     asyncBuffer[i] = asyncBuffer[asyncBufferPosition + i];
            // }
            Array.Copy(asyncBuffer, asyncBufferPosition, asyncBuffer, 0, asyncBuffer.Length - asyncBufferPosition); // shift the buffer down
            var readIdx = asyncBuffer.Length - asyncBufferPosition;
            var readAmount = asyncBufferPosition;
            asyncBufferPosition = 0;
            asyncBufferValid = readIdx;

            asyncReadTask = Task.Run(() =>
            {
                asyncBufferValid += InternalRead(asyncBuffer, readIdx, readAmount);
            });
        }

        return actualRead;
    }
    
    private byte[] _work;
    private long posAhead = 0;
    public int InternalRead(byte[] buffer, int offset, int count)
    {
        int actualRead = 0;
        int feedAmount;

        if(_position % BlockSize > 0)
        {
            // Read is unaligned, there must be something in the buffer
            int min = (int)(_position + count > posAhead ? posAhead - _position : count);
            Array.Copy(_work, _position % BlockSize, buffer, offset, (int)min);
            count -= min;
            offset += min;
            _position += min;
            actualRead += min;
        }

        // read as many blocks as possible
        var blocks = count / BlockSize;
        if(blocks > 0)
        {
            // Read directly into target buffer
            feedAmount = DiskStream.Read(buffer, offset, blocks * BlockSize);
            actualRead += feedAmount;
            _position += feedAmount;
            posAhead += feedAmount;
            offset += feedAmount;
            count -= feedAmount;
            if(feedAmount < blocks * BlockSize)
            {
                // stream ended.
                return actualRead;
            }
        }

        // read the remainder, if there is any, which would be unaligned
        var remain = count % BlockSize;
        if(remain > 0)
        {
            feedAmount = DiskStream.Read(_work, 0, BlockSize);
            posAhead += feedAmount;
            actualRead += remain;
            Array.Copy(_work, 0, buffer, offset, remain);
            _position += remain;
        }

        return (int)actualRead;
    }

    public void DiscardReadBuffers()
    {
        posAhead = 0;
        asyncBufferPosition = -1;
        asyncBufferValid = ReadBufferSize;
        if(asyncReadTask != null)
        {
            asyncReadTask.Wait();
        }
        asyncReadTask = null;
    }

    public int ReadSector(byte[] buffer)
    {
        if(buffer.Length != 512)
        {
            throw new ArgumentException("Buffer must be of size 512");
        }

        return InternalRead(buffer, 0, 512);
    }

    public override int ReadByte()
    {
        throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        DiscardReadBuffers();

        switch(origin)
        {
            case SeekOrigin.Begin:
                break;
            case SeekOrigin.Current:
                offset += _position;
                break;
            case SeekOrigin.End:
                offset = Length + offset;
                break;
        }

        if(offset < 0) 
            offset = 0;
        if(offset > Length)
            offset = Length;

        _position = DiskStream.Seek(offset / BlockSize * BlockSize, SeekOrigin.Begin);
        if(offset - _position > 0)
        {
            DiskStream.Read(_work, 0, BlockSize);
            posAhead = _position + BlockSize;
            _position = offset;
        }

        return _position;
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    private byte[] asyncWriteBuffer0;
    private byte[] asyncWriteBuffer1;
    private int awbPos = 0;
    private bool useBuffer1 = false;
    private Task taskWrite = null;
    private bool needsBigFlush => awbPos > 0;
    public override void Write(byte[] buffer, int offset, int count)
    {
        if(WriteBufferSize == 0)
        {
            InternalWrite(buffer, offset, count);
            return;
        }

        while(count > 0)
        {
            var remain = WriteBufferSize - awbPos;
            var toCopy = Math.Min(remain, count);
            Array.Copy(buffer, offset, useBuffer1 ? asyncWriteBuffer1 : asyncWriteBuffer0, awbPos, toCopy);
            awbPos += toCopy;
            count -= toCopy;
            offset += toCopy;
            if(awbPos == WriteBufferSize)
            {
                useBuffer1 = !useBuffer1;
                awbPos = 0;
                if (taskWrite != null)
                {
                    taskWrite.Wait();
                }
                taskWrite = Task.Run(() => { DiskStream.Write(useBuffer1 ? asyncWriteBuffer0 : asyncWriteBuffer1, 0, WriteBufferSize); });
            }
        }
    }

    public override void WriteByte(byte value)
    {
        Write(new byte[] { value }, 0, 1);
    }

    protected bool needsFlush = false;
    protected void InternalWrite(byte[] buffer, int offset, int count)
    {
        if(_position % BlockSize > 0)
        {
            var remain = (int)(BlockSize - _position % BlockSize);
            var toCopy = Math.Min(remain, count);
            Array.Copy(_work, _position % BlockSize, buffer, offset, toCopy);
            _position += toCopy;
            count -= toCopy;
            offset += toCopy;
            if(toCopy == remain)
            {
                DiskStream.Write(_work, 0, BlockSize);
                needsFlush = false;
            }
        }

        var blocks = count / BlockSize;
        if(blocks > 0)
        {
            var blobSize = blocks * BlockSize;
            DiskStream.Write(buffer, offset, blobSize);
            _position += blobSize;
            count -= blobSize;
            offset += blobSize;
        }

        if(count > 0)
        {
            Array.Copy(buffer, offset, _work, 0, count);
            _position += count;
            needsFlush = true;
        }
    }

    protected void InternalWriteByte(byte value)
    {
        if(_position % BlockSize > 0)
        {
            _work[_position % BlockSize] = value;
            _position++;
            if(_position % BlockSize == 0)
            {
                DiskStream.Write(_work, 0, BlockSize);
                needsFlush = false;
            }
        }
        else
        {
            _work[0] = value;
            _position++;
            needsFlush = true;
        }
    }

    public override void Flush()
    {
        if(taskWrite!=null)
            taskWrite.Wait();
        if(needsBigFlush)
        {
            InternalWrite(useBuffer1 ? asyncWriteBuffer1 : asyncWriteBuffer0, 0, awbPos);
            awbPos = 0;
        }
        if(needsFlush && (_position % BlockSize > 0))
        {
            var rawSector = new byte[BlockSize];
            DiskStream.Read(rawSector, 0, BlockSize);
            Array.Copy(_work, 0, rawSector, _position % BlockSize, BlockSize - (_position % BlockSize));
            DiskStream.Write(rawSector, 0, BlockSize);
        }
    }



    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
            string lpFileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

    public static SafeFileHandle CreateDeviceHandle(string path, FileAccess access, FileShare share)
    {
        SafeFileHandle handle = CreateFile(path, access, share, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);

        int win32Error = Marshal.GetLastWin32Error();
        if (win32Error != 0)
            throw new Win32Exception(win32Error);

        return handle;
    }
}