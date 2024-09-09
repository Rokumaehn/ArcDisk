# ArcDisk
ArcDisk is a disk imaging/backup utility for removable media (SD-Cards) written in C#. It can create images in zip/7z/lzma formats and write those images back.
The main advantage is that it compresses/decompresses on the fly, so there is usually much less disk space required in comparison to reading a raw image first. It is also possible to read only the allocated part of a disk which further reduces the image size and time to read while also making it possible to write images across sd cards with different sizes.
## Limitations
* To ensure the images integrity, the volumes on the disk to be read should not have any drive letters assigned, so the OS does not write to any volume while the image is being created. This can be ensured manually via diskmgmt.msc
* For the 7z-method, the archive should not be modified after the read if it should be written back, because the 7z-parser is quite simple and only accepts a specific format
* To write to a disk, it is currently necessary to delete all partitions on the disk manually first, for example via diskmgmt.msc or diskpart
## Notes
* The output file of the zip method is compatible with other disk imaging utilities such as Raspberry PI Imager and Etcher.
* The 7z method (and lzma-method) has the highest compression ratio but is VERY slow.
## Credits
* LZMA-SDK (www.7-zip.org)
* RawDiskLib (https://github.com/LordMike/RawDiskLib)
