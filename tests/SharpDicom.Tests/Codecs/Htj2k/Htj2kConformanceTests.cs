using System;
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
    /// Conformance tests that verify HTJ2K output against OpenJPH reference implementation.
    /// </summary>
    /// <remarks>
    /// These tests require the OpenJPH command-line tools to be installed:
    /// - macOS: brew install openjph
    /// - Linux: Build from source (https://github.com/aous72/OpenJPH)
    /// - Windows: Download binaries from GitHub releases
    ///
    /// Tests are skipped automatically when OpenJPH is not available.
    /// Run with: dotnet test --filter "Category=Conformance"
    /// </remarks>
    [TestFixture]
    [Category("Conformance")]
    public class Htj2kConformanceTests
    {
        private static readonly string? OjphCompressPath = FindOjphCompress();
        private static readonly string? OjphExpandPath = FindOjphExpand();

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

        [SetUp]
        public void Setup()
        {
            if (OjphExpandPath == null && OjphCompressPath == null)
            {
                Assert.Ignore("OpenJPH not installed - skipping conformance tests. Install with: brew install openjph");
            }
        }

        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void Htj2k_OurEncode_OjphDecode_Grayscale8_Matches()
        {
            if (OjphExpandPath == null)
            {
                Assert.Ignore("ojph_expand not found");
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
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void Htj2k_OjphEncode_OurDecode_Grayscale8_Matches()
        {
            if (OjphCompressPath == null)
            {
                Assert.Ignore("ojph_compress not found");
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
                    Arguments = $"-i \"{tempPgm}\" -o \"{tempJ2c}\" -reversible on",
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
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void Htj2k_OurEncode_OjphDecode_Grayscale16_Matches()
        {
            if (OjphExpandPath == null)
            {
                Assert.Ignore("ojph_expand not found");
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
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void Htj2k_OjphEncode_OurDecode_Grayscale16_Matches()
        {
            if (OjphCompressPath == null)
            {
                Assert.Ignore("ojph_compress not found");
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
                    Arguments = $"-i \"{tempPgm}\" -o \"{tempJ2c}\" -reversible on",
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
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
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
                // Binary format
                int headerSize = 0;
                for (int i = 0; i <= lineIdx; i++)
                {
                    headerSize += lines[i].Length + 1; // +1 for newline
                }

                var allBytes = File.ReadAllBytes(path);
                var dataSize = width * height * (maxVal > 255 ? 2 : 1);
                var result = new byte[dataSize];
                Array.Copy(allBytes, headerSize, result, 0, dataSize);
                return result;
            }
            else
            {
                // ASCII format (P2) - not commonly used by ojph but handle anyway
                throw new NotImplementedException("P2 ASCII PGM format not implemented");
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
            stream.Write(data, 0, data.Length);
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
