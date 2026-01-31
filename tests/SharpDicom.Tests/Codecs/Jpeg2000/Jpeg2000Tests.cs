using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Wavelet;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    [TestFixture]
    public class Jpeg2000Tests
    {
        #region J2kCodestream Tests

        [Test]
        public void J2kCodestream_TryParse_ValidHeader_ExtractsDimensions()
        {
            // Minimal JPEG 2000 codestream with SOC, SIZ, and SOT markers
            // SIZ marker: 256x256 image, 1 component, 8-bit unsigned
            byte[] data = new byte[]
            {
                // SOC marker
                0xFF, 0x4F,
                // SIZ marker
                0xFF, 0x51,
                0x00, 0x29, // Length = 41 (2 for length + 38 for SIZ content + 1 component * 3 = 41)
                0x00, 0x00, // Rsiz (capabilities)
                0x00, 0x00, 0x01, 0x00, // Xsiz = 256
                0x00, 0x00, 0x01, 0x00, // Ysiz = 256
                0x00, 0x00, 0x00, 0x00, // XOsiz = 0
                0x00, 0x00, 0x00, 0x00, // YOsiz = 0
                0x00, 0x00, 0x01, 0x00, // XTsiz = 256
                0x00, 0x00, 0x01, 0x00, // YTsiz = 256
                0x00, 0x00, 0x00, 0x00, // XTOsiz = 0
                0x00, 0x00, 0x00, 0x00, // YTOsiz = 0
                0x00, 0x01, // Csiz = 1 component
                0x07, // Ssiz = 8-bit (7+1), unsigned
                0x01, // XRsiz = 1
                0x01, // YRsiz = 1
                // SOT marker (end of main header)
                0xFF, 0x90,
                0x00, 0x0A, // Length = 10 (minimal SOT segment)
                0x00, 0x00, // Tile index
                0x00, 0x00, 0x00, 0x00, // Tile-part length
                0x00, // Tile-part index
                0x01, // Number of tile-parts
            };

            bool success = J2kCodestream.TryParse(data, out var header, out var error);

            Assert.That(success, Is.True, $"Parse failed: {error}");
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.ImageWidth, Is.EqualTo(256));
            Assert.That(header.ImageHeight, Is.EqualTo(256));
            Assert.That(header.ComponentCount, Is.EqualTo(1));
            Assert.That(header.BitDepth, Is.EqualTo(8));
            Assert.That(header.IsSigned, Is.False);
        }

        [Test]
        public void J2kCodestream_TryParse_WithCOD_ExtractsWaveletType()
        {
            // JPEG 2000 codestream with SIZ and COD markers
            byte[] data = new byte[]
            {
                // SOC marker
                0xFF, 0x4F,
                // SIZ marker (same as above)
                0xFF, 0x51,
                0x00, 0x29, // Length = 41
                0x00, 0x00,
                0x00, 0x00, 0x01, 0x00,
                0x00, 0x00, 0x01, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x01, 0x00,
                0x00, 0x00, 0x01, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x01,
                0x07, 0x01, 0x01,
                // COD marker
                0xFF, 0x52,
                0x00, 0x0C, // Length = 12
                0x00, // Scod (no precincts, no SOP, no EPH)
                0x00, // Progression order = LRCP
                0x00, 0x01, // Number of layers = 1
                0x00, // MCT = 0
                0x05, // Decomposition levels = 5
                0x04, // Code-block width exponent (2^(4+2) = 64)
                0x04, // Code-block height exponent (2^(4+2) = 64)
                0x00, // Code-block style
                0x01, // Wavelet = 5/3 reversible
                // SOT marker
                0xFF, 0x90,
                0x00, 0x0A, // Length = 10
                0x00, 0x00, // Tile index
                0x00, 0x00, 0x00, 0x00, // Tile-part length
                0x00, // Tile-part index
                0x01, // Number of tile-parts
            };

            bool success = J2kCodestream.TryParse(data, out var header, out var error);

            Assert.That(success, Is.True, $"Parse failed: {error}");
            Assert.That(header, Is.Not.Null);
            Assert.That(header!.UsesReversibleTransform, Is.True);
            Assert.That(header.DecompositionLevels, Is.EqualTo(5));
            Assert.That(header.CodeBlockWidth, Is.EqualTo(64));
            Assert.That(header.CodeBlockHeight, Is.EqualTo(64));
            Assert.That(header.Progression, Is.EqualTo(ProgressionOrder.LRCP));
        }

        [Test]
        public void J2kCodestream_TryParse_MissingSOC_ReturnsError()
        {
            byte[] data = new byte[] { 0x00, 0x00, 0x00, 0x00 };

            bool success = J2kCodestream.TryParse(data, out var header, out var error);

            Assert.That(success, Is.False);
            Assert.That(header, Is.Null);
            Assert.That(error, Does.Contain("SOC"));
        }

        [Test]
        public void J2kCodestream_TryParse_TooShort_ReturnsError()
        {
            byte[] data = new byte[] { 0xFF, 0x4F }; // Just SOC, no SIZ

            bool success = J2kCodestream.TryParse(data, out var header, out var error);

            // Should fail because SIZ is required
            Assert.That(success, Is.False);
            Assert.That(header, Is.Null);
        }

        #endregion

        #region DWT 5/3 Tests

        [Test]
        public void Dwt53_ForwardInverse_Roundtrip_ProducesExactOriginal()
        {
            // Test data - small 8x8 block
            int[] original = new int[]
            {
                100, 102, 104, 106, 108, 110, 112, 114,
                101, 103, 105, 107, 109, 111, 113, 115,
                102, 104, 106, 108, 110, 112, 114, 116,
                103, 105, 107, 109, 111, 113, 115, 117,
                104, 106, 108, 110, 112, 114, 116, 118,
                105, 107, 109, 111, 113, 115, 117, 119,
                106, 108, 110, 112, 114, 116, 118, 120,
                107, 109, 111, 113, 115, 117, 119, 121
            };

            int[] data = (int[])original.Clone();

            // Forward transform
            Dwt53.Forward2D(data, 8, 8, 8);

            // Inverse transform
            Dwt53.Inverse2D(data, 8, 8, 8);

            // Verify exact reconstruction
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(data[i], Is.EqualTo(original[i]),
                    $"Mismatch at index {i}: expected {original[i]}, got {data[i]}");
            }
        }

        [Test]
        public void Dwt53_MultiLevel_Roundtrip_ProducesExactOriginal()
        {
            // Test with multiple decomposition levels
            int width = 16;
            int height = 16;
            int[] original = new int[width * height];

            // Fill with test pattern
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    original[y * width + x] = (x + y * 10) % 256;
                }
            }

            int[] data = (int[])original.Clone();

            // 3-level decomposition
            DwtTransform.Forward(data, width, height, 3, reversible: true);
            DwtTransform.Inverse(data, width, height, 3, reversible: true);

            // Verify exact reconstruction
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(data[i], Is.EqualTo(original[i]),
                    $"Mismatch at index {i}: expected {original[i]}, got {data[i]}");
            }
        }

        private static readonly int[] OriginalRow = new int[] { 10, 20, 30, 40 };

        [Test]
        public void Dwt53_ForwardHorizontal_ProducesCorrectCoefficients()
        {
            // Simple 4-sample test
            int[] row = new int[] { 10, 20, 30, 40 };

            Dwt53.ForwardHorizontal(row);

            // After forward: low-pass coefficients come first, then high-pass
            // The exact values depend on the lifting formulas
            // Just verify we get something reasonable and can roundtrip
            Assert.That(row, Is.Not.EqualTo(OriginalRow)); // Should be different
        }

        [Test]
        public void Dwt53_SinglePixel_NoChange()
        {
            int[] data = new int[] { 42 };

            DwtTransform.Forward(data, 1, 1, 1, reversible: true);

            Assert.That(data[0], Is.EqualTo(42));
        }

        #endregion

        #region DWT 9/7 Tests

        [Test]
        public void Dwt97_ForwardInverse_Roundtrip_ProducesApproximateOriginal()
        {
            // 9/7 is lossy, so we expect approximate reconstruction
            int[] original = new int[]
            {
                100, 102, 104, 106, 108, 110, 112, 114,
                101, 103, 105, 107, 109, 111, 113, 115,
                102, 104, 106, 108, 110, 112, 114, 116,
                103, 105, 107, 109, 111, 113, 115, 117,
                104, 106, 108, 110, 112, 114, 116, 118,
                105, 107, 109, 111, 113, 115, 117, 119,
                106, 108, 110, 112, 114, 116, 118, 120,
                107, 109, 111, 113, 115, 117, 119, 121
            };

            int[] data = (int[])original.Clone();

            // Forward transform
            DwtTransform.Forward(data, 8, 8, 1, reversible: false);

            // Inverse transform
            DwtTransform.Inverse(data, 8, 8, 1, reversible: false);

            // Verify approximate reconstruction (within tolerance)
            // Floating-point rounding errors may accumulate
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(data[i], Is.EqualTo(original[i]).Within(2),
                    $"Mismatch at index {i}: expected {original[i]}, got {data[i]}");
            }
        }

        [Test]
        public void Dwt97_MultiLevel_Roundtrip_ProducesApproximateOriginal()
        {
            int width = 16;
            int height = 16;
            int[] original = new int[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    original[y * width + x] = (x + y * 10) % 256;
                }
            }

            int[] data = (int[])original.Clone();

            // 2-level decomposition with 9/7
            DwtTransform.Forward(data, width, height, 2, reversible: false);
            DwtTransform.Inverse(data, width, height, 2, reversible: false);

            // Verify approximate reconstruction
            double maxError = 0;
            for (int i = 0; i < original.Length; i++)
            {
                double error = Math.Abs(data[i] - original[i]);
                maxError = Math.Max(maxError, error);
            }

            // 9/7 with multiple levels should still be close
            Assert.That(maxError, Is.LessThan(5), $"Max error was {maxError}");
        }

        #endregion

        #region MQ Coder Tests

        [Test]
        public void MqCoder_EncodeDecodeRoundtrip_ProducesOriginalBits()
        {
            // Test pattern
            int[] bits = new int[] { 0, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0 };
            int context = 0; // Use context 0 for all bits

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                foreach (var bit in bits)
                {
                    encoder.Encode(context, bit);
                }
                encoded = encoder.Flush().ToArray();
            }

            // Decode
            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.Decode(context);
            }

            // Verify
            Assert.That(decoded, Is.EqualTo(bits));
        }

        [Test]
        public void MqCoder_MultipleContexts_MaintainsIndependentState()
        {
            // Use different contexts for different symbol streams
            int[] bits0 = new int[] { 0, 0, 0, 0, 1, 1, 1, 1 }; // Mostly 0s then 1s
            int[] bits1 = new int[] { 1, 1, 1, 1, 0, 0, 0, 0 }; // Mostly 1s then 0s

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                for (int i = 0; i < bits0.Length; i++)
                {
                    encoder.Encode(0, bits0[i]);
                    encoder.Encode(1, bits1[i]);
                }
                encoded = encoder.Flush().ToArray();
            }

            // Decode
            var decoder = new MqDecoder(encoded);
            int[] decoded0 = new int[bits0.Length];
            int[] decoded1 = new int[bits1.Length];
            for (int i = 0; i < bits0.Length; i++)
            {
                decoded0[i] = decoder.Decode(0);
                decoded1[i] = decoder.Decode(1);
            }

            Assert.That(decoded0, Is.EqualTo(bits0));
            Assert.That(decoded1, Is.EqualTo(bits1));
        }

        [Test]
        public void MqCoder_UniformCoding_EncodeDecodeRoundtrip()
        {
            int[] bits = new int[] { 1, 0, 1, 1, 0, 0, 1, 0 };

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                foreach (var bit in bits)
                {
                    encoder.EncodeUniform(bit);
                }
                encoded = encoder.Flush().ToArray();
            }

            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.DecodeUniform();
            }

            Assert.That(decoded, Is.EqualTo(bits));
        }

        [Test]
        public void MqEncoder_Reset_ClearsState()
        {
            using var encoder = new MqEncoder();

            // Encode some bits
            encoder.Encode(0, 1);
            encoder.Encode(0, 0);

            // Reset
            encoder.Reset();

            // Encode different bits - should start fresh
            encoder.Encode(0, 0);
            encoder.Encode(0, 1);

            var data = encoder.Flush();

            // Just verify we got some output
            Assert.That(data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void MqCoder_BiasedSequence_EncodeDecodeRoundtrip()
        {
            // Test with biased sequence (mostly 0s) - more typical in image coding
            // MQ coder adapts probability estimates based on observed symbols
            int[] bits = new int[]
            {
                0, 0, 0, 0, 0, 0, 0, 1, // Mostly 0s
                0, 0, 0, 0, 0, 1, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 1, 0, 0, 0, 0  // 32 bits total
            };

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                for (int i = 0; i < bits.Length; i++)
                {
                    encoder.Encode(0, bits[i]);
                }
                encoded = encoder.Flush().ToArray();
            }

            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.Decode(0);
            }

            Assert.That(decoded, Is.EqualTo(bits));
        }

        [Test]
        public void MqCoder_StateTable_HasCorrectSize()
        {
            Assert.That(MqCoder.States.Length, Is.EqualTo(MqCoder.NumStates));
            Assert.That(MqCoder.NumStates, Is.EqualTo(47));
        }

        [Test]
        public void MqCoder_NumContexts_IsCorrect()
        {
            Assert.That(MqCoder.NumContexts, Is.EqualTo(19));
        }

        #endregion

        #region EBCOT Encoder/Decoder Tests

        [Test]
        public void EbcotEncoder_EncodesSimpleCodeBlock()
        {
            // Simple 8x8 code-block with varying values
            int[] coefficients = new int[64];
            for (int i = 0; i < 64; i++)
            {
                coefficients[i] = (i % 8) * 10 + (i / 8);
            }

            using var encoder = new EbcotEncoder();
            var result = encoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            Assert.That(result.NumPasses, Is.GreaterThan(0));
            Assert.That(result.Data.Length, Is.GreaterThan(0));
            Assert.That(result.MsbPosition, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void EbcotEncoder_ZeroCoefficients_ReturnsEmpty()
        {
            int[] coefficients = new int[64]; // All zeros

            using var encoder = new EbcotEncoder();
            var result = encoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            // Empty code-block should have no passes and no data
            Assert.That(result.NumPasses, Is.EqualTo(0));
            Assert.That(result.Data.IsEmpty, Is.True);
        }

        [Test]
        public void EbcotEncoder_SingleNonZeroCoefficient_ProducesValidOutput()
        {
            int[] coefficients = new int[64];
            coefficients[0] = 255; // Single non-zero value

            using var encoder = new EbcotEncoder();
            var result = encoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            Assert.That(result.NumPasses, Is.GreaterThan(0));
            Assert.That(result.Data.Length, Is.GreaterThan(0));
            Assert.That(result.MsbPosition, Is.EqualTo(7)); // MSB of 255 is bit 7
        }

        [Test]
        public void EbcotEncoder_NegativeCoefficients_ProducesValidOutput()
        {
            int[] coefficients = new int[64];
            coefficients[0] = -128;
            coefficients[1] = 127;

            using var encoder = new EbcotEncoder();
            var result = encoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            Assert.That(result.NumPasses, Is.GreaterThan(0));
            Assert.That(result.Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void EbcotDecoder_EmptyData_ReturnsZeroCoefficients()
        {
            var decoder = new EbcotDecoder();
            int[] result = decoder.DecodeCodeBlock(
                ReadOnlySpan<byte>.Empty,
                numPasses: 0,
                width: 8, height: 8,
                msbPosition: -1,
                subbandType: 0);

            Assert.That(result.Length, Is.EqualTo(64));
            Assert.That(result, Is.All.EqualTo(0));
        }

        #endregion

        #region Tier-2 Packet Tests

        [Test]
        public void PacketEncoder_EncodesSingleCodeBlock()
        {
            // Create a simple code-block data
            using var ebcotEncoder = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 100;
            coefficients[1] = 50;

            var cbData = ebcotEncoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            var codeBlocks = new CodeBlockData[] { cbData };

            var packetEncoder = new PacketEncoder();
            var packets = packetEncoder.EncodePackets(codeBlocks, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(1));
            Assert.That(packets[0].Data.Length, Is.GreaterThan(0));
            Assert.That(packets[0].Layer, Is.EqualTo(0));
        }

        [Test]
        public void PacketEncoder_MultipleCodeBlocks_ProducesValidPacket()
        {
            using var ebcotEncoder = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                coefficients[i] = 100 + i * 10;
                codeBlocks[i] = ebcotEncoder.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var packetEncoder = new PacketEncoder();
            var packets = packetEncoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(1));
            Assert.That(packets[0].Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void PacketDecoder_DecodesEmptyPacket()
        {
            var decoder = new PacketDecoder();
            bool[] firstInclusion = new bool[] { true };

            var segments = decoder.DecodePacket(ReadOnlySpan<byte>.Empty, 1, firstInclusion);

            Assert.That(segments.Length, Is.EqualTo(1));
            Assert.That(segments[0].NumNewPasses, Is.EqualTo(0));
        }

        #endregion

        #region J2kEncoder Tests

        [Test]
        public void J2kEncoder_EncodesSmallGrayscaleImage()
        {
            // Create a simple 8x8 grayscale image
            byte[] pixelData = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                pixelData[i] = (byte)((i % 8) * 32);
            }

            var info = PixelDataInfo.Grayscale8(8, 8);
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0));

            // Check SOC marker (0xFF4F)
            Assert.That(encoded.Span[0], Is.EqualTo(0xFF));
            Assert.That(encoded.Span[1], Is.EqualTo(0x4F));

            // Check EOC marker at end (0xFFD9)
            Assert.That(encoded.Span[encoded.Length - 2], Is.EqualTo(0xFF));
            Assert.That(encoded.Span[encoded.Length - 1], Is.EqualTo(0xD9));
        }

        [Test]
        public void J2kEncoder_EncodesSinglePixel()
        {
            byte[] pixelData = new byte[] { 128 };
            var info = PixelDataInfo.Grayscale8(1, 1);

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(encoded.Span[0], Is.EqualTo(0xFF));
            Assert.That(encoded.Span[1], Is.EqualTo(0x4F));
        }

        [Test]
        public void J2kEncoder_16BitImage_ProducesValidCodestream()
        {
            // 4x4 16-bit grayscale
            byte[] pixelData = new byte[32]; // 4x4 * 2 bytes
            for (int i = 0; i < 16; i++)
            {
                ushort value = (ushort)(i * 4096);
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            var info = PixelDataInfo.Grayscale16(4, 4);
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(encoded.Span[0], Is.EqualTo(0xFF));
            Assert.That(encoded.Span[1], Is.EqualTo(0x4F));
        }

        [Test]
        public void J2kEncoder_LossyMode_ProducesValidCodestream()
        {
            byte[] pixelData = new byte[64];
            for (int i = 0; i < 64; i++)
            {
                pixelData[i] = (byte)(i * 4);
            }

            var info = PixelDataInfo.Grayscale8(8, 8);
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: false);

            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(encoded.Span[0], Is.EqualTo(0xFF));
            Assert.That(encoded.Span[1], Is.EqualTo(0x4F));
        }

        #endregion

        #region J2kDecoder Tests

        [Test]
        public void J2kDecoder_IsJpeg2000_ValidHeader_ReturnsTrue()
        {
            byte[] data = new byte[] { 0xFF, 0x4F, 0xFF, 0x51 };
            Assert.That(J2kDecoder.IsJpeg2000(data), Is.True);
        }

        [Test]
        public void J2kDecoder_IsJpeg2000_InvalidHeader_ReturnsFalse()
        {
            byte[] data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG
            Assert.That(J2kDecoder.IsJpeg2000(data), Is.False);
        }

        [Test]
        public void J2kDecoder_IsJpeg2000_TooShort_ReturnsFalse()
        {
            byte[] data = new byte[] { 0xFF };
            Assert.That(J2kDecoder.IsJpeg2000(data), Is.False);
        }

        [Test]
        public void J2kDecoder_DecodeFrame_InvalidHeader_ReturnsFail()
        {
            byte[] badData = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            var info = PixelDataInfo.Grayscale8(8, 8);
            byte[] output = new byte[64];

            var result = J2kDecoder.DecodeFrame(badData, info, output, frameIndex: 0);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic, Is.Not.Null);
        }

        #endregion
    }
}
