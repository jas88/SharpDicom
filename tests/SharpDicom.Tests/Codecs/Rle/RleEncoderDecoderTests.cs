using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Rle;

// Alias to disambiguate from SharpDicom.Data.PixelDataInfo
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Rle
{
    [TestFixture]
    public class RleEncoderDecoderTests
    {
        #region DecodeSegment Tests

        [Test]
        public void DecodeSegment_LiteralRun_DecodesCorrectly()
        {
            // Arrange - literal run of 5 bytes: [4, 1, 2, 3, 4, 5]
            // Header byte 4 means copy next 5 bytes literally
            var compressed = new byte[] { 4, 1, 2, 3, 4, 5 };
            var output = new byte[10];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(5));
            Assert.That(output[0..5], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void DecodeSegment_ReplicateRun_DecodesCorrectly()
        {
            // Arrange - replicate run: [-4 (0xFC), 0xAB] means repeat 0xAB 5 times
            // Header byte -4 means repeat next byte (-(-4)+1) = 5 times
            var compressed = new byte[] { 0xFC, 0xAB };
            var output = new byte[10];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(5));
            Assert.That(output[0..5], Is.EqualTo(new byte[] { 0xAB, 0xAB, 0xAB, 0xAB, 0xAB }));
        }

        [Test]
        public void DecodeSegment_MixedRuns_DecodesCorrectly()
        {
            // Arrange - mixed: literal(3: A,B,C), replicate(4: D), literal(2: E,F)
            // [2, A, B, C, 0xFD, D, 1, E, F]
            var compressed = new byte[] { 2, 0x41, 0x42, 0x43, 0xFD, 0x44, 1, 0x45, 0x46 };
            var output = new byte[20];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(9)); // 3 + 4 + 2
            Assert.That(output[0..9], Is.EqualTo(new byte[] { 0x41, 0x42, 0x43, 0x44, 0x44, 0x44, 0x44, 0x45, 0x46 }));
        }

        [Test]
        public void DecodeSegment_NoOp_IsSkipped()
        {
            // Arrange - no-op byte is -128 (0x80), should be skipped
            var compressed = new byte[] { 0x80, 2, 0x41, 0x42, 0x43 };
            var output = new byte[10];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(3)); // Only the literal run after noop
            Assert.That(output[0..3], Is.EqualTo(new byte[] { 0x41, 0x42, 0x43 }));
        }

        [Test]
        public void DecodeSegment_EmptyInput_ReturnsZero()
        {
            // Arrange
            var compressed = Array.Empty<byte>();
            var output = new byte[10];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(0));
        }

        [Test]
        public void DecodeSegment_MaxLiteralRun_DecodesCorrectly()
        {
            // Arrange - maximum literal run: header 127 = 128 bytes
            var compressed = new byte[129]; // header + 128 bytes
            compressed[0] = 127; // 127 + 1 = 128 bytes
            for (int i = 0; i < 128; i++)
            {
                compressed[i + 1] = (byte)i;
            }
            var output = new byte[256];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(128));
            for (int i = 0; i < 128; i++)
            {
                Assert.That(output[i], Is.EqualTo((byte)i));
            }
        }

        [Test]
        public void DecodeSegment_MaxReplicateRun_DecodesCorrectly()
        {
            // Arrange - maximum replicate run: header -127 (0x81) = 128 repetitions
            var compressed = new byte[] { 0x81, 0xFF };
            var output = new byte[256];

            // Act
            int decoded = RleDecoder.DecodeSegment(compressed, output);

            // Assert
            Assert.That(decoded, Is.EqualTo(128));
            for (int i = 0; i < 128; i++)
            {
                Assert.That(output[i], Is.EqualTo(0xFF));
            }
        }

        #endregion

        #region EncodeSegment Tests

        [Test]
        public void EncodeSegment_AllSameBytes_CreatesReplicateRun()
        {
            // Arrange
            var input = new byte[10];
            Array.Fill(input, (byte)0xAB);
            var output = new byte[20];

            // Act
            int encoded = RleEncoder.EncodeSegment(input, output);

            // Assert - should be: header (-9 = 0xF7), value (0xAB)
            // 2 bytes total, already even so no padding needed
            Assert.That(encoded, Is.EqualTo(2));
            Assert.That(output[0], Is.EqualTo(0xF7)); // -9 in two's complement
            Assert.That(output[1], Is.EqualTo(0xAB));
        }

        [Test]
        public void EncodeSegment_AllDifferentBytes_CreatesLiteralRun()
        {
            // Arrange
            var input = new byte[] { 1, 2, 3, 4, 5 };
            var output = new byte[20];

            // Act
            int encoded = RleEncoder.EncodeSegment(input, output);

            // Assert - should be: header (4), literals (1,2,3,4,5), padding
            Assert.That(encoded, Is.EqualTo(6)); // 1 + 5 = 6 (already even)
            Assert.That(output[0], Is.EqualTo(4)); // 5 - 1 = 4
            Assert.That(output[1..6], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void EncodeSegment_MixedPattern_OptimalEncoding()
        {
            // Arrange - pattern: 1,2,3, then 5x0xAA, then 4,5
            var input = new byte[] { 1, 2, 3, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 4, 5 };
            var output = new byte[30];

            // Act
            int encoded = RleEncoder.EncodeSegment(input, output);

            // Assert - decode and verify roundtrip
            var decoded = new byte[20];
            int decodedLen = RleDecoder.DecodeSegment(output.AsSpan(0, encoded), decoded);

            Assert.That(decodedLen, Is.EqualTo(10));
            Assert.That(decoded[0..10], Is.EqualTo(input));
        }

        [Test]
        public void EncodeSegment_EmptyInput_ReturnsZero()
        {
            // Arrange
            var input = Array.Empty<byte>();
            var output = new byte[10];

            // Act
            int encoded = RleEncoder.EncodeSegment(input, output);

            // Assert
            Assert.That(encoded, Is.EqualTo(0));
        }

        [Test]
        public void EncodeSegment_OutputIsPaddedToEvenLength()
        {
            // Arrange - 3 identical bytes = 2 bytes output (header + value), needs padding
            var input = new byte[] { 0xBB, 0xBB, 0xBB };
            var output = new byte[10];

            // Act
            int encoded = RleEncoder.EncodeSegment(input, output);

            // Assert
            Assert.That(encoded % 2, Is.EqualTo(0)); // Must be even
        }

        #endregion

        #region Frame Roundtrip Tests

        [Test]
        public void Roundtrip_8BitGrayscale_MatchesOriginal()
        {
            // Arrange - 4x4 8-bit grayscale image
            var info = PixelDataInfo.Grayscale8(4, 4);
            var original = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                original[i] = (byte)(i * 16);
            }

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[16];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(16));
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_16BitGrayscale_MatchesOriginal()
        {
            // Arrange - 4x4 16-bit grayscale image
            var info = PixelDataInfo.Grayscale16(4, 4);
            var original = new byte[32];

            // Fill with 16-bit values (little-endian)
            for (int i = 0; i < 16; i++)
            {
                ushort value = (ushort)(i * 1000);
                original[i * 2] = (byte)(value & 0xFF);     // Low byte
                original[i * 2 + 1] = (byte)(value >> 8);   // High byte
            }

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[32];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(32));
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_8BitRgb_MatchesOriginal()
        {
            // Arrange - 4x4 8-bit RGB image (3 samples per pixel)
            var info = PixelDataInfo.Rgb8(4, 4);
            var original = new byte[48]; // 16 pixels * 3 bytes

            // Fill with RGB values
            for (int i = 0; i < 16; i++)
            {
                original[i * 3] = (byte)(i * 10);        // R
                original[i * 3 + 1] = (byte)(i * 11);    // G
                original[i * 3 + 2] = (byte)(i * 12);    // B
            }

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[48];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.EqualTo(48));
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_AllZeros_MatchesOriginal()
        {
            // Arrange - highly compressible all-zeros image
            var info = PixelDataInfo.Grayscale8(64, 64);
            var original = new byte[4096];

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[4096];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));

            // Verify compression achieved
            Assert.That(encoded.Length, Is.LessThan(original.Length));
        }

        [Test]
        public void Roundtrip_RandomData_MatchesOriginal()
        {
            // Arrange - incompressible random data
            var info = PixelDataInfo.Grayscale8(32, 32);
            var original = new byte[1024];
            new Random(42).NextBytes(original);

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[1024];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        [Test]
        public void Roundtrip_LargeImage_MatchesOriginal()
        {
            // Arrange - 512x512 16-bit image (common medical image size)
            var info = PixelDataInfo.Grayscale16(512, 512);
            var original = new byte[512 * 512 * 2];

            // Create gradient pattern
            for (int y = 0; y < 512; y++)
            {
                for (int x = 0; x < 512; x++)
                {
                    ushort value = (ushort)((x + y) % 65536);
                    int offset = (y * 512 + x) * 2;
                    original[offset] = (byte)(value & 0xFF);
                    original[offset + 1] = (byte)(value >> 8);
                }
            }

            // Act
            var encoded = RleEncoder.EncodeFrame(original, info);
            var decoded = new byte[512 * 512 * 2];
            var result = RleDecoder.DecodeFrame(encoded.Span, info, decoded, 0);

            // Assert
            Assert.That(result.Success, Is.True);
            Assert.That(decoded, Is.EqualTo(original));
        }

        #endregion

        #region DecodeFrame Error Tests

        [Test]
        public void DecodeFrame_InvalidHeader_ReturnsFail()
        {
            // Arrange - header too short
            var compressed = new byte[32];
            var info = PixelDataInfo.Grayscale8(4, 4);
            var output = new byte[16];

            // Act
            var result = RleDecoder.DecodeFrame(compressed, info, output, 0);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic?.Message, Does.Contain("short"));
        }

        [Test]
        public void DecodeFrame_WrongSegmentCount_ReturnsFail()
        {
            // Arrange - header says 1 segment but 16-bit needs 2
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 1u);  // Wrong count
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);

            var info = PixelDataInfo.Grayscale16(4, 4); // Needs 2 segments
            var output = new byte[32];

            // Act
            var result = RleDecoder.DecodeFrame(header, info, output, 0);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic?.Message, Does.Contain("segments"));
        }

        [Test]
        public void DecodeFrame_OutputBufferTooSmall_ReturnsFail()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(4, 4);
            var original = new byte[16];
            var encoded = RleEncoder.EncodeFrame(original, info);
            var smallOutput = new byte[8]; // Too small

            // Act
            var result = RleDecoder.DecodeFrame(encoded.Span, info, smallOutput, 0);

            // Assert
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostic?.Message, Does.Contain("too small"));
        }

        #endregion

        #region GetMaxEncodedSize Tests

        [Test]
        public void GetMaxEncodedSize_8BitGrayscale_CorrectSize()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale8(256, 256);

            // Act
            int maxSize = RleEncoder.GetMaxEncodedSize(info);

            // Assert
            // Header + worst-case expansion
            int pixelCount = 256 * 256;
            int expectedMax = 64 + (pixelCount + pixelCount / 128 + 2) * 1;
            Assert.That(maxSize, Is.EqualTo(expectedMax));
        }

        [Test]
        public void GetMaxEncodedSize_16BitGrayscale_CorrectSize()
        {
            // Arrange
            var info = PixelDataInfo.Grayscale16(256, 256);

            // Act
            int maxSize = RleEncoder.GetMaxEncodedSize(info);

            // Assert
            // Header + 2 segments (high/low bytes)
            int pixelCount = 256 * 256;
            int expectedMax = 64 + (pixelCount + pixelCount / 128 + 2) * 2;
            Assert.That(maxSize, Is.EqualTo(expectedMax));
        }

        #endregion
    }
}
