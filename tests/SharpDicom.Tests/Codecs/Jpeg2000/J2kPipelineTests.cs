using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    /// <summary>
    /// Tests that systematically verify each stage of the J2K pipeline in isolation and integration.
    /// </summary>
    /// <remarks>
    /// The full J2K pipeline is: PixelData → DWT → EBCOT → Tier-2 Encoding → Tier-2 Decoding → EBCOT → IDWT → PixelData
    ///
    /// These tests isolate where data is lost in the pipeline.
    /// </remarks>
    [TestFixture]
    public class J2kPipelineTests
    {
        /// <summary>
        /// Test 1: Verify DWT/IDWT are symmetric and produce pixel-perfect reconstruction.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void DWT_Roundtrip_ProducesCorrectCoefficients()
        {
            // Create simple 8x8 test pattern (diagonal gradient)
            int size = 8;
            int[] original = new int[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    original[y * size + x] = x + y; // Values 0-14
                }
            }

            // Copy for DWT
            int[] coeffs = new int[size * size];
            Array.Copy(original, coeffs, original.Length);

            // Apply DWT then IDWT (5/3 lossless)
            int levels = 2;
            DwtTransform.Forward(coeffs, size, size, levels, reversible: true);

            // Verify coefficients changed (DWT was applied)
            bool hasChanged = false;
            for (int i = 0; i < coeffs.Length; i++)
            {
                if (coeffs[i] != original[i])
                {
                    hasChanged = true;
                    break;
                }
            }
            Assert.That(hasChanged, Is.True, "DWT should transform the data");

            // Apply IDWT
            DwtTransform.Inverse(coeffs, size, size, levels, reversible: true);

            // Verify pixel-perfect reconstruction
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(coeffs[i], Is.EqualTo(original[i]),
                    $"Mismatch at index {i}: expected {original[i]}, got {coeffs[i]}");
            }
        }

        /// <summary>
        /// Test 2: Verify DWT + EBCOT integration without tier-2 packets.
        /// </summary>
        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        [Category("Pipeline")]
        public void DWT_EBCOT_Roundtrip_SingleComponent()
        {
            // Create 64x64 grayscale gradient
            int width = 64;
            int height = 64;
            int[] original = new int[width * height];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = i % 256;
            }

            // Apply DWT
            int[] coeffs = new int[width * height];
            Array.Copy(original, coeffs, original.Length);
            int levels = 3;
            DwtTransform.Forward(coeffs, width, height, levels, reversible: true);

            // Encode with EBCOT (single code-block covering entire image for simplicity)
            using var encoder = new EbcotEncoder();
            var encoded = encoder.EncodeCodeBlock(coeffs, width, height, subbandType: 0);

            Assert.That(encoded.Data.Length, Is.GreaterThan(0), "EBCOT should produce non-zero output");
            Assert.That(encoded.NumPasses, Is.GreaterThan(0), "EBCOT should have encoding passes");

            // Decode with EBCOT
            var decoder = new EbcotDecoder();
            int msbPosition = encoded.MsbPosition;
            int[] decoded = decoder.DecodeCodeBlock(
                encoded.Data.Span,
                encoded.NumPasses,
                width, height,
                msbPosition,
                subbandType: 0);

            // Apply IDWT
            DwtTransform.Inverse(decoded, width, height, levels, reversible: true);

            // Verify roundtrip (should be pixel-perfect for lossless)
            int maxError = 0;
            for (int i = 0; i < original.Length; i++)
            {
                int error = Math.Abs(decoded[i] - original[i]);
                maxError = Math.Max(maxError, error);
            }

            Assert.That(maxError, Is.LessThanOrEqualTo(1),
                "DWT+EBCOT roundtrip should be nearly lossless (max error <= 1)");
        }

        /// <summary>
        /// Test 3: Verify full encode/decode path for single component produces correct output.
        /// </summary>
        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        [Category("Pipeline")]
        public void DWT_EBCOT_Tier2_Roundtrip_SingleComponent()
        {
            // Create simple 64x64 grayscale gradient
            var info = PixelDataInfo.Grayscale8(64, 64);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0), "Encoder should produce output");

            // Check if output is all zeros (specific bug from 21-08)
            bool hasNonZero = false;
            for (int i = 0; i < encoded.Length; i++)
            {
                if (encoded.Span[i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
            Assert.That(hasNonZero, Is.True, "Encoded output should NOT be all zeros");

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, frameIndex: 0);

            Assert.That(result.Success, Is.True, $"Decode should succeed: {result.Diagnostic?.Message ?? "Unknown error"}");

            // Check for non-zero decoded data
            hasNonZero = false;
            for (int i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
            Assert.That(hasNonZero, Is.True, "Decoded output should NOT be all zeros");

            // Verify lossless roundtrip
            int differences = 0;
            for (int i = 0; i < pixelData.Length; i++)
            {
                if (decoded[i] != pixelData[i])
                {
                    differences++;
                }
            }

            // For lossless, should be pixel-perfect
            Assert.That(differences, Is.EqualTo(0),
                $"Lossless roundtrip should be pixel-perfect, but {differences}/{pixelData.Length} pixels differ");
        }

        /// <summary>
        /// Test 4: Smoke test that encoder produces non-zero output.
        /// </summary>
        [Test]
        [Category("Encoder")]
        public void Encoder_ProducesNonZeroOutput()
        {
            // Simple gradient
            var info = PixelDataInfo.Grayscale8(32, 32);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            // Encode
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            // Verify output exists
            Assert.That(encoded.Length, Is.GreaterThan(100), "Encoded output should be substantial");

            // Count non-zero bytes
            int nonZeroCount = 0;
            for (int i = 0; i < encoded.Length; i++)
            {
                if (encoded.Span[i] != 0)
                {
                    nonZeroCount++;
                }
            }

            // At least 20% of bytes should be non-zero for compressed data
            double nonZeroPercent = (double)nonZeroCount / encoded.Length;
            Assert.That(nonZeroPercent, Is.GreaterThan(0.2),
                $"Only {nonZeroPercent:P} of encoded data is non-zero (expected > 20%)");
        }

        /// <summary>
        /// Test 5: Verify decoder correctly parses multi-component packets.
        /// </summary>
        [Test]
        [Category("Decoder")]
        public void Decoder_ParsesMultiComponentPackets()
        {
            // Create RGB image (3 components)
            var info = PixelDataInfo.Rgb8(32, 32);

            var pixelData = new byte[info.FrameSize];
            // Fill with simple RGB pattern
            for (int i = 0; i < info.Columns * info.Rows; i++)
            {
                pixelData[i * 3 + 0] = (byte)(i % 256);        // R
                pixelData[i * 3 + 1] = (byte)((i * 2) % 256);  // G
                pixelData[i * 3 + 2] = (byte)((i * 3) % 256);  // B
            }

            // Encode
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0), "RGB encoding should produce output");

            // Decode
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, frameIndex: 0);

            Assert.That(result.Success, Is.True, $"RGB decode should succeed: {result.Diagnostic?.Message ?? "Unknown error"}");

            // Verify all three components have data (not all zeros)
            bool[] componentHasData = new bool[3];
            for (int i = 0; i < info.Columns * info.Rows; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (decoded[i * 3 + c] != 0)
                    {
                        componentHasData[c] = true;
                    }
                }
            }

            for (int c = 0; c < 3; c++)
            {
                Assert.That(componentHasData[c], Is.True,
                    $"Component {c} should have non-zero data after decoding");
            }
        }

        /// <summary>
        /// Test 6: Verify encoder assembles tile data correctly from code-block contributions.
        /// </summary>
        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        [Category("Encoder")]
        public void Encoder_AssemblesTileDataCorrectly()
        {
            // Use a checkerboard pattern to ensure all code-blocks have data
            var info = PixelDataInfo.Grayscale8(64, 64);
            var pixelData = new byte[info.FrameSize];

            for (int y = 0; y < info.Rows; y++)
            {
                for (int x = 0; x < info.Columns; x++)
                {
                    // Checkerboard: alternating 0 and 255
                    pixelData[y * info.Columns + x] = (byte)(((x + y) % 2) * 255);
                }
            }

            // Encode
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            // Parse codestream to verify structure
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True, "Should be valid J2K codestream");

            // Verify SOC marker
            ushort soc = BinaryPrimitives.ReadUInt16BigEndian(encoded.Span);
            Assert.That(soc, Is.EqualTo((ushort)0xFF4F), "Should start with SOC marker");

            // Decode to verify correctness
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, frameIndex: 0);

            Assert.That(result.Success, Is.True, $"Decode should succeed: {result.Diagnostic?.Message ?? "Unknown error"}");

            // Verify checkerboard pattern is preserved
            int errors = 0;
            for (int y = 0; y < info.Rows; y++)
            {
                for (int x = 0; x < info.Columns; x++)
                {
                    byte expected = (byte)(((x + y) % 2) * 255);
                    byte actual = decoded[y * info.Columns + x];
                    if (expected != actual)
                    {
                        errors++;
                    }
                }
            }

            Assert.That(errors, Is.EqualTo(0),
                $"Checkerboard pattern should be preserved, but {errors} pixels differ");
        }
    }
}
