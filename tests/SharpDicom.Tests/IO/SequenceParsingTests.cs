using System;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Comprehensive tests for DICOM sequence parsing with SequenceParser.
    /// </summary>
    [TestFixture]
    public class SequenceParsingTests
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

        #region Empty Sequence Tests

        [Test]
        public void ParseSequence_EmptyWithDefinedLength_ReturnsEmptyItems()
        {
            // Arrange - empty sequence (length = 0)
            var parser = new SequenceParser(explicitVR: true);
            var buffer = Array.Empty<byte>();
            var tag = new DicomTag(0x0040, 0x0100); // Scheduled Procedure Step Sequence

            // Act
            var sequence = parser.ParseSequence(buffer, tag, length: 0);

            // Assert
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence.Tag, Is.EqualTo(tag));
            Assert.That(sequence.Items, Is.Not.Null);
            Assert.That(sequence.Items.Count, Is.EqualTo(0));
        }

        [Test]
        public void ParseSequence_EmptyWithUndefinedLength_ReturnsEmptyItems()
        {
            // Arrange - undefined length sequence with just delimiter
            var parser = new SequenceParser(explicitVR: true);

            var bytes = new List<byte>();
            bytes.AddRange(SequenceDelimitationTag);
            bytes.AddRange(ZeroLength);

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var sequence = parser.ParseSequence(bytes.ToArray(), tag, SequenceParser.UndefinedLength);

            // Assert
            Assert.That(sequence.Items, Is.Not.Null);
            Assert.That(sequence.Items.Count, Is.EqualTo(0));
        }

        #endregion

        #region Single Item Tests

        [Test]
        public void ParseSequence_SingleItemWithDefinedLength_ParsesCorrectly()
        {
            // Arrange - sequence with one item containing PatientName
            var parser = new SequenceParser(explicitVR: true);

            // Build element: PatientName (0010,0010) = "Test^Name"
            var patientNameValue = System.Text.Encoding.ASCII.GetBytes("Test^Name ");  // 10 bytes, padded to even

            var itemContent = new List<byte>();
            // PatientName tag (0010,0010)
            itemContent.AddRange(UInt16LE(0x0010));
            itemContent.AddRange(UInt16LE(0x0010));
            // VR = PN
            itemContent.Add((byte)'P');
            itemContent.Add((byte)'N');
            // Length (2 bytes for short VRs)
            itemContent.AddRange(UInt16LE((ushort)patientNameValue.Length));
            // Value
            itemContent.AddRange(patientNameValue);

            var sequence = new List<byte>();
            // Item tag
            sequence.AddRange(ItemTag);
            // Item length
            sequence.AddRange(UInt32LE((uint)itemContent.Count));
            // Item content
            sequence.AddRange(itemContent);

            var tag = new DicomTag(0x0008, 0x1115); // Referenced Series Sequence

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, (uint)sequence.Count);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            var item = result.Items[0];
            Assert.That(item.Contains(DicomTag.PatientName), Is.True);
            var nameElement = item[DicomTag.PatientName] as DicomStringElement;
            Assert.That(nameElement, Is.Not.Null);
            Assert.That(nameElement!.GetString()?.Trim(), Is.EqualTo("Test^Name"));
        }

        [Test]
        public void ParseSequence_SingleItemWithUndefinedLength_ParsesCorrectly()
        {
            // Arrange - item with undefined length
            var parser = new SequenceParser(explicitVR: true);

            // Build element: PatientID (0010,0020) = "12345"
            var patientIdValue = System.Text.Encoding.ASCII.GetBytes("12345 "); // 6 bytes (even)

            var itemContent = new List<byte>();
            // PatientID tag (0010,0020)
            itemContent.AddRange(UInt16LE(0x0010));
            itemContent.AddRange(UInt16LE(0x0020));
            // VR = LO
            itemContent.Add((byte)'L');
            itemContent.Add((byte)'O');
            // Length
            itemContent.AddRange(UInt16LE((ushort)patientIdValue.Length));
            // Value
            itemContent.AddRange(patientIdValue);
            // Item delimitation
            itemContent.AddRange(ItemDelimitationTag);
            itemContent.AddRange(ZeroLength);

            var sequence = new List<byte>();
            // Item tag
            sequence.AddRange(ItemTag);
            // Undefined length
            sequence.AddRange(UndefinedLengthBytes);
            // Item content (including delimiter)
            sequence.AddRange(itemContent);
            // Sequence delimitation
            sequence.AddRange(SequenceDelimitationTag);
            sequence.AddRange(ZeroLength);

            var tag = new DicomTag(0x0008, 0x1115);

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, SequenceParser.UndefinedLength);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            Assert.That(result.Items[0].Contains(DicomTag.PatientID), Is.True);
        }

        #endregion

        #region Multiple Item Tests

        [Test]
        public void ParseSequence_TwoItems_ParsesBoth()
        {
            // Arrange
            var parser = new SequenceParser(explicitVR: true);
            var sequence = BuildSequenceWithStringItems(new[] { "Item1 ", "Item2 " });

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence, tag, (uint)sequence.Length);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void ParseSequence_FiveItems_ParsesAll()
        {
            // Arrange
            var parser = new SequenceParser(explicitVR: true);
            var values = new[] { "One   ", "Two   ", "Three ", "Four  ", "Five  " };  // 6 chars each (even)
            var sequence = BuildSequenceWithStringItems(values);

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence, tag, (uint)sequence.Length);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(5));
        }

        #endregion

        #region Nested Sequence Tests

        [Test]
        public void ParseSequence_NestedTwoLevels_ParsesCorrectly()
        {
            // Arrange - outer sequence containing inner sequence
            var parser = new SequenceParser(explicitVR: true);

            // Inner sequence: contains one item with a string element
            var innerItemContent = BuildStringElement(DicomTag.PatientName, "Nested");
            var innerSequence = new List<byte>();
            innerSequence.AddRange(ItemTag);
            innerSequence.AddRange(UInt32LE((uint)innerItemContent.Length));
            innerSequence.AddRange(innerItemContent);

            // Inner sequence element header (SQ VR)
            var innerSeqTag = new DicomTag(0x0008, 0x1115); // Referenced Series Sequence
            var innerSeqElement = new List<byte>();
            innerSeqElement.AddRange(UInt16LE(innerSeqTag.Group));
            innerSeqElement.AddRange(UInt16LE(innerSeqTag.Element));
            innerSeqElement.Add((byte)'S');
            innerSeqElement.Add((byte)'Q');
            innerSeqElement.AddRange(new byte[] { 0x00, 0x00 }); // Reserved
            innerSeqElement.AddRange(UInt32LE((uint)innerSequence.Count));
            innerSeqElement.AddRange(innerSequence);

            // Outer item containing inner sequence
            var outerItem = new List<byte>();
            outerItem.AddRange(ItemTag);
            outerItem.AddRange(UInt32LE((uint)innerSeqElement.Count));
            outerItem.AddRange(innerSeqElement);

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(outerItem.ToArray(), outerTag, (uint)outerItem.Count);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            var nestedSeq = result.Items[0].GetSequence(innerSeqTag);
            Assert.That(nestedSeq, Is.Not.Null);
            Assert.That(nestedSeq!.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void ParseSequence_NestedThreeLevels_ParsesCorrectly()
        {
            // Arrange - 3 levels of nesting
            var parser = new SequenceParser(explicitVR: true, options: new DicomReaderOptions { MaxSequenceDepth = 10 });

            var innermost = BuildStringElement(DicomTag.PatientName, "Deep  ");  // 6 chars even
            var current = WrapInItem(innermost);

            // Wrap 2 more times (total 3 levels)
            for (int i = 0; i < 2; i++)
            {
                var seqTag = new DicomTag(0x0008, (ushort)(0x1115 + i));
                var seqElement = BuildSequenceElement(seqTag, current);
                current = WrapInItem(seqElement);
            }

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(current, outerTag, (uint)current.Length);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));

            // Navigate 2 levels deep to verify structure (outermost is 0x1116, then 0x1115)
            var item = result.Items[0];
            var seq1 = item.GetSequence(new DicomTag(0x0008, 0x1116));
            Assert.That(seq1, Is.Not.Null, "Level 1 sequence (0x1116) not found");
            Assert.That(seq1!.Items.Count, Is.EqualTo(1));

            item = seq1.Items[0];
            var seq2 = item.GetSequence(new DicomTag(0x0008, 0x1115));
            Assert.That(seq2, Is.Not.Null, "Level 2 sequence (0x1115) not found");
            Assert.That(seq2!.Items.Count, Is.EqualTo(1));

            // Innermost should have PatientName
            Assert.That(seq2.Items[0].Contains(DicomTag.PatientName), Is.True);
        }

        #endregion

        #region Depth Limit Tests

        [Test]
        public void ParseSequence_ExceedsMaxDepth_Throws()
        {
            // Arrange - depth limit of 3, try to parse depth 4
            var options = new DicomReaderOptions { MaxSequenceDepth = 3 };
            var parser = new SequenceParser(explicitVR: true, options: options);

            // Build 4 levels of nesting (outer = 1, inner sequences = 2, 3, 4)
            var innermost = BuildStringElement(DicomTag.PatientName, "TooDeep");
            var current = WrapInItem(innermost);

            for (int i = 0; i < 3; i++) // 3 more levels = 4 total
            {
                var seqTag = new DicomTag(0x0008, (ushort)(0x1115 + i));
                var seqElement = BuildSequenceElement(seqTag, current);
                current = WrapInItem(seqElement);
            }

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act & Assert
            var ex = Assert.Throws<DicomDataException>(() =>
                parser.ParseSequence(current, outerTag, (uint)current.Length));

            Assert.That(ex!.Message, Does.Contain("depth"));
            Assert.That(ex.Message, Does.Contain("exceeds"));
        }

        [Test]
        public void ParseSequence_AtMaxDepth_Succeeds()
        {
            // Arrange - depth limit of 3, parse exactly depth 3
            var options = new DicomReaderOptions { MaxSequenceDepth = 3 };
            var parser = new SequenceParser(explicitVR: true, options: options);

            // Build 3 levels of nesting (outer = 1, nested = 2, 3)
            var innermost = BuildStringElement(DicomTag.PatientName, "AtLimit");
            var current = WrapInItem(innermost);

            for (int i = 0; i < 2; i++) // 2 more levels = 3 total
            {
                var seqTag = new DicomTag(0x0008, (ushort)(0x1115 + i));
                var seqElement = BuildSequenceElement(seqTag, current);
                current = WrapInItem(seqElement);
            }

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(current, outerTag, (uint)current.Length);

            // Assert - should succeed
            Assert.That(result.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void ParseSequence_LargeMaxDepth_AcceptsDeepNesting()
        {
            // Arrange - high depth limit, verify deep nesting works
            var options = new DicomReaderOptions { MaxSequenceDepth = 128 };
            var parser = new SequenceParser(explicitVR: true, options: options);

            // Build 10 levels (well within 128 limit)
            var innermost = BuildStringElement(DicomTag.PatientName, "VeryDeep");
            var current = WrapInItem(innermost);

            for (int i = 0; i < 9; i++)
            {
                var seqTag = new DicomTag(0x0008, (ushort)(0x1115 + i));
                var seqElement = BuildSequenceElement(seqTag, current);
                current = WrapInItem(seqElement);
            }

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(current, outerTag, (uint)current.Length);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
        }

        #endregion

        #region Delimiter Handling Tests

        [Test]
        public void ParseSequence_SequenceDelimitationItem_StopsCorrectly()
        {
            // Arrange - undefined length sequence
            var parser = new SequenceParser(explicitVR: true);

            var sequence = new List<byte>();

            // First item
            var item1Content = BuildStringElement(DicomTag.PatientName, "First ");  // 6 chars even
            sequence.AddRange(ItemTag);
            sequence.AddRange(UInt32LE((uint)item1Content.Length));
            sequence.AddRange(item1Content);

            // Sequence delimitation
            sequence.AddRange(SequenceDelimitationTag);
            sequence.AddRange(ZeroLength);

            // Extra garbage after delimiter (should be ignored)
            sequence.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, SequenceParser.UndefinedLength);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void ParseSequence_ItemDelimitationItem_StopsItemCorrectly()
        {
            // Arrange - item with undefined length
            var parser = new SequenceParser(explicitVR: true);

            var itemContent = new List<byte>();
            itemContent.AddRange(BuildStringElement(DicomTag.PatientName, "Test  "));  // 6 chars
            itemContent.AddRange(ItemDelimitationTag);
            itemContent.AddRange(ZeroLength);

            var sequence = new List<byte>();
            sequence.AddRange(ItemTag);
            sequence.AddRange(UndefinedLengthBytes);
            sequence.AddRange(itemContent);
            sequence.AddRange(SequenceDelimitationTag);
            sequence.AddRange(ZeroLength);

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, SequenceParser.UndefinedLength);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            Assert.That(result.Items[0].Contains(DicomTag.PatientName), Is.True);
        }

        #endregion

        #region Parent Reference Tests

        [Test]
        public void ParseSequence_ItemHasParentSet()
        {
            // Arrange
            var parser = new SequenceParser(explicitVR: true);
            var parentDataset = new DicomDataset();

            var itemContent = BuildStringElement(DicomTag.PatientName, "Child ");  // 6 chars
            var sequence = new List<byte>();
            sequence.AddRange(ItemTag);
            sequence.AddRange(UInt32LE((uint)itemContent.Length));
            sequence.AddRange(itemContent);

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, (uint)sequence.Count, parentDataset);

            // Assert
            Assert.That(result.Items[0].Parent, Is.SameAs(parentDataset));
        }

        [Test]
        public void ParseSequence_NestedItemHasCorrectParentChain()
        {
            // Arrange - outer -> inner, inner item parent should be outer item
            var parser = new SequenceParser(explicitVR: true);
            var rootDataset = new DicomDataset();

            // Build nested structure
            var innerItem = BuildStringElement(DicomTag.PatientID, "Nested");
            var innerSeq = WrapInItem(innerItem);
            var innerSeqTag = new DicomTag(0x0008, 0x1115);
            var innerSeqElement = BuildSequenceElement(innerSeqTag, innerSeq);
            var outerItem = WrapInItem(innerSeqElement);

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(outerItem, outerTag, (uint)outerItem.Length, rootDataset);

            // Assert
            var outerItemDataset = result.Items[0];
            Assert.That(outerItemDataset.Parent, Is.SameAs(rootDataset));

            var innerSequence = outerItemDataset.GetSequence(innerSeqTag);
            Assert.That(innerSequence, Is.Not.Null);

            var innerItemDataset = innerSequence!.Items[0];
            Assert.That(innerItemDataset.Parent, Is.SameAs(outerItemDataset));
        }

        #endregion

        #region Implicit VR Tests

        [Test]
        public void ParseSequence_ImplicitVR_ParsesCorrectly()
        {
            // Arrange - implicit VR sequence
            var parser = new SequenceParser(explicitVR: false);

            // In implicit VR, element header is just tag (4 bytes) + length (4 bytes)
            var patientNameValue = System.Text.Encoding.ASCII.GetBytes("Test    "); // 8 bytes

            var itemContent = new List<byte>();
            // PatientName tag (0010,0010)
            itemContent.AddRange(UInt16LE(0x0010));
            itemContent.AddRange(UInt16LE(0x0010));
            // Length (4 bytes in implicit VR)
            itemContent.AddRange(UInt32LE((uint)patientNameValue.Length));
            // Value
            itemContent.AddRange(patientNameValue);

            var sequence = new List<byte>();
            sequence.AddRange(ItemTag);
            sequence.AddRange(UInt32LE((uint)itemContent.Count));
            sequence.AddRange(itemContent);

            var tag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(sequence.ToArray(), tag, (uint)sequence.Count);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            Assert.That(result.Items[0].Contains(DicomTag.PatientName), Is.True);
        }

        #endregion

        #region Max Total Items Tests

        [Test]
        public void ParseSequence_ExceedsMaxTotalItems_Throws()
        {
            // Arrange - set low limit and try to exceed it
            var options = new DicomReaderOptions { MaxTotalItems = 3 };
            var parser = new SequenceParser(explicitVR: true, options: options);

            // Build sequence with 5 items
            var sequence = BuildSequenceWithStringItems(new[] { "1     ", "2     ", "3     ", "4     ", "5     " });  // 6 chars each
            var tag = new DicomTag(0x0040, 0x0100);

            // Act & Assert
            var ex = Assert.Throws<DicomDataException>(() =>
                parser.ParseSequence(sequence, tag, (uint)sequence.Length));

            Assert.That(ex!.Message, Does.Contain("item count exceeds"));
        }

        #endregion

        #region Nested Undefined Length Tests

        [Test]
        public void ParseSequence_NestedUndefinedLength_ParsesCorrectly()
        {
            // Arrange - outer undefined-length sequence containing inner undefined-length sequence
            // This tests the fix for FindSequenceContentLength depth tracking
            var parser = new SequenceParser(explicitVR: true);

            // Inner sequence content: one item with PatientName
            var innerItemContent = BuildStringElement(DicomTag.PatientName, "Inner ");
            var innerSequenceContent = new List<byte>();
            innerSequenceContent.AddRange(ItemTag);
            innerSequenceContent.AddRange(UInt32LE((uint)innerItemContent.Length));
            innerSequenceContent.AddRange(innerItemContent);
            innerSequenceContent.AddRange(SequenceDelimitationTag);
            innerSequenceContent.AddRange(ZeroLength);

            // Inner sequence element with undefined length
            var innerSeqTag = new DicomTag(0x0008, 0x1115); // Referenced Series Sequence
            var innerSeqElement = new List<byte>();
            innerSeqElement.AddRange(UInt16LE(innerSeqTag.Group));
            innerSeqElement.AddRange(UInt16LE(innerSeqTag.Element));
            innerSeqElement.Add((byte)'S');
            innerSeqElement.Add((byte)'Q');
            innerSeqElement.AddRange(new byte[] { 0x00, 0x00 }); // Reserved
            innerSeqElement.AddRange(UndefinedLengthBytes); // Undefined length!
            innerSeqElement.AddRange(innerSequenceContent);

            // Outer item content: PatientID + inner sequence
            var patientIdElement = BuildStringElement(DicomTag.PatientID, "OUT001");
            var outerItemContent = new List<byte>();
            outerItemContent.AddRange(patientIdElement);
            outerItemContent.AddRange(innerSeqElement);

            // Outer item with undefined length
            var outerSequenceContent = new List<byte>();
            outerSequenceContent.AddRange(ItemTag);
            outerSequenceContent.AddRange(UndefinedLengthBytes);
            outerSequenceContent.AddRange(outerItemContent);
            outerSequenceContent.AddRange(ItemDelimitationTag);
            outerSequenceContent.AddRange(ZeroLength);
            outerSequenceContent.AddRange(SequenceDelimitationTag);
            outerSequenceContent.AddRange(ZeroLength);

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(outerSequenceContent.ToArray(), outerTag, SequenceParser.UndefinedLength);

            // Assert
            Assert.That(result.Items.Count, Is.EqualTo(1));
            var outerItem = result.Items[0];
            Assert.That(outerItem.GetString(DicomTag.PatientID), Is.EqualTo("OUT001"));

            var innerSeq = outerItem.GetSequence(innerSeqTag);
            Assert.That(innerSeq, Is.Not.Null);
            Assert.That(innerSeq!.Items.Count, Is.EqualTo(1));
            Assert.That(innerSeq.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Inner"));
        }

        [Test]
        public void ParseSequence_ThreeLevelUndefinedLength_ParsesCorrectly()
        {
            // Arrange - 3 levels of nesting, all with undefined length
            var parser = new SequenceParser(explicitVR: true, options: new DicomReaderOptions { MaxSequenceDepth = 10 });

            // Level 3 (innermost): item with PatientName
            var level3ItemContent = BuildStringElement(DicomTag.PatientName, "Deep  ");
            var level3SeqContent = new List<byte>();
            level3SeqContent.AddRange(ItemTag);
            level3SeqContent.AddRange(UInt32LE((uint)level3ItemContent.Length));
            level3SeqContent.AddRange(level3ItemContent);
            level3SeqContent.AddRange(SequenceDelimitationTag);
            level3SeqContent.AddRange(ZeroLength);

            // Level 3 sequence element (undefined length)
            var level3Tag = new DicomTag(0x0008, 0x1115);
            var level3Element = BuildUndefinedLengthSequenceElement(level3Tag, level3SeqContent.ToArray());

            // Level 2: item with PatientID + level 3 sequence
            var level2PatientId = BuildStringElement(DicomTag.PatientID, "MID001");
            var level2ItemContent = new List<byte>();
            level2ItemContent.AddRange(level2PatientId);
            level2ItemContent.AddRange(level3Element);

            var level2SeqContent = new List<byte>();
            level2SeqContent.AddRange(ItemTag);
            level2SeqContent.AddRange(UndefinedLengthBytes);
            level2SeqContent.AddRange(level2ItemContent);
            level2SeqContent.AddRange(ItemDelimitationTag);
            level2SeqContent.AddRange(ZeroLength);
            level2SeqContent.AddRange(SequenceDelimitationTag);
            level2SeqContent.AddRange(ZeroLength);

            // Level 2 sequence element (undefined length)
            var level2Tag = new DicomTag(0x0008, 0x1110);
            var level2Element = BuildUndefinedLengthSequenceElement(level2Tag, level2SeqContent.ToArray());

            // Level 1 (outermost): item with StudyDate + level 2 sequence
            var level1StudyDate = BuildStringElement(new DicomTag(0x0008, 0x0020), "20240115");
            var level1ItemContent = new List<byte>();
            level1ItemContent.AddRange(level1StudyDate);
            level1ItemContent.AddRange(level2Element);

            var outerSeqContent = new List<byte>();
            outerSeqContent.AddRange(ItemTag);
            outerSeqContent.AddRange(UndefinedLengthBytes);
            outerSeqContent.AddRange(level1ItemContent);
            outerSeqContent.AddRange(ItemDelimitationTag);
            outerSeqContent.AddRange(ZeroLength);
            outerSeqContent.AddRange(SequenceDelimitationTag);
            outerSeqContent.AddRange(ZeroLength);

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(outerSeqContent.ToArray(), outerTag, SequenceParser.UndefinedLength);

            // Assert - traverse all 3 levels
            Assert.That(result.Items.Count, Is.EqualTo(1));

            var level1Item = result.Items[0];
            Assert.That(level1Item.GetString(new DicomTag(0x0008, 0x0020)), Is.EqualTo("20240115"));

            var level2Seq = level1Item.GetSequence(level2Tag);
            Assert.That(level2Seq, Is.Not.Null);
            Assert.That(level2Seq!.Items.Count, Is.EqualTo(1));

            var level2Item = level2Seq.Items[0];
            Assert.That(level2Item.GetString(DicomTag.PatientID), Is.EqualTo("MID001"));

            var level3Seq = level2Item.GetSequence(level3Tag);
            Assert.That(level3Seq, Is.Not.Null);
            Assert.That(level3Seq!.Items.Count, Is.EqualTo(1));
            Assert.That(level3Seq.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Deep"));
        }

        [Test]
        public void ParseSequence_MixedDefinedAndUndefinedLength_ParsesCorrectly()
        {
            // Arrange - outer undefined, inner defined length
            var parser = new SequenceParser(explicitVR: true);

            // Inner sequence with defined length
            var innerItemContent = BuildStringElement(DicomTag.PatientName, "Inner ");
            var innerSeqContent = new List<byte>();
            innerSeqContent.AddRange(ItemTag);
            innerSeqContent.AddRange(UInt32LE((uint)innerItemContent.Length));
            innerSeqContent.AddRange(innerItemContent);

            var innerSeqTag = new DicomTag(0x0008, 0x1115);
            var innerSeqElement = BuildSequenceElement(innerSeqTag, innerSeqContent.ToArray()); // Defined length

            // Outer item with additional element after nested sequence
            var beforeElement = BuildStringElement(DicomTag.PatientID, "BEFORE");
            var afterElement = BuildStringElement(new DicomTag(0x0008, 0x0020), "20240115"); // StudyDate

            var outerItemContent = new List<byte>();
            outerItemContent.AddRange(beforeElement);
            outerItemContent.AddRange(innerSeqElement);
            outerItemContent.AddRange(afterElement);

            // Outer sequence with undefined length
            var outerSeqContent = new List<byte>();
            outerSeqContent.AddRange(ItemTag);
            outerSeqContent.AddRange(UndefinedLengthBytes);
            outerSeqContent.AddRange(outerItemContent);
            outerSeqContent.AddRange(ItemDelimitationTag);
            outerSeqContent.AddRange(ZeroLength);
            outerSeqContent.AddRange(SequenceDelimitationTag);
            outerSeqContent.AddRange(ZeroLength);

            var outerTag = new DicomTag(0x0040, 0x0100);

            // Act
            var result = parser.ParseSequence(outerSeqContent.ToArray(), outerTag, SequenceParser.UndefinedLength);

            // Assert - verify all elements including those after nested sequence
            Assert.That(result.Items.Count, Is.EqualTo(1));
            var item = result.Items[0];
            Assert.That(item.GetString(DicomTag.PatientID), Is.EqualTo("BEFORE"));
            Assert.That(item.GetString(new DicomTag(0x0008, 0x0020)), Is.EqualTo("20240115"));

            var innerSeq = item.GetSequence(innerSeqTag);
            Assert.That(innerSeq, Is.Not.Null);
            Assert.That(innerSeq!.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Inner"));
        }

        private byte[] BuildUndefinedLengthSequenceElement(DicomTag tag, byte[] content)
        {
            var element = new List<byte>();
            element.AddRange(UInt16LE(tag.Group));
            element.AddRange(UInt16LE(tag.Element));
            element.Add((byte)'S');
            element.Add((byte)'Q');
            element.AddRange(new byte[] { 0x00, 0x00 }); // Reserved
            element.AddRange(UndefinedLengthBytes);
            element.AddRange(content);
            return element.ToArray();
        }

        #endregion

        #region Helper Methods

        private byte[] BuildStringElement(DicomTag tag, string value)
        {
            // Ensure value is even length (pad with space if odd)
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value);
            if (valueBytes.Length % 2 != 0)
            {
                var padded = new byte[valueBytes.Length + 1];
                Array.Copy(valueBytes, padded, valueBytes.Length);
                padded[valueBytes.Length] = 0x20; // Space padding
                valueBytes = padded;
            }

            var element = new List<byte>();
            // Tag
            element.AddRange(UInt16LE(tag.Group));
            element.AddRange(UInt16LE(tag.Element));
            // VR = LO (Long String)
            element.Add((byte)'L');
            element.Add((byte)'O');
            // Length
            element.AddRange(UInt16LE((ushort)valueBytes.Length));
            // Value
            element.AddRange(valueBytes);

            return element.ToArray();
        }

        private byte[] WrapInItem(byte[] content)
        {
            var item = new List<byte>();
            item.AddRange(ItemTag);
            item.AddRange(UInt32LE((uint)content.Length));
            item.AddRange(content);
            return item.ToArray();
        }

        private byte[] BuildSequenceElement(DicomTag tag, byte[] sequenceContent)
        {
            var element = new List<byte>();
            // Tag
            element.AddRange(UInt16LE(tag.Group));
            element.AddRange(UInt16LE(tag.Element));
            // VR = SQ
            element.Add((byte)'S');
            element.Add((byte)'Q');
            // Reserved bytes
            element.AddRange(new byte[] { 0x00, 0x00 });
            // Length
            element.AddRange(UInt32LE((uint)sequenceContent.Length));
            // Content
            element.AddRange(sequenceContent);

            return element.ToArray();
        }

        private byte[] BuildSequenceWithStringItems(string[] values)
        {
            var sequence = new List<byte>();

            foreach (var value in values)
            {
                var itemContent = BuildStringElement(DicomTag.PatientName, value);
                sequence.AddRange(ItemTag);
                sequence.AddRange(UInt32LE((uint)itemContent.Length));
                sequence.AddRange(itemContent);
            }

            return sequence.ToArray();
        }

        #endregion
    }
}
