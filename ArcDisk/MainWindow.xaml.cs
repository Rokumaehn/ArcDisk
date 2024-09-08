using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.Intrinsics.Arm;
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
using Microsoft.VisualBasic;
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

    private long lastBytesRW = 0;
    private long seconds = 0;

    private void ProgressTimerCallback(object? state)
    {
        if(CurrentOperation == ImagingOperations.None || CurrentSizeToRW == 0)
        {
            return;
        }

        seconds++;

        var cur = BytesCopied;

        if(seconds % 10 == 0)
        {
            var diff = cur - lastBytesRW;
            lastBytesRW = cur;

            progIo.Dispatcher.Invoke(() =>
            {
                txtProgress.Text = $"{diff/1024.0f/1024.0f/10.0:0.###} MB/s";
            });
        }

        progIo.Dispatcher.Invoke(() =>
        {
            progIo.Value = (int)(cur * 100.0 / CurrentSizeToRW);
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
    public BufferedHashingDiskStreamWriter CurrentDiskStreamWriter { get; set; }
    public FileStream CurrentArchiveStream { get; set; }
    public ZipArchive CurrentArchive { get; set; }
    public ZipArchiveEntry CurrentArchiveEntry { get; set; }
    public Stream CurrentArchiveEntryStream { get; set; }
    public long CurrentSizeToRW { get; set; }
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
        Lzma,
        SevenZip
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
            CurrentSizeToRW = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(CurrentDisk);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;

            // Option 1: Using dotntet ZipArchive
            
            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Create, true);
            CurrentArchiveEntry = CurrentArchive.CreateEntry(imgFileName, CompressionLevel.SmallestSize);
            CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
            IoTask = CopyBytesAsync(CurrentSizeToRW, CurrentDiskStream, CurrentArchiveEntryStream);

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

                CurrentSizeToRW = 0;
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
            CurrentSizeToRW = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(CurrentDisk);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;

            // LZMA part
            
            var st = new WrappedDiskStream(CurrentDiskStream, CurrentSizeToRW);
            // // TESTING
            // var test = 1024 * 1024 * 100;
            // var st = new WrappedDiskStream(CurrentDiskStream, CurrentSizeToRead - test, test);
            // CurrentSizeToRead = test;
            // // END TESTING

            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            IoTask = Task.Run(() => { LzmaStreamer.Compress(CurrentArchiveStream, st, CurrentSizeToRW, new CompressionProgress(this)); });

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                CurrentDiskStream.Close();
                CurrentDiskStream = null;
                CurrentDisk = null;

                CurrentArchiveStream.Close();
                CurrentArchiveStream.Dispose();
                CurrentArchiveStream = null;

                CurrentSizeToRW = 0;
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



    protected void Read7z()
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog();
        saveFileDialog.Filter = "Compressed Image (*.img.7z)|*.img.7z";
        if (saveFileDialog.ShowDialog() == true)
        {
            var disk = lstDrives.SelectedItem as DiskListItem;
            if (disk == null)
            {
                return;
            }

            var path = saveFileDialog.FileName;
            var imgFileName = Path.GetFileNameWithoutExtension(path);
            CurrentSizeToRW = disk.Size;
            var physicalIndex = disk.PhysicalIndex;

            CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
            CurrentDiskStream = CurrentDisk.CreateDiskStream();
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(CurrentDisk);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;

            // LZMA part
            
            var st = new WrappedDiskStream(CurrentDiskStream, CurrentSizeToRW);
            // // TESTING
            // var test = 1024 * 1024 * 100;
            // var st = new WrappedDiskStream(CurrentDiskStream, CurrentSizeToRead - test, test);
            // CurrentSizeToRead = test;
            // // END TESTING

            CurrentArchiveStream = new CrcFileStream(path, FileMode.Create, FileAccess.Write);
            CurrentArchiveStream.Write(Encoding.ASCII.GetBytes("7z"), 0, 2);
            CurrentArchiveStream.Write([0xbc, 0xaf, 0x27, 0x1c, 0, 4], 0, 6);
            CurrentArchiveStream.Write(BitConverter.GetBytes((UInt32)0), 0, 4); // StartHeader CRC
            CurrentArchiveStream.Write(BitConverter.GetBytes((UInt64)0), 0, 8); // NextHeaderOffset
            CurrentArchiveStream.Write(BitConverter.GetBytes((UInt64)0), 0, 8); // NextHeaderSize
            CurrentArchiveStream.Write(BitConverter.GetBytes((UInt32)0), 0, 4); // NextHeaderCRC
            (CurrentArchiveStream as CrcFileStream)?.ResetCrc();
            IoTask = Task.Run(() => { LzmaStreamer.Compress(CurrentArchiveStream, st, CurrentSizeToRW, new CompressionProgress(this), false); });

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                CurrentDiskStream.Close();
                CurrentDiskStream = null;
                CurrentDisk = null;

                Complete7zArchive(CurrentArchiveStream, (ulong)CurrentSizeToRW, imgFileName, DateTime.Now, (CurrentArchiveStream as CrcFileStream)?.GetCrcUint() ?? 0);

                CurrentArchiveStream.Close();
                CurrentArchiveStream.Dispose();
                CurrentArchiveStream = null;

                CurrentSizeToRW = 0;
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


    protected void Complete7zArchive(FileStream stream, ulong unpackedSize, string fileName, DateTime fileMTime, UInt32 streamCrc)
    {
        ulong packedSize = (ulong)(stream.Length) - 32UL;

        byte[] encoderProps;
        using(var ms = new MemoryStream())
        {
            LzmaStreamer.encoder.WriteCoderProperties(ms);
            ms.Flush();
            encoderProps = ms.ToArray();
        }

        var header = SevenZipHeader.GetHeader(fileName, packedSize, unpackedSize, encoderProps, fileMTime, streamCrc);
        stream.Write(header, 0, header.Length);

        System.IO.Hashing.Crc32 crc = new System.IO.Hashing.Crc32();
        crc.Append(header);
        var crcHeader = crc.GetCurrentHash();
        crc.Reset();
        List<byte> lst = new List<byte>();
        lst.AddRange(BitConverter.GetBytes((UInt64)packedSize));
        lst.AddRange(BitConverter.GetBytes((UInt64)header.Length));
        lst.AddRange(crcHeader);
        crc.Append(lst.ToArray());
        var crcStartHeader = crc.GetCurrentHash();

        stream.Seek(8, SeekOrigin.Begin);
        stream.Write(crcStartHeader, 0, 4); // StartHeader CRC
        stream.Write(BitConverter.GetBytes((UInt64)packedSize), 0, 8); // NextHeader Offset
        stream.Write(BitConverter.GetBytes((UInt64)header.Length), 0, 8); // NextHeader Size
        stream.Write(crcHeader, 0, 4); // NextHeader CRC
        stream.Flush();
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
            case ImagingFormats.SevenZip:
                Read7z();
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

            CurrentSizeToRW = disk.Size;
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

    private void WriteZip(string filename, string imgFileName)
    {
        var disk = lstDrives.SelectedItem as DiskListItem;
        if (disk == null)
        {
            return;
        }

        var path = filename;
        var physicalIndex = disk.PhysicalIndex;

        CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.ReadWrite);
        CurrentDiskStreamWriter = new BufferedHashingDiskStreamWriter(CurrentDisk.CreateDiskStream(), 1024 * 1024 * 4);
        btnRead.IsEnabled = false;
        btnWrite.IsEnabled = false;
        cmbFormat.IsEnabled = false;
        lastBytesRW = 0;

        CurrentArchiveStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Read, true);
        CurrentArchiveEntry = CurrentArchive.GetEntry(imgFileName);
        CurrentSizeToRW = CurrentArchiveEntry.Length;
        CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
        IoTask = CopyBytesAsync(CurrentSizeToRW, CurrentArchiveEntryStream, CurrentDiskStreamWriter);

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            CurrentDiskStreamWriter.Close();
            CurrentDiskStreamWriter = null;
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

            CurrentSizeToRW = 0;
            CurrentOperation = ImagingOperations.None;
            progIo.Dispatcher.Invoke(() =>
            {
                btnRead.IsEnabled = true;
                btnWrite.IsEnabled = true;
                cmbFormat.IsEnabled = true;
                progIo.Value = 0;
            });
        });
    }

    private void WriteLzma(string filename, string imgFileName)
    {
        var disk = lstDrives.SelectedItem as DiskListItem;
        if (disk == null)
        {
            return;
        }

        var path = filename;
        var physicalIndex = disk.PhysicalIndex;

        CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.ReadWrite);
        CurrentDiskStreamWriter = new BufferedHashingDiskStreamWriter(CurrentDisk.CreateDiskStream(), 1024 * 1024 * 4);
        CurrentSizeToRW = disk.Size;
        btnRead.IsEnabled = false;
        btnWrite.IsEnabled = false;
        cmbFormat.IsEnabled = false;
        lastBytesRW = 0;

        CurrentArchiveStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        CurrentArchiveStream.Seek(5, SeekOrigin.Begin);
        var buf = new byte[8];
        CurrentArchiveStream.Read(buf, 0, 8);
        CurrentSizeToRW = BitConverter.ToInt64(buf);
        CurrentArchiveStream.Seek(0, SeekOrigin.Begin);
        IoTask = Task.Run(() => { LzmaStreamer.Decompress(CurrentDiskStreamWriter, CurrentArchiveStream, CurrentSizeToRW, new DecompressionProgress(this)); });

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            CurrentDiskStreamWriter.Close();
            CurrentDiskStreamWriter = null;
            CurrentDisk = null;

            CurrentArchiveStream.Close();
            CurrentArchiveStream.Dispose();
            CurrentArchiveStream = null;

            CurrentSizeToRW = 0;
            CurrentOperation = ImagingOperations.None;
            progIo.Dispatcher.Invoke(() =>
            {
                btnRead.IsEnabled = true;
                btnWrite.IsEnabled = true;
                cmbFormat.IsEnabled = true;
                progIo.Value = 0;
            });
        });
    }

    private void Write7z(string filename, string imgFileName)
    {
        var disk = lstDrives.SelectedItem as DiskListItem;
        if (disk == null)
        {
            return;
        }

        cmbFormat.SelectedItem = ImagingFormats.SevenZip;

        var path = filename;
        var physicalIndex = disk.PhysicalIndex;

        CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.ReadWrite);
        CurrentDiskStreamWriter = new BufferedHashingDiskStreamWriter(CurrentDisk.CreateDiskStream(), 1024 * 1024 * 4);
        btnRead.IsEnabled = false;
        btnWrite.IsEnabled = false;
        cmbFormat.IsEnabled = false;
        lastBytesRW = 0;

        CurrentArchiveStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        SevenZipHeader header = new(CurrentArchiveStream);
        CurrentArchiveStream.Close();
        CurrentArchiveStream.Dispose();
        if(header.IsValid == false)
        {
            CurrentDiskStreamWriter.Close();
            CurrentDiskStreamWriter = null;
            CurrentDisk = null;
            btnRead.IsEnabled = true;
            btnWrite.IsEnabled = true;
            cmbFormat.IsEnabled = true;
            MessageBox.Show("Could not parse 7z archive", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        CurrentArchiveStream = new WindowedFileStream(path, FileMode.Open, FileAccess.Read, 32, (long)(header.PackedSize));
        CurrentSizeToRW = (long)(header.UnpackedSize);
        CurrentArchiveStream.Seek(32, SeekOrigin.Begin);
        IoTask = Task.Run(() => { LzmaStreamer.Decompress(CurrentDiskStreamWriter, CurrentArchiveStream, disk.Size, new DecompressionProgress(this), header.EncoderProperties, (long)(header.UnpackedSize)); });

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            CurrentDiskStreamWriter.Close();
            CurrentDiskStreamWriter = null;
            CurrentDisk = null;

            CurrentArchiveStream.Close();
            CurrentArchiveStream.Dispose();
            CurrentArchiveStream = null;

            CurrentSizeToRW = 0;
            CurrentOperation = ImagingOperations.None;
            progIo.Dispatcher.Invoke(() =>
            {
                btnRead.IsEnabled = true;
                btnWrite.IsEnabled = true;
                cmbFormat.IsEnabled = true;
                progIo.Value = 0;
            });
        });
    }

    private void btnWrite_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = "Image Disk Image(*.img.zip;*.img.lzma;*.img.7z)|*.img.zip;*.img.lzma;*.img.7z";
        if (openFileDialog.ShowDialog() == true)
        {
            var filename = openFileDialog.FileName;
            var extension = Path.GetExtension(filename);
            var imgFileName = Path.GetFileNameWithoutExtension(filename);

            switch (extension)
            {
                case ".zip":
                    WriteZip(filename, imgFileName);
                    break;
                case ".lzma":
                    WriteLzma(filename, imgFileName);
                    break;
                case ".7z":
                    Write7z(filename, imgFileName);
                    break;
                default:
                    break;
            }
        }
    }

}