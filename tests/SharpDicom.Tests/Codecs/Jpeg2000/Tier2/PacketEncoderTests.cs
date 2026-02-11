using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier2
{
    [TestFixture]
    public class PacketEncoderTests
    {
        #region Single Code-Block Roundtrip

        [Test]
        public void SingleCodeBlock_EncodeDecodeRoundtrip_DataMatches()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 100;
            coefficients[1] = 50;
            coefficients[7] = -30;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            var codeBlocks = new CodeBlockData[] { cbData };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(1));
            Assert.That(packets[0].Data.Length, Is.GreaterThan(0));

            // Decode
            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results.Length, Is.EqualTo(1));
            Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses));
            Assert.That(results[0].Data.Length, Is.EqualTo(cbData.Data.Length));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(cbData.Data.ToArray()));
        }

        [Test]
        public void SingleCodeBlock_ZeroBitPlanes_PreservedThroughRoundtrip()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 100;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            int expectedZeroBitPlanes = cbData.MsbPosition >= 0 ? (31 - cbData.MsbPosition) : 0;

            var codeBlocks = new CodeBlockData[] { cbData };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].ZeroBitPlanes, Is.EqualTo(expectedZeroBitPlanes));
        }

        #endregion

        #region Multiple Code-Blocks 2x2

        [Test]
        public void MultipleCodeBlocks_2x2_EncodeDecodeRoundtrip()
        {
            using var ebcot = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                coefficients[0] = 50 + i * 30;
                coefficients[1] = 20 + i * 10;
                codeBlocks[i] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(1));

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            Assert.That(results.Length, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(results[i].TotalPasses, Is.EqualTo(codeBlocks[i].NumPasses),
                    $"Code-block {i} pass count mismatch");
                Assert.That(results[i].Data.Length, Is.EqualTo(codeBlocks[i].Data.Length),
                    $"Code-block {i} data length mismatch");
                Assert.That(results[i].Data.ToArray(), Is.EqualTo(codeBlocks[i].Data.ToArray()),
                    $"Code-block {i} data content mismatch");
            }
        }

        [Test]
        public void MultipleCodeBlocks_2x2_ZeroBitPlanes_AllPreserved()
        {
            using var ebcot = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            // Use different magnitudes to get different MSB positions
            int[] magnitudes = { 255, 15, 1, 127 };
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                coefficients[0] = magnitudes[i];
                codeBlocks[i] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            for (int i = 0; i < 4; i++)
            {
                int expected = codeBlocks[i].MsbPosition >= 0 ? (31 - codeBlocks[i].MsbPosition) : 0;
                Assert.That(results[i].ZeroBitPlanes, Is.EqualTo(expected),
                    $"Code-block {i} zero bitplane mismatch");
            }
        }

        #endregion

        #region Empty Packet

        [Test]
        public void EmptyPacket_AllCodeBlocksEmpty_DecodesCorrectly()
        {
            var codeBlocks = new CodeBlockData[]
            {
                CodeBlockData.Empty,
                CodeBlockData.Empty,
                CodeBlockData.Empty,
                CodeBlockData.Empty
            };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(1));
            // Empty packet should still have at least the non-empty flag byte
            Assert.That(packets[0].Data.Length, Is.GreaterThan(0));

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            Assert.That(results.Length, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(results[i].TotalPasses, Is.EqualTo(0),
                    $"Empty code-block {i} should have 0 passes");
                Assert.That(results[i].Data.IsEmpty, Is.True,
                    $"Empty code-block {i} should have no data");
            }
        }

        [Test]
        public void EmptyPacket_StartsWithZeroBit()
        {
            var codeBlocks = new CodeBlockData[]
            {
                CodeBlockData.Empty,
            };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            // The first bit of an empty packet is 0 (non-empty flag = 0).
            // After padding, the first byte should have MSB = 0.
            Assert.That(packets[0].Data.Span[0] & 0x80, Is.EqualTo(0),
                "Empty packet non-empty flag should be 0 (MSB of first byte)");
        }

        #endregion

        #region Bit-Stuffing

        [Test]
        public void BitStuffing_NoFFFollowedByByteWithMsbSet()
        {
            // Create code-block data that is likely to produce 0xFF bytes in the header
            // by using many code-blocks with varying data sizes
            using var ebcot = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                for (int j = 0; j < 64; j++)
                {
                    coefficients[j] = (j + i * 17) * 3;
                }
                codeBlocks[i] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            // Verify bit-stuffing rule: after any 0xFF byte in the header,
            // the next byte must have its MSB = 0 (ITU-T T.800 requirement).
            // The header ends before the code-block data starts.
            // We check the entire packet since the encoder writes header then data.
            var data = packets[0].Data.Span;

            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF)
                {
                    Assert.That(data[i + 1] & 0x80, Is.EqualTo(0),
                        $"Bit-stuffing violation at position {i}: 0xFF followed by 0x{data[i + 1]:X2} (MSB is set)");
                }
            }
        }

        [Test]
        public void BitStuffing_VerifyOnSingleCodeBlock()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            for (int j = 0; j < 64; j++)
            {
                coefficients[j] = j * 5 - 100;
            }
            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var data = packets[0].Data.Span;
            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == 0xFF)
                {
                    Assert.That(data[i + 1] & 0x80, Is.EqualTo(0),
                        $"Bit-stuffing violation at position {i}");
                }
            }
        }

        #endregion

        #region Lblock Growth

        [Test]
        public void LblockGrowth_LargeData_RoundtripsCorrectly()
        {
            // Create a code-block with large encoded data to force Lblock > 3.
            // With initial Lblock=3 and 1 pass (floor(log2(1))=0), the length
            // field has 3 bits, allowing a max data length of 7 bytes.
            // A code-block with many significant coefficients will exceed that.
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            var rng = new Random(42);
            for (int j = 0; j < 64; j++)
            {
                coefficients[j] = rng.Next(-500, 500);
            }

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            // Verify that encoded data is larger than what Lblock=3 can represent (7 bytes)
            Assert.That(cbData.Data.Length, Is.GreaterThan(7),
                "Test setup: code-block data should exceed initial Lblock capacity");

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses));
            Assert.That(results[0].Data.Length, Is.EqualTo(cbData.Data.Length));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(cbData.Data.ToArray()));
        }

        [Test]
        public void LblockGrowth_VeryLargeCodeBlock_RoundtripsCorrectly()
        {
            // Use a 16x16 code-block with highly varied coefficients
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[256];
            var rng = new Random(123);
            for (int j = 0; j < 256; j++)
            {
                coefficients[j] = rng.Next(-2000, 2000);
            }

            var cbData = ebcot.EncodeCodeBlock(coefficients, 16, 16, subbandType: 0);

            // This should produce a substantial amount of encoded data
            Assert.That(cbData.Data.Length, Is.GreaterThan(50),
                "Test setup: 16x16 code-block should produce substantial encoded data");

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(cbData.Data.ToArray()));
        }

        #endregion

        #region Multi-Layer

        [Test]
        public void MultiLayer_TwoLayers_BothDecodeCorrectly()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 200;
            coefficients[1] = 100;
            coefficients[2] = -50;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            // Need enough passes to distribute across 2 layers
            Assert.That(cbData.NumPasses, Is.GreaterThanOrEqualTo(2),
                "Test setup: code-block needs at least 2 passes for 2-layer test");

            var codeBlocks = new CodeBlockData[] { cbData };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 1, 1, numLayers: 2, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(2));

            // Both packets should have data
            Assert.That(packets[0].Data.Length, Is.GreaterThan(0), "Layer 0 should have data");
            Assert.That(packets[1].Data.Length, Is.GreaterThan(0), "Layer 1 should have data");
            Assert.That(packets[0].Layer, Is.EqualTo(0));
            Assert.That(packets[1].Layer, Is.EqualTo(1));

            // Decode all layers and verify cumulative result matches original
            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses));
            Assert.That(results[0].Data.Length, Is.EqualTo(cbData.Data.Length));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(cbData.Data.ToArray()));
        }

        [Test]
        public void MultiLayer_TwoLayers_2x2CodeBlocks_AllDecodeCorrectly()
        {
            using var ebcot = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                coefficients[0] = 100 + i * 50;
                coefficients[1] = 40 + i * 20;
                codeBlocks[i] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 2, ProgressionOrder.LRCP);

            Assert.That(packets.Length, Is.EqualTo(2));

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            Assert.That(results.Length, Is.EqualTo(4));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(results[i].TotalPasses, Is.EqualTo(codeBlocks[i].NumPasses),
                    $"Code-block {i} pass count mismatch after 2-layer decode");
                Assert.That(results[i].Data.ToArray(), Is.EqualTo(codeBlocks[i].Data.ToArray()),
                    $"Code-block {i} data content mismatch after 2-layer decode");
            }
        }

        [Test]
        public void MultiLayer_TagTreesProgressAcrossLayers()
        {
            // Verify that decoding layer 0 then layer 1 works with tag tree state
            // carried across packets. Use individual DecodePacket calls.
            using var ebcot = new EbcotEncoder();

            int[] coefficients = new int[64];
            coefficients[0] = 200;
            coefficients[1] = 100;
            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 2, ProgressionOrder.LRCP);

            // Decode layer by layer using the full-parameter DecodePacket method
            var decoder = new PacketDecoder();
            decoder.InitPrecinct(1, 1);
            bool[] firstInclusion = { true };

            var seg0 = decoder.DecodePacket(packets[0].Data.Span, 1, firstInclusion, 1, 1, 0);
            Assert.That(seg0[0].NumNewPasses, Is.GreaterThan(0), "Layer 0 should include passes");
            Assert.That(seg0[0].IsFirstInclusion, Is.True, "Layer 0 should be first inclusion");

            var seg1 = decoder.DecodePacket(packets[1].Data.Span, 1, firstInclusion, 1, 1, 1);
            Assert.That(seg1[0].IsFirstInclusion, Is.False, "Layer 1 should not be first inclusion");

            int totalPasses = seg0[0].NumNewPasses + seg1[0].NumNewPasses;
            Assert.That(totalPasses, Is.EqualTo(cbData.NumPasses));
        }

        #endregion

        #region Zero Bitplane Coding

        [Test]
        public void ZeroBitPlanes_SmallValue_CorrectlyEncoded()
        {
            // A coefficient with value 1 has MsbPosition = 0, zeroBitPlanes = 31
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 1;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            Assert.That(cbData.MsbPosition, Is.EqualTo(0));

            int expectedZeroBitPlanes = 31;

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].ZeroBitPlanes, Is.EqualTo(expectedZeroBitPlanes));
        }

        [Test]
        public void ZeroBitPlanes_LargeValue_CorrectlyEncoded()
        {
            // A coefficient with value 255 has MsbPosition = 7, zeroBitPlanes = 24
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 255;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            Assert.That(cbData.MsbPosition, Is.EqualTo(7));

            int expectedZeroBitPlanes = 24;

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].ZeroBitPlanes, Is.EqualTo(expectedZeroBitPlanes));
        }

        [Test]
        public void ZeroBitPlanes_VaryingValues_2x2TagTree()
        {
            // Different zero bitplane values for a 2x2 grid exercises the tag tree
            using var ebcot = new EbcotEncoder();

            // magnitudes: 1 (msb=0, zbp=31), 3 (msb=1, zbp=30), 7 (msb=2, zbp=29), 255 (msb=7, zbp=24)
            int[] magnitudes = { 1, 3, 7, 255 };
            int[] expectedZbp = { 31, 30, 29, 24 };

            var codeBlocks = new CodeBlockData[4];
            for (int i = 0; i < 4; i++)
            {
                int[] coefficients = new int[64];
                coefficients[0] = magnitudes[i];
                codeBlocks[i] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            }

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            for (int i = 0; i < 4; i++)
            {
                Assert.That(results[i].ZeroBitPlanes, Is.EqualTo(expectedZbp[i]),
                    $"Code-block {i} (magnitude={magnitudes[i]}) zero bitplane mismatch");
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void EmptyCodeBlockArray_ReturnsEmptyPackets()
        {
            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(Array.Empty<CodeBlockData>(), 0, 0, numLayers: 1, ProgressionOrder.LRCP);

            Assert.That(packets, Is.Empty);
        }

        [Test]
        public void SingleCodeBlock_1x1_MinimalCase()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[1];
            coefficients[0] = 42;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 1, 1, subbandType: 0);
            var codeBlocks = new CodeBlockData[] { cbData };

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

            Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(cbData.Data.ToArray()));
        }

        [Test]
        public void MixedEmptyAndNonEmpty_CodeBlocks_RoundtripsCorrectly()
        {
            using var ebcot = new EbcotEncoder();

            var codeBlocks = new CodeBlockData[4];
            // Code-blocks 0 and 2 have data, 1 and 3 are empty
            int[] coefficients = new int[64];
            coefficients[0] = 100;
            codeBlocks[0] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            codeBlocks[1] = CodeBlockData.Empty;

            coefficients = new int[64];
            coefficients[0] = 200;
            codeBlocks[2] = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);
            codeBlocks[3] = CodeBlockData.Empty;

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(codeBlocks, 2, 2, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            var results = decoder.DecodeAllPackets(packets, numCodeBlocks: 4, codeBlocksWide: 2, codeBlocksHigh: 2);

            // Non-empty code-blocks should have data
            Assert.That(results[0].TotalPasses, Is.EqualTo(codeBlocks[0].NumPasses));
            Assert.That(results[0].Data.ToArray(), Is.EqualTo(codeBlocks[0].Data.ToArray()));

            Assert.That(results[2].TotalPasses, Is.EqualTo(codeBlocks[2].NumPasses));
            Assert.That(results[2].Data.ToArray(), Is.EqualTo(codeBlocks[2].Data.ToArray()));

            // Empty code-blocks should have no data
            Assert.That(results[1].TotalPasses, Is.EqualTo(0));
            Assert.That(results[1].Data.IsEmpty, Is.True);
            Assert.That(results[3].TotalPasses, Is.EqualTo(0));
            Assert.That(results[3].Data.IsEmpty, Is.True);
        }

        [Test]
        public void PacketDecoder_DecodePacket_EmptySpan_ReturnsEmptySegments()
        {
            var decoder = new PacketDecoder();
            bool[] firstInclusion = { true, true };

            var segments = decoder.DecodePacket(ReadOnlySpan<byte>.Empty, 2, firstInclusion);

            Assert.That(segments.Length, Is.EqualTo(2));
            Assert.That(segments[0].NumNewPasses, Is.EqualTo(0));
            Assert.That(segments[1].NumNewPasses, Is.EqualTo(0));
        }

        [Test]
        public void PacketDecoder_BytesConsumed_TracksCorrectly()
        {
            using var ebcot = new EbcotEncoder();
            int[] coefficients = new int[64];
            coefficients[0] = 100;

            var cbData = ebcot.EncodeCodeBlock(coefficients, 8, 8, subbandType: 0);

            var encoder = new PacketEncoder();
            var packets = encoder.EncodePackets(
                new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

            var decoder = new PacketDecoder();
            decoder.InitPrecinct(1, 1);
            bool[] firstInclusion = { true };

            decoder.DecodePacket(packets[0].Data.Span, 1, firstInclusion, 1, 1, 0);

            Assert.That(decoder.BytesConsumed, Is.GreaterThan(0));
            Assert.That(decoder.BytesConsumed, Is.LessThanOrEqualTo(packets[0].Data.Length));
        }

        #endregion

        #region NumPasses Coding (Table B.4)

        [Test]
        public void NumPasses_VariousValues_RoundtripCorrectly()
        {
            // Test code-blocks with different numbers of passes, which exercise
            // different branches of the Table B.4 coding:
            //   1 pass -> "0"
            //   2 passes -> "10"
            //   3-5 passes -> "11xx"
            //   6-36 passes -> "1111xxxxx"
            //   37+ passes -> "111111111xxxxxxx"
            using var ebcot = new EbcotEncoder();

            // Create different code-blocks that produce varying pass counts
            // by using different numbers of significant coefficients
            var testCases = new[]
            {
                new { Coeff = 1, Width = 4, Height = 4, Label = "minimal" },
                new { Coeff = 64, Width = 8, Height = 8, Label = "moderate" },
                new { Coeff = 1000, Width = 8, Height = 8, Label = "large" },
            };

            foreach (var tc in testCases)
            {
                int[] coefficients = new int[tc.Width * tc.Height];
                coefficients[0] = tc.Coeff;

                var cbData = ebcot.EncodeCodeBlock(coefficients, tc.Width, tc.Height, subbandType: 0);
                if (cbData.NumPasses == 0) continue;

                var encoder = new PacketEncoder();
                var packets = encoder.EncodePackets(
                    new CodeBlockData[] { cbData }, 1, 1, numLayers: 1, ProgressionOrder.LRCP);

                var decoder = new PacketDecoder();
                var results = decoder.DecodeAllPackets(
                    packets, numCodeBlocks: 1, codeBlocksWide: 1, codeBlocksHigh: 1);

                Assert.That(results[0].TotalPasses, Is.EqualTo(cbData.NumPasses),
                    $"NumPasses roundtrip failed for {tc.Label} (magnitude={tc.Coeff})");
            }
        }

        #endregion
    }
}
