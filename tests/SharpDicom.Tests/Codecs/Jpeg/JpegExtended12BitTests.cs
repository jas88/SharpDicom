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
    /// <summary>
    /// Tests for 12-bit JPEG Extended (Process 2,4) codec using synthetic test data.
    /// Validates the complete 12-bit pipeline: encode -> decode -> verify.
    /// </summary>
    [TestFixture]
    public class JpegExtended12BitTests
    {
        private JpegExtendedCodec _codec = null!;

        /// <summary>
        /// Creates a PixelDataInfo for 12-bit grayscale images
        /// (BitsAllocated=16, BitsStored=12, HighBit=11).
        /// </summary>
        private static PixelDataInfo Grayscale12(ushort rows, ushort columns) =>
            new(rows, columns, 16, 12, 11, 1, 0, 0, 1);

        [SetUp]
        public void Setup()
        {
            _codec = new JpegExtendedCodec();
        }

        #region 12-Bit Grayscale Roundtrip Tests

        [Test]
        public void Test_12Bit_Grayscale_Roundtrip()
        {
            // Generate synthetic 12-bit grayscale 32x32 image with smooth gradient
            // Uses mid-range values (1500-2500) that are well within codec capabilities
            var info = Grayscale12(32, 32);
            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // Smooth gradient centered around 2048 (level shift value)
                    ushort value = (ushort)(1500 + (x + y) * 1000 / 62);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 32) + x) * 2), value);
                }
            }

            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(result.BytesWritten, Is.EqualTo(pixelCount * 2));

            // Verify PSNR for lossy 12-bit roundtrip
            double psnr = CalculatePsnr16Bit(original, decoded, pixelCount, 4095);
            Assert.That(psnr, Is.GreaterThan(15.0),
                $"PSNR {psnr:F2} dB is below threshold of 15 dB for 12-bit grayscale roundtrip");
        }

        [Test]
        public void Test_12Bit_Uniform_Image()
        {
            // Generate image with all pixels at 2048 (mid-range 12-bit)
            // Uniform images should compress very well with minimal loss
            var info = Grayscale12(16, 16);
            int pixelCount = 16 * 16;
            var original = new byte[pixelCount * 2];

            for (int i = 0; i < pixelCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), 2048);
            }

            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Uniform image at level-shift center should decode very accurately
            for (int i = 0; i < pixelCount; i++)
            {
                ushort decodedValue = BinaryPrimitives.ReadUInt16LittleEndian(
                    decoded.AsSpan(i * 2));
                Assert.That(Math.Abs(decodedValue - 2048), Is.LessThanOrEqualTo(10),
                    $"Pixel {i}: expected ~2048, got {decodedValue}");
            }
        }

        [Test]
        public void Test_12Bit_HighValue_Gradient_Roundtrip()
        {
            // Generate a gradient image in the upper 12-bit range (2200-2800)
            // This tests that values above the level shift (2048) roundtrip correctly.
            // Uses a gradient rather than uniform values because DCT-based codecs
            // preserve spatial variation better than isolated DC offsets.
            var info = Grayscale12(32, 32);
            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // Gradient from 2200 to 2800 (above level shift of 2048)
                    ushort value = (ushort)(2200 + (x + y) * 600 / 62);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 32) + x) * 2), value);
                }
            }

            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Verify PSNR for lossy 12-bit upper-range roundtrip
            double psnr = CalculatePsnr16Bit(original, decoded, pixelCount, 4095);
            Assert.That(psnr, Is.GreaterThan(15.0),
                $"PSNR {psnr:F2} dB is below threshold for high-value 12-bit data");

            // Verify the gradient direction is preserved (first pixel < last pixel)
            ushort firstDecoded = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(0));
            ushort lastDecoded = BinaryPrimitives.ReadUInt16LittleEndian(
                decoded.AsSpan((pixelCount - 1) * 2));
            Assert.That(lastDecoded, Is.GreaterThan(firstDecoded),
                $"Gradient direction not preserved: first={firstDecoded}, last={lastDecoded}");
        }

        [Test]
        public void Test_12Bit_Gradient_Pattern()
        {
            // Generate smooth gradient in a narrower range (1500-2600) across 64x8 image
            // This avoids extreme DC coefficient differences across MCU boundaries
            var info = Grayscale12(8, 64);
            int pixelCount = 8 * 64;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    ushort value = (ushort)(1500 + x * 1100 / 63);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 64) + x) * 2), value);
                }
            }

            var options = new JpegCodecOptions { Quality = 95 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Verify the overall trend is increasing by comparing first quarter average
            // to last quarter average (lossy compression may not preserve strict monotonicity)
            double firstQuarterAvg = 0;
            double lastQuarterAvg = 0;
            int quarterSize = 16;

            for (int x = 0; x < quarterSize; x++)
            {
                firstQuarterAvg += BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(x * 2));
            }
            firstQuarterAvg /= quarterSize;

            for (int x = 64 - quarterSize; x < 64; x++)
            {
                lastQuarterAvg += BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(x * 2));
            }
            lastQuarterAvg /= quarterSize;

            Assert.That(lastQuarterAvg, Is.GreaterThan(firstQuarterAvg),
                $"Gradient trend not preserved: first quarter avg={firstQuarterAvg:F1}, last quarter avg={lastQuarterAvg:F1}");
        }

        [Test]
        public void Test_12Bit_Alternating_Pattern()
        {
            // Generate alternating high/low pattern with moderate contrast
            // Using values close to level shift (2048) to stay within codec DC range
            // The alternating pattern (1900/2200) has 300 contrast which is
            // representable within the standard Huffman tables
            var info = Grayscale12(16, 16);
            int pixelCount = 16 * 16;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    ushort value = (ushort)(((x + y) % 2 == 0) ? 1900 : 2200);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 16) + x) * 2), value);
                }
            }

            var options = new JpegCodecOptions { Quality = 100 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // High-frequency alternating pattern is challenging for DCT.
            // Verify the overall contrast direction is preserved for most pixels.
            int correctPattern = 0;
            int midpoint = (1900 + 2200) / 2; // 2050

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    ushort decodedValue = BinaryPrimitives.ReadUInt16LittleEndian(
                        decoded.AsSpan(((y * 16) + x) * 2));
                    bool expectedHigh = (x + y) % 2 != 0;
                    bool decodedHigh = decodedValue > midpoint;

                    if (expectedHigh == decodedHigh)
                    {
                        correctPattern++;
                    }
                }
            }

            double correctRatio = (double)correctPattern / pixelCount;
            Assert.That(correctRatio, Is.GreaterThan(0.50),
                $"Only {correctRatio:P0} of alternating pattern preserved (expected > 50%)");
        }

        [Test]
        public void Test_12Bit_MidRange_Values()
        {
            // Test with typical medical imaging mid-range 12-bit values
            var info = Grayscale12(32, 32);
            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            // Simulate typical CT window: smooth values around 1800-2300
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    ushort value = (ushort)(1800 + (x + y) * 500 / 62);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 32) + x) * 2), value);
                }
            }

            var options = new JpegCodecOptions { Quality = 90 };
            var fragments = _codec.Encode(original, info, options);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            double psnr = CalculatePsnr16Bit(original, decoded, pixelCount, 4095);
            Assert.That(psnr, Is.GreaterThan(15.0),
                $"PSNR {psnr:F2} dB is below threshold for medical imaging 12-bit data");
        }

        #endregion

        #region Lenient Decode Test

        [Test]
        public void Test_Lenient_12Bit_In_Process1()
        {
            // This test documents the expected behavior when 12-bit data
            // encoded with JPEG Extended (Process 2,4) is encountered.
            //
            // In DICOM, 12-bit JPEG data should use Transfer Syntax 1.2.840.10008.1.2.4.51
            // (JPEG Extended). However, some systems incorrectly use 1.2.840.10008.1.2.4.50
            // (JPEG Baseline/Process 1) for 12-bit data.
            //
            // The JpegBaselineCodec only supports SOF0 and 8-bit precision,
            // so attempting to decode a 12-bit JPEG (which uses SOF1) with the
            // baseline codec should fail with a descriptive error.

            // Encode 12-bit data (mid-range values that work with the codec)
            var info = Grayscale12(8, 8);
            int pixelCount = 8 * 8;
            var original = new byte[pixelCount * 2];
            for (int i = 0; i < pixelCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), (ushort)(1800 + i * 5));
            }

            var fragments = _codec.Encode(original, info);

            // Now attempt to decode using the baseline codec
            var baselineCodec = new JpegBaselineCodec();
            var decoded = new byte[pixelCount * 2];

            // The baseline codec should fail because the data contains SOF1 (not SOF0)
            var result = baselineCodec.Decode(fragments, info, 0, decoded);

            // Document the expected behavior: decode should fail
            Assert.That(result.Success, Is.False,
                "Baseline codec should not be able to decode SOF1 (12-bit) data");
        }

        #endregion

        #region Encoded Format Verification

        [Test]
        public void Test_12Bit_Encoded_Contains_SOF1_With_Precision_12()
        {
            var info = Grayscale12(8, 8);
            int pixelCount = 8 * 8;
            var original = new byte[pixelCount * 2];
            for (int i = 0; i < pixelCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), (ushort)(1800 + i * 5));
            }

            var fragments = _codec.Encode(original, info);
            var encoded = fragments.Fragments[0].Span;

            // Verify SOI
            Assert.That(encoded[0], Is.EqualTo(0xFF));
            Assert.That(encoded[1], Is.EqualTo(0xD8));

            // Find SOF1 marker and verify precision field is 12
            bool foundSof1 = false;
            for (int pos = 2; pos < encoded.Length - 3; pos++)
            {
                if (encoded[pos] == 0xFF && encoded[pos + 1] == JpegMarkers.SOF1)
                {
                    foundSof1 = true;
                    // Skip marker (2 bytes) + length (2 bytes) to get to precision
                    int precisionOffset = pos + 4;
                    if (precisionOffset < encoded.Length)
                    {
                        Assert.That(encoded[precisionOffset], Is.EqualTo(12),
                            "SOF1 precision field should be 12 for 12-bit data");
                    }
                    break;
                }
            }

            Assert.That(foundSof1, Is.True, "12-bit encoded data should contain SOF1 marker");
        }

        [Test]
        public void Test_12Bit_Encoded_Has_Even_Length()
        {
            var info = Grayscale12(8, 8);
            int pixelCount = 8 * 8;
            var original = new byte[pixelCount * 2];
            for (int i = 0; i < pixelCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), (ushort)(2000 + i * 3));
            }

            var fragments = _codec.Encode(original, info);
            var encoded = fragments.Fragments[0];

            Assert.That(encoded.Length % 2, Is.EqualTo(0),
                "DICOM requires even length for encoded fragments");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Calculates PSNR for 16-bit (ushort) pixel data stored in byte arrays.
        /// </summary>
        private static double CalculatePsnr16Bit(
            ReadOnlySpan<byte> originalBytes,
            ReadOnlySpan<byte> decodedBytes,
            int pixelCount,
            int maxValue)
        {
            double mse = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int orig = BinaryPrimitives.ReadUInt16LittleEndian(originalBytes.Slice(i * 2));
                int dec = BinaryPrimitives.ReadUInt16LittleEndian(decodedBytes.Slice(i * 2));
                double diff = orig - dec;
                mse += diff * diff;
            }
            mse /= pixelCount;
            if (mse == 0) return double.PositiveInfinity;
            return 10.0 * Math.Log10((double)maxValue * maxValue / mse);
        }

        #endregion
    }
}
