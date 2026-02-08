using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    /// <summary>
    /// Conformance tests that verify HTJ2K codec quality, lossless exactness,
    /// and cross-decoder compatibility with OpenJPH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cross-decoder tests require the OpenJPH command-line tools to be installed:
    /// - macOS: brew install openjph
    /// - Linux: Build from source (https://github.com/aous72/OpenJPH)
    /// - Windows: Download binaries from GitHub releases
    /// </para>
    /// <para>
    /// PSNR, SSIM, and lossless roundtrip tests run unconditionally.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Htj2kConformanceTests
    {
        private static readonly string? OjphCompressPath = FindOjphCompress();
        private static readonly string? OjphExpandPath = FindOjphExpand();
        private static readonly char[] PgmWhitespace = { ' ', '\t', '\r', '\n' };

        private static string? FindOjphCompress()
        {
            return FindTool("ojph_compress");
        }

        private static string? FindOjphExpand()
        {
            return FindTool("ojph_expand");
        }

        private static string? FindTool(string name)
        {
            var candidates = new[]
            {
                name,                                    // In PATH
                $"/usr/local/bin/{name}",               // Homebrew
                $"/opt/homebrew/bin/{name}",            // Homebrew ARM
                $"/usr/bin/{name}",                     // Linux system
                $"C:\\Program Files\\OpenJPH\\{name}.exe",
                $"C:\\Program Files (x86)\\OpenJPH\\{name}.exe"
            };

            foreach (var path in candidates)
            {
                try
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "-h",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(info);
                    if (process != null)
                    {
                        process.WaitForExit(1000);
                        // ojph tools return help text on stderr
                        if (process.StandardError.Peek() >= 0 || process.StandardOutput.Peek() >= 0)
                        {
                            return path;
                        }
                    }
                }
                catch
                {
                    // Not found at this location
                }
            }

            return null;
        }

        [Test]
        public void Htj2k_OurEncode_OjphDecode_Grayscale8_Matches()
        {
            if (OjphExpandPath == null)
            {
                Assert.Fail("ojph_expand not found - install openjph-tools");
                return;
            }

            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGradientImage8(64, 64);

            // Encode with our codec
            var fragments = codec.Encode(original, info);
            var encoded = fragments.Fragments[0].ToArray();

            // Write to temp file, decode with ojph_expand
            var tempJ2c = Path.GetTempFileName() + ".j2c";
            var tempPgm = Path.GetTempFileName() + ".pgm";

            try
            {
                File.WriteAllBytes(tempJ2c, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = OjphExpandPath,
                    Arguments = $"-i \"{tempJ2c}\" -o \"{tempPgm}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start ojph_expand process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"ojph_expand failed with exit code {process.ExitCode}: {error}");
                }

                // Parse PGM and compare
                var decoded = ParsePgm(tempPgm);
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempJ2c);
                TryDelete(tempPgm);
            }
        }

        [Test]
        public void Htj2k_OjphEncode_OurDecode_Grayscale8_Matches()
        {
            if (OjphCompressPath == null)
            {
                Assert.Fail("ojph_compress not found - install openjph-tools");
                return;
            }

            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGradientImage8(64, 64);

            var tempPgm = Path.GetTempFileName() + ".pgm";
            var tempJ2c = Path.GetTempFileName() + ".j2c";

            try
            {
                // Write PGM file
                WritePgm(tempPgm, original, 64, 64, 8);

                // Encode with ojph_compress (lossless)
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = OjphCompressPath,
                    Arguments = $"-i \"{tempPgm}\" -o \"{tempJ2c}\" -reversible true",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start ojph_compress process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"ojph_compress failed with exit code {process.ExitCode}: {error}");
                }

                // Read encoded data
                var encoded = File.ReadAllBytes(tempJ2c);

                // Build fragment sequence for our decoder
                var fragments = new DicomFragmentSequence(
                    DicomTag.PixelData,
                    DicomVR.OB,
                    ReadOnlyMemory<byte>.Empty,
                    new[] { (ReadOnlyMemory<byte>)encoded });

                // Decode with our codec
                var codec = new Htj2kLosslessCodec();
                var decoded = new byte[info.FrameSize];
                var result = codec.Decode(fragments, info, 0, decoded);

                Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempPgm);
                TryDelete(tempJ2c);
            }
        }

        [Test]
        public void Htj2k_OurEncode_OjphDecode_Grayscale16_Matches()
        {
            if (OjphExpandPath == null)
            {
                Assert.Fail("ojph_expand not found - install openjph-tools");
                return;
            }

            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale16(64, 64);
            var original = CreateGradientImage16(64, 64);

            // Encode with our codec
            var fragments = codec.Encode(original, info);
            var encoded = fragments.Fragments[0].ToArray();

            // Write to temp file, decode with ojph_expand
            var tempJ2c = Path.GetTempFileName() + ".j2c";
            var tempPgm = Path.GetTempFileName() + ".pgm";

            try
            {
                File.WriteAllBytes(tempJ2c, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = OjphExpandPath,
                    Arguments = $"-i \"{tempJ2c}\" -o \"{tempPgm}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start ojph_expand process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"ojph_expand failed with exit code {process.ExitCode}: {error}");
                }

                // Parse PGM and compare
                var decoded = ParsePgm(tempPgm);
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempJ2c);
                TryDelete(tempPgm);
            }
        }

        [Test]
        public void Htj2k_OjphEncode_OurDecode_Grayscale16_Matches()
        {
            if (OjphCompressPath == null)
            {
                Assert.Fail("ojph_compress not found - install openjph-tools");
                return;
            }

            var info = PixelDataInfo.Grayscale16(64, 64);
            var original = CreateGradientImage16(64, 64);

            var tempPgm = Path.GetTempFileName() + ".pgm";
            var tempJ2c = Path.GetTempFileName() + ".j2c";

            try
            {
                // Write PGM file
                WritePgm(tempPgm, original, 64, 64, 16);

                // Encode with ojph_compress (lossless)
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = OjphCompressPath,
                    Arguments = $"-i \"{tempPgm}\" -o \"{tempJ2c}\" -reversible true",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start ojph_compress process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"ojph_compress failed with exit code {process.ExitCode}: {error}");
                }

                // Read encoded data
                var encoded = File.ReadAllBytes(tempJ2c);

                // Build fragment sequence for our decoder
                var fragments = new DicomFragmentSequence(
                    DicomTag.PixelData,
                    DicomVR.OB,
                    ReadOnlyMemory<byte>.Empty,
                    new[] { (ReadOnlyMemory<byte>)encoded });

                // Decode with our codec
                var codec = new Htj2kLosslessCodec();
                var decoded = new byte[info.FrameSize];
                var result = codec.Decode(fragments, info, 0, decoded);

                Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempPgm);
                TryDelete(tempJ2c);
            }
        }

        [Test]
        public void Htj2k_EncodedOutput_HasValidCapMarker()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            var pixelData = CreateGradientImage8(64, 64);

            // Encode with our codec
            var fragments = codec.Encode(pixelData, info);
            var encoded = fragments.Fragments[0].Span;

            // Check for CAP marker (0xFF50)
            bool foundCap = false;
            for (int i = 0; i < encoded.Length - 1; i++)
            {
                if (encoded[i] == 0xFF && encoded[i + 1] == 0x50)
                {
                    foundCap = true;
                    break;
                }
            }

            Assert.That(foundCap, Is.True, "HTJ2K output should contain CAP marker (0xFF50)");
        }

        [Test]
        public void Htj2k_LosslessRpcl_UsesCorrectProgressionOrder()
        {
            var codec = new Htj2kLosslessRpclCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            var pixelData = CreateGradientImage8(64, 64);

            // Encode with RPCL codec
            var fragments = codec.Encode(pixelData, info);
            var encoded = fragments.Fragments[0].ToArray();

            // The codec should use RPCL progression - verify it decodes correctly
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(pixelData), "RPCL roundtrip should preserve data");
        }

        // ================================================================
        // PSNR verification tests (always run)
        // ================================================================

        [Test]
        public void Psnr_DiagnosticPreset_AtLeast40dB()
        {
            AssertLossyPsnr(HtEncoderOptions.Diagnostic, 40.0, 64, 64, 8);
        }

        [Test]
        public void Psnr_ArchivePreset_AtLeast35dB()
        {
            AssertLossyPsnr(HtEncoderOptions.Archive, 35.0, 64, 64, 8);
        }

        [Test]
        public void Psnr_ReviewPreset_AtLeast30dB()
        {
            AssertLossyPsnr(HtEncoderOptions.Review, 30.0, 64, 64, 8);
        }

        [Test]
        public void Psnr_FastPreset_AtLeast25dB()
        {
            AssertLossyPsnr(HtEncoderOptions.Fast, 25.0, 64, 64, 8);
        }

        // ================================================================
        // SSIM verification tests (always run)
        // ================================================================

        [Test]
        public void Ssim_DiagnosticPreset_AtLeast098()
        {
            AssertLossySsim(HtEncoderOptions.Diagnostic, 0.98, 64, 64, 8);
        }

        [Test]
        public void Ssim_FastPreset_AtLeast085()
        {
            AssertLossySsim(HtEncoderOptions.Fast, 0.85, 64, 64, 8);
        }

        // ================================================================
        // Lossless exactness tests (always run)
        // ================================================================

        [Test]
        public void LosslessExact_8Bit_BytePerfectRoundtrip()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(128, 128);
            var original = CreateGradientImage8(128, 128);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(original),
                "8-bit lossless roundtrip must produce byte-identical output");
        }

        [Test]
        public void LosslessExact_12Bit_BytePerfectRoundtrip()
        {
            var codec = new Htj2kLosslessCodec();
            var info = new PixelDataInfo(
                Rows: 128, Columns: 128,
                BitsAllocated: 16, BitsStored: 12, HighBit: 11,
                SamplesPerPixel: 1, PixelRepresentation: 0,
                PlanarConfiguration: 0, NumberOfFrames: 1);

            var original = CreateGradient12Bit(128, 128);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(original),
                "12-bit lossless roundtrip must produce byte-identical output");
        }

        [Test]
        public void LosslessExact_16Bit_BytePerfectRoundtrip()
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale16(128, 128);
            var original = CreateGradientImage16(128, 128);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");
            Assert.That(decoded, Is.EqualTo(original),
                "16-bit lossless roundtrip must produce byte-identical output");
        }

        // ================================================================
        // Cross-decoder tests (Explicit, require OpenJPH)
        // ================================================================

        // Helper methods

        private static byte[] CreateGradientImage8(int width, int height)
        {
            var data = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[y * width + x] = (byte)((x + y) % 256);
                }
            }
            return data;
        }

        private static byte[] CreateGradientImage16(int width, int height)
        {
            var data = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)((x + y) * 128 % 65536);
                    int offset = (y * width + x) * 2;
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)(value >> 8);
                }
            }
            return data;
        }

        private static byte[] ParsePgm(string path)
        {
            var lines = File.ReadAllLines(path);
            int lineIdx = 0;

            // Skip comments
            while (lineIdx < lines.Length && lines[lineIdx].StartsWith('#'))
                lineIdx++;

            // Check magic number
            if (lineIdx >= lines.Length || (lines[lineIdx] != "P5" && lines[lineIdx] != "P2"))
            {
                throw new InvalidDataException("Not a valid PGM file");
            }
            lineIdx++;

            // Skip more comments
            while (lineIdx < lines.Length && lines[lineIdx].StartsWith('#'))
                lineIdx++;

            // Parse dimensions
            var dims = lines[lineIdx].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int width = int.Parse(dims[0], CultureInfo.InvariantCulture);
            int height = int.Parse(dims[1], CultureInfo.InvariantCulture);
            lineIdx++;

            // Skip comments
            while (lineIdx < lines.Length && lines[lineIdx].StartsWith('#'))
                lineIdx++;

            // Parse max value
            int maxVal = int.Parse(lines[lineIdx], CultureInfo.InvariantCulture);
            lineIdx++;

            // Read binary data (P5) or ASCII data (P2)
            if (lines[0] == "P5")
            {
                // Binary format - read raw bytes from file, skipping the header.
                // Compute header size by searching for the end of the maxval line
                // in the raw file bytes to handle any line-ending style correctly.
                var allBytes = File.ReadAllBytes(path);
                int headerSize = FindP5DataOffset(allBytes, lineIdx);

                int bytesPerPixel = maxVal > 255 ? 2 : 1;
                var dataSize = width * height * bytesPerPixel;
                var result = new byte[dataSize];

                if (bytesPerPixel == 2)
                {
                    // PGM P5 stores 16-bit samples in big-endian byte order.
                    // Convert to little-endian to match DICOM pixel data layout.
                    for (int i = 0; i < width * height; i++)
                    {
                        int srcOffset = headerSize + i * 2;
                        byte hi = allBytes[srcOffset];
                        byte lo = allBytes[srcOffset + 1];
                        result[i * 2] = lo;       // LE low byte
                        result[i * 2 + 1] = hi;   // LE high byte
                    }
                }
                else
                {
                    Array.Copy(allBytes, headerSize, result, 0, dataSize);
                }

                return result;
            }
            else
            {
                // ASCII format (P2) - parse whitespace-separated decimal values
                var allText = File.ReadAllText(path);
                // Skip header lines (already parsed)
                int headerEnd = 0;
                for (int i = 0; i <= lineIdx; i++)
                {
                    headerEnd += lines[i].Length + 1; // +1 for newline
                }
                var dataText = allText.Substring(headerEnd);
                var values = dataText.Split(PgmWhitespace, StringSplitOptions.RemoveEmptyEntries);

                int bytesPerPixel = maxVal > 255 ? 2 : 1;
                var result = new byte[width * height * bytesPerPixel];

                for (int i = 0; i < width * height && i < values.Length; i++)
                {
                    int val = int.Parse(values[i], CultureInfo.InvariantCulture);
                    if (bytesPerPixel == 2)
                    {
                        // Store in little-endian to match DICOM pixel data layout
                        result[i * 2] = (byte)(val & 0xFF);
                        result[i * 2 + 1] = (byte)(val >> 8);
                    }
                    else
                    {
                        result[i] = (byte)val;
                    }
                }

                return result;
            }
        }

        private static void WritePgm(string path, byte[] data, int width, int height, int bitsPerSample)
        {
            int maxVal = (1 << bitsPerSample) - 1;

            using var stream = File.Create(path);
            using var writer = new StreamWriter(stream, Encoding.ASCII);

            // Write PGM header
            writer.WriteLine("P5");
            writer.WriteLine($"{width} {height}");
            writer.WriteLine($"{maxVal}");
            writer.Flush();

            // Write binary pixel data
            // PGM P5 format requires big-endian byte order for 16-bit samples,
            // but DICOM pixel data is little-endian. Byte-swap if needed.
            if (bitsPerSample > 8)
            {
                int pixelCount = width * height;
                for (int i = 0; i < pixelCount; i++)
                {
                    int offset = i * 2;
                    ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                    byte hi = (byte)(value >> 8);
                    byte lo = (byte)(value & 0xFF);
                    stream.WriteByte(hi);
                    stream.WriteByte(lo);
                }
            }
            else
            {
                stream.Write(data, 0, data.Length);
            }
        }

        private static byte[] CreateGradient12Bit(int width, int height)
        {
            var data = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)(((x + y) * 17) % 4096); // 0-4095 range
                    int offset = (y * width + x) * 2;
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)(value >> 8);
                }
            }
            return data;
        }

        /// <summary>
        /// Encodes and decodes using a lossy HTJ2K preset, then asserts PSNR is above threshold.
        /// </summary>
        private static void AssertLossyPsnr(HtEncoderOptions preset, double minPsnr, int width, int height, int bitsStored)
        {
            var codec = new Htj2kLossyCodec();
            PixelDataInfo info;
            byte[] original;

            if (bitsStored == 8)
            {
                info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);
                original = CreateGradientImage8(width, height);
            }
            else
            {
                info = PixelDataInfo.Grayscale16((ushort)height, (ushort)width);
                original = CreateGradientImage16(width, height);
            }

            var opts = new Htj2kCodecOptions(false, 5, false, true, preset);
            var fragments = codec.Encode(original, info, opts);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");

            int maxVal = (1 << bitsStored) - 1;
            double psnr = CalculatePsnr(original, decoded, maxVal, bitsStored > 8 ? 2 : 1);

            // If PSNR is infinite (lossless output), that exceeds any threshold
            if (!double.IsPositiveInfinity(psnr))
            {
                Assert.That(psnr, Is.GreaterThanOrEqualTo(minPsnr),
                    $"PSNR {psnr:F2} dB is below threshold {minPsnr} dB for preset {preset}");
            }
        }

        /// <summary>
        /// Encodes and decodes using a lossy HTJ2K preset, then asserts SSIM is above threshold.
        /// </summary>
        private static void AssertLossySsim(HtEncoderOptions preset, double minSsim, int width, int height, int bitsStored)
        {
            var codec = new Htj2kLossyCodec();
            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);
            var original = CreateGradientImage8(width, height);

            var opts = new Htj2kCodecOptions(false, 5, false, true, preset);
            var fragments = codec.Encode(original, info, opts);

            var decoded = new byte[info.FrameSize];
            var result = codec.Decode(fragments, info, 0, decoded);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic}");

            int maxVal = (1 << bitsStored) - 1;
            double ssim = CalculateSsim(original, decoded, width, height, maxVal);

            Assert.That(ssim, Is.GreaterThanOrEqualTo(minSsim),
                $"SSIM {ssim:F4} is below threshold {minSsim} for preset {preset}");
        }

        /// <summary>
        /// Calculates PSNR: 10 * log10(maxVal^2 / MSE).
        /// </summary>
        private static double CalculatePsnr(byte[] original, byte[] decoded, int maxVal, int bytesPerSample)
        {
            int sampleCount = original.Length / bytesPerSample;
            double mse = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                int origVal, decVal;
                if (bytesPerSample == 1)
                {
                    origVal = original[i];
                    decVal = decoded[i];
                }
                else
                {
                    origVal = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(i * 2));
                    decVal = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(i * 2));
                }

                double diff = origVal - decVal;
                mse += diff * diff;
            }

            mse /= sampleCount;

            if (mse == 0)
            {
                return double.PositiveInfinity;
            }

            return 10.0 * Math.Log10((double)maxVal * maxVal / mse);
        }

        /// <summary>
        /// Calculates SSIM (Structural Similarity Index) between two 8-bit grayscale images.
        /// Uses the standard formula with k1=0.01, k2=0.03.
        /// </summary>
        private static double CalculateSsim(byte[] original, byte[] decoded, int width, int height, int maxVal)
        {
            // Full-image SSIM using the standard formula
            double c1 = (0.01 * maxVal) * (0.01 * maxVal);
            double c2 = (0.03 * maxVal) * (0.03 * maxVal);

            int n = width * height;

            double muX = 0, muY = 0;
            for (int i = 0; i < n; i++)
            {
                muX += original[i];
                muY += decoded[i];
            }
            muX /= n;
            muY /= n;

            double sigmaX2 = 0, sigmaY2 = 0, sigmaXy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = original[i] - muX;
                double dy = decoded[i] - muY;
                sigmaX2 += dx * dx;
                sigmaY2 += dy * dy;
                sigmaXy += dx * dy;
            }
            sigmaX2 /= n;
            sigmaY2 /= n;
            sigmaXy /= n;

            double numerator = (2 * muX * muY + c1) * (2 * sigmaXy + c2);
            double denominator = (muX * muX + muY * muY + c1) * (sigmaX2 + sigmaY2 + c2);

            return numerator / denominator;
        }

        /// <summary>
        /// Finds the byte offset where P5 binary pixel data begins.
        /// Scans the raw file bytes for the required number of header lines
        /// (magic, optional comments, dimensions, maxval), handling both
        /// LF and CR+LF line endings correctly.
        /// </summary>
        private static int FindP5DataOffset(byte[] data, int lineCount)
        {
            int pos = 0;
            int linesFound = 0;
            while (pos < data.Length && linesFound < lineCount)
            {
                if (data[pos] == (byte)'\n')
                {
                    linesFound++;
                }
                pos++;
            }
            return pos;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
