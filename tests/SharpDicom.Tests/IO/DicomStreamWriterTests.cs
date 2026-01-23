using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Tests for DicomStreamWriter element writing functionality.
    /// </summary>
    [TestFixture]
    public class DicomStreamWriterTests
    {
        #region Explicit VR Little Endian - 16-bit Length VRs

        [Test]
        public void WriteElement_ExplicitVR_AE_WritesCorrect8ByteHeader()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var options = new DicomWriterOptions
            {
                TransferSyntax = TransferSyntax.ExplicitVRLittleEndian
            };
            var writer = new DicomStreamWriter(buffer, options);

            var tag = new DicomTag(0x0008, 0x0054); // RetrieveAETitle
            var vr = DicomVR.AE;
            var value = Encoding.ASCII.GetBytes("MYAE");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(12)); // 8 header + 4 value

            // Tag: (0008,0054)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0x0008));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0x0054));

            // VR: "AE"
            Assert.That(result[4], Is.EqualTo((byte)'A'));
            Assert.That(result[5], Is.EqualTo((byte)'E'));

            // Length: 4 (16-bit)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));

            // Value
            Assert.That(result.Slice(8).ToArray(), Is.EqualTo(value));
        }

        [Test]
        public void WriteElement_ExplicitVR_CS_WritesCorrectHeader()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x0060); // Modality
            var vr = DicomVR.CS;
            var value = Encoding.ASCII.GetBytes("CT");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(10)); // 8 header + 2 value

            // VR: "CS"
            Assert.That(result[4], Is.EqualTo((byte)'C'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));

            // Length: 2
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(2));
        }

        [Test]
        public void WriteElement_ExplicitVR_DA_WritesDateValue()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x0020); // StudyDate
            var vr = DicomVR.DA;
            var value = Encoding.ASCII.GetBytes("20250127");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(16)); // 8 header + 8 value

            // VR: "DA"
            Assert.That(result[4], Is.EqualTo((byte)'D'));
            Assert.That(result[5], Is.EqualTo((byte)'A'));

            // Length: 8
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(8));
        }

        [Test]
        public void WriteElement_ExplicitVR_DS_WritesDecimalString()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0018, 0x0050); // SliceThickness
            var vr = DicomVR.DS;
            var value = Encoding.ASCII.GetBytes("1.5");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // VR: "DS"
            Assert.That(result[4], Is.EqualTo((byte)'D'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));

            // Value with padding (odd length -> padded to 4)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));
        }

        [Test]
        public void WriteElement_ExplicitVR_IS_WritesIntegerString()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0020, 0x0013); // InstanceNumber
            var vr = DicomVR.IS;
            var value = Encoding.ASCII.GetBytes("42");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'I'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(2));
        }

        [Test]
        public void WriteElement_ExplicitVR_LO_WritesLongString()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x1030); // StudyDescription
            var vr = DicomVR.LO;
            var value = Encoding.ASCII.GetBytes("Test Study");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'L'));
            Assert.That(result[5], Is.EqualTo((byte)'O'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(10));
        }

        [Test]
        public void WriteElement_ExplicitVR_PN_WritesPersonName()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0010, 0x0010); // PatientName
            var vr = DicomVR.PN;
            var value = Encoding.ASCII.GetBytes("Doe^John");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'P'));
            Assert.That(result[5], Is.EqualTo((byte)'N'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(8));
        }

        [Test]
        public void WriteElement_ExplicitVR_SH_WritesShortString()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x0050); // AccessionNumber
            var vr = DicomVR.SH;
            var value = Encoding.ASCII.GetBytes("ACC123");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'S'));
            Assert.That(result[5], Is.EqualTo((byte)'H'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(6));
        }

        [Test]
        public void WriteElement_ExplicitVR_UI_WritesUniqueIdentifier()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0020, 0x000D); // StudyInstanceUID
            var vr = DicomVR.UI;
            var value = Encoding.ASCII.GetBytes("1.2.3.4.5");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'I'));

            // UI with odd length gets null padding
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(10));
            Assert.That(result[17], Is.EqualTo((byte)'\0')); // Null padding
        }

        [Test]
        public void WriteElement_ExplicitVR_US_WritesUnsignedShort()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0028, 0x0010); // Rows
            var vr = DicomVR.US;
            var value = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(value, 512);

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(2));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(8)), Is.EqualTo(512));
        }

        [Test]
        public void WriteElement_ExplicitVR_UL_WritesUnsignedLong()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x7FE0, 0x0001); // ExtendedOffsetTable
            var vr = DicomVR.UL;
            var value = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(value, 65536);

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'L'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(8)), Is.EqualTo(65536));
        }

        [Test]
        public void WriteElement_ExplicitVR_FL_WritesSingleFloat()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0018, 0x0088); // SpacingBetweenSlices
            var vr = DicomVR.FL;
            var value = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(value, 2.5f);

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'F'));
            Assert.That(result[5], Is.EqualTo((byte)'L'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));
        }

        [Test]
        public void WriteElement_ExplicitVR_FD_WritesDoubleFloat()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0018, 0x9089); // DiffusionBValue
            var vr = DicomVR.FD;
            var value = new byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(value, 1000.0);

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'F'));
            Assert.That(result[5], Is.EqualTo((byte)'D'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(8));
        }

        [Test]
        public void WriteElement_ExplicitVR_TM_WritesTimeValue()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x0030); // StudyTime
            var vr = DicomVR.TM;
            var value = Encoding.ASCII.GetBytes("143000");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result[4], Is.EqualTo((byte)'T'));
            Assert.That(result[5], Is.EqualTo((byte)'M'));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(6));
        }

        #endregion

        #region Explicit VR Little Endian - 32-bit Length VRs

        [Test]
        public void WriteElement_ExplicitVR_OB_Writes12ByteHeader()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0042, 0x0011); // EncapsulatedDocument
            var vr = DicomVR.OB;
            var value = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(16)); // 12 header + 4 value

            // Tag: (0042,0011)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0x0042));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0x0011));

            // VR: "OB"
            Assert.That(result[4], Is.EqualTo((byte)'O'));
            Assert.That(result[5], Is.EqualTo((byte)'B'));

            // Reserved bytes
            Assert.That(result[6], Is.EqualTo(0x00));
            Assert.That(result[7], Is.EqualTo(0x00));

            // Length: 4 (32-bit)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(8)), Is.EqualTo(4));

            // Value
            Assert.That(result.Slice(12).ToArray(), Is.EqualTo(value));
        }

        [Test]
        public void WriteElement_ExplicitVR_OW_Writes32BitLength()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x7FE0, 0x0010); // PixelData
            var vr = DicomVR.OW;
            var value = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(18)); // 12 header + 6 value

            // VR: "OW"
            Assert.That(result[4], Is.EqualTo((byte)'O'));
            Assert.That(result[5], Is.EqualTo((byte)'W'));

            // Reserved bytes
            Assert.That(result[6], Is.EqualTo(0x00));
            Assert.That(result[7], Is.EqualTo(0x00));

            // Length: 6 (32-bit)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(8)), Is.EqualTo(6));
        }

        [Test]
        public void WriteElement_ExplicitVR_UN_WritesUnknown()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0009, 0x1001); // Private element
            var vr = DicomVR.UN;
            var value = new byte[] { 0xAA, 0xBB };

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(14)); // 12 header + 2 value

            // VR: "UN"
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'N'));

            // Reserved bytes
            Assert.That(result[6], Is.EqualTo(0x00));
            Assert.That(result[7], Is.EqualTo(0x00));
        }

        [Test]
        public void WriteElement_ExplicitVR_UC_WritesUnlimitedCharacters()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0018, 0x9004); // ContentQualification
            var vr = DicomVR.UC;
            var value = Encoding.ASCII.GetBytes("Test long string value here");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // VR: "UC"
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'C'));

            // Reserved bytes
            Assert.That(result[6], Is.EqualTo(0x00));
            Assert.That(result[7], Is.EqualTo(0x00));

            // 32-bit length
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(8)), Is.EqualTo(28)); // 27 + 1 padding
        }

        [Test]
        public void WriteElement_ExplicitVR_UT_WritesUnlimitedText()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0008, 0x0115); // CodingSchemeIdentificationSequence
            var vr = DicomVR.UT;
            var value = Encoding.ASCII.GetBytes("Long text content here");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // VR: "UT"
            Assert.That(result[4], Is.EqualTo((byte)'U'));
            Assert.That(result[5], Is.EqualTo((byte)'T'));

            // Reserved + 32-bit length
            Assert.That(result[6], Is.EqualTo(0x00));
            Assert.That(result[7], Is.EqualTo(0x00));
        }

        #endregion

        #region Implicit VR Little Endian

        [Test]
        public void WriteElement_ImplicitVR_Writes8ByteHeaderWithoutVR()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var options = new DicomWriterOptions
            {
                TransferSyntax = TransferSyntax.ImplicitVRLittleEndian
            };
            var writer = new DicomStreamWriter(buffer, options);

            var tag = new DicomTag(0x0008, 0x0060); // Modality
            var vr = DicomVR.CS;
            var value = Encoding.ASCII.GetBytes("CT");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(10)); // 8 header + 2 value

            // Tag: (0008,0060)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0x0008));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0x0060));

            // Length: 2 (32-bit, no VR field)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(4)), Is.EqualTo(2));

            // Value
            Assert.That(result.Slice(8, 2).ToArray(), Is.EqualTo(value));
        }

        [Test]
        public void WriteElement_ImplicitVR_OB_Uses32BitLength()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: false, littleEndian: true);

            var tag = new DicomTag(0x7FE0, 0x0010); // PixelData
            var vr = DicomVR.OW;
            var value = new byte[] { 0x00, 0x01, 0x02, 0x03 };

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(12)); // 8 header + 4 value

            // Length: 4 (32-bit)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(4)), Is.EqualTo(4));
        }

        [Test]
        public void WriteElement_ImplicitVR_UI_AppliesNullPadding()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: false, littleEndian: true);

            var tag = new DicomTag(0x0020, 0x000D); // StudyInstanceUID
            var vr = DicomVR.UI;
            var value = Encoding.ASCII.GetBytes("1.2.3"); // Odd length

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(14)); // 8 header + 6 value (padded)

            // Length: 6 (padded from 5)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(4)), Is.EqualTo(6));

            // Padding byte should be null
            Assert.That(result[13], Is.EqualTo((byte)'\0'));
        }

        #endregion

        #region Big Endian

        [Test]
        public void WriteElement_ExplicitVR_BigEndian_WritesTagBigEndian()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var options = new DicomWriterOptions
            {
                TransferSyntax = TransferSyntax.ExplicitVRBigEndian
            };
            var writer = new DicomStreamWriter(buffer, options);

            var tag = new DicomTag(0x0008, 0x0060); // Modality
            var vr = DicomVR.CS;
            var value = Encoding.ASCII.GetBytes("MR");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Tag in big endian
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(result), Is.EqualTo(0x0008));
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(result.Slice(2)), Is.EqualTo(0x0060));

            // VR: "CS" (always ASCII)
            Assert.That(result[4], Is.EqualTo((byte)'C'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));

            // Length in big endian
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(result.Slice(6)), Is.EqualTo(2));
        }

        [Test]
        public void WriteElement_ExplicitVR_BigEndian_32BitLength()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: false);

            var tag = new DicomTag(0x7FE0, 0x0010); // PixelData
            var vr = DicomVR.OW;
            var value = new byte[] { 0x00, 0x01, 0x02, 0x03 };

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Length in big endian (32-bit)
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(result.Slice(8)), Is.EqualTo(4));
        }

        #endregion

        #region Value Padding

        [Test]
        public void WriteElement_OddLengthStringVR_PadsWithSpace()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0010, 0x0010); // PatientName
            var vr = DicomVR.PN;
            var value = Encoding.ASCII.GetBytes("ABC"); // 3 bytes - odd

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Length should be 4 (padded)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));

            // Value: "ABC" + space
            Assert.That(result[8], Is.EqualTo((byte)'A'));
            Assert.That(result[9], Is.EqualTo((byte)'B'));
            Assert.That(result[10], Is.EqualTo((byte)'C'));
            Assert.That(result[11], Is.EqualTo((byte)' ')); // Space padding
        }

        [Test]
        public void WriteElement_OddLengthBinaryVR_PadsWithNull()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0042, 0x0011); // EncapsulatedDocument
            var vr = DicomVR.OB;
            var value = new byte[] { 0x01, 0x02, 0x03 }; // 3 bytes - odd

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Length should be 4 (padded)
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(8)), Is.EqualTo(4));

            // Value + null padding
            Assert.That(result[12], Is.EqualTo(0x01));
            Assert.That(result[13], Is.EqualTo(0x02));
            Assert.That(result[14], Is.EqualTo(0x03));
            Assert.That(result[15], Is.EqualTo(0x00)); // Null padding
        }

        [Test]
        public void WriteElement_OddLengthUI_PadsWithNull()
        {
            // UI VR uses null padding (0x00) not space (0x20)
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0020, 0x000D); // StudyInstanceUID
            var vr = DicomVR.UI;
            var value = Encoding.ASCII.GetBytes("1.2.3.4.5"); // 9 bytes - odd

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Length should be 10 (padded)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(10));

            // Last byte should be null, not space
            Assert.That(result[17], Is.EqualTo(0x00)); // Null padding for UI
        }

        [Test]
        public void WriteElement_EvenLengthValue_NoPadding()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0010, 0x0010); // PatientName
            var vr = DicomVR.PN;
            var value = Encoding.ASCII.GetBytes("ABCD"); // 4 bytes - even

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // Length should be 4 (no padding)
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(4));
            Assert.That(result.Length, Is.EqualTo(12)); // 8 header + 4 value
        }

        #endregion

        #region Edge Cases

        [Test]
        public void WriteElement_EmptyValue_WritesZeroLength()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0x0010, 0x0010); // PatientName
            var vr = DicomVR.PN;
            var value = ReadOnlySpan<byte>.Empty;

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(8)); // Header only
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(6)), Is.EqualTo(0));
        }

        [Test]
        public void WriteTag_WritesOnly4Bytes()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0xFFFE, 0xE000); // Item

            // Act
            writer.WriteTag(tag);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(4));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0xFFFE));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0xE000));
        }

        [Test]
        public void WriteTagWithLength_Writes8Bytes()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var tag = new DicomTag(0xFFFE, 0xE000); // Item

            // Act
            writer.WriteTagWithLength(tag, 0xFFFFFFFF);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(8));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0xFFFE));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0xE000));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(result.Slice(4)), Is.EqualTo(0xFFFFFFFF));
        }

        [Test]
        public void WriteBytes_WritesRawBytes()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            // Act
            writer.WriteBytes(bytes);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.ToArray(), Is.EqualTo(bytes));
        }

        [Test]
        public void WriteUInt16_WritesCorrectEndianness()
        {
            // Arrange - Little Endian
            var bufferLE = new ArrayBufferWriter<byte>();
            var writerLE = new DicomStreamWriter(bufferLE, explicitVR: true, littleEndian: true);

            // Arrange - Big Endian
            var bufferBE = new ArrayBufferWriter<byte>();
            var writerBE = new DicomStreamWriter(bufferBE, explicitVR: true, littleEndian: false);

            // Act
            writerLE.WriteUInt16(0x1234);
            writerBE.WriteUInt16(0x1234);

            // Assert
            Assert.That(bufferLE.WrittenSpan.ToArray(), Is.EqualTo(new byte[] { 0x34, 0x12 })); // LE
            Assert.That(bufferBE.WrittenSpan.ToArray(), Is.EqualTo(new byte[] { 0x12, 0x34 })); // BE
        }

        [Test]
        public void WriteUInt32_WritesCorrectEndianness()
        {
            // Arrange - Little Endian
            var bufferLE = new ArrayBufferWriter<byte>();
            var writerLE = new DicomStreamWriter(bufferLE, explicitVR: true, littleEndian: true);

            // Arrange - Big Endian
            var bufferBE = new ArrayBufferWriter<byte>();
            var writerBE = new DicomStreamWriter(bufferBE, explicitVR: true, littleEndian: false);

            // Act
            writerLE.WriteUInt32(0x12345678);
            writerBE.WriteUInt32(0x12345678);

            // Assert
            Assert.That(bufferLE.WrittenSpan.ToArray(), Is.EqualTo(new byte[] { 0x78, 0x56, 0x34, 0x12 })); // LE
            Assert.That(bufferBE.WrittenSpan.ToArray(), Is.EqualTo(new byte[] { 0x12, 0x34, 0x56, 0x78 })); // BE
        }

        [Test]
        public void WriteElement_WithIDicomElement_WritesCorrectly()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            var element = new DicomStringElement(
                new DicomTag(0x0010, 0x0010),
                DicomVR.PN,
                Encoding.ASCII.GetBytes("Doe^John"));

            // Act
            writer.WriteElement(element);

            // Assert
            var result = buffer.WrittenSpan;
            Assert.That(result.Length, Is.EqualTo(16)); // 8 header + 8 value

            // Verify tag
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result), Is.EqualTo(0x0010));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(result.Slice(2)), Is.EqualTo(0x0010));

            // Verify VR
            Assert.That(result[4], Is.EqualTo((byte)'P'));
            Assert.That(result[5], Is.EqualTo((byte)'N'));
        }

        [Test]
        public void WriteElement_NullElement_ThrowsArgumentNullException()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new DicomStreamWriter(buffer, explicitVR: true, littleEndian: true);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => writer.WriteElement(null!));
        }

        [Test]
        public void Constructor_NullWriter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DicomStreamWriter(null!, new DicomWriterOptions()));
        }

        #endregion

        #region Transfer Syntax Integration

        [Test]
        public void WriteElement_DeflatedExplicitVRLittleEndian_UsesExplicitVR()
        {
            // Arrange
            var buffer = new ArrayBufferWriter<byte>();
            var options = new DicomWriterOptions
            {
                TransferSyntax = TransferSyntax.DeflatedExplicitVRLittleEndian
            };
            var writer = new DicomStreamWriter(buffer, options);

            var tag = new DicomTag(0x0008, 0x0060); // Modality
            var vr = DicomVR.CS;
            var value = Encoding.ASCII.GetBytes("CT");

            // Act
            writer.WriteElement(tag, vr, value);

            // Assert
            var result = buffer.WrittenSpan;

            // VR should be present (explicit VR)
            Assert.That(result[4], Is.EqualTo((byte)'C'));
            Assert.That(result[5], Is.EqualTo((byte)'S'));
        }

        [Test]
        public void DicomWriterOptions_Default_UsesExplicitVRLittleEndian()
        {
            // Assert
            Assert.That(DicomWriterOptions.Default.TransferSyntax, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
            Assert.That(DicomWriterOptions.Default.SequenceLength, Is.EqualTo(SequenceLengthEncoding.Undefined));
            Assert.That(DicomWriterOptions.Default.BufferSize, Is.EqualTo(81920));
        }

        #endregion
    }
}
