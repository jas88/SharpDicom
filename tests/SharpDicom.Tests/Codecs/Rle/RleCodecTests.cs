using System;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Rle;
using SharpDicom.Data;

// Alias to disambiguate from SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Rle
{
    [TestFixture]
    public class RleCodecTests
    {
        private RleCodec _codec = null!;

        [SetUp]
        public void Setup()
        {
            CodecRegistry.Reset();
            _codec = new RleCodec();
        }

        #region Property Tests

        [Test]
        public void TransferSyntax_IsRleLossless()
        {
            Assert.That(_codec.TransferSyntax, Is.EqualTo(TransferSyntax.RLELossless));
        }

        [Test]
        public void Name_IsRleLossless()
        {
            Assert.That(_codec.Name, Is.EqualTo("RLE Lossless"));
        }

        [Test]
        public void Capabilities_CanEncode()
        {
            Assert.That(_codec.Capabilities.CanEncode, Is.True);
        }

        [Test]
        public void Capabilities_CanDecode()
        {
            Assert.That(_codec.Capabilities.CanDecode, Is.True);
        }

        [Test]
        public void Capabilities_IsLossless()
        {
            Assert.That(_codec.Capabilities.IsLossy, Is.False);
        }

        [Test]
        public void Capabilities_SupportsMultiFrame()
        {
            Assert.That(_codec.Capabilities.SupportsMultiFrame, Is.True);
        }

        [Test]
        public void Capabilities_Supports8And16BitDepths()
        {
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(8));
            Assert.That(_codec.Capabilities.SupportedBitDepths, Contains.Item(16));
        }

        [Test]
        public void Capabilities_SupportsGrayscaleAndRgb()
        {
            Assert.That(_codec.Capabilities.SupportedSamplesPerPixel, Contains.Item(1));
            Assert.That(_codec.Capabilities.SupportedSamplesPerPixel, Contains.Item(3));
        }

        #endregion

        #region Encode Tests

        [Test]
        public void Encode_SingleFrame_CreatesValidFragment()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var pixelData = new byte[16];
            for (int i = 0; i < 16; i++) pixelData[i] = (byte)(i * 16);

            // Act
            var fragments = _codec.Encode(pixelData, info);

            // Assert
            Assert.That(fragments.FragmentCount, Is.EqualTo(1));
            Assert.That(fragments.Fragments[0].Length, Is.GreaterThan(64)); // At least header
        }

        [Test]
        public void Encode_MultiFrame_CreatesMultipleFragments()
        {
            // Arrange - 3 frames
            var info = PixelDataInfo.Grayscale8(4, 4, 3);
            var pixelData = new byte[48]; // 3 * 16
            for (int i = 0; i < 48; i++) pixelData[i] = (byte)(i % 256);

            // Act
            var fragments = _codec.Encode(pixelData, info);

            // Assert
            Assert.That(fragments.FragmentCount, Is.EqualTo(3));
        }

        [Test]
        public void Encode_WithBasicOffsetTable_GeneratesOffsets()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4, 3);
            var pixelData = new byte[48];
            var options = new RleCodecOptions { GenerateBasicOffsetTable = true };

            // Act
            var fragments = _codec.Encode(pixelData, info, options);

            // Assert - BOT should have 3 offsets (12 bytes)
            Assert.That(fragments.OffsetTable.IsEmpty, Is.False);
            Assert.That(fragments.ParsedBasicOffsets.Count, Is.EqualTo(3));
        }

        [Test]
        public void Encode_WithoutBasicOffsetTable_NoOffsets()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4, 3);
            var pixelData = new byte[48];
            var options = new RleCodecOptions { GenerateBasicOffsetTable = false };

            // Act
            var fragments = _codec.Encode(pixelData, info, options);

            // Assert
            Assert.That(fragments.OffsetTable.IsEmpty, Is.True);
        }

        #endregion

        #region Decode Tests

        [Test]
        public void Decode_ValidFragment_ReturnsSuccess()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var original = new byte[16];
            for (int i = 0; i < 16; i++) original[i] = (byte)(i * 16);

            var fragments = _codec.Encode(original, info);
            var destination = new byte[16];

            // Act
            var result = _codec.Decode(fragments, info, 0, destination);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(16));
        }

        [Test]
        public void Decode_InvalidFrameIndex_ReturnsFail()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new List<ReadOnlyMemory<byte>> { new byte[100] });
            var destination = new byte[16];

            // Act
            var result = _codec.Decode(fragments, info, 5, destination); // Index 5 doesn't exist

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic?.Message, Does.Contain("out of range"));
        }

        [Test]
        public void Decode_NegativeFrameIndex_ReturnsFail()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new List<ReadOnlyMemory<byte>> { new byte[100] });
            var destination = new byte[16];

            // Act
            var result = _codec.Decode(fragments, info, -1, destination);

            // Assert
            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void Decode_NullFragments_Throws()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var destination = new byte[16];

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _codec.Decode(null!, info, 0, destination));
        }

        #endregion

        #region Roundtrip Tests

        [Test]
        public void Roundtrip_8BitGrayscale_Lossless()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            new Random(42).NextBytes(original);

            // Act
            var fragments = _codec.Encode(original, info);
            var decoded = new byte[1024];
            var result = _codec.Decode(fragments, info, 0, decoded);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_16BitGrayscale_Lossless()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale16(32, 32);
            var original = new byte[2048];
            new Random(43).NextBytes(original);

            // Act
            var fragments = _codec.Encode(original, info);
            var decoded = new byte[2048];
            var result = _codec.Decode(fragments, info, 0, decoded);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_8BitRgb_Lossless()
        {
            // Arrange
            var info = PixelDataInfo.Rgb8(32, 32);
            var original = new byte[3072]; // 32*32*3
            new Random(44).NextBytes(original);

            // Act
            var fragments = _codec.Encode(original, info);
            var decoded = new byte[3072];
            var result = _codec.Decode(fragments, info, 0, decoded);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_MultiFrame_AllFramesCorrect()
        {
            // Arrange - 3 frames
            var info = PixelDataInfo.Grayscale8(16, 16, 3);
            var original = new byte[768]; // 256 * 3
            new Random(45).NextBytes(original);

            // Act
            var fragments = _codec.Encode(original, info);

            // Assert each frame
            for (int frame = 0; frame < 3; frame++)
            {
                var decoded = new byte[256];
                var result = _codec.Decode(fragments, info, frame, decoded);

                Assert.That(result.Success, Is.True, $"Frame {frame} decode failed");
                Assert.That(decoded, Is.EqualTo(original.AsSpan(frame * 256, 256).ToArray()),
                    $"Frame {frame} data mismatch");
            }
        }

        #endregion

        #region Validation Tests

        [Test]
        public void ValidateCompressedData_ValidData_ReturnsValid()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var original = new byte[16];
            var fragments = _codec.Encode(original, info);

            // Act
            var result = _codec.ValidateCompressedData(fragments, info);

            // Assert
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }

        [Test]
        public void ValidateCompressedData_WrongSegmentCount_ReturnsInvalid()
        {
            // Arrange - create a fragment with wrong segment count
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 1u);  // 1 segment
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);

            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new List<ReadOnlyMemory<byte>> { header });

            var info = PixelDataInfo.Grayscale16(4, 4); // Expects 2 segments

            // Act
            var result = _codec.ValidateCompressedData(fragments, info);

            // Assert
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Count, Is.EqualTo(1));
            Assert.That(result.Issues[0].Message, Does.Contain("segment count"));
        }

        [Test]
        public void ValidateCompressedData_InvalidHeader_ReturnsInvalid()
        {
            // Arrange - fragment too short for valid header
            var fragments = new DicomFragmentSequence(
                DicomTag.PixelData,
                DicomVR.OB,
                ReadOnlyMemory<byte>.Empty,
                new List<ReadOnlyMemory<byte>> { new byte[32] }); // Too short

            var info = PixelDataInfo.Grayscale8(4, 4);

            // Act
            var result = _codec.ValidateCompressedData(fragments, info);

            // Assert
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Count, Is.EqualTo(1));
        }

        [Test]
        public void ValidateCompressedData_NullFragments_ReturnsInvalid()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);

            // Act
            var result = _codec.ValidateCompressedData(null!, info);

            // Assert
            Assert.That(result.IsValid, Is.False);
        }

        #endregion

        #region Async Tests

        [Test]
        public async System.Threading.Tasks.Task DecodeAsync_ValidFragment_ReturnsSuccess()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var original = new byte[16];
            var fragments = _codec.Encode(original, info);
            var destination = new byte[16];

            // Act
            var result = await _codec.DecodeAsync(fragments, info, 0, destination);

            // Assert
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public async System.Threading.Tasks.Task EncodeAsync_ValidPixelData_ReturnsFragments()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var pixelData = new byte[16];

            // Act
            var fragments = await _codec.EncodeAsync(pixelData, info);

            // Assert
            Assert.That(fragments.FragmentCount, Is.EqualTo(1));
        }

        #endregion

        #region Registry Tests

        [Test]
        public void RegisteredInCodecRegistry_CanRetrieve()
        {
            // Arrange & Act
            CodecRegistry.Register<RleCodec>();
            var codec = CodecRegistry.GetCodec(TransferSyntax.RLELossless);

            // Assert
            Assert.That(codec, Is.Not.Null);
            Assert.That(codec, Is.TypeOf<RleCodec>());
        }

        [Test]
        public void CodecRegistry_CanDecode_ReturnsTrue()
        {
            // Arrange
            CodecRegistry.Register<RleCodec>();

            // Act & Assert
            Assert.That(CodecRegistry.CanDecode(TransferSyntax.RLELossless), Is.True);
        }

        [Test]
        public void CodecRegistry_CanEncode_ReturnsTrue()
        {
            // Arrange
            CodecRegistry.Register<RleCodec>();

            // Act & Assert
            Assert.That(CodecRegistry.CanEncode(TransferSyntax.RLELossless), Is.True);
        }

        #endregion
    }
}
