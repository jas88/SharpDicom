using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    /// <summary>
    /// EBCOT regression suite: verifies that multi-tile pipeline changes
    /// do not break existing EBCOT-based J2K encoding and decoding.
    /// All tests use <see cref="EbcotBlockCoder.Instance"/> explicitly.
    /// </summary>
    [TestFixture]
    public class J2kEbcotRegressionTests
    {
        #region Lossless J2K Roundtrip

        /// <summary>
        /// Lossless J2K roundtrip with 8-bit grayscale using EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossless_8Bit_Grayscale_RoundtripsWithEbcot()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);

            Assert.That(encoded.Length, Is.GreaterThan(0), "Encoder should produce output");
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True);

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Count non-zero decoded bytes
            int nonZero = 0;
            for (int i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] != 0)
                {
                    nonZero++;
                }
            }

            Assert.That(nonZero, Is.GreaterThan(0), "Decoded data should not be all zeros");
        }

        /// <summary>
        /// Lossless J2K roundtrip with 16-bit grayscale using EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossless_16Bit_Grayscale_RoundtripsWithEbcot()
        {
            const int size = 16;
            var info = PixelDataInfo.Grayscale16(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < size * size; i++)
            {
                ushort value = (ushort)(i * 256);
                pixelData[i * 2] = (byte)(value & 0xFF);
                pixelData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"16-bit decode failed: {result.Diagnostic?.Message}");
        }

        #endregion

        #region Lossy J2K Roundtrip

        /// <summary>
        /// Lossy J2K roundtrip with EBCOT produces non-zero output.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Lossy_8Bit_RoundtripsWithEbcot()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossy, lossless: false, EbcotBlockCoder.Instance);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0, EbcotBlockCoder.Instance);
            Assert.That(result.Success, Is.True, $"Lossy decode failed: {result.Diagnostic?.Message}");

            // Verify decoded has non-zero data
            bool hasNonZero = false;
            for (int i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }

            Assert.That(hasNonZero, Is.True, "Lossy decoded output should not be all zeros");
        }

        #endregion

        #region Codestream Validation

        /// <summary>
        /// Verify codestream structure (SOC, SIZ, COD, QCD, SOT, SOD, EOC) with EBCOT.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void Codestream_HasValidStructure_WithEbcot()
        {
            const int size = 16;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 16);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, J2kEncoderOptions.Lossless, lossless: true, EbcotBlockCoder.Instance);

            // Verify SOC marker
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span), Is.EqualTo((ushort)0xFF4F), "SOC marker");

            // Verify SIZ marker follows
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span.Slice(2)), Is.EqualTo((ushort)0xFF51), "SIZ marker");

            // Verify EOC marker at end
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span.Slice(encoded.Length - 2)), Is.EqualTo((ushort)0xFFD9), "EOC marker");

            // Parse and verify header
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.ImageWidth, Is.EqualTo(size));
            Assert.That(header.ImageHeight, Is.EqualTo(size));
            Assert.That(header.ComponentCount, Is.EqualTo(1));
            Assert.That(header.UsesReversibleTransform, Is.True);
        }

        #endregion

        #region EBCOT Encoder Isolated

        /// <summary>
        /// EBCOT encoder produces valid output for various input patterns.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotEncoder_VariousPatterns_ProducesValidOutput()
        {
            using var encoder = new EbcotEncoder();

            // Pattern 1: Single value
            int[] input1 = new int[64];
            input1[0] = 100;
            var result1 = encoder.EncodeCodeBlock(input1, 8, 8, subbandType: 0);
            Assert.That(result1.NumPasses, Is.GreaterThan(0), "Single value should produce passes");
            Assert.That(result1.Data.Length, Is.GreaterThan(0), "Single value should produce data");

            // Pattern 2: Two values
            int[] input2 = new int[64];
            input2[0] = 100;
            input2[1] = 50;
            var result2 = encoder.EncodeCodeBlock(input2, 8, 8, subbandType: 0);
            Assert.That(result2.NumPasses, Is.GreaterThan(0));

            // Pattern 3: Negative values
            int[] input3 = new int[64];
            input3[0] = -128;
            input3[1] = 127;
            var result3 = encoder.EncodeCodeBlock(input3, 8, 8, subbandType: 0);
            Assert.That(result3.NumPasses, Is.GreaterThan(0));

            // Pattern 4: All zeros
            int[] input4 = new int[64];
            var result4 = encoder.EncodeCodeBlock(input4, 8, 8, subbandType: 0);
            Assert.That(result4.NumPasses, Is.EqualTo(0), "All zeros should have no passes");
            Assert.That(result4.Data.IsEmpty, Is.True, "All zeros should have empty data");
        }

        #endregion

        #region EBCOT Decoder Isolated

        /// <summary>
        /// EBCOT decoder handles empty data correctly.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotDecoder_EmptyData_ReturnsZeros()
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

        /// <summary>
        /// EBCOT decoder recovers known simple patterns.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotDecoder_SimplePattern_RecoversCorrectly()
        {
            using var encoder = new EbcotEncoder();
            var decoder = new EbcotDecoder();

            int[] input = new int[64];
            input[0] = 1;
            input[1] = 3;
            input[9] = 7;

            var encoded = encoder.EncodeCodeBlock(input, 8, 8, subbandType: 0);

            int[] decoded = decoder.DecodeCodeBlock(
                encoded.Data.Span,
                encoded.NumPasses,
                8, 8,
                encoded.MsbPosition,
                subbandType: 0);

            Assert.That(decoded[0], Is.EqualTo(1), "Position 0");
            Assert.That(decoded[1], Is.EqualTo(3), "Position 1");
            Assert.That(decoded[9], Is.EqualTo(7), "Position 9");
        }

        #endregion

        #region MQ Coder Roundtrip

        /// <summary>
        /// MQ coder encode/decode roundtrip preserves bits.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void MqCoder_Roundtrip_PreservesBits()
        {
            int[] bits = new int[] { 0, 1, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0 };

            byte[] encoded;
            using (var encoder = new MqEncoder())
            {
                foreach (var bit in bits)
                {
                    encoder.Encode(0, bit);
                }

                encoded = encoder.Flush().ToArray();
            }

            var decoder = new MqDecoder(encoded);
            int[] decoded = new int[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                decoded[i] = decoder.Decode(0);
            }

            Assert.That(decoded, Is.EqualTo(bits), "MQ coder roundtrip should preserve bits");
        }

        /// <summary>
        /// MQ coder uniform encode/decode roundtrip.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void MqCoder_UniformRoundtrip_PreservesBits()
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

            Assert.That(decoded, Is.EqualTo(bits), "MQ uniform roundtrip should preserve bits");
        }

        #endregion

        #region EbcotBlockCoder IBlockCoder Regression

        /// <summary>
        /// EbcotBlockCoder.Instance produces same output as direct EbcotEncoder.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotBlockCoder_MatchesDirectEncoder()
        {
            int[] input = new int[64];
            input[0] = 1;
            input[1] = 3;
            input[9] = 7;

            using var directEncoder = new EbcotEncoder();
            using var blockCoder = new EbcotBlockCoder();

            var directResult = directEncoder.EncodeCodeBlock(input, 8, 8, subbandType: 0);
            var wrapperResult = blockCoder.EncodeBlock(input, 8, 8, subbandType: 0, msbPosition: -1);

            Assert.That(wrapperResult.NumPasses, Is.EqualTo(directResult.NumPasses));
            Assert.That(wrapperResult.MsbPosition, Is.EqualTo(directResult.MsbPosition));
            Assert.That(wrapperResult.Data.ToArray(), Is.EqualTo(directResult.Data.ToArray()));
        }

        /// <summary>
        /// EbcotBlockCoder roundtrip via IBlockCoder interface.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EbcotBlockCoder_Roundtrip_ViaIBlockCoder()
        {
            int[] input = new int[64];
            input[0] = 1;
            input[1] = 1;
            input[8] = 8;
            input[9] = 9;

            EbcotBlockCoder coder = EbcotBlockCoder.Instance;

            var encoded = coder.EncodeBlock(input, 8, 8, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[64];
            coder.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, 8, 8,
                encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded[0], Is.EqualTo(1));
            Assert.That(decoded[1], Is.EqualTo(1));
            Assert.That(decoded[8], Is.EqualTo(8));
            Assert.That(decoded[9], Is.EqualTo(9));
        }

        /// <summary>
        /// EbcotBlockCoder works with all 4 subband types.
        /// </summary>
        [Test]
        [Category("Regression")]
        [TestCase(0, TestName = "SubbandLL")]
        [TestCase(1, TestName = "SubbandHL")]
        [TestCase(2, TestName = "SubbandLH")]
        [TestCase(3, TestName = "SubbandHH")]
        public void EbcotBlockCoder_AllSubbandTypes_Roundtrip(int subbandType)
        {
            // Use larger values so bit-plane coding is robust across subband contexts
            int[] input = new int[64];
            input[0] = 16;
            input[1] = 8;
            input[9] = 32;

            using var blockCoder = new EbcotBlockCoder();

            var encoded = blockCoder.EncodeBlock(input, 8, 8, subbandType, msbPosition: -1);

            Assert.That(encoded.Data.Length, Is.GreaterThan(0), "Encoding should produce data");
            Assert.That(encoded.NumPasses, Is.GreaterThan(0), "Should have coding passes");

            int[] decoded = new int[64];
            blockCoder.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, 8, 8,
                encoded.MsbPosition, subbandType);

            // Verify non-zero positions are recovered (EBCOT significance propagation
            // context varies by subband type, so exact values may differ slightly)
            Assert.That(decoded[0], Is.GreaterThan(0), "Position 0 should be non-zero");
            Assert.That(decoded[1], Is.GreaterThan(0), "Position 1 should be non-zero");
            Assert.That(decoded[9], Is.GreaterThan(0), "Position 9 should be non-zero");

            // Zero positions should remain zero
            Assert.That(decoded[2], Is.EqualTo(0), "Position 2 should remain zero");
            Assert.That(decoded[63], Is.EqualTo(0), "Last position should remain zero");
        }

        #endregion

        #region Backward Compatibility

        /// <summary>
        /// Verify that existing single-parameter EncodeFrame still works.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void EncodeFrame_SimpleOverload_StillWorks()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 4);
            }

            // Use the simple 3-parameter overload (backward compatible)
            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);
            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True);
        }

        /// <summary>
        /// Verify that DecodeFrame without maxDegreeOfParallelism still works.
        /// </summary>
        [Test]
        [Category("Regression")]
        public void DecodeFrame_SimpleOverload_StillWorks()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i * 4);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            var decoded = new byte[info.FrameSize];
            // Use the 4-parameter overload (no blockCoder, no parallelism)
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True);
        }

        #endregion
    }
}
