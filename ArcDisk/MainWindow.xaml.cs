using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32;
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
            var bytesPerSectorProperty = instance.CimInstanceProperties["BytesPerSector"];
            UInt32 bytesPerSector = (UInt32)bytesPerSectorProperty.Value;
            var partitionsProperty = instance.CimInstanceProperties["Partitions"];
            UInt32 partitions = (UInt32)partitionsProperty.Value;

            if(deviceId==null || size==0)
            {
                continue;
            }

            Disks.Add(new DiskListItem
            {
                Caption = caption ?? "<N/A>",
                Model = model ?? "<N/A>",
                DeviceId = deviceId,
                Size = (long)size,
                BytesPerSector = (int)bytesPerSector,
                Partitions = (int)partitions
            });
        }

        
    }

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

            var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Read, disk.Partitions);
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(phys);
            }
            var st = new WrappedPhysicalDiskStream(phys, CurrentSizeToRW);

            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;
            
            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Create, true);
            CurrentArchiveEntry = CurrentArchive.CreateEntry(imgFileName, CompressionLevel.SmallestSize);
            CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
            IoTask = CopyBytesAsync(CurrentSizeToRW, st, CurrentArchiveEntryStream);

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                st.Close();
                st.Dispose();
                phys.Close();
                phys.Dispose();

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

            var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Read, disk.Partitions);
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(phys);
            }
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;

            // LZMA part
            
            var st = new WrappedPhysicalDiskStream(phys, CurrentSizeToRW);
            // // TESTING
            // var test = 1024 * 1024 * 100;
            // var st = new WrappedPhysicalDiskStream(phys, CurrentSizeToRW - test, test);
            // CurrentSizeToRW = test;
            // // END TESTING

            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            IoTask = Task.Run(() => { LzmaStreamer.Compress(CurrentArchiveStream, st, CurrentSizeToRW, new CompressionProgress(this)); });

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                st.Close();
                st.Dispose();
                phys.Close();
                phys.Dispose();

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

            var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Read, disk.Partitions);
            if(chkAllocd.IsChecked == true)
            {
                CurrentSizeToRW = GetAllocatedSize(phys);
            }

            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;
            lastBytesRW = 0;

            // LZMA part
            
            var st = new WrappedPhysicalDiskStream(phys, CurrentSizeToRW);
            // // TESTING
            // var test = 1024 * 1024 * 100;
            // var st = new WrappedPhysicalDiskStream(phys, CurrentSizeToRead - test, test);
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
                st.Close();
                st.Dispose();
                phys.Close();
                phys.Dispose();

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

    public long GetAllocatedSize(PhysicalDiskStream disk)
    {
        var mbr = new byte[512];
        disk.ReadSector(mbr);
        disk.Seek(0, SeekOrigin.Begin);
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

    PhysicalDiskStream pds = null;

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

        btnQuery.IsEnabled = false;
        btnRead.IsEnabled = false;
        btnWrite.IsEnabled = false;
        cmbFormat.IsEnabled = false;



        pds = new PhysicalDiskStream(disk.PhysicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Write, disk.Partitions);
        pds.DeletePartitionTable();


        // var phys = new PhysicalDiskStream(disk.PhysicalIndex, disk.BytesPerSector, disk.Size, FileAccess.ReadWrite, disk.Partitions);

        // var empty = new byte[512];
        // for(int i=0; i < 512; i++)
        // {
        //     empty[i] = 0;
        // }
        // phys.Write(empty, 0, 512);
        // phys.Flush();
        // phys.Close();






        //var phys = new PhysicalDiskStream(disk.PhysicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Read, disk.Partitions);
        //var file = new FileStream(@"C:\amiga\backups\Emu68-32GB-Kioxia.img", FileMode.Open, FileAccess.Read);
        
        // var c0 = 0;
        // var c1 = 0;
        // var buffer = new byte[1024 * 1024 * 64];
        // var comp = new byte[1024 * 1024 * 64];
        // c0 = phys.Read(buffer, 0, buffer.Length);
        // c1 = file.Read(comp, 0, comp.Length);
        // int iBlockOffset = 0;
        // while(c0 > 0 && c1 > 0)
        // {
        //     if(c0 != c1)
        //     {
        //         Console.WriteLine("Length Mismatch.");
        //         break;
        //     }

        //     for(int i=0; i < c0; i++)
        //     {
        //         if(buffer[i] != comp[i])
        //         {
        //             Console.WriteLine("Mismatch Block {0} Offset in Block {1} Offset in File{2}.", iBlockOffset, i, iBlockOffset * 1024 * 1024 * 64 + i);
        //             break;
        //         }
        //     }

        //     if(c0 < 1024 * 1024 * 64)
        //     {
        //         break;
        //     }

        //     c0 = phys.Read(buffer, 0, buffer.Length);
        //     c1 = file.Read(comp, 0, comp.Length);
        //     iBlockOffset++;
        // }

        // file.Close();
        // file.Dispose();
        // phys.Close();
        // phys.Dispose();

        btnQuery.IsEnabled = true;
        btnRead.IsEnabled = true;
        btnWrite.IsEnabled = true;
        cmbFormat.IsEnabled = true;

        // CurrentSizeToRW = disk.Size;
        // var physicalIndex = disk.PhysicalIndex;

        // CurrentDisk = new RawDiskLib.RawDisk(RawDiskLib.DiskNumberType.PhysicalDisk, physicalIndex, System.IO.FileAccess.Read);
        // CurrentDiskStream = CurrentDisk.CreateDiskStream();
        // var mbr = CurrentDisk.ReadSectors(0, 1);
        // var parts = new List<PartitionInfo>();
        // for(int i=0; i < 4; i++)
        // {
        //     var offset = 446 + i * 16;
        //     var type = mbr[offset + 4];
        //     if(type == 0)
        //     {
        //         continue;
        //     }

        //     var chsFirst = BitConverter.ToUInt32(mbr, offset + 1);
        //     var chsLast = BitConverter.ToUInt32(mbr, offset + 5);

        //     var partition = new PartitionInfo
        //     {
        //         Name = $"Partition {i}",
        //         Type = type.ToString(),
        //         FirstCylinder = (chsFirst & 0x00FF00 << 2) | (chsFirst >> 16) & 0xFF,
        //         FirstHead = (chsFirst) & 0xFF,
        //         FirstSector = (chsFirst >> 8) & 0x3F,
        //         LastCylinder = (chsLast & 0x00FF00 << 2) | (chsLast >> 16) & 0xFF,
        //         LastHead = (chsLast) & 0xFF,
        //         LastSector = (chsLast >> 8) & 0x3F,
        //         Offset = BitConverter.ToUInt32(mbr, offset + 8),
        //         Size = BitConverter.ToUInt32(mbr, offset + 12)
        //     };
        //     parts.Add(partition);

        //     Console.WriteLine(partition);
        // }

        // var last = parts.Last();

        // var s1 = CurrentDisk.ReadSectors(last.Offset + last.Size - 1, 1);
        // var s2 = CurrentDisk.ReadSectors(last.Offset + last.Size, 1);
        // var s3 = CurrentDisk.ReadSectors(last.Offset + last.Size + 1, 1);

        // var allocSizeMB = ((last.Offset + last.Size) * 512) / 1024.0 / 1024.0;

        // CurrentDiskStream.Close();
        // CurrentDiskStream = null;
        // CurrentDisk = null;
        
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

        var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Write, disk.Partitions);

        btnRead.IsEnabled = false;
        btnWrite.IsEnabled = false;
        cmbFormat.IsEnabled = false;
        lastBytesRW = 0;

        CurrentArchiveStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Read, true);
        CurrentArchiveEntry = CurrentArchive.GetEntry(imgFileName);
        CurrentSizeToRW = CurrentArchiveEntry.Length;
        CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
        IoTask = CopyBytesAsync(CurrentSizeToRW, CurrentArchiveEntryStream, phys);

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            phys.Close();
            phys.Dispose();

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

        var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Write, disk.Partitions);

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
        IoTask = Task.Run(() => { LzmaStreamer.Decompress(phys, CurrentArchiveStream, CurrentSizeToRW, new DecompressionProgress(this)); });

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            phys.Flush();
            phys.Close();
            phys.Dispose();

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

        var phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Write, disk.Partitions);
        // phys.DeletePartitionTable();
        // phys.Close();
        // phys = new PhysicalDiskStream(physicalIndex, disk.BytesPerSector, disk.Size, FileAccess.Write, 0);

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
            phys.Close();
            phys.Dispose();
            phys = null;
            btnRead.IsEnabled = true;
            btnWrite.IsEnabled = true;
            cmbFormat.IsEnabled = true;
            MessageBox.Show("Could not parse 7z archive", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        CurrentArchiveStream = new WindowedFileStream(path, FileMode.Open, FileAccess.Read, 32, (long)(header.PackedSize));
        CurrentSizeToRW = (long)(header.UnpackedSize);
        CurrentArchiveStream.Seek(32, SeekOrigin.Begin);
        IoTask = Task.Run(() => { LzmaStreamer.Decompress(phys, CurrentArchiveStream, disk.Size, new DecompressionProgress(this), header.EncoderProperties, (long)(header.UnpackedSize)); });

        CurrentOperation = ImagingOperations.Write;

        IoTask.ContinueWith((t) =>
        {
            phys.Flush();
            phys.Close();
            phys.Dispose();

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