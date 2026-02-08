using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for HT block coding three-stream bidirectional bit I/O.
    /// </summary>
    [TestFixture]
    public class HtBitIOTests
    {
        #region ILW Parsing Tests

        [Test]
        public void HtCleanupReader_MinSegment_ParsesILW()
        {
            // Minimal segment: just ILW (2 bytes), VLC start = 0
            byte[] segment = { 0x00, 0x00 };
            var reader = new HtCleanupReader(segment);

            Assert.That(reader.VlcStart, Is.EqualTo(0));
            Assert.That(reader.MelEnd, Is.EqualTo(0)); // segment.Length - 2
        }

        [Test]
        public void HtCleanupReader_ILW_VlcOffset_Correct()
        {
            // ILW value = 10 (0x00A)
            // Byte 0: 0x00 (upper 8 bits of 0x00A >> 4 = 0)
            // Byte 1: 0xA0 (lower 4 bits of 0x00A << 4 = 0xA0)
            // Total segment: 10 MagSgn bytes + some VLC/MEL + 2 ILW
            byte[] segment = new byte[16];
            segment[14] = 0x00; // ILW high: 10 >> 4 = 0
            segment[15] = 0xA0; // ILW low: (10 & 0xF) << 4 = 0xA0

            var reader = new HtCleanupReader(segment);

            Assert.That(reader.VlcStart, Is.EqualTo(10));
            Assert.That(reader.MelEnd, Is.EqualTo(14)); // 16 - 2
        }

        [Test]
        public void HtCleanupReader_ILW_LargeOffset()
        {
            // ILW value = 255 (0xFF)
            // Byte 0: 255 >> 4 = 15 = 0x0F
            // Byte 1: (255 & 0xF) << 4 = 0xF0
            byte[] segment = new byte[260];
            segment[258] = 0x0F;
            segment[259] = 0xF0;

            var reader = new HtCleanupReader(segment);

            Assert.That(reader.VlcStart, Is.EqualTo(255));
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

        #endregion

        #region MagSgn Stream Tests

        [Test]
        public void HtCleanupReader_ReadMagSgnBits_ForwardRead()
        {
            // Segment: 4 MagSgn bytes + 0 VLC + 0 MEL + 2 ILW
            // ILW value = 4 (VLC starts at byte 4)
            byte[] segment = new byte[6];
            segment[0] = 0xAB; // MagSgn byte 0: 1010 1011
            segment[1] = 0xCD; // MagSgn byte 1: 1100 1101
            segment[2] = 0xEF; // MagSgn byte 2: 1110 1111
            segment[3] = 0x12; // MagSgn byte 3: 0001 0010
            // ILW = 4 -> (4 >> 4 = 0, (4 & 0xF) << 4 = 0x40)
            segment[4] = 0x00;
            segment[5] = 0x40;

            var reader = new HtCleanupReader(segment);

            // Read 8 bits -> should get 0xAB
            uint bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0xAB));

            // Read 4 bits -> upper nibble of 0xCD = 0xC
            bits = reader.ReadMagSgnBits(4);
            Assert.That(bits, Is.EqualTo(0xC));

            // Read 4 more bits -> lower nibble of 0xCD = 0xD
            bits = reader.ReadMagSgnBits(4);
            Assert.That(bits, Is.EqualTo(0xD));
        }

        [Test]
        public void HtCleanupReader_ReadMagSgnBits_SingleBits()
        {
            // MagSgn byte: 0b10110100
            byte[] segment = new byte[3];
            segment[0] = 0xB4; // 1011 0100
            segment[1] = 0x00; // ILW high
            segment[2] = 0x10; // ILW low (VLC start = 1)

            var reader = new HtCleanupReader(segment);

            // Read individual bits from MSB to LSB
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(1u)); // bit 7
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(0u)); // bit 6
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(1u)); // bit 5
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(1u)); // bit 4
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(0u)); // bit 3
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(1u)); // bit 2
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(0u)); // bit 1
            Assert.That(reader.ReadMagSgnBits(1), Is.EqualTo(0u)); // bit 0
        }

        #endregion

        #region VLC Stream Tests

        [Test]
        public void HtCleanupReader_ReadVlcBits_ForwardFromOffset()
        {
            // Segment: 2 MagSgn bytes + 2 VLC bytes + 2 ILW
            byte[] segment = new byte[6];
            segment[0] = 0x11; // MagSgn
            segment[1] = 0x22; // MagSgn
            segment[2] = 0xAA; // VLC byte 0: 1010 1010
            segment[3] = 0x55; // VLC byte 1: 0101 0101
            // ILW = 2 -> VLC starts at byte 2
            segment[4] = 0x00;
            segment[5] = 0x20;

            var reader = new HtCleanupReader(segment);

            Assert.That(reader.VlcStart, Is.EqualTo(2));

            // Read 8 VLC bits -> should get 0xAA
            uint bits = reader.ReadVlcBits(8);
            Assert.That(bits, Is.EqualTo(0xAA));

            // Read next 8 bits -> should get 0x55
            bits = reader.ReadVlcBits(8);
            Assert.That(bits, Is.EqualTo(0x55));
        }

        [Test]
        public void HtCleanupReader_PeekVlcBits_DoesNotAdvance()
        {
            byte[] segment = new byte[4];
            segment[0] = 0xFF; // MagSgn
            segment[1] = 0xAB; // VLC byte
            // ILW = 1 -> VLC starts at byte 1
            segment[2] = 0x00;
            segment[3] = 0x10;

            var reader = new HtCleanupReader(segment);

            // Peek 7 bits
            uint bits1 = reader.PeekVlcBits(7);
            uint bits2 = reader.PeekVlcBits(7);

            Assert.That(bits1, Is.EqualTo(bits2), "Peek should not advance position");
        }

        [Test]
        public void HtCleanupReader_PeekThenAdvance_Works()
        {
            byte[] segment = new byte[5];
            segment[0] = 0xFF; // MagSgn
            segment[1] = 0b10110101; // VLC: 1011 0101
            segment[2] = 0b11001100; // VLC: 1100 1100
            // ILW = 1
            segment[3] = 0x00;
            segment[4] = 0x10;

            var reader = new HtCleanupReader(segment);

            // Peek 3 bits -> upper 3 bits of 0xB5 = 101
            uint peeked = reader.PeekVlcBits(3);
            Assert.That(peeked, Is.EqualTo(0b101));

            // Advance by 3 bits
            reader.AdvanceVlc(3);

            // Peek next 4 bits -> next 4 bits: 1010 1
            uint next = reader.PeekVlcBits(4);
            Assert.That(next, Is.EqualTo(0b1010));
        }

        #endregion

        #region MEL Stream Tests

        [Test]
        public void HtCleanupReader_DecodeMelSignificance_Works()
        {
            // Create a segment with MEL data
            // MEL is in the bytes between VLC end and ILW, read backward
            // Simple test: MEL byte = 0xFF (all 1-bits = all significant)
            byte[] segment = new byte[5];
            segment[0] = 0x00; // MagSgn
            segment[1] = 0x00; // VLC
            segment[2] = 0xFF; // MEL byte (backward: this is the first byte read)
            // ILW = 1 (VLC starts at byte 1)
            segment[3] = 0x00;
            segment[4] = 0x10;

            var reader = new HtCleanupReader(segment);

            // First significance query should use the MEL byte
            bool sig = reader.DecodeMelSignificance();
            // The exact result depends on MEL state and bit interpretation
            // At state 0, a 1-bit means significant
            Assert.That(sig, Is.True, "First MEL decode with 0xFF should be significant");
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

                // At minimum, segment should have the 2 ILW bytes
                Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));

                // ILW should encode VLC start = 0 (no MagSgn data)
                int ilwHigh = segment[^2];
                int ilwLow = segment[^1];
                int vlcOffset = (ilwHigh << 4) | (ilwLow >> 4);
                Assert.That(vlcOffset, Is.EqualTo(0), "Empty writer should have VLC offset = 0");
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void HtCleanupWriter_WriteMagSgn_AppearsBefore_VLC()
        {
            var writer = new HtCleanupWriter(64);
            try
            {
                writer.WriteMagSgnBits(0xAB, 8);
                writer.WriteVlcBits(0x55, 7);

                byte[] segment = writer.Finalize();

                // Parse ILW to find VLC start
                int vlcOffset = (segment[^2] << 4) | (segment[^1] >> 4);

                // MagSgn should be at byte 0
                Assert.That(segment[0], Is.EqualTo(0xAB), "MagSgn byte should be first");

                // VLC start should be 1 (1 byte of MagSgn)
                Assert.That(vlcOffset, Is.EqualTo(1), "VLC should start after MagSgn");
            }
            finally
            {
                writer.Dispose();
            }
        }

        [Test]
        public void HtCleanupWriter_Dispose_DoesNotThrow()
        {
            var writer = new HtCleanupWriter(64);
            writer.Dispose(); // Should not throw
        }

        [Test]
        public void HtCleanupWriter_MultipleFlush_ProducesConsistent()
        {
            var writer = new HtCleanupWriter(64);
            try
            {
                writer.WriteMagSgnBits(0xDE, 8);
                writer.WriteMagSgnBits(0xAD, 8);
                writer.WriteVlcBits(0x42, 7);

                byte[] segment = writer.Finalize();

                // Check MagSgn data
                Assert.That(segment[0], Is.EqualTo(0xDE));
                Assert.That(segment[1], Is.EqualTo(0xAD));

                // Check ILW points to correct offset
                int vlcOffset = (segment[^2] << 4) | (segment[^1] >> 4);
                Assert.That(vlcOffset, Is.EqualTo(2), "VLC starts after 2 MagSgn bytes");
            }
            finally
            {
                writer.Dispose();
            }
        }

        #endregion

        #region Roundtrip Tests

        [Test]
        public void Roundtrip_MagSgnStream()
        {
            // Write MagSgn bits, then read them back
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
        public void Roundtrip_VlcStream()
        {
            // Write VLC bits, then read them back
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
                    $"VLC group {i}: expected {expectedBits[i]:B7}, got {readBits:B7}");
            }
        }

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
                "Segment should have MagSgn + VLC + ILW at minimum");

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
        public void HtCleanupReader_ExactlyTwoBytes_WorksAsEmptySegment()
        {
            // Minimal valid segment: just ILW, no data
            byte[] segment = { 0x00, 0x00 };
            var reader = new HtCleanupReader(segment);

            Assert.That(reader.VlcStart, Is.EqualTo(0));
            Assert.That(reader.MelEnd, Is.EqualTo(0));
        }

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

            // Should still have a valid ILW
            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));

            int vlcOffset = (segment[^2] << 4) | (segment[^1] >> 4);
            Assert.That(vlcOffset, Is.EqualTo(0),
                "No MagSgn written, VLC offset should be 0");
        }

        [Test]
        public void HtCleanupReader_ReadBeyondMagSgn_PadsWithZero()
        {
            // 1 MagSgn byte + ILW
            byte[] segment = new byte[3];
            segment[0] = 0xFF; // MagSgn
            segment[1] = 0x00; // ILW (VLC start = 1)
            segment[2] = 0x10;

            var reader = new HtCleanupReader(segment);

            // Read the MagSgn byte
            uint bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0xFF));

            // Reading past should get zeros (padding)
            bits = reader.ReadMagSgnBits(8);
            Assert.That(bits, Is.EqualTo(0u), "Beyond MagSgn should pad with zeros");
        }

        #endregion
    }
}
