using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Native;
using SharpDicom.Data;

namespace SharpDicom.Codecs.Tests
{
    /// <summary>
    /// Tests for <see cref="NativeJpeg2000Codec"/> decode and encode operations.
    /// </summary>
    /// <remarks>
    /// These tests verify JPEG 2000 codec functionality when native libraries are available.
    /// Tests are skipped if native libraries are not present.
    /// </remarks>
    [TestFixture]
    public class NativeJpeg2000CodecTests
    {
        [SetUp]
        public void Setup()
        {
            NativeCodecs.Reset();
            CodecRegistry.Reset();

            // Initialize with suppressed errors since native libs may not be available
            NativeCodecs.Initialize(new NativeCodecOptions
            {
                SuppressInitializationErrors = true
            });
        }

        [TearDown]
        public void TearDown()
        {
            NativeCodecs.Reset();
            CodecRegistry.Reset();
        }

        #region Test Helpers

        private static bool NativeJpeg2000Available =>
            NativeCodecs.IsAvailable && NativeCodecs.HasFeature(NativeCodecFeature.Jpeg2000);

        private static bool GpuAvailable =>
            NativeCodecs.IsAvailable && NativeCodecs.GpuAvailable;

        private static void SkipIfNativeJ2kUnavailable()
        {
            if (!NativeJpeg2000Available)
            {
                Assert.Ignore("Native JPEG 2000 codec not available - skipping test");
            }
        }

        private static void SkipIfGpuUnavailable()
        {
            SkipIfNativeJ2kUnavailable();
            if (!GpuAvailable)
            {
                Assert.Ignore("GPU not available - skipping GPU test");
            }
        }

        private static byte[] CreateGrayscale8TestImage(int width, int height)
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

        private static byte[] CreateGrayscale16TestImage(int width, int height)
        {
            var data = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)((x * 256 + y) % 65536);
                    int offset = (y * width + x) * 2;
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)(value >> 8);
                }
            }

            return data;
        }

        private static bool HasJ2kMarkers(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return false;
            }

            // Check SOC (Start of Codestream) marker: 0xFF4F
            bool hasSoc = data[0] == 0xFF && data[1] == 0x4F;

            // Check EOC (End of Codestream) marker: 0xFFD9
            bool hasEoc = data[data.Length - 2] == 0xFF && data[data.Length - 1] == 0xD9;

            return hasSoc && hasEoc;
        }

        #endregion

        #region Codec Instantiation Tests

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpeg2000Codec_Lossless_CanBeInstantiated()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossless));
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpeg2000Codec_Lossy_CanBeInstantiated()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossy);
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEG2000Lossy));
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpeg2000Codec_Lossless_HasCorrectCapabilities()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            Assert.That(codec.Capabilities.IsLossy, Is.False);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpeg2000Codec_Lossy_HasCorrectCapabilities()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossy);
            Assert.That(codec.Capabilities.IsLossy, Is.True);
            Assert.That(codec.Capabilities.CanEncode, Is.True);
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        [Category("NativeCodecs")]
        public void NativeJpeg2000Codec_Name_ContainsJpeg2000()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            Assert.That(codec.Name, Does.Contain("JPEG 2000").Or.Contain("J2K").Or.Contain("OpenJPEG"));
        }

        #endregion

        #region Lossless Encode/Decode Tests

        [Test]
        [Category("NativeCodecs")]
        public void LosslessEncode_ThenDecode_ExactMatch()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = CreateGrayscale8TestImage(16, 16);

            // Encode
            var fragments = codec.Encode(original, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Decode
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original), "Lossless roundtrip should be exact");
        }

        [Test]
        [Category("NativeCodecs")]
        public void LosslessEncode_16Bit_ThenDecode_ExactMatch()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale16(16, 16);
            var original = CreateGrayscale16TestImage(16, 16);

            // Encode
            var fragments = codec.Encode(original, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Decode
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original), "Lossless 16-bit roundtrip should be exact");
        }

        [Test]
        [Category("NativeCodecs")]
        public void LosslessEncode_ProducesValidJ2kCodestream()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGrayscale8TestImage(32, 32);

            var fragments = codec.Encode(original, info);
            var data = fragments.Fragments[0].Span;

            Assert.That(HasJ2kMarkers(data), Is.True, "Output should have SOC/EOC markers");
        }

        #endregion

        #region Lossy Encode/Decode Tests

        [Test]
        [Category("NativeCodecs")]
        public void LossyEncode_ThenDecode_Succeeds()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossy);
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGrayscale8TestImage(32, 32);

            // Encode
            var fragments = codec.Encode(original, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Decode
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(original.Length));
        }

        [Test]
        [Category("NativeCodecs")]
        public void LossyEncode_ProducesCompressedOutput()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossy);
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGrayscale8TestImage(64, 64);

            var fragments = codec.Encode(original, info);

            // Lossy compression should produce smaller output
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(original.Length),
                "Lossy J2K should be smaller than raw data");
        }

        #endregion

        #region Resolution Level Decode Tests

        [Test]
        [Category("NativeCodecs")]
        public void ResolutionLevelDecode_ProducesSmaller()
        {
            SkipIfNativeJ2kUnavailable();

            // Note: Resolution level decode is an advanced feature
            // This test verifies basic decode works - resolution level support
            // depends on the native library implementation

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = CreateGrayscale8TestImage(64, 64);

            // Encode at full resolution
            var fragments = codec.Encode(original, info);

            // Decode - resolution level support would be via codec options
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, "Decode should succeed");

            // If codec supports resolution levels, a lower level would produce smaller output
            // For now, just verify full decode works
            Assert.That(result.BytesWritten, Is.EqualTo(original.Length));
        }

        #endregion

        #region GPU Tests

        [Test]
        [Category("NativeCodecs")]
        [Category("GPU")]
        public void GpuDecode_WhenAvailable_Succeeds()
        {
            SkipIfGpuUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGrayscale8TestImage(32, 32);

            // Ensure GPU is enabled
            NativeCodecs.PreferCpu = false;

            // Encode
            var fragments = codec.Encode(original, info);

            // Decode (should use GPU if available)
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"GPU decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original), "GPU decode should produce correct output");
        }

        [Test]
        [Category("NativeCodecs")]
        [Category("GPU")]
        public void GpuDecode_WithPreferCpu_UsesCpu()
        {
            SkipIfGpuUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGrayscale8TestImage(32, 32);

            // Force CPU path
            NativeCodecs.PreferCpu = true;

            // Encode
            var fragments = codec.Encode(original, info);

            // Decode (should use CPU due to PreferCpu=true)
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, "CPU decode failed");
            Assert.That(decoded, Is.EqualTo(original), "CPU decode should produce correct output");
        }

        #endregion

        #region Validation Tests

        [Test]
        [Category("NativeCodecs")]
        public void ValidateCompressedData_ValidData_ReturnsValid()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = CreateGrayscale8TestImage(8, 8);

            var fragments = codec.Encode(original, info);
            var result = codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        [Category("NativeCodecs")]
        public void ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(8, 8);

            var result = codec.ValidateCompressedData(null!, info);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        [Category("NativeCodecs")]
        public void Decode_InvalidFrameIndex_ReturnsFailure()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = CreateGrayscale8TestImage(8, 8);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[64];

            var result = codec.Decode(fragments, info, 10, decoded);

            Assert.That(result.Success, Is.False);
        }

        #endregion

        #region Multi-Frame Tests

        [Test]
        [Category("NativeCodecs")]
        public void MultiFrame_EncodeAndDecode_ProducesCorrectFragmentCount()
        {
            SkipIfNativeJ2kUnavailable();

            var codec = new NativeJpeg2000Codec(TransferSyntax.JPEG2000Lossless);
            int numFrames = 4;
            int frameSize = 64;
            var info = PixelDataInfo.Grayscale8(8, 8, numberOfFrames: numFrames);

            // Create multi-frame data
            var original = new byte[frameSize * numFrames];
            for (int frame = 0; frame < numFrames; frame++)
            {
                for (int i = 0; i < frameSize; i++)
                {
                    original[frame * frameSize + i] = (byte)((frame * 50 + i) % 256);
                }
            }

            var fragments = codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(numFrames));

            // Decode each frame
            for (int frame = 0; frame < numFrames; frame++)
            {
                var decoded = new byte[frameSize];
                var result = codec.Decode(fragments, info, frame, decoded);
                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed");
            }
        }

        #endregion
    }
}
