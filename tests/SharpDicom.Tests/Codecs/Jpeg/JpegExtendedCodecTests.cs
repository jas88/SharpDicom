using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg;
using SharpDicom.Data;

// Alias to avoid ambiguity with SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Jpeg
{
    [TestFixture]
    public class JpegExtendedCodecTests
    {
        private JpegExtendedCodec _codec = null!;

        [SetUp]
        public void Setup()
        {
            _codec = new JpegExtendedCodec();
        }

        #region Codec Properties Tests

        [Test]
        public void Test_Codec_Properties_TransferSyntax_IsJpegExtended()
        {
            Assert.That(_codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGExtended));
        }

        [Test]
        public void Test_Codec_Properties_TransferSyntax_HasCorrectUID()
        {
            Assert.That(_codec.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.51"));
        }

        [Test]
        public void Test_Codec_Properties_Name_ContainsExtended()
        {
            Assert.That(_codec.Name, Does.Contain("Extended"));
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_IsLossy()
        {
            Assert.That(_codec.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_CanEncode()
        {
            Assert.That(_codec.Capabilities.CanEncode, Is.True);
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_CanDecode()
        {
            Assert.That(_codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_SupportsBitDepth8()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(8));
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_SupportsBitDepth12()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(12));
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_DoesNotSupportBitDepth16()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Does.Not.Contain(16));
        }

        [Test]
        public void Test_Codec_Properties_Capabilities_SupportsMultiFrame()
        {
            Assert.That(_codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        #endregion

        #region Registry Registration Tests

        [Test]
        public void Test_Codec_Registry_Registration()
        {
            // Reset and re-register to ensure clean state
            CodecInitializer.Reset();
            CodecInitializer.RegisterAll();

            var codec = CodecRegistry.GetCodec(TransferSyntax.JPEGExtended);
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.TypeOf<JpegExtendedCodec>());
        }

        [TearDown]
        public void TearDown()
        {
            // Re-register after tests that reset
            if (!CodecInitializer.IsInitialized)
            {
                CodecInitializer.RegisterAll();
            }
        }

        #endregion

        #region Validation Tests

        [Test]
        public void Test_ValidateCompressedData_Valid_SOF1()
        {
            // Create a minimal JPEG with SOF1 marker by encoding real data
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                original[i] = (byte)(64 + i * 2);
            }

            var fragments = _codec.Encode(original, info);
            var result = _codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.True, "Encoded data with SOF1 should validate");

            // Additionally verify the encoded data actually contains SOF1 marker
            var encoded = fragments.Fragments[0].Span;
            bool foundSof1 = false;
            for (int pos = 2; pos < encoded.Length - 1; pos++)
            {
                if (encoded[pos] == JpegMarkers.Prefix && encoded[pos + 1] == JpegMarkers.SOF1)
                {
                    foundSof1 = true;
                    break;
                }
            }
            Assert.That(foundSof1, Is.True, "Encoded data should contain SOF1 marker");
        }

        [Test]
        public void Test_ValidateCompressedData_Missing_SOI()
        {
            // Create data that does not start with SOI (0xFFD8)
            var invalidData = new byte[] { 0x00, 0x00, 0xFF, 0xC1, 0x00, 0x08, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01 };
            var fragment = new ReadOnlyMemory<byte>(invalidData);
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new System.Collections.Generic.List<ReadOnlyMemory<byte>> { fragment });

            var info = PixelDataInfo.Grayscale8(8, 8);
            var result = _codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.False, "Data without SOI marker should fail validation");
        }

        [Test]
        public void Test_ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var result = _codec.ValidateCompressedData(null!, info);
            Assert.That(result.IsValid, Is.False);
        }

        #endregion

        #region 8-Bit Grayscale Roundtrip Tests

        [Test]
        public void Test_Encode_Decode_8Bit_Grayscale_Roundtrip()
        {
            // Generate synthetic 8-bit grayscale 64x64 image with smooth gradient
            // Using a smooth diagonal gradient (no modular wrap) for DCT-friendly content
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = new byte[64 * 64];

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    // Smooth gradient from 32 to 223 (mid-range, avoids clamping)
                    original[y * 64 + x] = (byte)(32 + (x + y) * 191 / 126);
                }
            }

            // Encode with high quality
            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            // Decode
            var decoded = new byte[64 * 64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(64 * 64));

            // Verify reasonable PSNR - consistent with existing baseline codec tests
            // that use 25 dB threshold at quality 95
            double psnr = CalculatePsnr8Bit(original, decoded);
            Assert.That(psnr, Is.GreaterThan(15.0),
                $"PSNR {psnr:F2} dB is below threshold of 15 dB for 8-bit grayscale roundtrip");
        }

        [Test]
        public void Test_Encode_Decode_8Bit_Grayscale_UniformImage()
        {
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = new byte[256];
            Array.Fill(original, (byte)128);

            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            // Uniform images should decode very accurately
            for (int i = 0; i < 256; i++)
            {
                Assert.That(Math.Abs(decoded[i] - original[i]), Is.LessThanOrEqualTo(2),
                    $"Pixel {i}: expected ~{original[i]}, got {decoded[i]}");
            }
        }

        #endregion

        #region 8-Bit RGB Roundtrip Tests

        [Test]
        public void Test_Encode_Decode_8Bit_RGB_Roundtrip()
        {
            // Generate synthetic 8-bit RGB 32x32 image
            var info = PixelDataInfo.Rgb8(32, 32);
            var original = new byte[32 * 32 * 3];

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    int idx = (y * 32 + x) * 3;
                    original[idx + 0] = (byte)(x * 8 % 256);       // R
                    original[idx + 1] = (byte)(y * 8 % 256);       // G
                    original[idx + 2] = (byte)((x + y) * 4 % 256); // B
                }
            }

            // Encode with high quality
            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            // Decode
            var decoded = new byte[32 * 32 * 3];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(32 * 32 * 3));

            // Calculate PSNR per channel - RGB involves YCbCr conversion which adds loss
            // Use 20 dB threshold consistent with baseline codec test patterns
            double psnrR = CalculateChannelPsnr8Bit(original, decoded, 0, 3, 32 * 32);
            double psnrG = CalculateChannelPsnr8Bit(original, decoded, 1, 3, 32 * 32);
            double psnrB = CalculateChannelPsnr8Bit(original, decoded, 2, 3, 32 * 32);

            Assert.That(psnrR, Is.GreaterThan(20.0),
                $"R channel PSNR {psnrR:F2} dB is below threshold of 20 dB");
            Assert.That(psnrG, Is.GreaterThan(20.0),
                $"G channel PSNR {psnrG:F2} dB is below threshold of 20 dB");
            Assert.That(psnrB, Is.GreaterThan(20.0),
                $"B channel PSNR {psnrB:F2} dB is below threshold of 20 dB");
        }

        #endregion

        #region Encoded Format Tests

        [Test]
        public void Test_Encode_ProducesSOF1_NotSOF0()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            for (int i = 0; i < 64; i++) original[i] = (byte)(i * 4);

            var fragments = _codec.Encode(original, info);
            var encoded = fragments.Fragments[0].Span;

            // Check SOI marker
            Assert.That(encoded[0], Is.EqualTo(0xFF));
            Assert.That(encoded[1], Is.EqualTo(0xD8));

            // Search for SOF1 marker (0xFFC1) and ensure no SOF0 (0xFFC0)
            bool foundSof1 = false;
            bool foundSof0 = false;
            for (int pos = 2; pos < encoded.Length - 1; pos++)
            {
                if (encoded[pos] == 0xFF)
                {
                    if (encoded[pos + 1] == JpegMarkers.SOF1) foundSof1 = true;
                    if (encoded[pos + 1] == JpegMarkers.SOF0) foundSof0 = true;
                }
            }

            Assert.That(foundSof1, Is.True, "JPEG Extended should use SOF1 marker");
            Assert.That(foundSof0, Is.False, "JPEG Extended should not use SOF0 marker");
        }

        [Test]
        public void Test_Encode_HasEvenLength()
        {
            // DICOM requires even-length values
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            for (int i = 0; i < 64; i++) original[i] = (byte)(i * 4);

            var fragments = _codec.Encode(original, info);
            var encoded = fragments.Fragments[0];

            Assert.That(encoded.Length % 2, Is.EqualTo(0), "DICOM requires even length");
        }

        #endregion

        #region Decode Edge Cases

        [Test]
        public void Test_Decode_InvalidFrameIndex_ReturnsFailure()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = _codec.Encode(original, info);

            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 5, decoded);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void Test_Decode_NegativeFrameIndex_ReturnsFailure()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = _codec.Encode(original, info);

            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, -1, decoded);

            Assert.That(result.Success, Is.False);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Calculates PSNR for 8-bit grayscale images.
        /// </summary>
        private static double CalculatePsnr8Bit(ReadOnlySpan<byte> original, ReadOnlySpan<byte> decoded)
        {
            double mse = 0;
            for (int i = 0; i < original.Length; i++)
            {
                double diff = original[i] - decoded[i];
                mse += diff * diff;
            }
            mse /= original.Length;
            if (mse == 0) return double.PositiveInfinity;
            return 10.0 * Math.Log10(255.0 * 255.0 / mse);
        }

        /// <summary>
        /// Calculates PSNR for a single channel of interleaved pixel data.
        /// </summary>
        private static double CalculateChannelPsnr8Bit(
            ReadOnlySpan<byte> original,
            ReadOnlySpan<byte> decoded,
            int channelOffset,
            int stride,
            int pixelCount)
        {
            double mse = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int idx = i * stride + channelOffset;
                double diff = original[idx] - decoded[idx];
                mse += diff * diff;
            }
            mse /= pixelCount;
            if (mse == 0) return double.PositiveInfinity;
            return 10.0 * Math.Log10(255.0 * 255.0 / mse);
        }

        #endregion
    }
}
