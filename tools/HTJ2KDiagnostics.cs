// Quick diagnostic tool to dump HTJ2K encoder output
using System;
using System.IO;
using System.Globalization;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs;

class HTJ2KDiagnostics
{
    static void Main()
    {
        Console.WriteLine("SharpDicom HTJ2K Encoder Diagnostics");
        Console.WriteLine("=====================================\n");

        // Test 1: Minimal 8×8 image
        DumpMinimal8x8();

        // Test 2: Single codeblock
        DumpSingleCodeblock();
    }

    static void DumpMinimal8x8()
    {
        Console.WriteLine("Test 1: Minimal 8×8 image (4 pixels=1, rest=0)");

        var codec = new Htj2kLosslessCodec();
        var info = PixelDataInfo.Grayscale8(8, 8);
        var pixelData = new byte[64];
        pixelData[0] = 1;
        pixelData[1] = 1;
        pixelData[2] = 1;
        pixelData[3] = 1;

        var fragments = codec.Encode(pixelData, info);
        var encoded = fragments.Fragments[0].ToArray();

        var path = "/tmp/sharpdicom_htj2k_8x8.j2c";
        File.WriteAllBytes(path, encoded);
        Console.WriteLine($"Written to: {path}");
        Console.WriteLine($"Size: {encoded.Length} bytes\n");

        Console.WriteLine("First 128 bytes (hex):");
        DumpHex(encoded, 0, Math.Min(128, encoded.Length));

        Console.WriteLine("\nTo compare with OpenJPH:");
        Console.WriteLine("  # Create test PGM");
        Console.WriteLine("  printf 'P5\\n8 8\\n255\\n' > /tmp/test.pgm");
        Console.WriteLine("  printf '\\x01\\x01\\x01\\x01' >> /tmp/test.pgm; dd if=/dev/zero bs=1 count=60 >> /tmp/test.pgm 2>/dev/null");
        Console.WriteLine("  # Encode with OpenJPH");
        Console.WriteLine("  ojph_compress -i /tmp/test.pgm -o /tmp/openjph_8x8.j2c -reversible true");
        Console.WriteLine("  # Compare");
        Console.WriteLine("  diff <(xxd /tmp/sharpdicom_htj2k_8x8.j2c) <(xxd /tmp/openjph_8x8.j2c) | head -40");
        Console.WriteLine();
    }

    static void DumpSingleCodeblock()
    {
        Console.WriteLine("Test 2: Single 64×64 codeblock (first pixel=5, rest=0)");

        int width = 64;
        int height = 64;
        var coefficients = new int[width * height];
        coefficients[0] = 5;

        var cleanupData = SharpDicom.Codecs.Jpeg2000.Tier1.HtCleanup.Encode(
            coefficients, width, height, subbandType: 0);

        Console.WriteLine($"Cleanup pass output: {cleanupData.Length} bytes\n");

        Console.WriteLine("First 64 bytes:");
        DumpHex(cleanupData, 0, Math.Min(64, cleanupData.Length));

        if (cleanupData.Length >= 2)
        {
            byte ilwLow = cleanupData[cleanupData.Length - 2];
            byte ilwHigh = cleanupData[cleanupData.Length - 1];
            int scup = (ilwHigh << 4) | (ilwLow & 0x0F);
            Console.WriteLine($"\nILW (last 2 bytes): {ilwLow:X2} {ilwHigh:X2}");
            Console.WriteLine($"Decoded scup (MEL+VLC length): {scup}");
            Console.WriteLine($"MagSgn length: {cleanupData.Length - scup}");
        }
        Console.WriteLine();
    }

    static void DumpHex(byte[] data, int start, int length)
    {
        for (int i = start; i < start + length && i < data.Length; i += 16)
        {
            Console.Write($"{i:X4}: ");
            for (int j = 0; j < 16 && i + j < data.Length && i + j < start + length; j++)
            {
                Console.Write($"{data[i + j]:X2} ");
            }
            Console.WriteLine();
        }
    }
}
