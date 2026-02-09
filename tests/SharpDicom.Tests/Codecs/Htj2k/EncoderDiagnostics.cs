using System;
using System.IO;
using System.Text;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs;
using NUnit.Framework;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    /// <summary>
    /// Diagnostic tests to compare our encoder output with OpenJPH byte-by-byte.
    /// </summary>
    [TestFixture]
    public class EncoderDiagnostics
    {
        [Test]
        public void DumpMinimalEncodeOutput()
        {
            // Create minimal test case: 8×8 image with simple pattern
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var pixelData = new byte[64];

            // Simple pattern: first 4 pixels = 1, rest = 0
            pixelData[0] = 1;
            pixelData[1] = 1;
            pixelData[2] = 1;
            pixelData[3] = 1;

            var fragments = codec.Encode(pixelData, info);
            var encoded = fragments.Fragments[0].ToArray();

            // Dump to file for comparison
            var dumpPath = "/tmp/sharpdicom_htj2k_8x8_minimal.j2c";
            File.WriteAllBytes(dumpPath, encoded);

            // Also dump hex
            var hexDump = new StringBuilder();
            hexDump.AppendLine("SharpDicom HTJ2K Encoder Output (8×8, 4 pixels=1, rest=0)");
            hexDump.AppendLine($"Total bytes: {encoded.Length}");
            hexDump.AppendLine();

            for (int i = 0; i < encoded.Length; i += 16)
            {
                hexDump.Append($"{i:X4}: ");
                for (int j = 0; j < 16 && i + j < encoded.Length; j++)
                {
                    hexDump.Append($"{encoded[i + j]:X2} ");
                }
                hexDump.AppendLine();
            }

            var hexPath = "/tmp/sharpdicom_htj2k_8x8_minimal.hex";
            File.WriteAllText(hexPath, hexDump.ToString());

            TestContext.WriteLine($"Binary output: {dumpPath}");
            TestContext.WriteLine($"Hex dump: {hexPath}");
            TestContext.WriteLine($"Total size: {encoded.Length} bytes");
            TestContext.WriteLine();
            TestContext.WriteLine("First 64 bytes:");
            for (int i = 0; i < Math.Min(64, encoded.Length); i += 16)
            {
                var line = new StringBuilder($"{i:X4}: ");
                for (int j = 0; j < 16 && i + j < encoded.Length; j++)
                {
                    line.Append($"{encoded[i + j]:X2} ");
                }
                TestContext.WriteLine(line.ToString());
            }

            TestContext.WriteLine();
            TestContext.WriteLine("To compare with OpenJPH:");
            TestContext.WriteLine($"1. Create 8×8 PGM: echo 'P5\\n8 8\\n255' > /tmp/test.pgm; " +
                                "python3 -c \"import sys; sys.stdout.buffer.write(bytes([1,1,1,1] + [0]*60))\" >> /tmp/test.pgm");
            TestContext.WriteLine($"2. Encode: ojph_compress -i /tmp/test.pgm -o /tmp/openjph_8x8.j2c -reversible true");
            TestContext.WriteLine($"3. Compare: diff <(xxd /tmp/sharpdicom_htj2k_8x8_minimal.j2c) <(xxd /tmp/openjph_8x8.j2c)");
        }

        [Test]
        public void CompareCodeBlockData()
        {
            // Focus on a single codeblock to isolate the problem
            TestContext.WriteLine("Testing HT Cleanup pass encoding directly");

            // Create minimal coefficient data for 1 codeblock (64×64 is typical size)
            int width = 64;
            int height = 64;
            var coefficients = new int[width * height];

            // Set first quad (2×2) to have one significant value
            coefficients[0] = 5;  // Top-left = 5
            // All others = 0

            var cleanupData = SharpDicom.Codecs.Jpeg2000.Tier1.HtCleanup.Encode(
                coefficients, width, height, subbandType: 0);

            TestContext.WriteLine($"Cleanup pass output: {cleanupData.Length} bytes");
            TestContext.WriteLine("First 32 bytes:");
            for (int i = 0; i < Math.Min(32, cleanupData.Length); i++)
            {
                TestContext.Write($"{cleanupData[i]:X2} ");
                if ((i + 1) % 16 == 0) TestContext.WriteLine();
            }
            TestContext.WriteLine();

            // Check ILW (last 2 bytes)
            if (cleanupData.Length >= 2)
            {
                byte ilwLow = cleanupData[cleanupData.Length - 2];
                byte ilwHigh = cleanupData[cleanupData.Length - 1];
                int scup = (ilwHigh << 4) | (ilwLow & 0x0F);
                TestContext.WriteLine($"ILW: last 2 bytes = {ilwLow:X2} {ilwHigh:X2}");
                TestContext.WriteLine($"Decoded scup (MEL+VLC length) = {scup}");
                TestContext.WriteLine($"MagSgn length = {cleanupData.Length - scup}");
            }
        }
    }
}
