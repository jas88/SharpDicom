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
    /// These tests require the charls-tools command-line utilities (cjpls and djpls):
    /// - Build from source: https://github.com/malaterre/charls-tools
    /// - Some Linux distributions package them alongside libcharls-dev
    ///
    /// Tests are skipped automatically when the tools are not available.
    /// Run with: dotnet test --filter "Category=Conformance"
    /// </remarks>
    [TestFixture]
    [Category("Conformance")]
    public class JpegLsConformanceTests
    {
        private static readonly string? DjplsPath = FindTool("djpls");
        private static readonly string? CjplsPath = FindTool("cjpls");

        private static string? FindTool(string toolName)
        {
            var candidates = new[]
            {
                toolName,                                                    // In PATH
                $"/usr/local/bin/{toolName}",                                // Homebrew / local install
                $"/opt/homebrew/bin/{toolName}",                             // Homebrew ARM
                $"/usr/bin/{toolName}",                                      // Linux system
                $"C:\\Program Files\\CharLS\\{toolName}.exe",                // Windows
                $"C:\\Program Files (x86)\\CharLS\\{toolName}.exe"
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
            if (DjplsPath == null || CjplsPath == null)
            {
                Assert.Fail("charls-tools not installed - build cjpls/djpls from: https://github.com/malaterre/charls-tools");
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

            // Write to temp file, decode with djpls
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = DjplsPath!,
                    Arguments = $"-i \"{tempJls}\" -o \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start djpls process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"djpls decode failed with exit code {process.ExitCode}: {error}");
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

                // Encode with cjpls
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CjplsPath!,
                    Arguments = $"-i \"{tempRaw}\" -o \"{tempJls}\" -s 64 64 -b 8 -c 1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start cjpls process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"cjpls encode failed with exit code {process.ExitCode}: {error}");
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

            // Write to temp file, decode with djpls
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = DjplsPath!,
                    Arguments = $"-i \"{tempJls}\" -o \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start djpls process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"djpls decode failed with exit code {process.ExitCode}: {error}");
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

                // Encode with cjpls
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = CjplsPath!,
                    Arguments = $"-i \"{tempRaw}\" -o \"{tempJls}\" -s 64 64 -b 16 -c 1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start cjpls process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"cjpls encode failed with exit code {process.ExitCode}: {error}");
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

            // Write to temp file, decode with djpls
            var tempJls = Path.GetTempFileName() + ".jls";
            var tempRaw = Path.GetTempFileName() + ".raw";

            try
            {
                File.WriteAllBytes(tempJls, encoded);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = DjplsPath!,
                    Arguments = $"-i \"{tempJls}\" -o \"{tempRaw}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    Assert.Fail("Failed to start djpls process");
                    return;
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    Assert.Fail($"djpls decode failed with exit code {process.ExitCode}: {error}");
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
