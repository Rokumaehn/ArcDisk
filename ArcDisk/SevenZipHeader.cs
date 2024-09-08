using System.IO;
using System.Text;

public class SevenZipHeader
{
    public bool IsValid { get; set; }
    public UInt32 StartHeaderCrc { get; set; }
    public UInt64 NextHeaderOffset { get; set; }
    public UInt64 NextHeaderSize { get; set; }
    public UInt32 NextHeaderCrc { get; set; }

    public UInt64 PackedSize { get; set; }
    public UInt32 StreamCrc { get; set; }
    public byte[] EncoderProperties { get; set; }
    public UInt64 UnpackedSize { get; set; }

    public SevenZipHeader(FileStream fs)
    {
        IsValid = false;
        
        var abStartHeader = new byte[32];
        fs.Read(abStartHeader, 0, abStartHeader.Length);
        StartHeaderCrc = BitConverter.ToUInt32(abStartHeader, 8);
        NextHeaderOffset = BitConverter.ToUInt64(abStartHeader, 12);
        NextHeaderSize = BitConverter.ToUInt64(abStartHeader, 20);
        NextHeaderCrc = BitConverter.ToUInt32(abStartHeader, 28);

        var abHeader = new byte[NextHeaderSize];
        fs.Seek((long)NextHeaderOffset, SeekOrigin.Current);
        fs.Read(abHeader, 0, abHeader.Length);

        if(!abHeader.AsSpan(0, 7).SequenceEqual(new byte[] { 0x01, 0x04, 0x06, 0x00, 0x01, 0x09, 0xFF }))
        {
            return;
        }
        PackedSize = BitConverter.ToUInt64(abHeader, 7);
        if(PackedSize != NextHeaderOffset)
        {
            return;
        }
        if(!abHeader.AsSpan(15, 2).SequenceEqual(new byte[] { 0x0A, 0x01 }))
        {
            return;
        }
        StreamCrc = BitConverter.ToUInt32(abHeader, 17);
        if(!abHeader.AsSpan(21, 10).SequenceEqual(new byte[] { 0x00, 0x07, 0x0B, 0x01, 0x00, 0x01, 0x23, 0x03, 0x01, 0x01 }))
        {
            return;
        }
        EncoderProperties = new byte[abHeader[31]];
        abHeader.AsSpan(32, abHeader[31]).CopyTo(EncoderProperties);
        var offset = 32 + abHeader[31];
        if(!abHeader.AsSpan(offset, 2).SequenceEqual(new byte[] { 0x0C, 0xFF }))
        {
            return;
        }
        UnpackedSize = BitConverter.ToUInt64(abHeader, offset + 2);

        IsValid = true;
    }

    public static byte[] GetHeader(string fileName, UInt64 packedSize, UInt64 unpackedSize, byte[] encoderProperties, DateTime fileMTime, UInt32 streamCrc)
    {
        List<byte> header = new List<byte>();
        var dtRef = new DateTime(1601, 01, 01, 0, 0, 0, DateTimeKind.Utc);

        header.Add(0x01); // Header
        header.Add(0x04); // MainStreamsInfo-PropertyID
        header.Add(0x06); // PackInfo-PropertyID
        header.Add(0x00); // Pack Position as 'NUMBER'
        header.Add(0x01); // Count of Streams as 'NUMBER'
        header.Add(0x09); // Size-PropertyID
        header.Add(0xFF); // Begin 8-byte 'NUMBER' for SizesOfPackedStreams
        header.AddRange(BitConverter.GetBytes((UInt64)packedSize)); // 1-Entry
        header.Add(0x0A); // CRC-PropertyID
        header.Add(0x01); // all defined
        header.AddRange(BitConverter.GetBytes((UInt32)streamCrc)); // 1-Entry
        header.Add(0x00); // End PackInfo
        header.Add(0x07); // UnPackInfo-PropertyID
        header.Add(0x0B); // Folder-PropertyID
        header.Add(0x01); // NUMBER of Folders
        header.Add(0x00); // not extended ???
        header.Add(0x01); // NUMBER of Coders ???
        header.Add(0x23); // Beginning of CoderProperty. Flag BYTE = 0x23 meaning: Bit[5]=1=Attributes, Bit[1:0]=3=SizeOfCoderID
        header.Add(0x03); // CoderID BYTE-Array(not little endian val!) BYTE0 : LZMA
        header.Add(0x01); // CoderID BYTE-Array(not little endian val!) BYTE1 : LZMA
        header.Add(0x01); // CoderID BYTE-Array(not little endian val!) BYTE2 : LZMA
        header.Add((byte)(encoderProperties.Length)); // PropertySize for LZMA attributes
        header.AddRange(encoderProperties); // LZMA attributes
        header.Add(0x0C); // CodersUnPackSize-PropertyID
        header.Add(0xFF); // Begin 8-byte 'NUMBER' for UnpackSize
        header.AddRange(BitConverter.GetBytes((UInt64)unpackedSize)); // 1-Entry
        header.Add(0x00); // END PropertyID - UnPackInfo
        // header.Add(0x08); // SubStreamInfo-PropertyID
        // header.Add(0x0A); // CRC-PropertyID
        // header.Add(0x01); // CRC-Property Size?
        // header.AddRange([0x1F, 0xC1, 0x2E, 0xB9]); // CRC
        // header.Add(0x00); // END PropertyID - SubStreamInfo
        header.Add(0x00); // END PropertyID - MainStreamsInfo
        header.Add(0x05); // FileInfo-PropertyID
        header.Add(0x01); // NUMBER of Files
        header.Add(0x11); // Name-PropertyID
        var encodedFileName = Encoding.Unicode.GetBytes(fileName);
        header.Add((byte)(encodedFileName.Length + 2 + 1));
        header.Add(0x00); // external flag = 0, FileName follows
        header.AddRange(encodedFileName); // FileName
        header.AddRange([0x00, 0x00]); // NULL-Char

        header.Add(0x14); // MTime-PropertyID
        header.Add(0x0A); // NUMBER for size of MTime
        header.Add(0x01); // Time exists
        header.Add(0x00); // external flag = 0, time follows
        var mTime = (UInt64)((fileMTime.ToUniversalTime() - dtRef).TotalNanoseconds / 100.0);
        //header.Add(0xFF); // Begin 8-byte 'NUMBER'
        header.AddRange(BitConverter.GetBytes(mTime)); // 1-Entry

        header.Add(0x15); // Attribute-PropertyID
        header.Add(0x06); // NUMBER size attributes
        header.AddRange([0x01, 0x00, 0x20, 0x00, 0x00, 0x00]); // test attribs

        header.Add(0x00); // END PropertyID - FileInfo
        header.Add(0x00); // END PropertyID - Header

        return header.ToArray();
    }
}