public class DiskListItem
{
    public string Caption { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public long Size { get; set; } = 0;
    public string DisplaySize => (Size / 1024 / 1024 / 1024).ToString() + " GB";
    public int PhysicalIndex => (DeviceId.StartsWith("\\\\.\\PHYSICALDRIVE")) ? int.Parse(DeviceId.Substring(17)) : -1;
}