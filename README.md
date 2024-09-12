# ArcDisk
ArcDisk is a disk imaging/backup utility for removable media (ex. SD-Cards) written in C#. It can create images in zip/7z/lzma formats and write those images back.
The main advantage is that it compresses/decompresses on the fly, so there is usually much less disk space required in comparison to reading a raw image first. It is also possible to read only the allocated part of a disk which further reduces the image size and time to read while also making it possible to write images across sd cards with different sizes.
## Limitations
* For the 7z-method, the archive should not be modified after the read, if it should be written back, because the 7z-parser is quite simple and only accepts a specific format
## Notes
* Currently disk locking is used for both read and write. It should work but if it doesn't, please open an issue where you name your media and card reader. I might also add an option in the future to optionally skip disk locking for reading the drive.
* The output file of the zip method is compatible with other disk imaging utilities such as Raspberry PI Imager and Etcher.
* The 7z method (and lzma-method) has the highest compression ratio but is VERY slow. I plan on making the dictionary size selectable but for now it is 64MB which is the same as Ultra in 7-zip.
## Credits
* LZMA-SDK (www.7-zip.org)
