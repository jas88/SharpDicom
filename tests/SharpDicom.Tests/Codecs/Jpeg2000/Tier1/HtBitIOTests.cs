using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for HT block coding three-stream bidirectional bit I/O.
    /// </summary>
    /// <remarks>
    /// The HT cleanup segment layout per ITU-T T.814 is:
    ///   [MagSgn bytes] [MEL bytes] [VLC bytes (backward)] with ILW at the end.
    /// ILW (scup) is stored as: last_byte = scup >> 4, second_to_last_byte lower nibble = scup and 0xF.
    /// MagSgn reads forward (LSB-first) with 0xFF bit-stuffing.
    /// VLC reads backward (LSB-first) with >0x8F bit-stuffing.
    /// MEL reads forward (MSB-first) with 0xFF bit-stuffing from position (lcup - scup).
    /// </remarks>
    [TestFixture]
    public class HtBitIOTests
    {
        #region ILW Parsing Tests

        [Test]
        public void HtCleanupReader_ParsesILW_Lcup_Scup()
        {
            // Build a minimal segment via the writer, then verify Lcup and Scup
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            Assert.That(reader.Lcup, Is.EqualTo(segment.Length));
            Assert.That(reader.Scup, Is.GreaterThanOrEqualTo(2));
            Assert.That(reader.Scup, Is.LessThanOrEqualTo(reader.Lcup));
        }

        [Test]
        public void HtCleanupReader_TooShort_Throws()
        {
            byte[] segment = { 0x42 }; // only 1 byte
            try
            {
                _ = new HtCleanupReader(segment);
                Assert.Fail("Expected ArgumentException");
            }
            catch (ArgumentException)
            {
                // Expected
            }
        }

        [Test]
        public void HtCleanupReader_Lcup_EqualsSegmentLength()
        {
            // Build a segment with some data
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteMagSgnBits(0xAB, 8);
                writer.WriteVlcBits(0x55, 7);
                writer.EncodeMel(true);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            Assert.That(reader.Lcup, Is.EqualTo(segment.Length));
        }

        #endregion

        #region Writer Tests

        [Test]
        public void HtCleanupWriter_EmptySegment_HasILW()
        {
            var writer = new HtCleanupWriter(64);
            try
            {
                byte[] segment = writer.Finalize();

                // At minimum, segment should have the MEL/VLC area with ILW
                Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void HtCleanupWriter_WriteMagSgn_ProducesLargerSegment()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segmentWithout;
            try
            {
                segmentWithout = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var writer2 = new HtCleanupWriter(64);
            byte[] segmentWith;
            try
            {
                writer2.WriteMagSgnBits(0xAB, 8);
                segmentWith = writer2.Finalize();
            }
            finally
            {
                writer2.Dispose();
            }

            Assert.That(segmentWith.Length, Is.GreaterThan(segmentWithout.Length),
                "Adding MagSgn data should make the segment larger");
        }

        [Test]
        public void HtCleanupWriter_Dispose_DoesNotThrow()
        {
            var writer = new HtCleanupWriter(64);
            writer.Dispose(); // Should not throw
        }

        #endregion

        #region MagSgn Stream Roundtrip Tests

        [Test]
        public void Roundtrip_MagSgnStream_SingleByte()
        {
            // Write 8 MagSgn bits, then read them back
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteMagSgnBits(0xAB, 8);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            uint bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0xABu));
        }

        [Test]
        public void Roundtrip_MagSgnStream_MultipleBitGroups()
        {
            uint[] expectedBits = { 0b101, 0b1100, 0b1, 0b0, 0b1111111 };
            int[] bitCounts = { 3, 4, 1, 1, 7 };

            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                for (int i = 0; i < expectedBits.Length; i++)
                {
                    writer.WriteMagSgnBits(expectedBits[i], bitCounts[i]);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            for (int i = 0; i < expectedBits.Length; i++)
            {
                uint readBits = reader.ReadMagSgnBits(bitCounts[i]);
                Assert.That(readBits, Is.EqualTo(expectedBits[i]),
                    $"MagSgn group {i}: expected {expectedBits[i]}, got {readBits}");
            }
        }

        [Test]
        public void Roundtrip_MagSgnStream_ManyBytes()
        {
            // Write 32 bytes of MagSgn data
            var writer = new HtCleanupWriter(256);
            byte[] segment;
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    writer.WriteMagSgnBits((uint)(i & 0xFF), 8);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            for (int i = 0; i < 32; i++)
            {
                uint readBits = reader.ReadMagSgnBits(8);
                Assert.That(readBits, Is.EqualTo((uint)(i & 0xFF)),
                    $"MagSgn byte {i}");
            }
        }

        #endregion

        #region VLC Stream Roundtrip Tests

        [Test]
        public void Roundtrip_VlcStream_SingleValue()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteVlcBits(0b1010101, 7);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            uint bits = reader.ReadVlcBits(7);
            Assert.That(bits, Is.EqualTo(0b1010101u));
        }

        [Test]
        public void Roundtrip_VlcStream_MultipleValues()
        {
            uint[] expectedBits = { 0b1010101, 0b1100110, 0b0000001 };
            int[] bitCounts = { 7, 7, 7 };

            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                for (int i = 0; i < expectedBits.Length; i++)
                {
                    writer.WriteVlcBits(expectedBits[i], bitCounts[i]);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            for (int i = 0; i < expectedBits.Length; i++)
            {
                uint readBits = reader.ReadVlcBits(bitCounts[i]);
                Assert.That(readBits, Is.EqualTo(expectedBits[i]),
                    $"VLC group {i}: expected 0b{Convert.ToString((int)expectedBits[i], 2).PadLeft(7, '0')}, got 0b{Convert.ToString((int)readBits, 2).PadLeft(7, '0')}");
            }
        }

        [Test]
        public void Roundtrip_VlcStream_ManyValues()
        {
            // Write 16 7-bit VLC values
            var writer = new HtCleanupWriter(256);
            byte[] segment;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    writer.WriteVlcBits((uint)(i & 0x7F), 7);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            for (int i = 0; i < 16; i++)
            {
                uint readBits = reader.ReadVlcBits(7);
                Assert.That(readBits, Is.EqualTo((uint)(i & 0x7F)),
                    $"VLC value {i}");
            }
        }

        [Test]
        public void HtCleanupReader_PeekVlcBits_DoesNotAdvance()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteVlcBits(0b1010101, 7);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            uint bits1 = reader.PeekVlcBits(7);
            uint bits2 = reader.PeekVlcBits(7);

            Assert.That(bits1, Is.EqualTo(bits2), "Peek should not advance position");
        }

        [Test]
        public void HtCleanupReader_PeekThenAdvance_Works()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteVlcBits(0b1010101, 7);
                writer.WriteVlcBits(0b1100110, 7);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            // Peek first value
            uint peeked = reader.PeekVlcBits(7);
            Assert.That(peeked, Is.EqualTo(0b1010101u));

            // Advance past it
            reader.AdvanceVlc(7);

            // Read second value
            uint next = reader.ReadVlcBits(7);
            Assert.That(next, Is.EqualTo(0b1100110u));
        }

        #endregion

        #region MEL Stream Tests

        [Test]
        public void HtCleanupReader_DecodeMelSignificance_Works()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.EncodeMel(true);  // significant
                writer.EncodeMel(false); // insignificant
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            Assert.That(reader.DecodeMelSignificance(), Is.True, "First MEL should be significant");
            Assert.That(reader.DecodeMelSignificance(), Is.False, "Second MEL should be insignificant");
        }

        #endregion

        #region Three-Stream Roundtrip Tests

        [Test]
        public void Roundtrip_AllThreeStreams()
        {
            // Write to all three streams, finalize, then read back

            // MagSgn data
            uint magSgnValue = 0b10110010;
            int magSgnBits = 8;

            // VLC data
            uint vlcValue = 0b1010101;
            int vlcBits = 7;

            // MEL data (a few significance decisions)
            bool[] melPattern = { false, false, true, false };

            var writer = new HtCleanupWriter(128);
            byte[] segment;
            try
            {
                writer.WriteMagSgnBits(magSgnValue, magSgnBits);
                writer.WriteVlcBits(vlcValue, vlcBits);
                foreach (bool sig in melPattern)
                {
                    writer.EncodeMel(sig);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            // Verify segment structure
            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(4),
                "Segment should have MagSgn + MEL/VLC + ILW at minimum");

            var reader = new HtCleanupReader(segment);

            // Read MagSgn back
            uint readMagSgn = reader.ReadMagSgnBits(magSgnBits);
            Assert.That(readMagSgn, Is.EqualTo(magSgnValue), "MagSgn roundtrip failed");

            // Read VLC back
            uint readVlc = reader.ReadVlcBits(vlcBits);
            Assert.That(readVlc, Is.EqualTo(vlcValue), "VLC roundtrip failed");

            // Read MEL back
            for (int i = 0; i < melPattern.Length; i++)
            {
                bool readMel = reader.DecodeMelSignificance();
                Assert.That(readMel, Is.EqualTo(melPattern[i]),
                    $"MEL quad {i}: expected {melPattern[i]}, got {readMel}");
            }
        }

        [Test]
        public void Roundtrip_LargerMagSgnAndVlc()
        {
            // Write many bytes of data to both streams
            var writer = new HtCleanupWriter(256);
            byte[] segment;
            try
            {
                // Write 32 bytes of MagSgn
                for (int i = 0; i < 32; i++)
                {
                    writer.WriteMagSgnBits((uint)(i & 0xFF), 8);
                }

                // Write 16 7-bit VLC values
                for (int i = 0; i < 16; i++)
                {
                    writer.WriteVlcBits((uint)(i & 0x7F), 7);
                }

                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            // Verify MagSgn
            for (int i = 0; i < 32; i++)
            {
                uint readBits = reader.ReadMagSgnBits(8);
                Assert.That(readBits, Is.EqualTo((uint)(i & 0xFF)),
                    $"MagSgn byte {i}");
            }

            // Verify VLC
            for (int i = 0; i < 16; i++)
            {
                uint readBits = reader.ReadVlcBits(7);
                Assert.That(readBits, Is.EqualTo((uint)(i & 0x7F)),
                    $"VLC value {i}");
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void HtCleanupWriter_OnlyMel_ProducesValidSegment()
        {
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                // Only write MEL data
                writer.EncodeMel(false);
                writer.EncodeMel(true);
                writer.EncodeMel(false);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            // Should still have a valid segment with ILW
            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));

            // Should be parseable
            var reader = new HtCleanupReader(segment);
            Assert.That(reader.DecodeMelSignificance(), Is.False, "First MEL should be insignificant");
            Assert.That(reader.DecodeMelSignificance(), Is.True, "Second MEL should be significant");
            Assert.That(reader.DecodeMelSignificance(), Is.False, "Third MEL should be insignificant");
        }

        [Test]
        public void HtCleanupReader_ReadMagSgnBeyondData_PadsWith0xFF()
        {
            // Build a segment with 1 byte of MagSgn
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteMagSgnBits(0x42, 8);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            // Read the actual MagSgn byte
            uint bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0x42u));

            // Reading beyond should pad with 0xFF per ITU-T T.814
            bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0xFFu), "Beyond MagSgn should pad with 0xFF");
        }

        [Test]
        public void Roundtrip_MagSgnWithBitStuffing()
        {
            // Write data that includes 0xFF byte values, triggering bit-stuffing
            var writer = new HtCleanupWriter(128);
            byte[] segment;
            try
            {
                // Writing 0xFF as an 8-bit value triggers bit-stuffing on the next byte
                writer.WriteMagSgnBits(0xFF, 8);
                writer.WriteMagSgnBits(0x42, 8);
                writer.WriteMagSgnBits(0xFF, 8);
                writer.WriteMagSgnBits(0x17, 8);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xFFu), "First byte");
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0x42u), "Second byte (after stuffing)");
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xFFu), "Third byte");
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0x17u), "Fourth byte (after stuffing)");
        }

        [Test]
        public void Roundtrip_LargeVlcValues()
        {
            // Write various bit widths to VLC
            var writer = new HtCleanupWriter(64);
            byte[] segment;
            try
            {
                writer.WriteVlcBits(0b1, 1);
                writer.WriteVlcBits(0b10, 2);
                writer.WriteVlcBits(0b101, 3);
                writer.WriteVlcBits(0b1010, 4);
                writer.WriteVlcBits(0b10101010, 8);
                writer.WriteVlcBits(0b1111, 4);
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            Assert.That(reader.ReadVlcBits(1), Is.EqualTo(0b1u));
            Assert.That(reader.ReadVlcBits(2), Is.EqualTo(0b10u));
            Assert.That(reader.ReadVlcBits(3), Is.EqualTo(0b101u));
            Assert.That(reader.ReadVlcBits(4), Is.EqualTo(0b1010u));
            Assert.That(reader.ReadVlcBits(8), Is.EqualTo(0b10101010u));
            Assert.That(reader.ReadVlcBits(4), Is.EqualTo(0b1111u));
        }

        [Test]
        public void Roundtrip_AllStreams_Complex()
        {
            // Complex scenario with interleaved writes to all streams
            var writer = new HtCleanupWriter(256);
            byte[] segment;
            try
            {
                // Write MagSgn
                writer.WriteMagSgnBits(0xDE, 8);
                writer.WriteMagSgnBits(0xAD, 8);

                // Write MEL events
                writer.EncodeMel(false);
                writer.EncodeMel(false);
                writer.EncodeMel(true);
                writer.EncodeMel(false);
                writer.EncodeMel(true);

                // Write VLC
                writer.WriteVlcBits(0b1001, 4);
                writer.WriteVlcBits(0b110011, 6);

                // More MagSgn
                writer.WriteMagSgnBits(0xBE, 8);
                writer.WriteMagSgnBits(0xEF, 8);

                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);

            // Read MagSgn
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xDEu));
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xADu));

            // Read MEL
            Assert.That(reader.DecodeMelSignificance(), Is.False);
            Assert.That(reader.DecodeMelSignificance(), Is.False);
            Assert.That(reader.DecodeMelSignificance(), Is.True);
            Assert.That(reader.DecodeMelSignificance(), Is.False);
            Assert.That(reader.DecodeMelSignificance(), Is.True);

            // Read VLC
            Assert.That(reader.ReadVlcBits(4), Is.EqualTo(0b1001u));
            Assert.That(reader.ReadVlcBits(6), Is.EqualTo(0b110011u));

            // Read remaining MagSgn
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xBEu));
            Assert.That(reader.ReadMagSgnBits(8), Is.EqualTo(0xEFu));
        }

        #endregion
    }
}
