using System;
using NUnit.Framework;
using SharpDicom.Codecs.Rle;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Tests.Codecs.Rle
{
    [TestFixture]
    public class RleSegmentHeaderTests
    {
        [Test]
        public void Parse_ValidHeader_ReturnsCorrectSegmentCount()
        {
            // Arrange - 2 segments (16-bit grayscale)
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 2u);  // Number of segments
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u); // Offset to segment 1
            BitConverter.TryWriteBytes(header.AsSpan(8), 100u); // Offset to segment 2

            // Act
            var result = RleSegmentHeader.Parse(header);

            // Assert
            Assert.That(result.NumberOfSegments, Is.EqualTo(2));
        }

        [Test]
        public void Parse_ValidHeader_ReturnsCorrectOffsets()
        {
            // Arrange - 3 segments (8-bit RGB)
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 3u);   // Number of segments
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);  // Offset to segment 1
            BitConverter.TryWriteBytes(header.AsSpan(8), 128u); // Offset to segment 2
            BitConverter.TryWriteBytes(header.AsSpan(12), 200u); // Offset to segment 3

            // Act
            var result = RleSegmentHeader.Parse(header);

            // Assert
            Assert.That(result.GetSegmentOffset(0), Is.EqualTo(64));
            Assert.That(result.GetSegmentOffset(1), Is.EqualTo(128));
            Assert.That(result.GetSegmentOffset(2), Is.EqualTo(200));
        }

        [Test]
        public void Parse_HeaderTooShort_Throws()
        {
            // Arrange
            var header = new byte[32]; // Too short

            // Act & Assert
            var ex = Assert.Throws<DicomCodecException>(() => RleSegmentHeader.Parse(header));
            Assert.That(ex!.Message, Does.Contain("too short"));
        }

        [Test]
        public void Parse_SegmentCountZero_Throws()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 0u); // Zero segments

            // Act & Assert
            var ex = Assert.Throws<DicomCodecException>(() => RleSegmentHeader.Parse(header));
            Assert.That(ex!.Message, Does.Contain("zero"));
        }

        [Test]
        public void Parse_SegmentCountTooHigh_Throws()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 16u); // 16 > MaxSegments (15)
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);

            // Act & Assert
            var ex = Assert.Throws<DicomCodecException>(() => RleSegmentHeader.Parse(header));
            Assert.That(ex!.Message, Does.Contain("exceeds maximum"));
        }

        [Test]
        public void Parse_FirstOffsetNot64_Throws()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 1u);  // 1 segment
            BitConverter.TryWriteBytes(header.AsSpan(4), 32u); // Wrong offset (should be 64)

            // Act & Assert
            var ex = Assert.Throws<DicomCodecException>(() => RleSegmentHeader.Parse(header));
            Assert.That(ex!.Message, Does.Contain("must be 64"));
        }

        [Test]
        public void GetSegmentOffset_IndexOutOfRange_Throws()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 2u);
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);
            BitConverter.TryWriteBytes(header.AsSpan(8), 100u);
            var parsed = RleSegmentHeader.Parse(header);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => parsed.GetSegmentOffset(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => parsed.GetSegmentOffset(-1));
        }

        [Test]
        public void TryParse_ValidHeader_ReturnsTrue()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 1u);
            BitConverter.TryWriteBytes(header.AsSpan(4), 64u);

            // Act
            bool result = RleSegmentHeader.TryParse(header, out var parsed, out var error);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(parsed.NumberOfSegments, Is.EqualTo(1));
        }

        [Test]
        public void TryParse_InvalidHeader_ReturnsFalseWithError()
        {
            // Arrange - header too short
            var header = new byte[32];

            // Act
            bool result = RleSegmentHeader.TryParse(header, out _, out var error);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(error, Does.Contain("too short"));
        }

        [Test]
        public void TryParse_ZeroSegments_ReturnsFalseWithError()
        {
            // Arrange
            var header = new byte[64];
            BitConverter.TryWriteBytes(header.AsSpan(0), 0u);

            // Act
            bool result = RleSegmentHeader.TryParse(header, out _, out var error);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain("zero"));
        }

        [Test]
        public void Create_ValidSegmentLengths_CreatesCorrectHeader()
        {
            // Arrange
            Span<int> lengths = stackalloc int[] { 100, 150, 200 };

            // Act
            var header = RleSegmentHeader.Create(lengths);

            // Assert
            Assert.That(header.NumberOfSegments, Is.EqualTo(3));
            Assert.That(header.GetSegmentOffset(0), Is.EqualTo(64));
            Assert.That(header.GetSegmentOffset(1), Is.EqualTo(164));  // 64 + 100
            Assert.That(header.GetSegmentOffset(2), Is.EqualTo(314));  // 64 + 100 + 150
        }

        [Test]
        public void Create_EmptySegments_Throws()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
            {
                var emptyLengths = Array.Empty<int>();
                RleSegmentHeader.Create(emptyLengths);
            });
        }

        [Test]
        public void Create_TooManySegments_Throws()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                var tooManyLengths = new int[16]; // 16 > MaxSegments (15)
                Array.Fill(tooManyLengths, 100);
                RleSegmentHeader.Create(tooManyLengths);
            });
            Assert.That(ex!.Message, Does.Contain("exceeds maximum"));
        }

        [Test]
        public void WriteTo_ProducesValidHeader()
        {
            // Arrange
            Span<int> lengths = stackalloc int[] { 100, 150 };
            var header = RleSegmentHeader.Create(lengths);
            var buffer = new byte[64];

            // Act
            header.WriteTo(buffer);

            // Assert
            var numSegments = BitConverter.ToUInt32(buffer, 0);
            var offset0 = BitConverter.ToUInt32(buffer, 4);
            var offset1 = BitConverter.ToUInt32(buffer, 8);

            Assert.That(numSegments, Is.EqualTo(2));
            Assert.That(offset0, Is.EqualTo(64));
            Assert.That(offset1, Is.EqualTo(164));
        }

        [Test]
        public void WriteTo_BufferTooSmall_Throws()
        {
            // Arrange
            Span<int> lengths = stackalloc int[] { 100 };
            var header = RleSegmentHeader.Create(lengths);
            var buffer = new byte[32]; // Too small

            // Act & Assert
            Assert.Throws<ArgumentException>(() => header.WriteTo(buffer));
        }

        [Test]
        public void Roundtrip_ParseWriteParse_Identical()
        {
            // Arrange
            var originalBuffer = new byte[64];
            BitConverter.TryWriteBytes(originalBuffer.AsSpan(0), 4u);
            BitConverter.TryWriteBytes(originalBuffer.AsSpan(4), 64u);
            BitConverter.TryWriteBytes(originalBuffer.AsSpan(8), 164u);
            BitConverter.TryWriteBytes(originalBuffer.AsSpan(12), 264u);
            BitConverter.TryWriteBytes(originalBuffer.AsSpan(16), 364u);

            // Act
            var parsed = RleSegmentHeader.Parse(originalBuffer);
            var writtenBuffer = new byte[64];
            parsed.WriteTo(writtenBuffer);
            var reparsed = RleSegmentHeader.Parse(writtenBuffer);

            // Assert
            Assert.That(reparsed.NumberOfSegments, Is.EqualTo(parsed.NumberOfSegments));
            for (int i = 0; i < parsed.NumberOfSegments; i++)
            {
                Assert.That(reparsed.GetSegmentOffset(i), Is.EqualTo(parsed.GetSegmentOffset(i)));
            }
        }
    }
}
