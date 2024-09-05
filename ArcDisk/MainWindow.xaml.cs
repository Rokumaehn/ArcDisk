using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32;
using RawDiskLib;
using SevenZip;

namespace ArcDisk;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public ObservableCollection<DiskListItem> Disks { get; set; } = new ObservableCollection<DiskListItem>();
    public System.Threading.Timer Timer { get; set; }

    private long lastBytesRead = 0;
    private long seconds = 0;

    private void ProgressTimerCallback(object? state)
    {
        if(CurrentOperation == ImagingOperations.None || CurrentSizeToRead == 0)
        {
            return;
        }

        seconds++;

        var cur = BytesCopied;

        if(seconds % 10 == 0)
        {
            var diff = cur - lastBytesRead;
            lastBytesRead = cur;

            progIo.Dispatcher.Invoke(() =>
            {
                txtProgress.Text = $"{diff/1024.0f/1024.0f/10.0} MB/s";
            });
        }

        progIo.Dispatcher.Invoke(() =>
        {
            progIo.Value = (int)(cur * 100.0 / CurrentSizeToRead);
        });
    }

    public MainWindow()
    {
        InitializeComponent();

        Timer = new(ProgressTimerCallback, this, 0, 1000);

        var test = RawDiskLib.Utils.GetAvailableDrives();

        lstDrives.ItemsSource = Disks;
        cmbFormat.ItemsSource = Enum.GetValues(typeof(ImagingFormats));
        cmbFormat.SelectedItem = ImagingFormats.Zip;

        Init();
    }

    private void Init()
    {
        var instances = CimSession.Create(null)
                .QueryInstances(@"root\cimv2", "WQL", @"SELECT *
                FROM Win32_DiskDrive WHERE MediaType = 'Removable Media'");
        
        foreach (var instance in instances)
        {
            var captionProperty = instance.CimInstanceProperties["Caption"];
            string? caption = captionProperty.Value as string;
            var modelProperty = instance.CimInstanceProperties["Model"];
            string? model = modelProperty.Value as string;
            var deviceIdProperty = instance.CimInstanceProperties["DeviceID"];
            string? deviceId = deviceIdProperty.Value as string;
            var sizeProperty = instance.CimInstanceProperties["Size"];
            ulong size = (ulong)sizeProperty.Value;
            var mediaTypeProperty = instance.CimInstanceProperties["MediaType"];

            if(deviceId==null || size==0)
            {
                continue;
            }

            Disks.Add(new DiskListItem
            {
                Caption = caption ?? "<N/A>",
                Model = model ?? "<N/A>",
                DeviceId = deviceId,
                Size = (long)size
            });
        }

        
    }

    public RawDiskLib.RawDisk CurrentDisk { get; set; }
    public RawDiskLib.RawDiskStream CurrentDiskStream { get; set; }
    public FileStream CurrentArchiveStream { get; set; }
    public ZipArchive CurrentArchive { get; set; }
    public ZipArchiveEntry CurrentArchiveEntry { get; set; }
    public Stream CurrentArchiveEntryStream { get; set; }
    public long CurrentSizeToRead { get; set; }
    public Task IoTask { get; set; }
    public ImagingOperations CurrentOperation { get; set; }
    public ImagingFormats CurrentFormat => (ImagingFormats)cmbFormat.SelectedItem;

    public enum ImagingOperations
    {
        None,
        Read,
        Write
    }

    public enum ImagingFormats
    {
        Zip,
        Lzma
    }

    public class CompressionProgress : ICodeProgress
    {
        MainWindow Wnd;
        public CompressionProgress( MainWindow wnd )
        {
            Wnd = wnd;
        }

        public void SetProgress(long inSize, long outSize)
        {
            Wnd.BytesCopied = inSize;
        }
    }

    public class DecompressionProgress : ICodeProgress
    {
        MainWindow Wnd;
        public DecompressionProgress( MainWindow wnd )
        {
            Wnd = wnd;
        }

        public void SetProgress(long inSize, long outSize)
        {
            Wnd.BytesCopied = outSize;
        }
    }

    public long BytesCopied { get; set; }
    public Task<long> CopyBytesAsync(long bytesRequired, Stream inStream, Stream outStream)
    {
        return Task.Run<long>(() =>
        {
            long readSoFar = 0L;
            BytesCopied = 0L;
            var buffer = new byte[64*1024];
            do
            {
                var toRead = Math.Min(bytesRequired - readSoFar, buffer.Length);
                var readNow = inStream.Read(buffer, 0, (int)toRead);
                if (readNow == 0)
                    break; // End of stream
                outStream.Write(buffer, 0, readNow);
                readSoFar += readNow;
                BytesCopied = readSoFar;
            } while (readSoFar < bytesRequired);
            return readSoFar;
        });
    }


    protected void ReadZip()
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Compressed Image (*.img.zip)|*.img.zip";
        if (saveFileDialog.ShowDialog() == true)
        {
            var disk = lstDrives.SelectedItem as DiskListItem;
            if (disk == null)
            {
                return;
            }

            var path = saveFileDialog.FileName;
            var imgFileName = Path.GetFileNameWithoutExtension(path);
            CurrentSizeToRead = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRead = GetAllocatedSize(CurrentDisk);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRead = 0;

            // Option 1: Using dotntet ZipArchive
            
            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Create, true);
            CurrentArchiveEntry = CurrentArchive.CreateEntry(imgFileName, CompressionLevel.SmallestSize);
            CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
            IoTask = CopyBytesAsync(CurrentSizeToRead, CurrentDiskStream, CurrentArchiveEntryStream);

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                CurrentDiskStream.Close();
                CurrentDiskStream = null;
                CurrentDisk = null;

                CurrentArchiveEntryStream.Close();
                CurrentArchiveEntryStream.Dispose();
                CurrentArchiveEntryStream = null;
                CurrentArchiveEntry = null;
                CurrentArchive.Dispose();
                CurrentArchive = null;
                CurrentArchiveStream.Close();
                CurrentArchiveStream.Dispose();
                CurrentArchiveStream = null;

                CurrentSizeToRead = 0;
                CurrentOperation = ImagingOperations.None;
                progIo.Dispatcher.Invoke(() =>
                {
                    btnRead.IsEnabled = true;
                    btnWrite.IsEnabled = true;
                    progIo.Value = 0;
                });
            });

            // Option 2: Using SharpZipLib

            // CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            // var zipStream = new ZipOutputStream(CurrentArchiveStream);
            // zipStream.SetLevel(9);
            // var entry = new ZipEntry(imgFileName);
            // zipStream.PutNextEntry(entry);
            // IoTask = CurrentDiskStream.CopyToAsync(zipStream);

            // CurrentOperation = ImagingOperations.Read;
            
            // IoTask.ContinueWith((t) =>
            // {
            //     zipStream.Close();
            //     CurrentDiskStream.Close();
            //     CurrentArchiveStream.Close();

            //     CurrentDiskStream = null;
            //     CurrentDisk = null;
            //     CurrentArchiveStream = null;
            //     CurrentArchive = null;

            //     CurrentSizeToRead = 0;
            //     CurrentOperation = ImagingOperations.None;
            //     progIo.Dispatcher.Invoke(() =>
            //     {
            //         btnRead.IsEnabled = true;
            //         btnWrite.IsEnabled = true;
            //         progIo.Value = 0;
            //     });
            // });
        }
    }

    protected void ReadLzma()
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Compressed Image (*.img.lzma)|*.img.lzma";
        if (saveFileDialog.ShowDialog() == true)
        {
            var disk = lstDrives.SelectedItem as DiskListItem;
            if (disk == null)
            {
                return;
            }

            var path = saveFileDialog.FileName;
            var imgFileName = Path.GetFileNameWithoutExtension(path);
            CurrentSizeToRead = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRead = GetAllocatedSize(CurrentDisk);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRead = 0;

            // LZMA part
            
            //CurrentSizeToRead = 1024 * 1024 * 100; // FOR TESTING
            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            IoTask = Task.Run(() => { LzmaStreamer.Compress(CurrentArchiveStream, new RawDiskStreamTruncatedRead(CurrentDiskStream, CurrentSizeToRead), CurrentSizeToRead, new CompressionProgress(this)); });

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                CurrentDiskStream.Close();
                CurrentDiskStream = null;
                CurrentDisk = null;

                CurrentArchiveStream.Close();
                CurrentArchiveStream.Dispose();
                CurrentArchiveStream = null;

                CurrentSizeToRead = 0;
                CurrentOperation = ImagingOperations.None;
                progIo.Dispatcher.Invoke(() =>
                {
                    btnRead.IsEnabled = true;
                    btnWrite.IsEnabled = true;
                    progIo.Value = 0;
                });
            });

        }
    }

    public class RawDiskStreamTruncatedRead : Stream
    {
        protected RawDiskStream DiskStream;
        protected long MaxLen;

        public RawDiskStreamTruncatedRead(RawDiskStream diskStream, long maxLen)
        {
            DiskStream = diskStream;
            MaxLen = maxLen;
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if(_position + count >= MaxLen)
            {
                count = (int)(MaxLen - _position);
            }

            if(count <= 0)
            {
                return 0;
            }

            var actual = DiskStream.Read(buffer, offset, count);
            if(actual > count)
            {
                var diff = count - actual;
                DiskStream.Seek(diff, SeekOrigin.Current);
                _position += count;
                return count;
            }

            _position += actual;
            return actual;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return DiskStream.Seek(offset, origin);
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

    private void btnRead_Click(object sender, RoutedEventArgs e)
    {
        if(CurrentOperation != ImagingOperations.None)
        {
            return;
        }

        switch (CurrentFormat)
        {
            case ImagingFormats.Zip:
                ReadZip();
                break;
            case ImagingFormats.Lzma:
                ReadLzma();
                break;
            default:
                break;
        }
    }

    class PartitionInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public uint FirstCylinder { get; set; }
        public uint FirstHead { get; set; }
        public uint FirstSector { get; set; }
        public uint LastCylinder { get; set; }
        public uint LastHead { get; set; }
        public uint LastSector { get; set; }
        public long Size { get; set; }
        public long Offset { get; set; }
    }

    public long GetAllocatedSize(RawDisk disk)
    {
        var mbr = CurrentDisk.ReadSectors(0, 1);
        var parts = new List<PartitionInfo>();
        for(int i=0; i < 4; i++)
        {
            var offset = 446 + i * 16;
            var type = mbr[offset + 4];
            if(type == 0)
            {
                continue;
            }

            var chsFirst = BitConverter.ToUInt32(mbr, offset + 1);
            var chsLast = BitConverter.ToUInt32(mbr, offset + 5);

            var partition = new PartitionInfo
            {
                Name = $"Partition {i}",
                Type = type.ToString(),
                FirstCylinder = (chsFirst & 0x00FF00 << 2) | (chsFirst >> 16) & 0xFF,
                FirstHead = (chsFirst) & 0xFF,
                FirstSector = (chsFirst >> 8) & 0x3F,
                LastCylinder = (chsLast & 0x00FF00 << 2) | (chsLast >> 16) & 0xFF,
                LastHead = (chsLast) & 0xFF,
                LastSector = (chsLast >> 8) & 0x3F,
                Offset = BitConverter.ToUInt32(mbr, offset + 8),
                Size = BitConverter.ToUInt32(mbr, offset + 12)
            };

            parts.Add(partition);
        }

        var max = parts.Max(p => p.Offset + p.Size);
        return max * 512;
    }

    private void btnQuery_Click(object sender, RoutedEventArgs e)
    {
        if(CurrentOperation != ImagingOperations.None)
        {
            return;
        }


            var disk = lstDrives.SelectedItem as DiskListItem;
            if (disk == null)
            {
                return;
            }

            CurrentSizeToRead = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            var mbr = CurrentDisk.ReadSectors(0, 1);
            var parts = new List<PartitionInfo>();
            for(int i=0; i < 4; i++)
            {
                var offset = 446 + i * 16;
                var type = mbr[offset + 4];
                if(type == 0)
                {
                    continue;
                }

                var chsFirst = BitConverter.ToUInt32(mbr, offset + 1);
                var chsLast = BitConverter.ToUInt32(mbr, offset + 5);

                var partition = new PartitionInfo
                {
                    Name = $"Partition {i}",
                    Type = type.ToString(),
                    FirstCylinder = (chsFirst & 0x00FF00 << 2) | (chsFirst >> 16) & 0xFF,
                    FirstHead = (chsFirst) & 0xFF,
                    FirstSector = (chsFirst >> 8) & 0x3F,
                    LastCylinder = (chsLast & 0x00FF00 << 2) | (chsLast >> 16) & 0xFF,
                    LastHead = (chsLast) & 0xFF,
                    LastSector = (chsLast >> 8) & 0x3F,
                    Offset = BitConverter.ToUInt32(mbr, offset + 8),
                    Size = BitConverter.ToUInt32(mbr, offset + 12)
                };
                parts.Add(partition);

                Console.WriteLine(partition);
            }

            var last = parts.Last();

            var s1 = CurrentDisk.ReadSectors(last.Offset + last.Size - 1, 1);
            var s2 = CurrentDisk.ReadSectors(last.Offset + last.Size, 1);
            var s3 = CurrentDisk.ReadSectors(last.Offset + last.Size + 1, 1);

            var allocSizeMB = ((last.Offset + last.Size) * 512) / 1024.0 / 1024.0;

            CurrentDiskStream.Close();
            CurrentDiskStream = null;
            CurrentDisk = null;
        
    }

    private void btnWrite_Click(object sender, RoutedEventArgs e)
    {
    }

}