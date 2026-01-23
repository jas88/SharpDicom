using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Integration tests for DicomFileReader with sequence elements.
    /// </summary>
    [TestFixture]
    public class DicomFileReaderSequenceTests
    {
        // Helper to build little-endian bytes
        private static byte[] UInt16LE(ushort value) => BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(value)
            : new[] { (byte)value, (byte)(value >> 8) };

        private static byte[] UInt32LE(uint value) => BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(value)
            : new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };

        // Item tag (FFFE,E000)
        private static readonly byte[] ItemTag = new byte[] { 0xFE, 0xFF, 0x00, 0xE0 };

        // Item delimitation tag (FFFE,E00D)
        private static readonly byte[] ItemDelimitationTag = new byte[] { 0xFE, 0xFF, 0x0D, 0xE0 };

        // Sequence delimitation tag (FFFE,E0DD)
        private static readonly byte[] SequenceDelimitationTag = new byte[] { 0xFE, 0xFF, 0xDD, 0xE0 };

        // Zero length (4 bytes)
        private static readonly byte[] ZeroLength = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        // Undefined length (0xFFFFFFFF)
        private static readonly byte[] UndefinedLengthBytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        #region Basic Sequence in File Tests

        [Test]
        public async Task ReadElementsAsync_FileWithSequence_YieldsSequenceElement()
        {
            // Arrange - DICOM file with a sequence element
            var data = CreateDicomFileWithSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var elements = new List<IDicomElement>();

            // Act
            await foreach (var element in reader.ReadElementsAsync())
            {
                elements.Add(element);
            }

            // Assert - should have the sequence
            Assert.That(elements, Has.Some.Matches<IDicomElement>(e =>
                e.Tag == new DicomTag(0x0040, 0x0100) && e is DicomSequence));
        }

        [Test]
        public async Task ReadElementsAsync_SequenceWithOneItem_HasCorrectItemCount()
        {
            // Arrange
            var data = CreateDicomFileWithSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            DicomSequence? foundSequence = null;

            // Act
            await foreach (var element in reader.ReadElementsAsync())
            {
                if (element is DicomSequence seq)
                {
                    foundSequence = seq;
                    break;
                }
            }

            // Assert
            Assert.That(foundSequence, Is.Not.Null);
            Assert.That(foundSequence!.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadDatasetAsync_FileWithSequence_DatasetContainsSequence()
        {
            // Arrange
            var data = CreateDicomFileWithSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert
            var sequenceTag = new DicomTag(0x0040, 0x0100);
            Assert.That(dataset.Contains(sequenceTag), Is.True);
            var sequence = dataset.GetSequence(sequenceTag);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(1));
        }

        #endregion

        #region Implicit VR File with Sequence Tests

        [Test]
        public async Task ReadElementsAsync_ImplicitVRFileWithSequence_ParsesCorrectly()
        {
            // Arrange - Implicit VR Little Endian file with sequence
            var data = CreateImplicitVRFileWithSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);
            var elements = new List<IDicomElement>();

            // Act
            await foreach (var element in reader.ReadElementsAsync())
            {
                elements.Add(element);
            }

            // Assert - should have the sequence
            Assert.That(elements, Has.Some.Matches<IDicomElement>(e =>
                e.Tag == new DicomTag(0x0040, 0x0100) && e is DicomSequence));
        }

        [Test]
        public async Task ReadDatasetAsync_ImplicitVRFileWithSequence_SequenceContainsItems()
        {
            // Arrange
            var data = CreateImplicitVRFileWithSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert
            var sequenceTag = new DicomTag(0x0040, 0x0100);
            var sequence = dataset.GetSequence(sequenceTag);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(1));
        }

        #endregion

        #region Nested Sequence Tests

        [Test]
        public async Task ReadDatasetAsync_NestedSequences_ParsesCorrectly()
        {
            // Arrange - file with 2-level nested sequence
            var data = CreateDicomFileWithNestedSequences();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert - outer sequence exists
            var outerSeqTag = new DicomTag(0x0040, 0x0100);
            var outerSequence = dataset.GetSequence(outerSeqTag);
            Assert.That(outerSequence, Is.Not.Null);
            Assert.That(outerSequence!.Items.Count, Is.EqualTo(1));

            // Assert - inner sequence exists within outer sequence item
            var innerSeqTag = new DicomTag(0x0008, 0x1115);
            var innerSequence = outerSequence.Items[0].GetSequence(innerSeqTag);
            Assert.That(innerSequence, Is.Not.Null);
            Assert.That(innerSequence!.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadDatasetAsync_NestedSequences_NestedItemsAccessible()
        {
            // Arrange
            var data = CreateDicomFileWithNestedSequences();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert - can navigate to innermost content
            var outerSequence = dataset.GetSequence(new DicomTag(0x0040, 0x0100));
            var innerSequence = outerSequence!.Items[0].GetSequence(new DicomTag(0x0008, 0x1115));
            var innermostItem = innerSequence!.Items[0];

            Assert.That(innermostItem.Contains(DicomTag.PatientName), Is.True);
        }

        #endregion

        #region DicomFile.Open Tests

        [Test]
        public async Task DicomFileOpen_FileWithSequence_DatasetContainsSequence()
        {
            // Arrange
            var data = CreateDicomFileWithSequence();
            using var stream = new MemoryStream(data);

            // Act
            var file = await DicomFile.OpenAsync(stream);

            // Assert
            var sequenceTag = new DicomTag(0x0040, 0x0100);
            Assert.That(file.Dataset.Contains(sequenceTag), Is.True);
            var sequence = file.Dataset.GetSequence(sequenceTag);
            Assert.That(sequence, Is.Not.Null);
        }

        [Test]
        public async Task DicomFileOpen_FileWithSequence_SequenceItemsAccessible()
        {
            // Arrange
            var data = CreateDicomFileWithSequence();
            using var stream = new MemoryStream(data);

            // Act
            var file = await DicomFile.OpenAsync(stream);
            var sequence = file.Dataset.GetSequence(new DicomTag(0x0040, 0x0100));

            // Assert
            Assert.That(sequence!.Items.Count, Is.EqualTo(1));
            Assert.That(sequence.Items[0].Contains(DicomTag.PatientName), Is.True);
        }

        [Test]
        public async Task DicomFileOpen_NestedSequences_NestedItemHasCorrectParent()
        {
            // Arrange
            var data = CreateDicomFileWithNestedSequences();
            using var stream = new MemoryStream(data);

            // Act
            var file = await DicomFile.OpenAsync(stream);

            // Assert - check parent references
            var outerSequence = file.Dataset.GetSequence(new DicomTag(0x0040, 0x0100));
            var outerItem = outerSequence!.Items[0];

            var innerSequence = outerItem.GetSequence(new DicomTag(0x0008, 0x1115));
            var innerItem = innerSequence!.Items[0];

            // Inner item should have the outer item as parent
            Assert.That(innerItem.Parent, Is.SameAs(outerItem));
        }

        #endregion

        #region Empty Sequence Tests

        [Test]
        public async Task ReadDatasetAsync_EmptySequence_SequenceExistsWithZeroItems()
        {
            // Arrange - file with empty sequence (0 items)
            var data = CreateDicomFileWithEmptySequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert
            var sequenceTag = new DicomTag(0x0040, 0x0100);
            Assert.That(dataset.Contains(sequenceTag), Is.True);
            var sequence = dataset.GetSequence(sequenceTag);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(0));
        }

        #endregion

        #region Multiple Sequences Tests

        [Test]
        public async Task ReadDatasetAsync_MultipleSequences_BothPresent()
        {
            // Arrange - file with two different sequences
            var data = CreateDicomFileWithMultipleSequences();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert
            var seq1Tag = new DicomTag(0x0040, 0x0100); // Scheduled Procedure Step Sequence
            var seq2Tag = new DicomTag(0x0008, 0x1115); // Referenced Series Sequence

            Assert.That(dataset.Contains(seq1Tag), Is.True);
            Assert.That(dataset.Contains(seq2Tag), Is.True);

            var seq1 = dataset.GetSequence(seq1Tag);
            var seq2 = dataset.GetSequence(seq2Tag);

            Assert.That(seq1, Is.Not.Null);
            Assert.That(seq2, Is.Not.Null);
            Assert.That(seq1!.Items.Count, Is.EqualTo(1));
            Assert.That(seq2!.Items.Count, Is.EqualTo(1));
        }

        #endregion

        #region Undefined Length Sequence Tests

        [Test]
        public async Task ReadDatasetAsync_UndefinedLengthSequence_ParsesCorrectly()
        {
            // Arrange - sequence with undefined length
            var data = CreateDicomFileWithUndefinedLengthSequence();
            using var stream = new MemoryStream(data);

            await using var reader = new DicomFileReader(stream);

            // Act
            var dataset = await reader.ReadDatasetAsync();

            // Assert
            var sequenceTag = new DicomTag(0x0040, 0x0100);
            var sequence = dataset.GetSequence(sequenceTag);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(1));
        }

        #endregion

        #region Helper Methods

        private static byte[] CreateDicomFileWithSequence()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information (Explicit VR LE)
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Dataset elements
            WriteElementExplicit(ms, 0x0008, 0x0005, "CS", PadToEven("ISO_IR 100"u8.ToArray()));

            // Sequence element with one item
            WriteSequenceWithItem(ms, 0x0040, 0x0100, CreateItemWithPatientName());

            return ms.ToArray();
        }

        private static byte[] CreateImplicitVRFileWithSequence()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information (specifies Implicit VR LE transfer syntax)
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2\0"); // Implicit VR LE
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Dataset elements in implicit VR format
            // In implicit VR, element is: tag (4 bytes) + length (4 bytes) + value
            WriteElementImplicit(ms, 0x0008, 0x0005, PadToEven("ISO_IR 100"u8.ToArray()));

            // Sequence element with one item (implicit VR)
            // VR for (0040,0100) = SQ is looked up from dictionary
            WriteSequenceWithItemImplicit(ms, 0x0040, 0x0100, CreateItemWithPatientNameImplicit());

            return ms.ToArray();
        }

        private static byte[] CreateDicomFileWithNestedSequences()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Build nested structure: outer sequence -> item -> inner sequence -> item -> PatientName
            var innerItemContent = CreatePatientNameElement();
            var innerSequenceContent = BuildSequenceItems(new[] { innerItemContent });
            var innerSequenceElement = BuildSequenceElement(0x0008, 0x1115, innerSequenceContent);

            var outerItemContent = innerSequenceElement;
            var outerSequenceContent = BuildSequenceItems(new[] { outerItemContent });

            // Write outer sequence
            WriteSequenceElement(ms, 0x0040, 0x0100, outerSequenceContent);

            return ms.ToArray();
        }

        private static byte[] CreateDicomFileWithEmptySequence()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Empty sequence (length = 0)
            WriteSequenceElement(ms, 0x0040, 0x0100, Array.Empty<byte>());

            return ms.ToArray();
        }

        private static byte[] CreateDicomFileWithMultipleSequences()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // First sequence
            WriteSequenceWithItem(ms, 0x0008, 0x1115, CreateItemWithPatientName()); // Referenced Series

            // Second sequence (must come after 0x0008 tags to maintain order)
            WriteSequenceWithItem(ms, 0x0040, 0x0100, CreateItemWithPatientID()); // Scheduled Procedure Step

            return ms.ToArray();
        }

        private static byte[] CreateDicomFileWithUndefinedLengthSequence()
        {
            using var ms = new MemoryStream();

            // 128 byte preamble
            ms.Write(new byte[128]);

            // DICM prefix
            ms.Write("DICM"u8);

            // File Meta Information
            var tsBytes = System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0");
            WriteElementExplicit(ms, 0x0002, 0x0010, "UI", tsBytes);

            // Sequence with undefined length
            WriteSequenceWithUndefinedLength(ms, 0x0040, 0x0100, CreateItemWithPatientName());

            return ms.ToArray();
        }

        private static byte[] CreateItemWithPatientName()
        {
            using var ms = new MemoryStream();
            WriteElementExplicit(ms, 0x0010, 0x0010, "PN", PadToEven("Test^Name"u8.ToArray()));
            return ms.ToArray();
        }

        private static byte[] CreateItemWithPatientID()
        {
            using var ms = new MemoryStream();
            WriteElementExplicit(ms, 0x0010, 0x0020, "LO", PadToEven("PATIENT001"u8.ToArray()));
            return ms.ToArray();
        }

        private static byte[] CreateItemWithPatientNameImplicit()
        {
            using var ms = new MemoryStream();
            WriteElementImplicit(ms, 0x0010, 0x0010, PadToEven("Test^Name"u8.ToArray()));
            return ms.ToArray();
        }

        private static byte[] CreatePatientNameElement()
        {
            using var ms = new MemoryStream();
            WriteElementExplicit(ms, 0x0010, 0x0010, "PN", PadToEven("Nested"u8.ToArray()));
            return ms.ToArray();
        }

        private static void WriteElementExplicit(MemoryStream ms, ushort group, ushort element,
            string vr, byte[] value)
        {
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));

            var dicomVR = new DicomVR(vr);
            if (dicomVR.Is32BitLength)
            {
                ms.Write(new byte[2]); // Reserved
                ms.Write(BitConverter.GetBytes((uint)value.Length));
            }
            else
            {
                ms.Write(BitConverter.GetBytes((ushort)value.Length));
            }

            ms.Write(value);
        }

        private static void WriteElementImplicit(MemoryStream ms, ushort group, ushort element, byte[] value)
        {
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write(BitConverter.GetBytes((uint)value.Length));
            ms.Write(value);
        }

        private static void WriteSequenceWithItem(MemoryStream ms, ushort group, ushort element, byte[] itemContent)
        {
            var sequenceContent = BuildSequenceItems(new[] { itemContent });
            WriteSequenceElement(ms, group, element, sequenceContent);
        }

        private static void WriteSequenceWithItemImplicit(MemoryStream ms, ushort group, ushort element, byte[] itemContent)
        {
            var sequenceContent = BuildSequenceItems(new[] { itemContent });
            // Implicit VR sequence: tag (4) + length (4) + items
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write(BitConverter.GetBytes((uint)sequenceContent.Length));
            ms.Write(sequenceContent);
        }

        private static void WriteSequenceElement(MemoryStream ms, ushort group, ushort element, byte[] sequenceContent)
        {
            // Explicit VR SQ header: tag (4) + VR (2) + reserved (2) + length (4)
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write("SQ"u8);
            ms.Write(new byte[2]); // Reserved
            ms.Write(BitConverter.GetBytes((uint)sequenceContent.Length));
            ms.Write(sequenceContent);
        }

        private static void WriteSequenceWithUndefinedLength(MemoryStream ms, ushort group, ushort element, byte[] itemContent)
        {
            // SQ header with undefined length
            ms.Write(BitConverter.GetBytes(group));
            ms.Write(BitConverter.GetBytes(element));
            ms.Write("SQ"u8);
            ms.Write(new byte[2]); // Reserved
            ms.Write(UndefinedLengthBytes); // Undefined length

            // Item
            ms.Write(ItemTag);
            ms.Write(BitConverter.GetBytes((uint)itemContent.Length));
            ms.Write(itemContent);

            // Sequence delimitation item
            ms.Write(SequenceDelimitationTag);
            ms.Write(ZeroLength);
        }

        private static byte[] BuildSequenceItems(byte[][] itemContents)
        {
            using var ms = new MemoryStream();
            foreach (var content in itemContents)
            {
                ms.Write(ItemTag);
                ms.Write(BitConverter.GetBytes((uint)content.Length));
                ms.Write(content);
            }
            return ms.ToArray();
        }

        private static byte[] BuildSequenceElement(ushort group, ushort element, byte[] sequenceContent)
        {
            using var ms = new MemoryStream();
            WriteSequenceElement(ms, group, element, sequenceContent);
            return ms.ToArray();
        }

        private static byte[] PadToEven(byte[] value)
        {
            if (value.Length % 2 == 0)
                return value;

            var padded = new byte[value.Length + 1];
            Array.Copy(value, padded, value.Length);
            padded[padded.Length - 1] = (byte)' ';
            return padded;
        }

        #endregion
    }
}
