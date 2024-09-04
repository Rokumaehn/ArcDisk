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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Management.Infrastructure;
using Microsoft.Win32;

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
        if(CurrentOperation == ImagingOperations.None || CurrentDiskStream == null || CurrentSizeToRead == 0)
        {
            return;
        }

        seconds++;

        var cur = CurrentDiskStream.Position;

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

    public enum ImagingOperations
    {
        None,
        Read,
        Write
    }


    private void btnRead_Click(object sender, RoutedEventArgs e)
    {
        if(CurrentOperation != ImagingOperations.None)
        {
            return;
        }

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
            btnRead.IsEnabled = false;
            btnWrite.IsEnabled = false;

            // Option 1: Using dotntet ZipArchive
            
            CurrentArchiveStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            CurrentArchive = new ZipArchive(CurrentArchiveStream, ZipArchiveMode.Create, true);
            CurrentArchiveEntry = CurrentArchive.CreateEntry(imgFileName, CompressionLevel.SmallestSize);
            CurrentArchiveEntryStream = CurrentArchiveEntry.Open();
            IoTask = CurrentDiskStream.CopyToAsync(CurrentArchiveEntryStream);

            CurrentOperation = ImagingOperations.Read;

            IoTask.ContinueWith((t) =>
            {
                CurrentArchiveEntryStream.Close();
                CurrentArchiveStream.Close();
                CurrentDiskStream.Close();

                CurrentArchiveEntryStream = null;
                CurrentArchiveEntry = null;
                CurrentArchiveStream = null;
                CurrentArchive = null;
                CurrentDiskStream = null;
                CurrentDisk = null;

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

    private void btnWrite_Click(object sender, RoutedEventArgs e)
    {
    }
}