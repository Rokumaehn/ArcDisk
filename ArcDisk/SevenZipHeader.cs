using System.Text;

public class SevenZipHeader
{
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
        //header.Add(0xFF); // Begin 8-byte 'NUMBER' for SizesOfPackedStreams
        header.AddRange(BitConverter.GetBytes(((UInt64)packedSize << 8) | 0x00000000000000FEUL)); // 1-Entry
        header.Add(0x0A); // CRC-PropertyID
        header.Add(0x01); // all defined
        header.AddRange(BitConverter.GetBytes((UInt32)streamCrc)); // 1-Entry

        //header.Add(0x00); // End Size-PropertyID ?????????

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
        //header.Add(0xFF); // Begin 8-byte 'NUMBER' for UnpackSize
        header.AddRange(BitConverter.GetBytes(((UInt64)unpackedSize << 8) | 0x00000000000000FEUL)); // 1-Entry
        header.Add(0x00); // END PropertyID - UnPackInfo
        
        //header.Add(0x00); // End Size-PropertyID ?????????
        
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