using System;
using System.IO;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Unit tests for <see cref="Part10Reader"/>.
    /// </summary>
    [TestFixture]
    public class Part10ReaderTests
    {
        #region Preamble and DICM Prefix Tests

        [Test]
        public void TryParseHeader_ValidPreambleAndDicm_Succeeds()
        {
            // 128 bytes preamble + DICM + FMI
            var buffer = CreateValidPart10Header();

            var reader = new Part10Reader();
            Assert.That(reader.TryParseHeader(buffer), Is.True);
            Assert.That(reader.HasDicmPrefix, Is.True);
            Assert.That(reader.Preamble.Length, Is.EqualTo(128));
        }

        [Test]
        public void TryParseHeader_NoPreamble_DicmAtStart_Succeeds()
        {
            // DICM at position 0 + FMI
            var buffer = CreatePart10WithoutPreamble();

            var reader = new Part10Reader();
            Assert.That(reader.TryParseHeader(buffer), Is.True);
            Assert.That(reader.HasDicmPrefix, Is.True);
            Assert.That(reader.Preamble.Length, Is.EqualTo(0));
        }

        [Test]
        public void TryParseHeader_NoDicm_RequireMode_Throws()
        {
            // Raw dataset, no DICM
            var buffer = CreateRawDataset();

            var reader = new Part10Reader(DicomReaderOptions.Strict);
            Assert.Throws<DicomPreambleException>(() => reader.TryParseHeader(buffer));
        }

        [Test]
        public void TryParseHeader_NoDicm_OptionalMode_Succeeds()
        {
            // Raw dataset, no DICM
            var buffer = CreateRawDataset();

            var reader = new Part10Reader(DicomReaderOptions.Lenient);
            Assert.That(reader.TryParseHeader(buffer), Is.True);
            Assert.That(reader.HasDicmPrefix, Is.False);
        }

        [Test]
        public void TryParseHeader_NoDicm_PermissiveMode_Succeeds()
        {
            // Raw dataset, no DICM
            var buffer = CreateRawDataset();

            var reader = new Part10Reader(DicomReaderOptions.Permissive);
            Assert.That(reader.TryParseHeader(buffer), Is.True);
            Assert.That(reader.HasDicmPrefix, Is.False);
        }

        [Test]
        public void TryParseHeader_EmptyFile_OptionalMode_Throws()
        {
            var buffer = Array.Empty<byte>();

            var reader = new Part10Reader(DicomReaderOptions.Lenient);
            Assert.Throws<DicomFileException>(() => reader.TryParseHeader(buffer));
        }

        [Test]
        public void TryParseHeader_NonDicomFile_OptionalMode_Throws()
        {
            // Some random bytes that don't look like DICOM
            var buffer = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }; // JPEG header

            var reader = new Part10Reader(DicomReaderOptions.Lenient);
            Assert.Throws<DicomFileException>(() => reader.TryParseHeader(buffer));
        }

        #endregion

        #region File Meta Information Tests

        [Test]
        public void TryParseHeader_ExtractsTransferSyntax()
        {
            var buffer = CreateValidPart10Header();

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            // Should have extracted transfer syntax from FMI
            Assert.That(reader.FileMetaInfo, Is.Not.Null);
            Assert.That(reader.TransferSyntax.IsKnown, Is.True);
            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.True);
            Assert.That(reader.TransferSyntax.IsLittleEndian, Is.True);
        }

        [Test]
        public void TryParseHeader_ExtractsFileMetaInfo()
        {
            var buffer = CreateValidPart10Header();

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.FileMetaInfo, Is.Not.Null);
            Assert.That(reader.FileMetaInfo!.Contains(DicomTag.TransferSyntaxUID), Is.True);
        }

        [Test]
        public void TryParseHeader_ExtractsMediaStorageSOPClassUID()
        {
            var buffer = CreateValidPart10Header();

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.FileMetaInfo, Is.Not.Null);
            Assert.That(reader.FileMetaInfo!.Contains(DicomTag.MediaStorageSOPClassUID), Is.True);

            var sopClassUid = reader.FileMetaInfo.GetString(DicomTag.MediaStorageSOPClassUID);
            Assert.That(sopClassUid, Is.Not.Null);
            Assert.That(sopClassUid, Does.StartWith("1.2.840.10008"));
        }

        [Test]
        public void TryParseHeader_MissingFMI_RequireMode_Throws()
        {
            // DICM prefix but no FMI elements
            using var ms = new MemoryStream();
            ms.Write(new byte[128]); // Preamble
            ms.Write("DICM"u8);
            // Start directly with group 0008 element
            WriteElement(ms, 0x0008, 0x0005, "CS", "ISO_IR 100 "u8.ToArray());

            var buffer = ms.ToArray();
            var reader = new Part10Reader(DicomReaderOptions.Strict);
            Assert.Throws<DicomMetaInfoException>(() => reader.TryParseHeader(buffer));
        }

        [Test]
        public void TryParseHeader_MissingFMI_OptionalMode_UsesDefaults()
        {
            // DICM prefix but no FMI elements
            using var ms = new MemoryStream();
            ms.Write(new byte[128]); // Preamble
            ms.Write("DICM"u8);
            // Start directly with group 0008 element
            WriteElement(ms, 0x0008, 0x0005, "CS", "ISO_IR 100 "u8.ToArray());

            var buffer = ms.ToArray();
            var reader = new Part10Reader(DicomReaderOptions.Lenient);
            reader.TryParseHeader(buffer);

            // Should use default Implicit VR Little Endian
            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.False);
            Assert.That(reader.TransferSyntax.IsLittleEndian, Is.True);
        }

        #endregion

        #region Dataset Start Position Tests

        [Test]
        public void DatasetStartPosition_AfterFmi()
        {
            var buffer = CreateValidPart10Header();

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            // Dataset should start after preamble (128) + DICM (4) + FMI
            Assert.That(reader.DatasetStartPosition, Is.GreaterThan(132));
        }

        [Test]
        public void DatasetStartPosition_NoPreamble_AfterFmi()
        {
            var buffer = CreatePart10WithoutPreamble();

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            // Dataset should start after DICM (4) + FMI
            Assert.That(reader.DatasetStartPosition, Is.GreaterThan(4));
        }

        [Test]
        public void DatasetStartPosition_RawDataset_AtStart()
        {
            var buffer = CreateRawDataset();

            var reader = new Part10Reader(DicomReaderOptions.Lenient);
            reader.TryParseHeader(buffer);

            // Dataset should start at position 0
            Assert.That(reader.DatasetStartPosition, Is.EqualTo(0));
        }

        #endregion

        #region Transfer Syntax Tests

        [Test]
        public void TryParseHeader_ExplicitVRLittleEndian_Recognized()
        {
            var buffer = CreatePart10WithTransferSyntax("1.2.840.10008.1.2.1"); // Explicit VR LE

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.1"));
            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.True);
            Assert.That(reader.TransferSyntax.IsLittleEndian, Is.True);
            Assert.That(reader.TransferSyntax.IsEncapsulated, Is.False);
        }

        [Test]
        public void TryParseHeader_ImplicitVRLittleEndian_Recognized()
        {
            var buffer = CreatePart10WithTransferSyntax("1.2.840.10008.1.2"); // Implicit VR LE

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2"));
            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.False);
            Assert.That(reader.TransferSyntax.IsLittleEndian, Is.True);
        }

        [Test]
        public void TryParseHeader_JPEGBaseline_Recognized()
        {
            var buffer = CreatePart10WithTransferSyntax("1.2.840.10008.1.2.4.50"); // JPEG Baseline

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.True);
            Assert.That(reader.TransferSyntax.IsEncapsulated, Is.True);
            Assert.That(reader.TransferSyntax.IsLossy, Is.True);
        }

        [Test]
        public void TryParseHeader_UnknownTransferSyntax_HandledGracefully()
        {
            var buffer = CreatePart10WithTransferSyntax("1.2.3.4.5.6.7.8.9"); // Unknown TS

            var reader = new Part10Reader();
            reader.TryParseHeader(buffer);

            Assert.That(reader.TransferSyntax.IsKnown, Is.False);
            // Unknown TSes default to explicit VR LE as most common
            Assert.That(reader.TransferSyntax.IsExplicitVR, Is.True);
        }

        #endregion

        #region VR Helper Property Tests

        [Test]
        public void IsStringVR_StringVRs_ReturnsTrue()
        {
            Assert.That(DicomVR.AE.IsStringVR, Is.True);
            Assert.That(DicomVR.AS.IsStringVR, Is.True);
            Assert.That(DicomVR.CS.IsStringVR, Is.True);
            Assert.That(DicomVR.DA.IsStringVR, Is.True);
            Assert.That(DicomVR.DS.IsStringVR, Is.True);
            Assert.That(DicomVR.DT.IsStringVR, Is.True);
            Assert.That(DicomVR.IS.IsStringVR, Is.True);
            Assert.That(DicomVR.LO.IsStringVR, Is.True);
            Assert.That(DicomVR.LT.IsStringVR, Is.True);
            Assert.That(DicomVR.PN.IsStringVR, Is.True);
            Assert.That(DicomVR.SH.IsStringVR, Is.True);
            Assert.That(DicomVR.ST.IsStringVR, Is.True);
            Assert.That(DicomVR.TM.IsStringVR, Is.True);
            Assert.That(DicomVR.UC.IsStringVR, Is.True);
            Assert.That(DicomVR.UI.IsStringVR, Is.True);
            Assert.That(DicomVR.UR.IsStringVR, Is.True);
            Assert.That(DicomVR.UT.IsStringVR, Is.True);
        }

        [Test]
        public void IsStringVR_NonStringVRs_ReturnsFalse()
        {
            Assert.That(DicomVR.FL.IsStringVR, Is.False);
            Assert.That(DicomVR.FD.IsStringVR, Is.False);
            Assert.That(DicomVR.SL.IsStringVR, Is.False);
            Assert.That(DicomVR.SS.IsStringVR, Is.False);
            Assert.That(DicomVR.UL.IsStringVR, Is.False);
            Assert.That(DicomVR.US.IsStringVR, Is.False);
            Assert.That(DicomVR.AT.IsStringVR, Is.False);
            Assert.That(DicomVR.OB.IsStringVR, Is.False);
            Assert.That(DicomVR.OW.IsStringVR, Is.False);
            Assert.That(DicomVR.SQ.IsStringVR, Is.False);
            Assert.That(DicomVR.UN.IsStringVR, Is.False);
        }

        [Test]
        public void IsNumericVR_NumericVRs_ReturnsTrue()
        {
            Assert.That(DicomVR.FL.IsNumericVR, Is.True);
            Assert.That(DicomVR.FD.IsNumericVR, Is.True);
            Assert.That(DicomVR.SL.IsNumericVR, Is.True);
            Assert.That(DicomVR.SS.IsNumericVR, Is.True);
            Assert.That(DicomVR.UL.IsNumericVR, Is.True);
            Assert.That(DicomVR.US.IsNumericVR, Is.True);
            Assert.That(DicomVR.AT.IsNumericVR, Is.True);
        }

        [Test]
        public void IsNumericVR_NonNumericVRs_ReturnsFalse()
        {
            Assert.That(DicomVR.AE.IsNumericVR, Is.False);
            Assert.That(DicomVR.LO.IsNumericVR, Is.False);
            Assert.That(DicomVR.UI.IsNumericVR, Is.False);
            Assert.That(DicomVR.OB.IsNumericVR, Is.False);
            Assert.That(DicomVR.OW.IsNumericVR, Is.False);
            Assert.That(DicomVR.SQ.IsNumericVR, Is.False);
            Assert.That(DicomVR.UN.IsNumericVR, Is.False);
        }

        #endregion

        #region Helper Methods

        private static byte[] CreateValidPart10Header()
        {
            return CreatePart10WithTransferSyntax("1.2.840.10008.1.2.1");
        }

        private static byte[] CreatePart10WithTransferSyntax(string transferSyntax)
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // Calculate group length
            // FMI Version: 8 + 2 + 2 + 4 + 2 = 18 bytes
            // Media Storage SOP Class: 8 + 26 = 34 bytes
            // Transfer Syntax: 8 + padded length
            var tsBytes = System.Text.Encoding.ASCII.GetBytes(transferSyntax);
            int tsLength = (tsBytes.Length % 2 == 1) ? tsBytes.Length + 1 : tsBytes.Length;
            int fmiContentLength = 18 + 34 + 8 + tsLength;

            // (0002,0000) UL GroupLength
            WriteElement(ms, 0x0002, 0x0000, "UL", BitConverter.GetBytes((uint)fmiContentLength));

            // (0002,0001) OB FileMetaInformationVersion
            WriteElementLong(ms, 0x0002, 0x0001, "OB", new byte[] { 0x00, 0x01 });

            // (0002,0002) UI MediaStorageSOPClassUID - CT Image Storage
            var sopClassUid = "1.2.840.10008.5.1.4.1.1.2 ";  // 26 chars (even)
            WriteElement(ms, 0x0002, 0x0002, "UI", System.Text.Encoding.ASCII.GetBytes(sopClassUid));

            // (0002,0010) UI TransferSyntaxUID
            var tsValue = tsBytes.Length % 2 == 1
                ? System.Text.Encoding.ASCII.GetBytes(transferSyntax + "\0")
                : tsBytes;
            WriteElement(ms, 0x0002, 0x0010, "UI", tsValue);

            return ms.ToArray();
        }

        private static byte[] CreatePart10WithoutPreamble()
        {
            using var ms = new MemoryStream();

            // DICM prefix (no preamble)
            ms.Write("DICM"u8);

            // Minimal FMI
            var tsUid = "1.2.840.10008.1.2.1 "; // 20 chars (even)
            WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes(tsUid));

            return ms.ToArray();
        }

        private static byte[] CreateRawDataset()
        {
            using var ms = new MemoryStream();

            // Start with typical dataset element (Specific Character Set)
            WriteElement(ms, 0x0008, 0x0005, "CS", "ISO_IR 100 "u8.ToArray());

            return ms.ToArray();
        }

        private static void WriteElement(MemoryStream ms, ushort group, ushort element,
            string vr, byte[] value)
        {
            // Tag
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            // VR
            ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));
            // Length (16-bit)
            ms.Write(BitConverter.GetBytes((ushort)value.Length));
            // Value
            ms.Write(value);
        }

        private static void WriteElementLong(MemoryStream ms, ushort group, ushort element,
            string vr, byte[] value)
        {
            // Tag
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            // VR
            ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));
            // Reserved
            ms.Write(new byte[2]);
            // Length (32-bit)
            ms.Write(BitConverter.GetBytes((uint)value.Length));
            // Value
            ms.Write(value);
        }

        #endregion
    }
}
