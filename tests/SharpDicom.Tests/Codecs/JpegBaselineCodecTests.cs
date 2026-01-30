using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg;
using SharpDicom.Data;

// Alias to avoid ambiguity with SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs
{
    [TestFixture]
    public class JpegBaselineCodecTests
    {
        private JpegBaselineCodec _codec = null!;

        [SetUp]
        public void Setup()
        {
            _codec = new JpegBaselineCodec();
        }

        #region Capability Tests

        [Test]
        public void TransferSyntax_ReturnsJpegBaseline()
        {
            Assert.That(_codec.TransferSyntax, Is.EqualTo(TransferSyntax.JPEGBaseline));
        }

        [Test]
        public void TransferSyntax_HasCorrectUID()
        {
            Assert.That(_codec.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.50"));
        }

        [Test]
        public void TransferSyntax_IsLossy()
        {
            Assert.That(_codec.TransferSyntax.IsLossy, Is.True);
        }

        [Test]
        public void TransferSyntax_IsEncapsulated()
        {
            Assert.That(_codec.TransferSyntax.IsEncapsulated, Is.True);
        }

        [Test]
        public void Capabilities_IndicatesLossy()
        {
            Assert.That(_codec.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void Capabilities_SupportsEncoding()
        {
            Assert.That(_codec.Capabilities.CanEncode, Is.True);
        }

        [Test]
        public void Capabilities_SupportsDecoding()
        {
            Assert.That(_codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Capabilities_SupportsMultiFrame()
        {
            Assert.That(_codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void Capabilities_SupportedBitDepths_Contains8()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(8));
        }

        [Test]
        public void Capabilities_SupportedBitDepths_DoesNotContain16()
        {
            // JPEG Baseline is 8-bit only
            Assert.That(_codec.Capabilities.SupportedBitDepths, Does.Not.Contain(16));
        }

        [Test]
        public void Capabilities_SupportedSamplesPerPixel_Contains1()
        {
            Assert.That(_codec.Capabilities.SupportedSamplesPerPixel, Contains.Item(1));
        }

        [Test]
        public void Capabilities_SupportedSamplesPerPixel_Contains3()
        {
            Assert.That(_codec.Capabilities.SupportedSamplesPerPixel, Contains.Item(3));
        }

        [Test]
        public void Name_ContainsProcess1()
        {
            Assert.That(_codec.Name, Does.Contain("Process 1"));
        }

        [Test]
        public void Name_ContainsBaseline()
        {
            Assert.That(_codec.Name, Does.Contain("Baseline"));
        }

        #endregion

        #region Grayscale Roundtrip Tests

        [Test]
        public void EncodeAndDecode_Grayscale8_RoundtripSucceeds()
        {
            // Create test 8x8 grayscale image with mid-range values
            // (avoids edge effects at 0 and 255)
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                original[i] = (byte)(64 + i * 2);  // Range 64-190
            }

            // Encode
            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            // Decode
            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(64));
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_LargerImage_RoundtripSucceeds()
        {
            // Create test 32x32 grayscale image
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    original[y * 32 + x] = (byte)((x + y) * 4);
                }
            }

            // Encode
            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);

            // Decode
            var decoded = new byte[1024];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Check PSNR (should be high for quality 95)
            double mse = 0;
            for (int i = 0; i < 1024; i++)
            {
                double diff = decoded[i] - original[i];
                mse += diff * diff;
            }
            mse /= 1024;
            double psnr = mse > 0 ? 10 * Math.Log10(255.0 * 255.0 / mse) : double.PositiveInfinity;

            // Accept PSNR > 25 dB for this initial implementation
            Assert.That(psnr, Is.GreaterThan(25),
                $"PSNR {psnr:F2} dB is too low for quality 95");
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_UniformImage_RoundtripSucceeds()
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

        #region RGB Roundtrip Tests

        [Test]
        public void EncodeAndDecode_Rgb8_RoundtripSucceeds()
        {
            // Create test 8x8 RGB image
            var info = PixelDataInfo.Rgb8(8, 8);
            var original = new byte[64 * 3];
            // Fill with gradient (mid-range values to avoid edge effects)
            for (int i = 0; i < 64; i++)
            {
                original[i * 3 + 0] = (byte)(64 + i * 2);     // R: 64-190
                original[i * 3 + 1] = 128;                     // G: constant
                original[i * 3 + 2] = (byte)(191 - i * 2);    // B: 127-191
            }

            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            var decoded = new byte[64 * 3];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(64 * 3));
        }

        [Test]
        public void EncodeAndDecode_Rgb8_RedImage_RoundtripSucceeds()
        {
            var info = PixelDataInfo.Rgb8(16, 16);
            var original = new byte[256 * 3];
            // Fill with solid red
            for (int i = 0; i < 256; i++)
            {
                original[i * 3 + 0] = 255;  // R
                original[i * 3 + 1] = 0;    // G
                original[i * 3 + 2] = 0;    // B
            }

            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[256 * 3];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
        }

        #endregion

        #region Quality Tests

        [Test]
        public void Encode_HighQuality_ProducesLargerFile()
        {
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            var random = new Random(42);
            random.NextBytes(original);

            var lowQualityOptions = new JpegCodecOptions { Quality = 50 };
            var highQualityOptions = new JpegCodecOptions { Quality = 95 };

            var lowQualityFragments = _codec.Encode(original, info, lowQualityOptions);
            var highQualityFragments = _codec.Encode(original, info, highQualityOptions);

            Assert.That(highQualityFragments.Fragments[0].Length,
                Is.GreaterThan(lowQualityFragments.Fragments[0].Length),
                "High quality should produce larger file");
        }

        [Test]
        public void EncodeAndDecode_Quality50_StillReasonableQuality()
        {
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    original[y * 32 + x] = (byte)((x + y) * 4);
                }
            }

            var options = new JpegCodecOptions { Quality = 50 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[1024];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);

            // PSNR should still be reasonable even at quality 50
            double mse = 0;
            for (int i = 0; i < 1024; i++)
            {
                double diff = decoded[i] - original[i];
                mse += diff * diff;
            }
            mse /= 1024;
            double psnr = mse > 0 ? 10 * Math.Log10(255.0 * 255.0 / mse) : double.PositiveInfinity;

            Assert.That(psnr, Is.GreaterThan(20),
                $"PSNR {psnr:F2} dB is too low even for quality 50");
        }

        #endregion

        #region Compression Tests

        [Test]
        public void Encode_Grayscale8_ProducesCompressedData()
        {
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            // Create a smooth gradient that compresses well
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    original[y * 32 + x] = (byte)((x + y) * 4);
                }
            }

            var options = new JpegCodecOptions { Quality = 75 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // JPEG should compress the data
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(original.Length),
                "Encoded data should be smaller than original for compressible data");
        }

        #endregion

        #region Multi-Frame Tests

        [Test]
        public void EncodeAndDecode_MultiFrame_AllFramesCorrect()
        {
            var info = PixelDataInfo.Grayscale8(8, 8, numberOfFrames: 3);
            var original = new byte[192];  // 3 frames * 64 pixels
            for (int frame = 0; frame < 3; frame++)
            {
                for (int i = 0; i < 64; i++)
                {
                    original[frame * 64 + i] = (byte)(frame * 80 + i);
                }
            }

            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(3));

            // Decode each frame
            for (int frame = 0; frame < 3; frame++)
            {
                var decoded = new byte[64];
                var result = _codec.Decode(fragments, info, frame, decoded);

                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed");

                var expected = new byte[64];
                Array.Copy(original, frame * 64, expected, 0, 64);

                // Check approximate match (lossy - use PSNR)
                double frameMse = 0;
                for (int i = 0; i < 64; i++)
                {
                    double diff = decoded[i] - expected[i];
                    frameMse += diff * diff;
                }
                frameMse /= 64;
                double framePsnr = frameMse > 0 ? 10 * Math.Log10(255.0 * 255.0 / frameMse) : double.PositiveInfinity;

                Assert.That(framePsnr, Is.GreaterThan(20),
                    $"Frame {frame} PSNR {framePsnr:F2} dB is too low");
            }
        }

        #endregion

        #region Validation Tests

        [Test]
        public void ValidateCompressedData_ValidData_ReturnsValid()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var random = new Random(42);
            random.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var result = _codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);

            var result = _codec.ValidateCompressedData(null!, info);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Decode_InvalidFrameIndex_ReturnsFailure()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = _codec.Encode(original, info);

            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 5, decoded);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void Decode_NegativeFrameIndex_ReturnsFailure()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = _codec.Encode(original, info);

            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, -1, decoded);

            Assert.That(result.Success, Is.False);
        }

        #endregion

        #region Options Tests

        [Test]
        public void JpegCodecOptions_Default_Quality75()
        {
            var options = JpegCodecOptions.Default;
            Assert.That(options.Quality, Is.EqualTo(75));
        }

        [Test]
        public void JpegCodecOptions_MedicalImaging_Quality90()
        {
            var options = JpegCodecOptions.MedicalImaging;
            Assert.That(options.Quality, Is.EqualTo(90));
        }

        [Test]
        public void JpegCodecOptions_MedicalImaging_NoSubsampling()
        {
            var options = JpegCodecOptions.MedicalImaging;
            Assert.That(options.Subsampling, Is.EqualTo(ChromaSubsampling.None));
        }

        [Test]
        public void JpegCodecOptions_QualityBelowRange_Throws()
        {
            var options = new JpegCodecOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Quality = 0);
        }

        [Test]
        public void JpegCodecOptions_QualityAboveRange_Throws()
        {
            var options = new JpegCodecOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Quality = 101);
        }

        [Test]
        public void JpegCodecOptions_WithQuality_CreatesCopy()
        {
            var original = JpegCodecOptions.Default;
            var modified = original.WithQuality(90);

            Assert.That(modified.Quality, Is.EqualTo(90));
            Assert.That(original.Quality, Is.EqualTo(75)); // Original unchanged
        }

        #endregion

        #region Encoded Format Tests

        [Test]
        public void Encode_ProducesValidJpegMarkerStructure()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            for (int i = 0; i < 64; i++) original[i] = (byte)(i * 4);

            var fragments = _codec.Encode(original, info);
            var encoded = fragments.Fragments[0].Span;

            // Check SOI marker
            Assert.That(encoded[0], Is.EqualTo(0xFF), "First byte should be 0xFF");
            Assert.That(encoded[1], Is.EqualTo(0xD8), "Second byte should be 0xD8 (SOI)");

            // Check EOI marker (might have padding)
            bool hasEoi = (encoded[encoded.Length - 2] == 0xFF && encoded[encoded.Length - 1] == 0xD9) ||
                          (encoded[encoded.Length - 3] == 0xFF && encoded[encoded.Length - 2] == 0xD9);
            Assert.That(hasEoi, Is.True, "Should end with EOI marker (0xFFD9)");
        }

        [Test]
        public void Encode_HasEvenLength()
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
    }
}
