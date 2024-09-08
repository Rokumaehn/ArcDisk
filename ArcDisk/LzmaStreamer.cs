using System;
using System.IO;
using SevenZip;

namespace ArcDisk;

public static class LzmaStreamer
{
    public static SevenZip.Compression.LZMA.Encoder encoder;

    public static void Compress(Stream output, Stream input, long inputLength, ICodeProgress progress, bool writeProperties = true)
    {
        encoder = new SevenZip.Compression.LZMA.Encoder();

        // Write the encoder properties
        encoder.SetCoderProperties(new CoderPropID[] {
            CoderPropID.DictionarySize,
        }, new object[] {
            1024 * 1024 * 64,
        });

        if(writeProperties)
        {
            encoder.WriteCoderProperties(output);
            
            // Write the decompressed file size.
            for (int i = 0; i < 8; i++)
            {
                output.WriteByte((byte)(input.Length >> (8 * i)));
            }
        }

        // do the magic
        encoder.Code(input, output, inputLength, -1, progress);
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