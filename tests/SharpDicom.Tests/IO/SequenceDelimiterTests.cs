using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO
{
    /// <summary>
    /// Tests for depth tracking edge cases in FindSequenceDelimiter implementations.
    /// These tests verify the fix for the off-by-one bug where SequenceDelimitationItem
    /// handling decremented depth unconditionally, even when depth == 0.
    /// </summary>
    [TestFixture]
    public class SequenceDelimiterTests
    {
        // DICOM delimiter tag constants
        private static readonly byte[] ItemTag = { 0xFE, 0xFF, 0x00, 0xE0 }; // (FFFE,E000)
        private static readonly byte[] ItemDelimitationTag = { 0xFE, 0xFF, 0x0D, 0xE0 }; // (FFFE,E00D)
        private static readonly byte[] SequenceDelimitationTag = { 0xFE, 0xFF, 0xDD, 0xE0 }; // (FFFE,E0DD)
        private static readonly byte[] ZeroLength = { 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] UndefinedLength = { 0xFF, 0xFF, 0xFF, 0xFF };

        /// <summary>
        /// Test 1: Single undefined-length sequence with no nesting.
        /// Verifies FindSequenceDelimiter returns correct position at depth 0.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_Depth0_ReturnsCorrectPosition()
        {
            // Arrange: Simple undefined-length sequence with one item
            // Structure: Item(undef) + ItemDelim + SeqDelim
            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,           // Item with undefined length
                ItemDelimitationTag, ZeroLength,    // End of item
                SequenceDelimitationTag, ZeroLength // End of sequence (should be found)
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 8 + 8; // After Item (8) + ItemDelim (8)
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                $"Should find SequenceDelimitationItem at position {expectedPos}");
        }

        /// <summary>
        /// Test 2: Sequence containing sequence, both undefined-length (depth 1).
        /// Verifies both delimiters found correctly and depth tracking works for one level.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_Depth1Nested_ReturnsOuterDelimiter()
        {
            // Arrange: Outer sequence contains Item which contains nested SQ element
            // Structure: Item(undef) + SQ(undef) + Item(undef) + ItemDelim + SeqDelim(inner) + ItemDelim + SeqDelim(outer)
            var sqElement = new List<byte>();
            sqElement.AddRange(new byte[] { 0x08, 0x00, 0x41, 0x11 }); // Tag (0008,1141) - Referenced SOP Sequence
            sqElement.AddRange(new byte[] { 0x53, 0x51 }); // VR: SQ
            sqElement.AddRange(new byte[] { 0x00, 0x00 }); // Reserved
            sqElement.AddRange(UndefinedLength); // Undefined length

            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,           // Outer item start
                sqElement.ToArray(),                 // Nested SQ element header
                ItemTag, UndefinedLength,           // Inner sequence item
                ItemDelimitationTag, ZeroLength,    // End inner item
                SequenceDelimitationTag, ZeroLength, // End inner sequence (depth 1, should NOT be returned)
                ItemDelimitationTag, ZeroLength,    // End outer item
                SequenceDelimitationTag, ZeroLength // End outer sequence (depth 0, should be found)
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            // Should skip the first SeqDelim (nested, depth 1) and find the second (depth 0)
            int expectedPos = 8 + 12 + 8 + 8 + 8 + 8; // All components before final SeqDelim
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should find outer SequenceDelimitationItem, not nested one");
        }

        /// <summary>
        /// Test 3: Three levels of Item nesting (depth 3).
        /// This test focuses on undefined-length Items containing Items.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_Depth3Nested_ReturnsCorrectDelimiter()
        {
            // Arrange: Nested Item structure (FindSequenceDelimiter tracks Item depth, not SQ depth)
            // Structure:
            //   Item(undef) [depth 0→1]
            //     Item(undef) [depth 1→2]
            //       Item(undef) [depth 2→3]
            //         ItemDelim [depth 3→2]
            //       ItemDelim [depth 2→1]
            //     ItemDelim [depth 1→0]
            //   SeqDelim [depth 0, should be found]

            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,           // depth 0→1
                ItemTag, UndefinedLength,           // depth 1→2
                ItemTag, UndefinedLength,           // depth 2→3
                ItemDelimitationTag, ZeroLength,    // depth 3→2
                ItemDelimitationTag, ZeroLength,    // depth 2→1
                ItemDelimitationTag, ZeroLength,    // depth 1→0
                SequenceDelimitationTag, ZeroLength // depth 0, found!
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 8 + 8 + 8 + 8 + 8 + 8; // 6 * 8 = 48
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should find SequenceDelimitationItem after all Items closed");
        }

        /// <summary>
        /// Test 4: Five levels of Item nesting (depth 5) - stress test for off-by-one at higher depths.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_Depth5Nested_HandlesDeepNesting()
        {
            // Arrange: Very deeply nested Items to stress test depth tracking
            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,           // depth 0→1
                ItemTag, UndefinedLength,           // depth 1→2
                ItemTag, UndefinedLength,           // depth 2→3
                ItemTag, UndefinedLength,           // depth 3→4
                ItemTag, UndefinedLength,           // depth 4→5
                ItemDelimitationTag, ZeroLength,    // depth 5→4
                ItemDelimitationTag, ZeroLength,    // depth 4→3
                ItemDelimitationTag, ZeroLength,    // depth 3→2
                ItemDelimitationTag, ZeroLength,    // depth 2→1
                ItemDelimitationTag, ZeroLength,    // depth 1→0
                SequenceDelimitationTag, ZeroLength // depth 0, found!
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 5 * 8 + 5 * 8; // 5 Items + 5 ItemDelims = 80 bytes
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should handle depth 5 nesting and find correct delimiter");
        }

        /// <summary>
        /// Test 5: Mixed defined-length and undefined-length Items.
        /// Verifies that defined-length Items don't affect depth tracking.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_MixedLengths_HandlesCorrectly()
        {
            // Arrange: Mix undefined-length Items (affect depth) with defined-length Items (don't affect depth)
            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,                    // depth 0→1
                ItemTag, new byte[] { 0x10, 0x00, 0x00, 0x00 }, // defined length 16, depth unchanged
                new byte[16],                                // 16 bytes of content
                ItemTag, UndefinedLength,                    // depth 1→2 (nested undefined item)
                ItemDelimitationTag, ZeroLength,            // depth 2→1
                ItemDelimitationTag, ZeroLength,            // depth 1→0
                SequenceDelimitationTag, ZeroLength        // depth 0, found!
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 8 + 8 + 16 + 8 + 8 + 8; // 56 bytes
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should handle mixed defined/undefined length items");
        }

        /// <summary>
        /// Test 6: Empty nested sequence (undefined-length sequence containing empty undefined-length sequence).
        /// Tests edge case where nested sequence has no items.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_EmptyNestedSequence_HandlesCorrectly()
        {
            // Arrange: Outer sequence with item containing empty nested sequence
            var sqHeader = CreateSQHeader(0x0008, 0x1115);

            var buffer = BuildBuffer(
                ItemTag, UndefinedLength,
                sqHeader,
                SequenceDelimitationTag, ZeroLength, // Empty sequence - immediate delimiter
                ItemDelimitationTag, ZeroLength,    // End outer item
                SequenceDelimitationTag, ZeroLength // End outer sequence (should be found)
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 8 + 12 + 8 + 8;
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should handle empty nested sequence correctly");
        }

        /// <summary>
        /// Test 7: Multiple sibling Items at same depth.
        /// Tests that closing one Item doesn't affect parsing of sibling Items.
        /// </summary>
        [Test]
        public void FindSequenceDelimiter_MultipleSiblings_ParsesAllCorrectly()
        {
            // Arrange: Sequence with multiple sibling undefined-length Items
            var buffer = BuildBuffer(
                // First Item at depth 1
                ItemTag, UndefinedLength,           // depth 0→1
                ItemDelimitationTag, ZeroLength,    // depth 1→0
                // Second Item at depth 1
                ItemTag, UndefinedLength,           // depth 0→1
                ItemDelimitationTag, ZeroLength,    // depth 1→0
                // Third Item at depth 1
                ItemTag, UndefinedLength,           // depth 0→1
                ItemDelimitationTag, ZeroLength,    // depth 1→0
                // End of sequence
                SequenceDelimitationTag, ZeroLength // depth 0, found!
            );

            // Act
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true);
            int delimiterPos = reader.FindSequenceDelimiter();

            // Assert
            int expectedPos = 3 * (8 + 8); // 3 Items, each 8+8 bytes = 48
            Assert.That(delimiterPos, Is.EqualTo(expectedPos),
                "Should handle multiple sibling items correctly");
        }


        // Helper methods

        /// <summary>
        /// Builds a byte buffer from multiple byte arrays.
        /// </summary>
        private static byte[] BuildBuffer(params byte[][] parts)
        {
            var result = new List<byte>();
            foreach (var part in parts)
            {
                result.AddRange(part);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Creates a DICOM SQ element header with explicit VR and undefined length.
        /// </summary>
        private static byte[] CreateSQHeader(ushort group, ushort element)
        {
            var header = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), group);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2, 2), element);
            header[4] = (byte)'S';
            header[5] = (byte)'Q';
            header[6] = 0x00; // Reserved
            header[7] = 0x00; // Reserved
            header[8] = 0xFF; // Undefined length
            header[9] = 0xFF;
            header[10] = 0xFF;
            header[11] = 0xFF;
            return header;
        }
    }
}
