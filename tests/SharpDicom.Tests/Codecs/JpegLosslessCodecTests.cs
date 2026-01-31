using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.JpegLossless;

// Alias to avoid ambiguity with SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs
{
    [TestFixture]
    public class JpegLosslessCodecTests
    {
        private JpegLosslessCodec _codec = null!;

        [SetUp]
        public void Setup()
        {
            _codec = new JpegLosslessCodec();
        }

        #region Capabilities Tests

        [Test]
        public void Capabilities_IndicatesLossless()
        {
            Assert.That(_codec.Capabilities.IsLossy, Is.False);
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
        public void Capabilities_SupportedBitDepths_Contains12()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(12));
        }

        [Test]
        public void Capabilities_SupportedBitDepths_Contains16()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(16));
        }

        [Test]
        public void TransferSyntax_HasCorrectUID()
        {
            Assert.That(_codec.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.70"));
        }

        [Test]
        public void TransferSyntax_IsNotLossy()
        {
            Assert.That(_codec.TransferSyntax.IsLossy, Is.False);
        }

        [Test]
        public void TransferSyntax_IsEncapsulated()
        {
            Assert.That(_codec.TransferSyntax.IsEncapsulated, Is.True);
        }

        [Test]
        public void Name_IncludesProcess14()
        {
            Assert.That(_codec.Name, Does.Contain("Process 14"));
        }

        #endregion

        #region Predictor Tests

        [Test]
        public void Predictor_SelectionValue1_ReturnsLeftNeighbor()
        {
            int predicted = Predictor.Predict(1, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo(100));
        }

        [Test]
        public void Predictor_SelectionValue2_ReturnsAboveNeighbor()
        {
            int predicted = Predictor.Predict(2, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo(50));
        }

        [Test]
        public void Predictor_SelectionValue3_ReturnsDiagonalNeighbor()
        {
            int predicted = Predictor.Predict(3, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo(75));
        }

        [Test]
        public void Predictor_SelectionValue4_ReturnsAPlusBMinusC()
        {
            int predicted = Predictor.Predict(4, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo(100 + 50 - 75)); // 75
        }

        [Test]
        public void Predictor_SelectionValue5_ReturnsAPlusHalfBMinusC()
        {
            int predicted = Predictor.Predict(5, a: 100, b: 50, c: 40);
            Assert.That(predicted, Is.EqualTo(100 + (50 - 40) / 2)); // 105
        }

        [Test]
        public void Predictor_SelectionValue6_ReturnsBPlusHalfAMinusC()
        {
            int predicted = Predictor.Predict(6, a: 100, b: 50, c: 40);
            Assert.That(predicted, Is.EqualTo(50 + (100 - 40) / 2)); // 80
        }

        [Test]
        public void Predictor_SelectionValue7_ReturnsAverageOfAandB()
        {
            int predicted = Predictor.Predict(7, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo((100 + 50) / 2)); // 75
        }

        [Test]
        public void Predictor_SelectionValue0_ReturnsZero()
        {
            int predicted = Predictor.Predict(0, a: 100, b: 50, c: 75);
            Assert.That(predicted, Is.EqualTo(0));
        }

        [Test]
        public void Predictor_InvalidSelectionValue_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Predictor.Predict(8, 100, 50, 75));
        }

        [Test]
        public void Predictor_GetDefaultValue_8Bit_Returns128()
        {
            int defaultValue = Predictor.GetDefaultValue(8, 0);
            Assert.That(defaultValue, Is.EqualTo(128));
        }

        [Test]
        public void Predictor_GetDefaultValue_16Bit_Returns32768()
        {
            int defaultValue = Predictor.GetDefaultValue(16, 0);
            Assert.That(defaultValue, Is.EqualTo(32768));
        }

        [Test]
        public void Predictor_GetDefaultValue_12Bit_Returns2048()
        {
            int defaultValue = Predictor.GetDefaultValue(12, 0);
            Assert.That(defaultValue, Is.EqualTo(2048));
        }

        #endregion

        #region Huffman Category Tests

        [Test]
        public void LosslessHuffman_GetCategory_Zero_ReturnsZero()
        {
            Assert.That(LosslessHuffman.GetCategory(0), Is.EqualTo(0));
        }

        [Test]
        public void LosslessHuffman_GetCategory_One_ReturnsOne()
        {
            Assert.That(LosslessHuffman.GetCategory(1), Is.EqualTo(1));
        }

        [Test]
        public void LosslessHuffman_GetCategory_MinusOne_ReturnsOne()
        {
            Assert.That(LosslessHuffman.GetCategory(-1), Is.EqualTo(1));
        }

        [Test]
        public void LosslessHuffman_GetCategory_Two_ReturnsTwo()
        {
            Assert.That(LosslessHuffman.GetCategory(2), Is.EqualTo(2));
        }

        [Test]
        public void LosslessHuffman_GetCategory_Three_ReturnsTwo()
        {
            Assert.That(LosslessHuffman.GetCategory(3), Is.EqualTo(2));
        }

        [Test]
        public void LosslessHuffman_GetCategory_255_ReturnsEight()
        {
            Assert.That(LosslessHuffman.GetCategory(255), Is.EqualTo(8));
        }

        [Test]
        public void LosslessHuffman_GetCategory_Minus256_ReturnsNine()
        {
            Assert.That(LosslessHuffman.GetCategory(-256), Is.EqualTo(9));
        }

        #endregion

        #region 8-bit Roundtrip Tests

        [Test]
        public void EncodeAndDecode_Grayscale8_BitPerfectRoundtrip()
        {
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                original[i] = (byte)i;
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original), "Lossless roundtrip must be bit-perfect");
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_UniformImage_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            Array.Fill(original, (byte)128);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_AllBlack_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            // All zeros

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_AllWhite_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            Array.Fill(original, (byte)255);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_RandomData_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            var random = new Random(42); // Fixed seed for reproducibility
            random.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[1024];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void EncodeAndDecode_Grayscale8_GradientVertical_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = new byte[256];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    original[y * 16 + x] = (byte)(y * 16); // Vertical gradient
                }
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        #endregion

        #region 16-bit Roundtrip Tests

        [Test]
        public void EncodeAndDecode_Grayscale16_BitPerfectRoundtrip()
        {
            var info = PixelDataInfo.Grayscale16(16, 16);
            var original = new byte[512];  // 256 pixels * 2 bytes
            for (int i = 0; i < 256; i++)
            {
                // Little-endian 16-bit values: 0, 256, 512, ...
                ushort value = (ushort)(i * 256);
                original[i * 2] = (byte)(value & 0xFF);
                original[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[512];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
            Assert.That(decoded, Is.EqualTo(original), "16-bit lossless roundtrip must be bit-perfect");
        }

        [Test]
        public void EncodeAndDecode_Grayscale16_RandomData_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale16(32, 32);
            var original = new byte[2048]; // 1024 pixels * 2 bytes
            var random = new Random(42);
            random.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[2048];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void EncodeAndDecode_Grayscale16_MaxValues_BitPerfect()
        {
            var info = PixelDataInfo.Grayscale16(8, 8);
            var original = new byte[128];
            for (int i = 0; i < 64; i++)
            {
                // Fill with 0xFFFF (max 16-bit value)
                original[i * 2] = 0xFF;
                original[i * 2 + 1] = 0xFF;
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[128];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
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

            var fragments = _codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // Gradient data should compress well with DPCM
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(original.Length),
                "Encoded data should be smaller than original for compressible data");
        }

        #endregion

        #region Multi-Frame Tests

        [Test]
        public void EncodeAndDecode_MultiFrame_AllFramesCorrect()
        {
            var info = PixelDataInfo.Grayscale8(8, 8, numberOfFrames: 4);
            var original = new byte[256];  // 4 frames * 64 pixels
            for (int frame = 0; frame < 4; frame++)
            {
                for (int i = 0; i < 64; i++)
                {
                    original[frame * 64 + i] = (byte)(frame * 50 + i);
                }
            }

            var fragments = _codec.Encode(original, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(4));

            // Decode each frame
            for (int frame = 0; frame < 4; frame++)
            {
                var decoded = new byte[64];
                var result = _codec.Decode(fragments, info, frame, decoded);

                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed");

                var expected = new byte[64];
                Array.Copy(original, frame * 64, expected, 0, 64);
                Assert.That(decoded, Is.EqualTo(expected), $"Frame {frame} mismatch");
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
        public void JpegLosslessCodecOptions_Default_HasSelectionValue1()
        {
            var options = JpegLosslessCodecOptions.Default;
            Assert.That(options.SelectionValue, Is.EqualTo(1));
        }

        [Test]
        public void JpegLosslessCodecOptions_Default_GeneratesOffsetTable()
        {
            var options = JpegLosslessCodecOptions.Default;
            Assert.That(options.GenerateBasicOffsetTable, Is.True);
        }

        [Test]
        public void Encode_InvalidSelectionValue_ThrowsArgumentException()
        {
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];

            // SV1 transfer syntax requires SelectionValue=1
            var options = new JpegLosslessCodecOptions(SelectionValue: 3);

            Assert.Throws<ArgumentException>(() => _codec.Encode(original, info, options));
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void EncodeAndDecode_ExtremeValues_8Bit_RoundtripSucceeds()
        {
            // Test with extreme values: 0 and 255
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];

            // Fill with extreme pattern: alternating min/max
            for (int i = 0; i < 64; i++)
            {
                original[i] = (i % 2 == 0) ? (byte)0 : (byte)255;
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[64];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Extreme 8-bit values must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_ExtremeValues_16Bit_RoundtripSucceeds()
        {
            // Test with extreme 16-bit values: 0 and 65535
            var info = PixelDataInfo.Grayscale16(8, 8);
            var original = new byte[128]; // 64 pixels * 2 bytes

            // Fill with extreme pattern
            for (int i = 0; i < 64; i++)
            {
                ushort value = (i % 2 == 0) ? (ushort)0 : (ushort)65535;
                original[i * 2] = (byte)(value & 0xFF);
                original[i * 2 + 1] = (byte)(value >> 8);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[128];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Extreme 16-bit values must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_OddDimensions_RoundtripSucceeds()
        {
            // Test with non-power-of-2 dimensions (7x13)
            var info = PixelDataInfo.Grayscale8(7, 13);
            var original = new byte[7 * 13]; // 91 pixels

            var random = new Random(42);
            random.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[91];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Odd dimensions must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_SinglePixel_RoundtripSucceeds()
        {
            // Edge case: 1x1 image
            var info = PixelDataInfo.Grayscale8(1, 1);
            var original = new byte[] { 128 };

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[1];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Single pixel must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_SingleRow_RoundtripSucceeds()
        {
            // Edge case: 1 row, multiple columns
            var info = PixelDataInfo.Grayscale8(16, 1);
            var original = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                original[i] = (byte)(i * 16);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[16];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Single row must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_SingleColumn_RoundtripSucceeds()
        {
            // Edge case: multiple rows, 1 column
            var info = PixelDataInfo.Grayscale8(1, 16);
            var original = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                original[i] = (byte)(i * 16);
            }

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[16];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Single column must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_LargerImage_RoundtripSucceeds()
        {
            // Test with a larger image (256x256)
            var info = PixelDataInfo.Grayscale8(256, 256);
            var original = new byte[256 * 256];

            var random = new Random(42);
            random.NextBytes(original);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[256 * 256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Larger image must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_UniformData_RoundtripSucceeds()
        {
            // Edge case: all pixels have same value (worst case for DPCM compression)
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];

            // All pixels = 128
            Array.Fill(original, (byte)128);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[1024];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "Uniform data must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_AllZeros_RoundtripSucceeds()
        {
            // Edge case: all zeros
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = new byte[256]; // All zeros by default

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "All-zero data must roundtrip exactly");
        }

        [Test]
        public void EncodeAndDecode_AllMax_RoundtripSucceeds()
        {
            // Edge case: all maximum values
            var info = PixelDataInfo.Grayscale8(16, 16);
            var original = new byte[256];
            Array.Fill(original, (byte)255);

            var fragments = _codec.Encode(original, info);
            var decoded = new byte[256];
            var result = _codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original), "All-max data must roundtrip exactly");
        }

        #endregion
    }
}
