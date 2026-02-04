using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.JpegLs;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.JpegLs
{
    /// <summary>
    /// Conformance tests that verify JPEG-LS output against CharLS reference implementation.
    /// </summary>
    /// <remarks>
    /// These tests require the CharLS command-line tool to be installed:
    /// - macOS: brew install charls
    /// - Linux: apt-get install charls or compile from source
    /// - Windows: Download from https://github.com/team-charls/charls
    ///
    /// Tests are skipped automatically when CharLS is not available.
    /// Run with: dotnet test --filter "Category=Conformance"
    /// </remarks>
    [TestFixture]
    [Category("Conformance")]
    public class JpegLsConformanceTests
    {
        private static readonly string? CharlsPath = FindCharls();

        private static string? FindCharls()
        {
            // Try to find charls executable
            var candidates = new[]
            {
                "charls",                                    // In PATH
                "/usr/local/bin/charls",                     // Homebrew
                "/opt/homebrew/bin/charls",                  // Homebrew ARM
                "/usr/bin/charls",                           // Linux system
                "C:\\Program Files\\CharLS\\charls.exe",     // Windows
                "C:\\Program Files (x86)\\CharLS\\charls.exe"
            };

            foreach (var path in candidates)
            {
                try
                {
                    var info = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(info);
                    if (process != null)
                    {
                        process.WaitForExit(1000);
                        if (process.ExitCode == 0 || process.StandardOutput.Peek() >= 0)
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
            if (CharlsPath == null)
            {
                Assert.Ignore("CharLS not installed - skipping conformance tests. Install with: brew install charls");
            }
        }

        [Test]
        public void JpegLs_OurEncode_CharlsDecode_Grayscale8_Matches()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGradientImage8(64, 64);

            // Encode with our codec
            var fragments = codec.Encode(original, info);
            var encoded = fragments.Fragments[0].ToArray();

            // Write to temp file, decode with CharLS
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CharlsPath!,
                    Arguments = $"--decode \"{tempJls}\" \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start CharLS process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"CharLS decode failed with exit code {process.ExitCode}: {error}");
                }

                // Compare decoded output
                var decoded = File.ReadAllBytes(tempRaw);
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempJls);
                TryDelete(tempRaw);
            }
        }

        [Test]
        public void JpegLs_CharlsEncode_OurDecode_Grayscale8_Matches()
        {
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGradientImage8(64, 64);

            var tempRaw = Path.GetTempFileName() + ".raw";
            var tempJls = Path.GetTempFileName() + ".jls";

            try
            {
                File.WriteAllBytes(tempRaw, original);

                // Encode with CharLS
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CharlsPath!,
                    Arguments = $"--encode \"{tempRaw}\" \"{tempJls}\" --width 64 --height 64 --bits-per-sample 8 --component-count 1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start CharLS process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"CharLS encode failed with exit code {process.ExitCode}: {error}");
                }

                // Read encoded data
                var encoded = File.ReadAllBytes(tempJls);

                // Build fragment sequence for our decoder
                var fragments = new DicomFragmentSequence(
                    DicomTag.PixelData,
                    DicomVR.OB,
                    ReadOnlyMemory<byte>.Empty,
                    new[] { (ReadOnlyMemory<byte>)encoded });

                // Decode with our codec
                var codec = new JpegLsLosslessCodec();
                var decoded = new byte[info.FrameSize];
                var result = codec.Decode(fragments, info, 0, decoded);

                Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempRaw);
                TryDelete(tempJls);
            }
        }

        [Test]
        public void JpegLs_OurEncode_CharlsDecode_Grayscale16_Matches()
        {
            var codec = new JpegLsLosslessCodec();
            var info = PixelDataInfo.Grayscale16(64, 64);
            var original = CreateGradientImage16(64, 64);

            // Encode with our codec
            var fragments = codec.Encode(original, info);
            var encoded = fragments.Fragments[0].ToArray();

            // Write to temp file, decode with CharLS
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CharlsPath!,
                    Arguments = $"--decode \"{tempJls}\" \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start CharLS process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"CharLS decode failed with exit code {process.ExitCode}: {error}");
                }

                // Compare decoded output
                var decoded = File.ReadAllBytes(tempRaw);
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempJls);
                TryDelete(tempRaw);
            }
        }

        [Test]
        public void JpegLs_CharlsEncode_OurDecode_Grayscale16_Matches()
        {
            var info = PixelDataInfo.Grayscale16(64, 64);
            var original = CreateGradientImage16(64, 64);

            var tempRaw = Path.GetTempFileName() + ".raw";
            var tempJls = Path.GetTempFileName() + ".jls";

            try
            {
                File.WriteAllBytes(tempRaw, original);

                // Encode with CharLS
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CharlsPath!,
                    Arguments = $"--encode \"{tempRaw}\" \"{tempJls}\" --width 64 --height 64 --bits-per-sample 16 --component-count 1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start CharLS process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"CharLS encode failed with exit code {process.ExitCode}: {error}");
                }

                // Read encoded data
                var encoded = File.ReadAllBytes(tempJls);

                // Build fragment sequence for our decoder
                var fragments = new DicomFragmentSequence(
                    DicomTag.PixelData,
                    DicomVR.OB,
                    ReadOnlyMemory<byte>.Empty,
                    new[] { (ReadOnlyMemory<byte>)encoded });

                // Decode with our codec
                var codec = new JpegLsLosslessCodec();
                var decoded = new byte[info.FrameSize];
                var result = codec.Decode(fragments, info, 0, decoded);

                Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
                Assert.That(decoded, Is.EqualTo(original), "Decoded data does not match original");
            }
            finally
            {
                TryDelete(tempRaw);
                TryDelete(tempJls);
            }
        }

        [Test]
        public void JpegLs_NearLossless_CharLS_BoundedError()
        {
            var codec = new JpegLsNearLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGradientImage8(64, 64);

            // Encode with NEAR=2 using our codec
            var options = new JpegLsCodecOptions(2, JlsInterleaveMode.None, true);
            var fragments = codec.Encode(original, info, options);
            var encoded = fragments.Fragments[0].ToArray();

            // Write to temp file, decode with CharLS
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CharlsPath!,
                    Arguments = $"--decode \"{tempJls}\" \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start CharLS process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"CharLS decode failed with exit code {process.ExitCode}: {error}");
                }

                // Compare decoded output - should have bounded error
                var decoded = File.ReadAllBytes(tempRaw);
                Assert.That(decoded.Length, Is.EqualTo(original.Length));

                for (int i = 0; i < original.Length; i++)
                {
                    int diff = Math.Abs(original[i] - decoded[i]);
                    Assert.That(diff, Is.LessThanOrEqualTo(2),
                        $"Error at position {i}: {diff} exceeds NEAR=2");
                }
            }
            finally
            {
                TryDelete(tempJls);
                TryDelete(tempRaw);
            }
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
