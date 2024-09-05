using System;
using System.IO;
using SevenZip;

namespace ArcDisk;

public static class LzmaStreamer
{
    public static void Compress(Stream output, Stream input, long inputLength, ICodeProgress progress)
    {
        SevenZip.Compression.LZMA.Encoder coder = new SevenZip.Compression.LZMA.Encoder();

        // Write the encoder properties
        coder.SetCoderProperties(new CoderPropID[] {
            CoderPropID.DictionarySize,
        }, new object[] {
            1024 * 1024 * 64,
        });
        coder.WriteCoderProperties(output);
        
        // Write the decompressed file size.
        for (int i = 0; i < 8; i++)
        {
            output.WriteByte((byte)(input.Length >> (8 * i)));
        }

        // do the magic
        coder.Code(input, output, inputLength, -1, progress);
    }

    public static bool Decompress(Stream output, Stream input, long outputLength, ICodeProgress progress)
    {
        SevenZip.Compression.LZMA.Decoder coder = new SevenZip.Compression.LZMA.Decoder();

        // Read the decoder properties
        byte[] properties = new byte[5];
        input.Read(properties, 0, 5);

        // Read in the decompressed file size.
        byte[] fileLengthBytes = new byte[8];
        input.Read(fileLengthBytes, 0, 8);
        long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

        if(outputLength < fileLength)
        {
            return false;
        }

        // do the magic
        coder.SetDecoderProperties(properties);
        coder.Code(input, output, input.Length, fileLength, progress);

        return true;
    }
}