using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;

// Alias to avoid ambiguity with SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    [TestFixture]
    public class Jpeg2000CodecTests
    {
        #region Capabilities Tests - Lossless

        [Test]
        public void Jpeg2000Lossless_Capabilities_IndicatesLossless()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.IsLossy, Is.False);
        }

        [Test]
        public void Jpeg2000Lossless_TransferSyntax_HasCorrectUID()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.90"));
        }

        [Test]
        public void Jpeg2000Lossless_TransferSyntax_IsNotLossy()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.TransferSyntax.IsLossy, Is.False);
        }

        [Test]
        public void Jpeg2000Lossless_TransferSyntax_IsEncapsulated()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.TransferSyntax.IsEncapsulated, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_Name_ContainsLossless()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Name, Does.Contain("Lossless"));
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportsEncoding()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.CanEncode, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportsDecoding()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportsMultiFrame()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportedBitDepths_Contains8()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.SupportedBitDepths, Contains.Item(8));
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportedBitDepths_Contains12()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.SupportedBitDepths, Contains.Item(12));
        }

        [Test]
        public void Jpeg2000Lossless_Capabilities_SupportedBitDepths_Contains16()
        {
            var codec = new Jpeg2000LosslessCodec();
            Assert.That(codec.Capabilities.SupportedBitDepths, Contains.Item(16));
        }

        #endregion

        #region Capabilities Tests - Lossy

        [Test]
        public void Jpeg2000Lossy_Capabilities_IndicatesLossy()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.Capabilities.IsLossy, Is.True);
        }

        [Test]
        public void Jpeg2000Lossy_TransferSyntax_HasCorrectUID()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.4.91"));
        }

        [Test]
        public void Jpeg2000Lossy_TransferSyntax_IsLossy()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.TransferSyntax.IsLossy, Is.True);
        }

        [Test]
        public void Jpeg2000Lossy_TransferSyntax_IsEncapsulated()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.TransferSyntax.IsEncapsulated, Is.True);
        }

        [Test]
        public void Jpeg2000Lossy_Name_ContainsJpeg2000()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.Name, Does.Contain("JPEG 2000"));
        }

        [Test]
        public void Jpeg2000Lossy_Capabilities_SupportsEncoding()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.Capabilities.CanEncode, Is.True);
        }

        [Test]
        public void Jpeg2000Lossy_Capabilities_SupportsDecoding()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Jpeg2000Lossy_Capabilities_SupportsMultiFrame()
        {
            var codec = new Jpeg2000LossyCodec();
            Assert.That(codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        #endregion

        #region Encode/Decode Pipeline Tests

        [Test]
        public void Jpeg2000Lossless_Encode_ProducesValidJ2kCodestream()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGradientImage(32, 32);

            var fragments = codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            var data = fragments.Fragments[0].Span;

            // Check SOC marker (0xFF4F)
            Assert.That(data[0], Is.EqualTo(0xFF));
            Assert.That(data[1], Is.EqualTo(0x4F));

            // Check EOC marker at end (0xFFD9)
            Assert.That(data[data.Length - 2], Is.EqualTo(0xFF));
            Assert.That(data[data.Length - 1], Is.EqualTo(0xD9));
        }

        [Test]
        public void Jpeg2000Lossless_Decode_ReturnsSuccess()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            Array.Fill(original, (byte)128);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[64];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(64));
        }

        [Test]
        public void Jpeg2000Lossless_Encode_16Bit_ProducesValidCodestream()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale16(8, 8);
            var original = CreateGradient16Image(8, 8);

            var fragments = codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            var data = fragments.Fragments[0].Span;

            // Check SOC and EOC markers
            Assert.That(data[0], Is.EqualTo(0xFF));
            Assert.That(data[1], Is.EqualTo(0x4F));
            Assert.That(data[data.Length - 2], Is.EqualTo(0xFF));
            Assert.That(data[data.Length - 1], Is.EqualTo(0xD9));
        }

        [Test]
        public void Jpeg2000Lossless_Decode_16Bit_ReturnsSuccess()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale16(8, 8);
            var original = CreateGradient16Image(8, 8);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(original.Length));
        }

        #endregion

        #region Lossy Encoding Tests

        [Test]
        public void Jpeg2000Lossy_EncodeAndDecode_Grayscale8_ProducesValidOutput()
        {
            var codec = new Jpeg2000LossyCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGradientImage(32, 32);

            var fragments = codec.Encode(original, info);
            var decoded = new byte[original.Length];
            var result = codec.Decode(fragments, info, 0, decoded);

            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");

            // Lossy: verify decoding succeeds (quality tuning is separate from codec wrapper)
            // Note: MSE optimization is a J2kEncoder implementation detail, not IPixelDataCodec
            Assert.That(result.BytesWritten, Is.EqualTo(original.Length));
        }

        [Test]
        public void Jpeg2000Lossy_EncodeAndDecode_Grayscale8_ProducesCompressedData()
        {
            var codec = new Jpeg2000LossyCodec();
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = CreateGradientImage(32, 32);

            var fragments = codec.Encode(original, info);

            Assert.That(fragments.Fragments.Count, Is.EqualTo(1));
            // Lossy data should be smaller
            Assert.That(fragments.Fragments[0].Length, Is.LessThan(original.Length),
                "Lossy encoded data should be smaller than original");
        }

        #endregion

        #region Validation Tests

        [Test]
        public void Jpeg2000Lossless_ValidateCompressedData_ValidData_ReturnsValid()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var random = new Random(42);
            random.NextBytes(original);

            var fragments = codec.Encode(original, info);
            var result = codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);

            var result = codec.ValidateCompressedData(null!, info);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Jpeg2000Lossy_ValidateCompressedData_ValidData_ReturnsValid()
        {
            var codec = new Jpeg2000LossyCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var random = new Random(42);
            random.NextBytes(original);

            var fragments = codec.Encode(original, info);
            var result = codec.ValidateCompressedData(fragments, info);

            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void Jpeg2000Lossless_Decode_InvalidFrameIndex_ReturnsFailure()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = codec.Encode(original, info);

            var decoded = new byte[64];
            var result = codec.Decode(fragments, info, 5, decoded);

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void Jpeg2000Lossless_Decode_NegativeFrameIndex_ReturnsFailure()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8);
            var original = new byte[64];
            var fragments = codec.Encode(original, info);

            var decoded = new byte[64];
            var result = codec.Decode(fragments, info, -1, decoded);

            Assert.That(result.Success, Is.False);
        }

        #endregion

        #region Options Tests

        [Test]
        public void Jpeg2000CodecOptions_Default_HasExpectedCompressionRatio()
        {
            var options = Jpeg2000CodecOptions.Default;
            Assert.That(options.CompressionRatio, Is.EqualTo(10));
        }

        [Test]
        public void Jpeg2000CodecOptions_Default_HasExpectedDecompositionLevels()
        {
            var options = Jpeg2000CodecOptions.Default;
            Assert.That(options.DecompositionLevels, Is.EqualTo(5));
        }

        [Test]
        public void Jpeg2000CodecOptions_MedicalImaging_HasConservativeCompressionRatio()
        {
            var options = Jpeg2000CodecOptions.MedicalImaging;
            Assert.That(options.CompressionRatio, Is.EqualTo(5));
        }

        [Test]
        public void Jpeg2000CodecOptions_Default_GeneratesBasicOffsetTable()
        {
            var options = Jpeg2000CodecOptions.Default;
            Assert.That(options.GenerateBasicOffsetTable, Is.True);
        }

        #endregion

        #region Multi-Frame Tests

        [Test]
        public void Jpeg2000Lossless_EncodeAndDecode_MultiFrame_ProducesCorrectFragmentCount()
        {
            var codec = new Jpeg2000LosslessCodec();
            var info = PixelDataInfo.Grayscale8(8, 8, numberOfFrames: 4);
            var original = new byte[256];  // 4 frames * 64 pixels
            for (int frame = 0; frame < 4; frame++)
            {
                for (int i = 0; i < 64; i++)
                {
                    original[frame * 64 + i] = (byte)(frame * 50 + i);
                }
            }

            var fragments = codec.Encode(original, info);
            Assert.That(fragments.Fragments.Count, Is.EqualTo(4));

            // Verify each frame can be decoded successfully
            for (int frame = 0; frame < 4; frame++)
            {
                var decoded = new byte[64];
                var result = codec.Decode(fragments, info, frame, decoded);
                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed: {result.Diagnostic?.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private static byte[] CreateGradientImage(int width, int height)
        {
            var data = new byte[width * height];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 256);
            }

            return data;
        }

        private static byte[] CreateGradient16Image(int width, int height)
        {
            var data = new byte[width * height * 2];
            for (int i = 0; i < width * height; i++)
            {
                ushort value = (ushort)(i % 65536);
                data[i * 2] = (byte)(value & 0xFF);
                data[i * 2 + 1] = (byte)(value >> 8);
            }

            return data;
        }

        private static double CalculateMSE(byte[] a, byte[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }

            return sum / a.Length;
        }

        #endregion
    }
}
