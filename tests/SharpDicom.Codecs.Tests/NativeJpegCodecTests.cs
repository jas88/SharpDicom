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
    /// Tests for NativeJpegCodec decode/encode operations.
    /// </summary>
    [TestFixture]
    [Category("Native")]
    public class NativeJpegCodecTests
    {
        private NativeJpegCodec _codec = null!;
        private bool _nativeAvailable;

        [OneTimeSetUp]
        public void Setup()
        {
            // Reset and initialize native codecs
            CodecRegistry.Reset();

            try
            {
                NativeCodecs.Initialize();
                _nativeAvailable = NativeCodecs.IsAvailable &&
                                   NativeCodecs.AvailableFeatures.HasFlag(CodecFeatures.Jpeg);

                if (_nativeAvailable)
                {
                    _codec = NativeJpegCodec.CreateBaseline();
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
                Assert.Ignore("Native JPEG library not available");
            }
        }

        [Test]
        public void Codec_Properties_AreCorrect()
        {
            EnsureNativeAvailable();

            Assert.That(_codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGBaseline));
            Assert.That(_codec.Name, Does.Contain("JPEG"));
            Assert.That(_codec.Capabilities.CanEncode, Is.True);
            Assert.That(_codec.Capabilities.CanDecode, Is.True);
            Assert.That(_codec.Capabilities.IsLossy, Is.True);
            Assert.That(_codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void Decode_NullFragments_ThrowsArgumentNullException()
        {
            EnsureNativeAvailable();

            var info = PixelDataInfo.Grayscale8(256, 256);
            var destination = new byte[256 * 256];

            Assert.Throws<ArgumentNullException>(() =>
                _codec.Decode(null!, info, 0, destination));
        }

        [Test]
        public void Decode_InvalidFrameIndex_ThrowsArgumentOutOfRange()
        {
            EnsureNativeAvailable();

            // Create a mock fragment sequence with one fragment
            var fragments = CreateTestFragmentSequence(1);
            var info = PixelDataInfo.Grayscale8(256, 256);
            var destination = new byte[256 * 256];

            // Frame index -1 is invalid
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _codec.Decode(fragments, info, -1, destination));

            // Frame index 5 is out of range (only 1 fragment)
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _codec.Decode(fragments, info, 5, destination));
        }

        [Test]
        public void Decode_EmptyFragment_ReturnsFailure()
        {
            EnsureNativeAvailable();

            // Create fragment sequence with empty data
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new[] { ReadOnlyMemory<byte>.Empty });

            var info = PixelDataInfo.Grayscale8(256, 256);
            var destination = new byte[256 * 256];

            var result = _codec.Decode(fragments, info, 0, destination);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic, Is.Not.Null);
            Assert.That(result.Diagnostic!.Value.Message, Does.Contain("Empty"));
        }

        [Test]
        public void Encode_ValidGrayscale8_ProducesValidJpeg()
        {
            EnsureNativeAvailable();

            const int width = 128;
            const int height = 128;
            var pixels = CreateGradientPattern(width, height);

            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var fragments = _codec.Encode(pixels, info, new NativeJpegCodecOptions { Quality = 90 });

            Assert.That(fragments.FragmentCount, Is.GreaterThan(0));

            // Verify JPEG header (SOI marker 0xFFD8)
            var jpeg = fragments.Fragments[0].Span;
            Assert.That(jpeg.Length, Is.GreaterThan(2));
            Assert.That(jpeg[0], Is.EqualTo(0xFF), "Expected JPEG SOI marker byte 1");
            Assert.That(jpeg[1], Is.EqualTo(0xD8), "Expected JPEG SOI marker byte 2");
        }

        [Test]
        public void Encode_ValidRgb8_ProducesValidJpeg()
        {
            EnsureNativeAvailable();

            const int width = 64;
            const int height = 64;
            var pixels = CreateColorPattern(width, height);

            var info = PixelDataInfo.Rgb8((ushort)height, (ushort)width);

            var fragments = _codec.Encode(pixels, info, new NativeJpegCodecOptions { Quality = 85 });

            Assert.That(fragments.FragmentCount, Is.GreaterThan(0));

            // Verify JPEG header
            var jpeg = fragments.Fragments[0].Span;
            Assert.That(jpeg[0], Is.EqualTo(0xFF));
            Assert.That(jpeg[1], Is.EqualTo(0xD8));
        }

        [Test]
        public async System.Threading.Tasks.Task EncodeAsync_ProducesValidResult()
        {
            EnsureNativeAvailable();

            const int width = 64;
            const int height = 64;
            var pixels = CreateGradientPattern(width, height);

            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var fragments = await _codec.EncodeAsync(pixels, info);
            Assert.That(fragments.FragmentCount, Is.GreaterThan(0));
        }

        [Test]
        public void Roundtrip_EncodeDecodeEncode_MaintainsReasonableQuality()
        {
            EnsureNativeAvailable();

            const int width = 64;
            const int height = 64;
            var original = CreateGradientPattern(width, height);

            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            // Encode with high quality
            var fragments = _codec.Encode(original, info, new NativeJpegCodecOptions { Quality = 95 });

            // Decode
            var decoded = new byte[width * height];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Calculate PSNR - JPEG is lossy so we can't expect exact match
            // but PSNR should be reasonable (> 30dB for Q95)
            double psnr = CalculatePsnr(original, decoded);

            Assert.That(psnr, Is.GreaterThan(30.0),
                $"PSNR {psnr:F2}dB should be > 30dB for Q95 JPEG");
        }

        [Test]
        public void ValidateCompressedData_ValidJpeg_ReturnsValid()
        {
            EnsureNativeAvailable();

            const int width = 32;
            const int height = 32;
            var pixels = CreateGradientPattern(width, height);
            var info = PixelDataInfo.Grayscale8((ushort)height, (ushort)width);

            var fragments = _codec.Encode(pixels, info);

            var validation = _codec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
        }

        [Test]
        public void ValidateCompressedData_InvalidData_ReturnsInvalid()
        {
            EnsureNativeAvailable();

            // Create fragment with invalid JPEG data
            var invalidData = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new[] { new ReadOnlyMemory<byte>(invalidData) });

            var info = PixelDataInfo.Grayscale8(32, 32);

            var validation = _codec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Is.Not.Empty);
        }

        [Test]
        public void ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            EnsureNativeAvailable();

            var info = PixelDataInfo.Grayscale8(32, 32);
            var validation = _codec.ValidateCompressedData(null!, info);

            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void ValidateCompressedData_EmptyFragments_ReturnsInvalid()
        {
            EnsureNativeAvailable();

            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                Array.Empty<ReadOnlyMemory<byte>>());

            var info = PixelDataInfo.Grayscale8(32, 32);
            var validation = _codec.ValidateCompressedData(fragments, info);

            Assert.That(validation.IsValid, Is.False);
        }

        [Test]
        public void JpegOptions_QualityRange_IsValid()
        {
            EnsureNativeAvailable();

            // Default options
            var options = NativeJpegCodecOptions.Default;
            Assert.That(options.Quality, Is.InRange(1, 100));

            // Custom options
            var customOptions = new NativeJpegCodecOptions { Quality = 75 };
            Assert.That(customOptions.Quality, Is.EqualTo(75));
        }

        [Test]
        public void JpegOptions_SubsamplingModes_CanBeSet()
        {
            EnsureNativeAvailable();

            var options = new NativeJpegCodecOptions
            {
                Quality = 85,
                Subsampling = JpegSubsampling.Subsample444
            };

            Assert.That(options.Subsampling, Is.EqualTo(JpegSubsampling.Subsample444));

            // Test all subsampling modes are valid enum values
            Assert.That(Enum.IsDefined(JpegSubsampling.Subsample420), Is.True);
            Assert.That(Enum.IsDefined(JpegSubsampling.Subsample422), Is.True);
            Assert.That(Enum.IsDefined(JpegSubsampling.Subsample444), Is.True);
            Assert.That(Enum.IsDefined(JpegSubsampling.Subsample440), Is.True);
            Assert.That(Enum.IsDefined(JpegSubsampling.Grayscale), Is.True);
        }

        [Test]
        public void CreateBaseline_ReturnsLossyCodec()
        {
            var codec = NativeJpegCodec.CreateBaseline();
            Assert.That(codec.Capabilities.IsLossy, Is.True);
            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGBaseline));
        }

        #region Helper Methods

        private static DicomFragmentSequence CreateTestFragmentSequence(int fragmentCount)
        {
            // Create minimal JPEG data (SOI + EOI only) for each fragment
            var jpegMinimal = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
            var fragments = Enumerable.Range(0, fragmentCount)
                .Select(_ => new ReadOnlyMemory<byte>(jpegMinimal))
                .ToList();

            return new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                fragments);
        }

        private static byte[] CreateGradientPattern(int width, int height)
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

        private static byte[] CreateColorPattern(int width, int height)
        {
            var pixels = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 3;
                    pixels[idx] = (byte)(x * 255 / (width - 1));     // R
                    pixels[idx + 1] = (byte)(y * 255 / (height - 1)); // G
                    pixels[idx + 2] = 128;                           // B
                }
            }
            return pixels;
        }

        private static double CalculatePsnr(byte[] original, byte[] decoded)
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
                return double.PositiveInfinity; // Perfect match

            return 10 * Math.Log10(255.0 * 255.0 / mse);
        }

        #endregion
    }
}
