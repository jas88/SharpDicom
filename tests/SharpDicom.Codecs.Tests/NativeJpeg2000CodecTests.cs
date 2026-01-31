using System;
using System.Linq;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;
using SharpDicom.Data;

// Alias to disambiguate from SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Tests for NativeJpeg2000Codec decode/encode operations.
    /// </summary>
    [TestFixture]
    [Category("Native")]
    public class NativeJpeg2000CodecTests
    {
        private NativeJpeg2000Codec _losslessCodec = null!;
        private NativeJpeg2000Codec _lossyCodec = null!;
        private bool _nativeAvailable;

        [OneTimeSetUp]
        public void Setup()
        {
            CodecRegistry.Reset();

            try
            {
                NativeCodecs.Initialize();
                _nativeAvailable = NativeCodecs.IsAvailable &&
                                   NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.Jpeg2000);

                if (_nativeAvailable)
                {
                    _losslessCodec = NativeJpeg2000Codec.CreateLossless();
                    _lossyCodec = NativeJpeg2000Codec.CreateLossy();
                }
            }
            catch (NativeCodecException)
            {
                _nativeAvailable = false;
            }
        }

        private void EnsureNativeAvailable()
        {
            if (!_nativeAvailable)
            {
                Assert.Ignore("Native JPEG 2000 library not available");
            }
        }

        [Test]
        public void LosslessCodec_Properties_AreCorrect()
        {
            EnsureNativeAvailable();

            Assert.That(_losslessCodec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossless));
            Assert.That(_losslessCodec.Name, Does.Contain("JPEG 2000"));
            Assert.That(_losslessCodec.Name, Does.Contain("Lossless"));
            Assert.That(_losslessCodec.Capabilities.IsLossy, Is.False);
        }

        [Test]
        public void LossyCodec_Properties_AreCorrect()
        {
            EnsureNativeAvailable();

            Assert.That(_lossyCodec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossy));
            Assert.That(_lossyCodec.Name, Does.Contain("JPEG 2000"));
            Assert.That(_lossyCodec.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void Decode_NullFragments_ThrowsArgumentNullException()
        {
            EnsureNativeAvailable();

            var info = PixelDataInfo.Grayscale16(256, 256);
            var destination = new byte[256 * 256 * 2];

            Assert.Throws<ArgumentNullException>(() =>
                _losslessCodec.Decode(null!, info, 0, destination));
        }

        [Test]
        public void Decode_InvalidFrameIndex_ThrowsArgumentOutOfRange()
        {
            EnsureNativeAvailable();

            var fragments = CreateTestFragmentSequence(1);
            var info = PixelDataInfo.Grayscale16(256, 256);
            var destination = new byte[256 * 256 * 2];

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _losslessCodec.Decode(fragments, info, -1, destination));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _losslessCodec.Decode(fragments, info, 5, destination));
        }

        [Test]
        public void Decode_EmptyFragment_ReturnsFailure()
        {
            EnsureNativeAvailable();

            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new[] { ReadOnlyMemory<byte>.Empty });

            var info = PixelDataInfo.Grayscale16(256, 256);
            var destination = new byte[256 * 256 * 2];

            var result = _losslessCodec.Decode(fragments, info, 0, destination);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic, Is.Not.Null);
        }

        [Test]
        public void LosslessEncode_ThenDecode_ExactMatch()
        {
            EnsureNativeAvailable();

            const int width = 32;
            const int height = 32;
            var original = CreateGradient16Pattern(width, height);

            var info = PixelDataInfo.Grayscale16((ushort)height, (ushort)width);

            // Encode lossless
            var fragments = _losslessCodec.Encode(original, info);

            Assert.That(fragments.FragmentCount, Is.GreaterThan(0),
                "Encoding should produce at least one fragment");

            // Decode
            var decoded = new byte[original.Length];
            var result = _losslessCodec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True,
                $"Decode failed: {result.Diagnostic?.Message}");

            // Lossless should be exact match
            Assert.That(decoded, Is.EqualTo(original),
                "Lossless JPEG 2000 roundtrip should produce exact match");
        }

        [Test]
        public void LossyEncode_ThenDecode_ProducesReasonableQuality()
        {
            EnsureNativeAvailable();

            const int width = 64;
            const int height = 64;
            var original = CreateGradient8Pattern(width, height);

            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            // Encode lossy with moderate compression
            var options = new NativeJpeg2000CodecOptions
            {
                Lossless = false,
                CompressionRatio = 10.0f
            };
            var fragments = _lossyCodec.Encode(original, info, options);

            Assert.That(fragments.FragmentCount, Is.GreaterThan(0));

            // Decode
            var decoded = new byte[original.Length];
            var result = _lossyCodec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True,
                $"Decode failed: {result.Diagnostic?.Message}");

            // Lossy should have reasonable quality (PSNR > 35dB)
            double psnr = CalculatePsnr8(original, decoded);
            Assert.That(psnr, Is.GreaterThan(35.0),
                $"PSNR {psnr:F2}dB should be > 35dB for moderate J2K compression");
        }

        [Test]
        public void ValidateCompressedData_ValidJ2k_ReturnsValid()
        {
            EnsureNativeAvailable();

            const int width = 32;
            const int height = 32;
            var pixels = CreateGradient8Pattern(width, height);
            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var fragments = _losslessCodec.Encode(pixels, info);

            var validation = _losslessCodec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.True);
        }

        [Test]
        public void ValidateCompressedData_InvalidData_ReturnsInvalid()
        {
            EnsureNativeAvailable();

            // Create fragment with invalid J2K data
            var invalidData = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new[] { new ReadOnlyMemory<byte>(invalidData) });

            var info = PixelDataInfo.Grayscale16(32, 32);

            var validation = _losslessCodec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void J2kOptions_DefaultLossless_HasCorrectSettings()
        {
            var options = NativeJpeg2000CodecOptions.DefaultLossless;
            Assert.That(options.Lossless, Is.True);
            Assert.That(options.TileSize, Is.EqualTo(0)); // No tiling
        }

        [Test]
        public void J2kOptions_DefaultLossy_HasCorrectSettings()
        {
            var options = NativeJpeg2000CodecOptions.DefaultLossy;
            Assert.That(options.Lossless, Is.False);
            Assert.That(options.CompressionRatio, Is.GreaterThan(1.0f));
        }

        [Test]
        public void J2kOptions_CanSetResolutionLevels()
        {
            var options = new NativeJpeg2000CodecOptions
            {
                ResolutionLevels = 4
            };
            Assert.That(options.ResolutionLevels, Is.EqualTo(4));
        }

        [Test]
        public void J2kOptions_CanSetTileSize()
        {
            var options = new NativeJpeg2000CodecOptions
            {
                TileSize = 256
            };
            Assert.That(options.TileSize, Is.EqualTo(256));
        }

        [Test]
        public void GpuEnabled_ReturnsBoolean()
        {
            // This should not throw even if GPU is not available
            var gpuEnabled = NativeJpeg2000Codec.GpuEnabled;
            Assert.That(gpuEnabled, Is.TypeOf<bool>());
        }

        [Test]
        [Category("GPU")]
        public void GpuDecode_WhenAvailable_UsesGpu()
        {
            if (!NativeCodecs.GpuAvailable ||
                !NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.GpuJpeg2000) ||
                !NativeJpeg2000Codec.GpuEnabled)
            {
                Assert.Ignore("GPU JPEG 2000 not available");
            }

            EnsureNativeAvailable();

            const int width = 256;
            const int height = 256;
            var original = CreateGradient16Pattern(width, height);

            var info = PixelDataInfo.Grayscale16((ushort)height, (ushort)width);
            var fragments = _losslessCodec.Encode(original, info);

            // GPU decode should work
            var decoded = new byte[original.Length];
            var result = _losslessCodec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void CreateLossless_ReturnsCorrectTransferSyntax()
        {
            var codec = NativeJpeg2000Codec.CreateLossless();
            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossless));
            Assert.That(codec.Capabilities.IsLossy, Is.False);
        }

        [Test]
        public void CreateLossy_ReturnsCorrectTransferSyntax()
        {
            var codec = NativeJpeg2000Codec.CreateLossy();
            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossy));
            Assert.That(codec.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void DecodeAsync_CompletesImmediately()
        {
            EnsureNativeAvailable();

            const int width = 32;
            const int height = 32;
            var original = CreateGradient8Pattern(width, height);
            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var fragments = _losslessCodec.Encode(original, info);
            var destination = new byte[original.Length];

            var task = _losslessCodec.DecodeAsync(fragments, info, 0, destination);

            Assert.That(task.IsCompleted, Is.True,
                "Sync-over-async should complete immediately");
        }

        [Test]
        public void EncodeAsync_CompletesImmediately()
        {
            EnsureNativeAvailable();

            const int width = 32;
            const int height = 32;
            var pixels = CreateGradient8Pattern(width, height);
            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var task = _losslessCodec.EncodeAsync(pixels, info);

            Assert.That(task.IsCompleted, Is.True,
                "Sync-over-async should complete immediately");
        }

        #region Helper Methods

        private static DicomFragmentSequence CreateTestFragmentSequence(int fragmentCount)
        {
            // Minimal J2K codestream (SOC marker only)
            var j2kMinimal = new byte[] { 0xFF, 0x4F };
            var fragments = Enumerable.Range(0, fragmentCount)
                .Select(_ => new ReadOnlyMemory<byte>(j2kMinimal))
                .ToList();

            return new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                fragments);
        }

        private static byte[] CreateGradient8Pattern(int width, int height)
        {
            var pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = (byte)((x + y) * 255 / (width + height - 2));
                }
            }
            return pixels;
        }

        private static byte[] CreateGradient16Pattern(int width, int height)
        {
            var pixels = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)((x + y) * 4095 / (width + height - 2));
                    int idx = (y * width + x) * 2;
                    pixels[idx] = (byte)(value & 0xFF);       // Low byte
                    pixels[idx + 1] = (byte)(value >> 8);     // High byte
                }
            }
            return pixels;
        }

        private static double CalculatePsnr8(byte[] original, byte[] decoded)
        {
            if (original.Length != decoded.Length || original.Length == 0)
                return 0;

            double mse = 0;
            for (int i = 0; i < original.Length; i++)
            {
                double diff = original[i] - decoded[i];
                mse += diff * diff;
            }
            mse /= original.Length;

            if (mse == 0)
                return double.PositiveInfinity;

            return 10 * Math.Log10(255.0 * 255.0 / mse);
        }

        #endregion
    }
}
