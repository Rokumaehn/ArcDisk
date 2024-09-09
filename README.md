# ArcDisk
ArcDisk is a disk imaging/backup utility for removable media (SD-Cards) written in C#. It can create images in zip/7z/lzma formats and write those images back.
## Limitations
* To ensure the images integrity, the volumes on the disk to be read should not have any drive letters assigned, so the OS does not write to any volume while the image is being created. This can be ensured manually via diskmgmt.msc
* For the 7z-method, the archive should not be modified after the read if it should be written back, because the 7z-parser is quite simple and only accepts a specific format
* To write to a disk, it is currently necessary to delete all partitions on the disk manually first, for example via diskmgmt.msc or diskpart
## Credits
* LZMA-SDK (www.7-zip.org)
* RawDiskLib (https://github.com/LordMike/RawDiskLib)
