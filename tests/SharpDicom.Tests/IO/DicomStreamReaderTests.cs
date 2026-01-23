using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Unit tests for <see cref="DicomStreamReader"/>.
    /// </summary>
    [TestFixture]
    public class DicomStreamReaderTests
    {
        [Test]
        public void TryReadElementHeader_ShortVR_ReadsCorrectly()
        {
            // PatientID (0010,0020) LO "TEST"
            // Tag: 10 00 20 00, VR: 4C 4F (LO), Length: 04 00
            byte[] data = { 0x10, 0x00, 0x20, 0x00, 0x4C, 0x4F, 0x04, 0x00 };

            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
            Assert.That(vr, Is.EqualTo(DicomVR.LO));
            Assert.That(length, Is.EqualTo(4));
            Assert.That(reader.Position, Is.EqualTo(8));
        }

        [Test]
        public void TryReadElementHeader_LongVR_ReadsCorrectly()
        {
            // PixelData (7FE0,0010) OW length=256
            // Tag: E0 7F 10 00, VR: 4F 57 (OW), Reserved: 00 00, Length: 00 01 00 00
            byte[] data = { 0xE0, 0x7F, 0x10, 0x00, 0x4F, 0x57, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 };

            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x7FE0));
            Assert.That(tag.Element, Is.EqualTo(0x0010));
            Assert.That(vr, Is.EqualTo(DicomVR.OW));
            Assert.That(length, Is.EqualTo(256));
            Assert.That(reader.Position, Is.EqualTo(12));
        }

        [Test]
        public void TryReadElementHeader_InsufficientData_ReturnsFalse()
        {
            byte[] data = { 0x10, 0x00, 0x20, 0x00 }; // Only 4 bytes

            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.False);
        }

        [Test]
        public void TryReadElementHeader_InsufficientDataForLongVR_ReturnsFalse()
        {
            // PixelData (7FE0,0010) OW but truncated (only 8 bytes, needs 12)
            byte[] data = { 0xE0, 0x7F, 0x10, 0x00, 0x4F, 0x57, 0x00, 0x00 };

            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadElementHeader(out _, out _, out _), Is.False);
        }

        [Test]
        public void TryReadElementHeader_ImplicitVR_ReadsCorrectly()
        {
            // PatientID (0010,0020) with implicit VR, length=4
            // Tag: 10 00 20 00, Length: 04 00 00 00
            byte[] data = { 0x10, 0x00, 0x20, 0x00, 0x04, 0x00, 0x00, 0x00 };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0010));
            Assert.That(tag.Element, Is.EqualTo(0x0020));
            // VR should be looked up from dictionary (LO for PatientID)
            Assert.That(vr, Is.EqualTo(DicomVR.LO));
            Assert.That(length, Is.EqualTo(4));
            Assert.That(reader.Position, Is.EqualTo(8));
        }

        [Test]
        public void TryReadElementHeader_ImplicitVR_UnknownTag_ReturnsUN()
        {
            // Unknown private tag (0011,0010) with implicit VR
            byte[] data = { 0x11, 0x00, 0x10, 0x00, 0x04, 0x00, 0x00, 0x00 };

            var reader = new DicomStreamReader(data, explicitVR: false);
            Assert.That(reader.TryReadElementHeader(out var tag, out var vr, out var length), Is.True);

            Assert.That(tag.Group, Is.EqualTo(0x0011));
            Assert.That(tag.Element, Is.EqualTo(0x0010));
            // Unknown tag should default to UN
            Assert.That(vr, Is.EqualTo(DicomVR.UN));
            Assert.That(length, Is.EqualTo(4));
        }

        [Test]
        public void TryReadValue_ReadsCorrectly()
        {
            byte[] data = { 0x54, 0x45, 0x53, 0x54 }; // "TEST"

            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadValue(4, out var value), Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(data));
            Assert.That(reader.Position, Is.EqualTo(4));
        }

        [Test]
        public void TryReadValue_UndefinedLength_ReturnsFalse()
        {
            byte[] data = new byte[100];
            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadValue(0xFFFFFFFF, out _), Is.False);
        }

        [Test]
        public void TryReadValue_InsufficientData_ReturnsFalse()
        {
            byte[] data = new byte[10];
            var reader = new DicomStreamReader(data);
            Assert.That(reader.TryReadValue(20, out _), Is.False);
        }

        [Test]
        public void TryReadValue_ExceedsMaxLength_ThrowsException()
        {
            byte[] data = new byte[100];
            var options = new DicomReaderOptions { MaxElementLength = 50 };

            // ref struct can't be captured in lambda, so use try/catch
            DicomDataException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data, options: options);
                reader.TryReadValue(100, out _);
            }
            catch (DicomDataException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.Message, Does.Contain("exceeds maximum"));
        }

        [Test]
        public void CheckDicmPrefix_ValidPrefix_ReturnsTrue()
        {
            byte[] data = { (byte)'D', (byte)'I', (byte)'C', (byte)'M' };
            var reader = new DicomStreamReader(data);
            Assert.That(reader.CheckDicmPrefix(), Is.True);
        }

        [Test]
        public void CheckDicmPrefix_InvalidPrefix_ReturnsFalse()
        {
            byte[] data = { (byte)'D', (byte)'I', (byte)'C', (byte)'X' };
            var reader = new DicomStreamReader(data);
            Assert.That(reader.CheckDicmPrefix(), Is.False);
        }

        [Test]
        public void CheckDicmPrefix_InsufficientData_ReturnsFalse()
        {
            byte[] data = { (byte)'D', (byte)'I', (byte)'C' };
            var reader = new DicomStreamReader(data);
            Assert.That(reader.CheckDicmPrefix(), Is.False);
        }

        [Test]
        public void ReadUInt16_LittleEndian_ReadsCorrectly()
        {
            byte[] data = { 0x34, 0x12 }; // 0x1234 in LE
            var reader = new DicomStreamReader(data, littleEndian: true);
            Assert.That(reader.ReadUInt16(), Is.EqualTo(0x1234));
            Assert.That(reader.Position, Is.EqualTo(2));
        }

        [Test]
        public void ReadUInt16_BigEndian_ReadsCorrectly()
        {
            byte[] data = { 0x12, 0x34 }; // 0x1234 in BE
            var reader = new DicomStreamReader(data, littleEndian: false);
            Assert.That(reader.ReadUInt16(), Is.EqualTo(0x1234));
        }

        [Test]
        public void ReadUInt16_InsufficientData_ThrowsException()
        {
            byte[] data = { 0x34 };

            // ref struct can't be captured in lambda, so use try/catch
            DicomDataException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data);
                reader.ReadUInt16();
            }
            catch (DicomDataException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.Message, Does.Contain("UInt16"));
        }

        [Test]
        public void ReadUInt32_LittleEndian_ReadsCorrectly()
        {
            byte[] data = { 0x78, 0x56, 0x34, 0x12 }; // 0x12345678 in LE
            var reader = new DicomStreamReader(data, littleEndian: true);
            Assert.That(reader.ReadUInt32(), Is.EqualTo(0x12345678));
            Assert.That(reader.Position, Is.EqualTo(4));
        }

        [Test]
        public void ReadUInt32_BigEndian_ReadsCorrectly()
        {
            byte[] data = { 0x12, 0x34, 0x56, 0x78 }; // 0x12345678 in BE
            var reader = new DicomStreamReader(data, littleEndian: false);
            Assert.That(reader.ReadUInt32(), Is.EqualTo(0x12345678));
        }

        [Test]
        public void ReadUInt32_InsufficientData_ThrowsException()
        {
            byte[] data = { 0x78, 0x56, 0x34 };

            // ref struct can't be captured in lambda, so use try/catch
            DicomDataException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data);
                reader.ReadUInt32();
            }
            catch (DicomDataException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.Message, Does.Contain("UInt32"));
        }

        [Test]
        public void Skip_MovesPosition()
        {
            byte[] data = new byte[100];
            var reader = new DicomStreamReader(data);
            reader.Skip(50);
            Assert.That(reader.Position, Is.EqualTo(50));
            Assert.That(reader.Remaining, Is.EqualTo(50));
        }

        [Test]
        public void Skip_ExceedsRemaining_ThrowsException()
        {
            byte[] data = new byte[10];

            // ref struct can't be captured in lambda, so use try/catch
            ArgumentOutOfRangeException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data);
                reader.Skip(20);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
        }

        [Test]
        public void Peek_ReturnsBytesWithoutAdvancingPosition()
        {
            byte[] data = { 0x01, 0x02, 0x03, 0x04 };
            var reader = new DicomStreamReader(data);

            var peeked = reader.Peek(2);
            Assert.That(peeked.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02 }));
            Assert.That(reader.Position, Is.EqualTo(0)); // Position unchanged
        }

        [Test]
        public void Peek_ExceedsRemaining_ThrowsException()
        {
            byte[] data = new byte[10];

            // ref struct can't be captured in lambda, so use try/catch
            ArgumentOutOfRangeException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data);
                reader.Peek(20);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
        }

        [Test]
        public void ReadBytes_ReturnsBytesAndAdvancesPosition()
        {
            byte[] data = { 0x01, 0x02, 0x03, 0x04 };
            var reader = new DicomStreamReader(data);

            var bytes = reader.ReadBytes(2);
            Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02 }));
            Assert.That(reader.Position, Is.EqualTo(2));
        }

        [Test]
        public void ReadBytes_ExceedsRemaining_ThrowsException()
        {
            byte[] data = new byte[10];

            // ref struct can't be captured in lambda, so use try/catch
            DicomDataException? caught = null;
            try
            {
                var reader = new DicomStreamReader(data);
                reader.ReadBytes(20);
            }
            catch (DicomDataException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
        }

        [Test]
        public void IsAtEnd_EmptyBuffer_ReturnsTrue()
        {
            byte[] empty = Array.Empty<byte>();
            var reader = new DicomStreamReader(empty);
            Assert.That(reader.IsAtEnd, Is.True);
        }

        [Test]
        public void IsAtEnd_AfterReadingAll_ReturnsTrue()
        {
            byte[] data = { 0x01, 0x02 };
            var reader = new DicomStreamReader(data);
            reader.Skip(2);
            Assert.That(reader.IsAtEnd, Is.True);
        }

        [Test]
        public void Remaining_ReturnsCorrectValue()
        {
            byte[] data = new byte[100];
            var reader = new DicomStreamReader(data);
            Assert.That(reader.Remaining, Is.EqualTo(100));

            reader.Skip(30);
            Assert.That(reader.Remaining, Is.EqualTo(70));
        }

        #region Is32BitLength Tests

        [Test]
        public void Is32BitLength_LongVRs_ReturnsTrue()
        {
            Assert.That(DicomVR.OB.Is32BitLength, Is.True);
            Assert.That(DicomVR.OD.Is32BitLength, Is.True);
            Assert.That(DicomVR.OF.Is32BitLength, Is.True);
            Assert.That(DicomVR.OL.Is32BitLength, Is.True);
            Assert.That(DicomVR.OW.Is32BitLength, Is.True);
            Assert.That(DicomVR.SQ.Is32BitLength, Is.True);
            Assert.That(DicomVR.UC.Is32BitLength, Is.True);
            Assert.That(DicomVR.UN.Is32BitLength, Is.True);
            Assert.That(DicomVR.UR.Is32BitLength, Is.True);
            Assert.That(DicomVR.UT.Is32BitLength, Is.True);
        }

        [Test]
        public void Is32BitLength_ShortVRs_ReturnsFalse()
        {
            Assert.That(DicomVR.AE.Is32BitLength, Is.False);
            Assert.That(DicomVR.AS.Is32BitLength, Is.False);
            Assert.That(DicomVR.AT.Is32BitLength, Is.False);
            Assert.That(DicomVR.CS.Is32BitLength, Is.False);
            Assert.That(DicomVR.DA.Is32BitLength, Is.False);
            Assert.That(DicomVR.DS.Is32BitLength, Is.False);
            Assert.That(DicomVR.DT.Is32BitLength, Is.False);
            Assert.That(DicomVR.FL.Is32BitLength, Is.False);
            Assert.That(DicomVR.FD.Is32BitLength, Is.False);
            Assert.That(DicomVR.IS.Is32BitLength, Is.False);
            Assert.That(DicomVR.LO.Is32BitLength, Is.False);
            Assert.That(DicomVR.LT.Is32BitLength, Is.False);
            Assert.That(DicomVR.PN.Is32BitLength, Is.False);
            Assert.That(DicomVR.SH.Is32BitLength, Is.False);
            Assert.That(DicomVR.SL.Is32BitLength, Is.False);
            Assert.That(DicomVR.SS.Is32BitLength, Is.False);
            Assert.That(DicomVR.ST.Is32BitLength, Is.False);
            Assert.That(DicomVR.TM.Is32BitLength, Is.False);
            Assert.That(DicomVR.UI.Is32BitLength, Is.False);
            Assert.That(DicomVR.UL.Is32BitLength, Is.False);
            Assert.That(DicomVR.US.Is32BitLength, Is.False);
        }

        #endregion

        #region Multiple Element Parsing Tests

        [Test]
        public void TryReadElementHeader_MultipleElements_ReadsSequentially()
        {
            // Two elements: PatientID (0010,0020) LO length=4, then PatientName (0010,0010) PN length=8
            byte[] data =
            {
                // First element
                0x10, 0x00, 0x20, 0x00, 0x4C, 0x4F, 0x04, 0x00,  // Header
                0x54, 0x45, 0x53, 0x54,                          // Value "TEST"
                // Second element
                0x10, 0x00, 0x10, 0x00, 0x50, 0x4E, 0x08, 0x00,  // Header
                0x4A, 0x4F, 0x48, 0x4E, 0x5E, 0x44, 0x4F, 0x45   // Value "JOHN^DOE"
            };

            var reader = new DicomStreamReader(data);

            // Read first element
            Assert.That(reader.TryReadElementHeader(out var tag1, out var vr1, out var length1), Is.True);
            Assert.That(tag1.Group, Is.EqualTo(0x0010));
            Assert.That(tag1.Element, Is.EqualTo(0x0020));
            Assert.That(vr1, Is.EqualTo(DicomVR.LO));
            Assert.That(length1, Is.EqualTo(4));

            Assert.That(reader.TryReadValue(length1, out var value1), Is.True);
            Assert.That(value1.ToArray(), Is.EqualTo(new byte[] { 0x54, 0x45, 0x53, 0x54 }));

            // Read second element
            Assert.That(reader.TryReadElementHeader(out var tag2, out var vr2, out var length2), Is.True);
            Assert.That(tag2.Group, Is.EqualTo(0x0010));
            Assert.That(tag2.Element, Is.EqualTo(0x0010));
            Assert.That(vr2, Is.EqualTo(DicomVR.PN));
            Assert.That(length2, Is.EqualTo(8));

            Assert.That(reader.TryReadValue(length2, out var value2), Is.True);
            Assert.That(value2.Length, Is.EqualTo(8));

            Assert.That(reader.IsAtEnd, Is.True);
        }

        #endregion

        #region Options Preset Tests

        [Test]
        public void DicomReaderOptions_Strict_HasCorrectValues()
        {
            var options = DicomReaderOptions.Strict;
            Assert.That(options.Preamble, Is.EqualTo(FilePreambleHandling.Require));
            Assert.That(options.FileMetaInfo, Is.EqualTo(FileMetaInfoHandling.Require));
            Assert.That(options.InvalidVR, Is.EqualTo(InvalidVRHandling.Throw));
        }

        [Test]
        public void DicomReaderOptions_Lenient_HasCorrectValues()
        {
            var options = DicomReaderOptions.Lenient;
            Assert.That(options.Preamble, Is.EqualTo(FilePreambleHandling.Optional));
            Assert.That(options.FileMetaInfo, Is.EqualTo(FileMetaInfoHandling.Optional));
            Assert.That(options.InvalidVR, Is.EqualTo(InvalidVRHandling.MapToUN));
        }

        [Test]
        public void DicomReaderOptions_Permissive_HasCorrectValues()
        {
            var options = DicomReaderOptions.Permissive;
            Assert.That(options.Preamble, Is.EqualTo(FilePreambleHandling.Ignore));
            Assert.That(options.FileMetaInfo, Is.EqualTo(FileMetaInfoHandling.Ignore));
            Assert.That(options.InvalidVR, Is.EqualTo(InvalidVRHandling.Preserve));
        }

        [Test]
        public void DicomReaderOptions_Default_IsSameAsLenient()
        {
            Assert.That(DicomReaderOptions.Default.Preamble, Is.EqualTo(DicomReaderOptions.Lenient.Preamble));
            Assert.That(DicomReaderOptions.Default.FileMetaInfo, Is.EqualTo(DicomReaderOptions.Lenient.FileMetaInfo));
            Assert.That(DicomReaderOptions.Default.InvalidVR, Is.EqualTo(DicomReaderOptions.Lenient.InvalidVR));
        }

        #endregion
    }
}
