using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.JpegLossless;
using SharpDicom.Data;

// Alias to avoid ambiguity with SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.JpegLossless
{
    /// <summary>
    /// Tests for JPEG Lossless codec with 16-bit and 12-bit data.
    /// All lossless tests verify bit-exact reconstruction.
    /// </summary>
    [TestFixture]
    public class JpegLossless16BitTests
    {
        private JpegLosslessCodec _codec = null!;

        [SetUp]
        public void Setup()
        {
            _codec = new JpegLosslessCodec();
        }

        #region 16-Bit Lossless Roundtrip Tests

        [Test]
        public void Test_16Bit_Lossless_Roundtrip()
        {
            // Generate 16-bit grayscale 32x32 image (full range 0-65535)
            var info = PixelDataInfo.Grayscale16(32, 32);
            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // Generate values across full 16-bit range
                    ushort value = (ushort)((x + y * 32) * 65535 / (pixelCount - 1));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 32) + x) * 2), value);
                }
            }

            var fragments = _codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(0));

            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Lossless: must be bit-exact
            Assert.That(decoded, Is.EqualTo(original),
                "16-bit lossless roundtrip must be bit-exact");
        }

        [Test]
        public void Test_12Bit_Lossless_Roundtrip()
        {
            // Generate 12-bit values in 16-bit container
            var info = new PixelDataInfo(
                Rows: 32,
                Columns: 32,
                BitsAllocated: 16,
                BitsStored: 12,
                HighBit: 11,
                SamplesPerPixel: 1,
                PixelRepresentation: 0,
                PlanarConfiguration: 0,
                NumberOfFrames: 1);

            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // 12-bit range: 0-4095
                    ushort value = (ushort)((x + y * 32) * 4095 / (pixelCount - 1));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 32) + x) * 2), value);
                }
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Lossless: must be bit-exact, even with 12-bit stored in 16-bit
            Assert.That(decoded, Is.EqualTo(original),
                "12-bit lossless roundtrip must be bit-exact");
        }

        [Test]
        public void Test_16Bit_Random_Data_Lossless()
        {
            // Generate pseudorandom 16-bit values with fixed seed for reproducibility
            var info = PixelDataInfo.Grayscale16(32, 32);
            int pixelCount = 32 * 32;
            var original = new byte[pixelCount * 2];

            var rng = new Random(12345); // Fixed seed
            rng.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Lossless: must be bit-exact even for random data
            Assert.That(decoded, Is.EqualTo(original),
                "16-bit random data lossless roundtrip must be bit-exact");
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void Test_16Bit_AllZeros_Lossless()
        {
            var info = PixelDataInfo.Grayscale16(16, 16);
            int pixelCount = 16 * 16;
            var original = new byte[pixelCount * 2];
            // All zeros

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original),
                "All-zeros 16-bit roundtrip must be bit-exact");
        }

        [Test]
        public void Test_16Bit_AllMax_Lossless()
        {
            var info = PixelDataInfo.Grayscale16(16, 16);
            int pixelCount = 16 * 16;
            var original = new byte[pixelCount * 2];

            for (int i = 0; i < pixelCount; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), 65535);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original),
                "All-max 16-bit roundtrip must be bit-exact");
        }

        [Test]
        public void Test_16Bit_Alternating_Lossless()
        {
            // Alternating min/max pattern - stress test for prediction
            var info = PixelDataInfo.Grayscale16(16, 16);
            int pixelCount = 16 * 16;
            var original = new byte[pixelCount * 2];

            for (int i = 0; i < pixelCount; i++)
            {
                ushort value = (i % 2 == 0) ? (ushort)0 : (ushort)65535;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    original.AsSpan(i * 2), value);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original),
                "Alternating pattern 16-bit roundtrip must be bit-exact");
        }

        [Test]
        public void Test_16Bit_MedicalImaging_Range_Lossless()
        {
            // Simulate CT data in typical HU range (0-4000 mapped to 16-bit)
            var info = PixelDataInfo.Grayscale16(64, 64);
            int pixelCount = 64 * 64;
            var original = new byte[pixelCount * 2];

            var rng = new Random(42);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    // Typical CT values: base of 1000 + some spatial structure + noise
                    int baseValue = 1000 + (x * 30) + (y * 20);
                    int noise = rng.Next(-50, 51);
                    ushort value = (ushort)Math.Clamp(baseValue + noise, 0, 65535);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 64) + x) * 2), value);
                }
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[pixelCount * 2];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original),
                "Medical imaging data 16-bit lossless roundtrip must be bit-exact");
        }

        #endregion

        #region Compression Tests

        [Test]
        public void Test_16Bit_Gradient_CompressesWell()
        {
            // Use a larger image with a gentle horizontal gradient.
            // DPCM prediction works best when neighboring pixels are similar.
            // With a horizontal predictor (SV=1), a smooth horizontal gradient
            // produces small, highly compressible residuals.
            var info = PixelDataInfo.Grayscale16(64, 64);
            int pixelCount = 64 * 64;
            var original = new byte[pixelCount * 2];

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    // Gentle horizontal gradient: small differences between adjacent pixels
                    ushort value = (ushort)(1000 + x * 16 + y * 16);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        original.AsSpan(((y * 64) + x) * 2), value);
                }
            }

            var fragments = _codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));

            // Gradient data with small residuals should compress well with DPCM
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(original.Length),
                $"Gradient data (raw {original.Length} bytes) should compress to smaller size, got {fragments.Fragments[0].Length} bytes");
        }

        #endregion
    }
}
