using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Roundtrip integration tests for DICOM file read-write-read identity.
    /// </summary>
    [TestFixture]
    public class DicomRoundtripTests
    {
        // Additional tags not in WellKnown
        private static readonly DicomTag StudyDate = new(0x0008, 0x0020);
        private static readonly DicomTag Modality = new(0x0008, 0x0060);
        private static readonly DicomTag AccessionNumber = new(0x0008, 0x0050);
        private static readonly DicomTag StudyDescription = new(0x0008, 0x1030);
        private static readonly DicomTag DerivationDescription = new(0x0008, 0x2111);
        private static readonly DicomTag ScheduledProcedureStepSequence = new(0x0040, 0x0100);
        private static readonly DicomTag ReferencedStudySequence = new(0x0008, 0x1110);
        private static readonly DicomTag ReferencedSeriesSequence = new(0x0008, 0x1115);

        // Standard UIDs for tests
        private const string TestSOPClassUID = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage
        private const string TestSOPInstanceUID = "1.2.3.4.5.6.7.8.9.0";

        #region Basic Roundtrip Tests

        [Test]
        public async Task Roundtrip_SimpleDataset_ValuesPreserved()
        {
            // Arrange - create simple dataset
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
            originalDataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "12345"));
            originalDataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "CT"));

            var originalFile = new DicomFile(originalDataset);

            // Act - write to memory
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);

            // Read back
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - verify identity
            Assert.That(roundtripFile.Dataset.GetString(DicomTag.PatientName), Is.EqualTo("Test^Patient"));
            Assert.That(roundtripFile.Dataset.GetString(DicomTag.PatientID), Is.EqualTo("12345"));
            Assert.That(roundtripFile.Dataset.GetString(StudyDate), Is.EqualTo("20240115"));
            Assert.That(roundtripFile.Dataset.GetString(Modality), Is.EqualTo("CT"));
        }

        [Test]
        public async Task Roundtrip_MultipleValueTypes_AllPreserved()
        {
            // Arrange - dataset with various VRs
            var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);

            // String VRs
            originalDataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Last^First^Middle"));
            originalDataset.Add(CreateStringElement(AccessionNumber, DicomVR.SH, "ACC123"));
            originalDataset.Add(CreateStringElement(StudyDescription, DicomVR.LO, "Brain MRI"));

            // Numeric VRs
            originalDataset.Add(new DicomBinaryElement(DicomTag.Rows, DicomVR.US, BitConverter.GetBytes((ushort)512)));
            originalDataset.Add(new DicomBinaryElement(DicomTag.Columns, DicomVR.US, BitConverter.GetBytes((ushort)512)));

            // Binary VR (private tag)
            originalDataset.Add(new DicomBinaryElement(new DicomTag(0x0029, 0x0010), DicomVR.OB, binaryData));

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            Assert.That(roundtripFile.Dataset.GetString(DicomTag.PatientName), Is.EqualTo("Last^First^Middle"));
            Assert.That(roundtripFile.Dataset.GetString(AccessionNumber), Is.EqualTo("ACC123"));
            Assert.That(roundtripFile.Dataset.GetString(StudyDescription), Is.EqualTo("Brain MRI"));

            var rows = roundtripFile.Dataset[DicomTag.Rows];
            Assert.That(rows, Is.Not.Null);
            Assert.That(BitConverter.ToUInt16(rows!.RawValue.Span), Is.EqualTo(512));

            var privateElement = roundtripFile.Dataset[new DicomTag(0x0029, 0x0010)];
            Assert.That(privateElement, Is.Not.Null);
            Assert.That(privateElement!.RawValue.ToArray(), Is.EqualTo(binaryData));
        }

        #endregion

        #region Sequence Roundtrip Tests

        [Test]
        public async Task Roundtrip_DatasetWithSequence_SequencePreserved()
        {
            // Arrange - dataset with sequence
            var sequenceItem = new DicomDataset();
            sequenceItem.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "ItemPatient^Name"));

            var sequence = new DicomSequence(ScheduledProcedureStepSequence, sequenceItem);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "SEQ001"));
            originalDataset.Add(sequence);

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - sequence preserved
            Assert.That(roundtripFile.Dataset.GetString(DicomTag.PatientID), Is.EqualTo("SEQ001"));

            var roundtripSequence = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(roundtripSequence, Is.Not.Null);
            Assert.That(roundtripSequence!.Items.Count, Is.EqualTo(1));
            Assert.That(roundtripSequence.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("ItemPatient^Name"));
        }

        [Test]
        public async Task Roundtrip_NestedSequences_AllLevelsPreserved()
        {
            // Arrange - nested sequences (3 levels deep)
            var innerItem = new DicomDataset();
            innerItem.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "InnerPatient"));

            var innerSequence = new DicomSequence(ReferencedSeriesSequence, innerItem);

            var middleItem = new DicomDataset();
            middleItem.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "MID001"));
            middleItem.Add(innerSequence);

            var middleSequence = new DicomSequence(ReferencedStudySequence, middleItem);

            var outerItem = new DicomDataset();
            outerItem.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));
            outerItem.Add(middleSequence);

            var outerSequence = new DicomSequence(ScheduledProcedureStepSequence, outerItem);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "MR"));
            originalDataset.Add(outerSequence);

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - traverse all levels
            Assert.That(roundtripFile.Dataset.GetString(Modality), Is.EqualTo("MR"));

            var rtOuterSeq = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtOuterSeq, Is.Not.Null);
            Assert.That(rtOuterSeq!.Items.Count, Is.EqualTo(1));

            var rtOuterItem = rtOuterSeq.Items[0];
            Assert.That(rtOuterItem.GetString(StudyDate), Is.EqualTo("20240115"));

            var rtMiddleSeq = rtOuterItem.GetSequence(ReferencedStudySequence);
            Assert.That(rtMiddleSeq, Is.Not.Null);
            Assert.That(rtMiddleSeq!.Items.Count, Is.EqualTo(1));

            var rtMiddleItem = rtMiddleSeq.Items[0];
            Assert.That(rtMiddleItem.GetString(DicomTag.PatientID), Is.EqualTo("MID001"));

            var rtInnerSeq = rtMiddleItem.GetSequence(ReferencedSeriesSequence);
            Assert.That(rtInnerSeq, Is.Not.Null);
            Assert.That(rtInnerSeq!.Items.Count, Is.EqualTo(1));

            var rtInnerItem = rtInnerSeq.Items[0];
            Assert.That(rtInnerItem.GetString(DicomTag.PatientName), Is.EqualTo("InnerPatient"));
        }

        [Test]
        public async Task Roundtrip_EmptySequence_PreservedAsEmpty()
        {
            // Arrange - dataset with empty sequence
            var emptySequence = new DicomSequence(ScheduledProcedureStepSequence);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "EMPTY001"));
            originalDataset.Add(emptySequence);

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            Assert.That(roundtripFile.Dataset.GetString(DicomTag.PatientID), Is.EqualTo("EMPTY001"));

            var rtSequence = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtSequence, Is.Not.Null);
            Assert.That(rtSequence!.Items.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task Roundtrip_SequenceWithMultipleItems_AllItemsPreserved()
        {
            // Arrange - sequence with multiple items
            var items = new DicomDataset[5];
            for (int i = 0; i < 5; i++)
            {
                var item = new DicomDataset();
                item.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, $"ITEM{i:D3}"));
                items[i] = item;
            }

            var sequence = new DicomSequence(ScheduledProcedureStepSequence, items);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "CT"));
            originalDataset.Add(sequence);

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            var rtSequence = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtSequence, Is.Not.Null);
            Assert.That(rtSequence!.Items.Count, Is.EqualTo(5));

            for (int i = 0; i < 5; i++)
            {
                Assert.That(rtSequence.Items[i].GetString(DicomTag.PatientID), Is.EqualTo($"ITEM{i:D3}"));
            }
        }

        #endregion

        #region Transfer Syntax Roundtrip Tests

        [Test]
        public async Task Roundtrip_ExplicitVRLittleEndian_Preserved()
        {
            // Arrange
            var originalDataset = CreateTestDataset();
            var originalFile = new DicomFile(originalDataset, TransferSyntax.ExplicitVRLittleEndian);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            Assert.That(roundtripFile.TransferSyntax, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
            VerifyTestDataset(roundtripFile.Dataset);
        }

        [Test]
        public async Task Roundtrip_ImplicitVRLittleEndian_Preserved()
        {
            // Arrange
            var originalDataset = CreateTestDataset();
            var originalFile = new DicomFile(originalDataset, TransferSyntax.ImplicitVRLittleEndian);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            Assert.That(roundtripFile.TransferSyntax, Is.EqualTo(TransferSyntax.ImplicitVRLittleEndian));
            VerifyTestDataset(roundtripFile.Dataset);
        }

        #endregion

        #region Sequence Length Mode Tests

        [Test]
        public async Task Roundtrip_SequenceWithUndefinedLength_Preserved()
        {
            // Arrange - using undefined length mode (default)
            var sequence = CreateTestSequence();
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "CT"));
            originalDataset.Add(sequence);

            var originalFile = new DicomFile(originalDataset);
            var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Undefined };

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms, options);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            var rtSequence = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtSequence, Is.Not.Null);
            Assert.That(rtSequence!.Items.Count, Is.EqualTo(1));
            Assert.That(rtSequence.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Test^Name"));
        }

        [Test]
        public async Task Roundtrip_SequenceWithDefinedLength_Preserved()
        {
            // Arrange - using defined length mode
            var sequence = CreateTestSequence();
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "MR"));
            originalDataset.Add(sequence);

            var originalFile = new DicomFile(originalDataset);
            var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Defined };

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms, options);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            var rtSequence = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtSequence, Is.Not.Null);
            Assert.That(rtSequence!.Items.Count, Is.EqualTo(1));
            Assert.That(rtSequence.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Test^Name"));
        }

        [Test]
        public async Task Roundtrip_NestedSequence_DefinedLength_AllLevelsPreserved()
        {
            // Arrange - nested sequences with defined length
            var innerItem = new DicomDataset();
            innerItem.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Inner^Name"));

            var innerSequence = new DicomSequence(ReferencedSeriesSequence, innerItem);

            var outerItem = new DicomDataset();
            outerItem.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "OUT001"));
            outerItem.Add(innerSequence);

            var outerSequence = new DicomSequence(ScheduledProcedureStepSequence, outerItem);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(Modality, DicomVR.CS, "CT"));
            originalDataset.Add(outerSequence);

            var originalFile = new DicomFile(originalDataset);
            var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Defined };

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms, options);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - traverse nested structure
            var rtOuterSeq = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);
            Assert.That(rtOuterSeq, Is.Not.Null);
            Assert.That(rtOuterSeq!.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("OUT001"));

            var rtInnerSeq = rtOuterSeq.Items[0].GetSequence(ReferencedSeriesSequence);
            Assert.That(rtInnerSeq, Is.Not.Null);
            Assert.That(rtInnerSeq!.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Inner^Name"));
        }

        #endregion

        #region Value Padding Tests

        [Test]
        public async Task Roundtrip_OddLengthStringValue_PaddedCorrectly()
        {
            // Arrange - odd-length string (should be padded)
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "A")); // 1 byte - odd

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - value preserved (padding is transparent)
            var value = roundtripFile.Dataset.GetString(DicomTag.PatientName);
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.TrimEnd(), Is.EqualTo("A"));
        }

        [Test]
        public async Task Roundtrip_OddLengthBinaryValue_PaddedCorrectly()
        {
            // Arrange - odd-length binary (should be padded with 0x00)
            var oddBytes = new byte[] { 0x01, 0x02, 0x03 }; // 3 bytes - odd
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OW, oddBytes));

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - first 3 bytes match (4th is padding)
            var element = roundtripFile.Dataset[DicomTag.PixelData];
            Assert.That(element, Is.Not.Null);
            var value = element!.RawValue.ToArray();
            Assert.That(value.Length, Is.EqualTo(4)); // Padded to even
            Assert.That(value[0], Is.EqualTo(0x01));
            Assert.That(value[1], Is.EqualTo(0x02));
            Assert.That(value[2], Is.EqualTo(0x03));
            Assert.That(value[3], Is.EqualTo(0x00)); // Padding byte
        }

        #endregion

        #region File Meta Information Tests

        [Test]
        public async Task Roundtrip_FileMetaInfo_Preserved()
        {
            // Arrange
            var originalDataset = CreateTestDataset();
            var originalFile = new DicomFile(originalDataset, TransferSyntax.ExplicitVRLittleEndian);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - FMI elements present
            Assert.That(roundtripFile.FileMetaInfo, Is.Not.Null);
            Assert.That(roundtripFile.FileMetaInfo.Count, Is.GreaterThan(0));

            // Transfer Syntax UID should be present
            var tsElement = roundtripFile.FileMetaInfo[DicomTag.TransferSyntaxUID];
            Assert.That(tsElement, Is.Not.Null);
        }

        #endregion

        #region Edge Cases

        [Test]
        public async Task Roundtrip_EmptyDataset_Succeeds()
        {
            // Arrange - minimal dataset with only required SOP UIDs
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - file is valid (may have auto-generated elements)
            Assert.That(roundtripFile, Is.Not.Null);
            Assert.That(roundtripFile.FileMetaInfo, Is.Not.Null);
        }

        [Test]
        public async Task Roundtrip_LargeStringValue_Preserved()
        {
            // Arrange - large string (LT VR supports up to 10240 bytes)
            var largeText = new string('X', 5000);
            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(CreateStringElement(DerivationDescription, DicomVR.ST, largeText));

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert
            var value = roundtripFile.Dataset.GetString(DerivationDescription);
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Length, Is.GreaterThanOrEqualTo(5000));
        }

        [Test]
        public async Task Roundtrip_MultipleSequences_AllPreserved()
        {
            // Arrange - dataset with multiple sequences
            var item1 = new DicomDataset();
            item1.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "SEQ1"));
            var seq1 = new DicomSequence(ReferencedStudySequence, item1);

            var item2 = new DicomDataset();
            item2.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "SEQ2"));
            var seq2 = new DicomSequence(ReferencedSeriesSequence, item2);

            var item3 = new DicomDataset();
            item3.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "SEQ3"));
            var seq3 = new DicomSequence(ScheduledProcedureStepSequence, item3);

            var originalDataset = new DicomDataset();
            AddRequiredSopUids(originalDataset);
            originalDataset.Add(seq1);
            originalDataset.Add(seq2);
            originalDataset.Add(seq3);

            var originalFile = new DicomFile(originalDataset);

            // Act
            using var ms = new MemoryStream();
            await originalFile.SaveAsync(ms);
            ms.Position = 0;
            var roundtripFile = await DicomFile.OpenAsync(ms);

            // Assert - all sequences preserved
            var rtSeq1 = roundtripFile.Dataset.GetSequence(ReferencedStudySequence);
            var rtSeq2 = roundtripFile.Dataset.GetSequence(ReferencedSeriesSequence);
            var rtSeq3 = roundtripFile.Dataset.GetSequence(ScheduledProcedureStepSequence);

            Assert.That(rtSeq1, Is.Not.Null);
            Assert.That(rtSeq2, Is.Not.Null);
            Assert.That(rtSeq3, Is.Not.Null);

            Assert.That(rtSeq1!.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("SEQ1"));
            Assert.That(rtSeq2!.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("SEQ2"));
            Assert.That(rtSeq3!.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("SEQ3"));
        }

        #endregion

        #region Helper Methods

        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            return new DicomStringElement(tag, vr, bytes);
        }

        /// <summary>
        /// Adds required SOP UIDs to a dataset for valid DICOM file generation.
        /// </summary>
        private static void AddRequiredSopUids(DicomDataset dataset)
        {
            dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, TestSOPClassUID));
            dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, TestSOPInstanceUID));
        }

        private DicomDataset CreateTestDataset()
        {
            var dataset = new DicomDataset();
            AddRequiredSopUids(dataset);
            dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
            dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST001"));
            dataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));
            dataset.Add(CreateStringElement(Modality, DicomVR.CS, "CT"));
            return dataset;
        }

        private void VerifyTestDataset(DicomDataset dataset)
        {
            Assert.That(dataset.GetString(DicomTag.PatientName), Is.EqualTo("Test^Patient"));
            Assert.That(dataset.GetString(DicomTag.PatientID), Is.EqualTo("TEST001"));
            Assert.That(dataset.GetString(StudyDate), Is.EqualTo("20240115"));
            Assert.That(dataset.GetString(Modality), Is.EqualTo("CT"));
        }

        private DicomSequence CreateTestSequence()
        {
            var item = new DicomDataset();
            item.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Name"));
            return new DicomSequence(ScheduledProcedureStepSequence, item);
        }

        #endregion
    }
}
