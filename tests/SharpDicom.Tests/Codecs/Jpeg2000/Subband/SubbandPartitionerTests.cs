using System;
using System.Linq;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Subband;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Subband
{
    /// <summary>
    /// Tests for <see cref="SubbandPartitioner"/> verifying correct subband dimensions,
    /// code-block grid sizes, and code-block to subband mapping.
    /// </summary>
    [TestFixture]
    public class SubbandPartitionerTests
    {
        #region Subband Count and Structure

        [Test]
        public void ZeroLevels_ReturnsSingleLLSubband()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 0, 64, 64);

            Assert.That(subbands.Length, Is.EqualTo(1));
            Assert.That(subbands[0].Type, Is.EqualTo(SubbandType.LL));
            Assert.That(subbands[0].Width, Is.EqualTo(256));
            Assert.That(subbands[0].Height, Is.EqualTo(256));
            Assert.That(subbands[0].OriginX, Is.EqualTo(0));
            Assert.That(subbands[0].OriginY, Is.EqualTo(0));
        }

        [Test]
        public void OneLevelRetursFourSubbands()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 1, 64, 64);

            Assert.That(subbands.Length, Is.EqualTo(4)); // 1 LL + 3 detail
        }

        [TestCase(1, 4)]
        [TestCase(2, 7)]
        [TestCase(3, 10)]
        [TestCase(5, 16)]
        public void SubbandCount_Is_1Plus3TimesLevels(int levels, int expectedCount)
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, levels, 64, 64);
            Assert.That(subbands.Length, Is.EqualTo(expectedCount));
        }

        [Test]
        public void SubbandOrder_LLThenDetailLevels()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 3, 64, 64);

            // Index 0 = LL at level 0
            Assert.That(subbands[0].Type, Is.EqualTo(SubbandType.LL));
            Assert.That(subbands[0].ResolutionLevel, Is.EqualTo(0));

            // Indices 1-3: detail at level 1 (deepest detail)
            Assert.That(subbands[1].Type, Is.EqualTo(SubbandType.HL));
            Assert.That(subbands[1].ResolutionLevel, Is.EqualTo(1));
            Assert.That(subbands[2].Type, Is.EqualTo(SubbandType.LH));
            Assert.That(subbands[2].ResolutionLevel, Is.EqualTo(1));
            Assert.That(subbands[3].Type, Is.EqualTo(SubbandType.HH));
            Assert.That(subbands[3].ResolutionLevel, Is.EqualTo(1));

            // Indices 4-6: detail at level 2
            Assert.That(subbands[4].ResolutionLevel, Is.EqualTo(2));
            Assert.That(subbands[5].ResolutionLevel, Is.EqualTo(2));
            Assert.That(subbands[6].ResolutionLevel, Is.EqualTo(2));

            // Indices 7-9: detail at level 3 (shallowest detail)
            Assert.That(subbands[7].ResolutionLevel, Is.EqualTo(3));
            Assert.That(subbands[8].ResolutionLevel, Is.EqualTo(3));
            Assert.That(subbands[9].ResolutionLevel, Is.EqualTo(3));
        }

        #endregion

        #region 256x256 with 5 levels

        [Test]
        public void Square256_5Levels_LLDimensions()
        {
            // 256 -> 128 -> 64 -> 32 -> 16 -> 8
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 5, 64, 64);

            var ll = subbands[0];
            Assert.That(ll.Type, Is.EqualTo(SubbandType.LL));
            Assert.That(ll.Width, Is.EqualTo(8));
            Assert.That(ll.Height, Is.EqualTo(8));
        }

        [Test]
        public void Square256_5Levels_DetailDimensionsAtEachLevel()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 5, 64, 64);

            // Level 5 (shallowest, decomposing 256x256):
            // HL: floor(256/2) x ceil(256/2) = 128 x 128
            // LH: ceil(256/2) x floor(256/2) = 128 x 128
            // HH: floor(256/2) x floor(256/2) = 128 x 128
            int idx5 = 1 + 3 * (5 - 1); // index 13
            Assert.That(subbands[idx5].Type, Is.EqualTo(SubbandType.HL));
            Assert.That(subbands[idx5].Width, Is.EqualTo(128));
            Assert.That(subbands[idx5].Height, Is.EqualTo(128));
            Assert.That(subbands[idx5 + 1].Width, Is.EqualTo(128));
            Assert.That(subbands[idx5 + 1].Height, Is.EqualTo(128));
            Assert.That(subbands[idx5 + 2].Width, Is.EqualTo(128));
            Assert.That(subbands[idx5 + 2].Height, Is.EqualTo(128));

            // Level 1 (deepest detail, decomposing 16x16):
            // HL: floor(16/2) x ceil(16/2) = 8 x 8
            int idx1 = 1;
            Assert.That(subbands[idx1].Width, Is.EqualTo(8));
            Assert.That(subbands[idx1].Height, Is.EqualTo(8));
        }

        [Test]
        public void Square256_5Levels_TotalSubbands()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 5, 64, 64);
            Assert.That(subbands.Length, Is.EqualTo(16)); // 1 + 3*5
        }

        #endregion

        #region 255x255 (odd dimensions)

        [Test]
        public void Odd255_NoOffByOne_LLDimensions()
        {
            // 255 -> ceil(255/2) = 128 -> 64 -> 32 -> 16 -> 8
            var subbands = SubbandPartitioner.GetSubbands(255, 255, 5, 64, 64);

            var ll = subbands[0];
            Assert.That(ll.Width, Is.EqualTo(8));
            Assert.That(ll.Height, Is.EqualTo(8));
        }

        [Test]
        public void Odd255_Level5Detail_AsymmetricDimensions()
        {
            // Decomposing 255x255:
            // HL: floor(255/2) x ceil(255/2) = 127 x 128
            // LH: ceil(255/2) x floor(255/2) = 128 x 127
            // HH: floor(255/2) x floor(255/2) = 127 x 127
            var subbands = SubbandPartitioner.GetSubbands(255, 255, 5, 64, 64);

            int idx5 = 1 + 3 * (5 - 1);
            Assert.That(subbands[idx5].Type, Is.EqualTo(SubbandType.HL));
            Assert.That(subbands[idx5].Width, Is.EqualTo(127));
            Assert.That(subbands[idx5].Height, Is.EqualTo(128));

            Assert.That(subbands[idx5 + 1].Type, Is.EqualTo(SubbandType.LH));
            Assert.That(subbands[idx5 + 1].Width, Is.EqualTo(128));
            Assert.That(subbands[idx5 + 1].Height, Is.EqualTo(127));

            Assert.That(subbands[idx5 + 2].Type, Is.EqualTo(SubbandType.HH));
            Assert.That(subbands[idx5 + 2].Width, Is.EqualTo(127));
            Assert.That(subbands[idx5 + 2].Height, Is.EqualTo(127));
        }

        [Test]
        public void Odd255_DetailSumsMatchLLAtNextLevel()
        {
            // At each level, the LL that was decomposed should have width = HL_w + LL_w (or LH_w)
            var subbands = SubbandPartitioner.GetSubbands(255, 255, 3, 64, 64);

            // The LL region that level 3 decomposes is 255x255 (the full image).
            // LL of level 2 should be ceil(255/2) = 128 wide
            // HL of level 3 should be floor(255/2) = 127 wide
            // Sum: 128 + 127 = 255
            int idx3 = 1 + 3 * (3 - 1);  // index 7 = level 3 detail

            // Verify the HL width + LH width = parent dimension (255)
            var hl3 = subbands[idx3];
            var lh3 = subbands[idx3 + 1];
            Assert.That(hl3.Width + lh3.Width, Is.EqualTo(255),
                "HL_w + LH_w should equal parent dimension for odd width");
            Assert.That(hl3.Height + lh3.Height, Is.EqualTo(255),
                "HL_h + LH_h should equal parent dimension for odd height");
        }

        #endregion

        #region 1x1 image

        [Test]
        public void SinglePixel_1x1_WithLevels_ProducesValidStructure()
        {
            // A 1x1 image with 1 decomposition level:
            // LL = ceil(1/2) x ceil(1/2) = 1x1
            // HL = floor(1/2) x ceil(1/2) = 0x1
            // LH = ceil(1/2) x floor(1/2) = 1x0
            // HH = floor(1/2) x floor(1/2) = 0x0
            var subbands = SubbandPartitioner.GetSubbands(1, 1, 1, 64, 64);

            Assert.That(subbands.Length, Is.EqualTo(4));

            var ll = subbands[0];
            Assert.That(ll.Width, Is.EqualTo(1));
            Assert.That(ll.Height, Is.EqualTo(1));
            Assert.That(ll.CodeBlockGridWidth, Is.EqualTo(1));
            Assert.That(ll.CodeBlockGridHeight, Is.EqualTo(1));

            // Detail subbands should have 0-dimension(s) and 0 code-blocks
            var hl = subbands[1];
            Assert.That(hl.Width, Is.EqualTo(0));
            Assert.That(hl.CodeBlockGridWidth, Is.EqualTo(0));

            var lh = subbands[2];
            Assert.That(lh.Height, Is.EqualTo(0));
            Assert.That(lh.CodeBlockGridHeight, Is.EqualTo(0));

            var hh = subbands[3];
            Assert.That(hh.Width, Is.EqualTo(0));
            Assert.That(hh.Height, Is.EqualTo(0));
            Assert.That(hh.TotalCodeBlocks, Is.EqualTo(0));
        }

        [Test]
        public void SinglePixel_ZeroLevels_SingleLL()
        {
            var subbands = SubbandPartitioner.GetSubbands(1, 1, 0, 64, 64);

            Assert.That(subbands.Length, Is.EqualTo(1));
            Assert.That(subbands[0].Width, Is.EqualTo(1));
            Assert.That(subbands[0].Height, Is.EqualTo(1));
            Assert.That(subbands[0].TotalCodeBlocks, Is.EqualTo(1));
        }

        #endregion

        #region 512x256 (non-square)

        [Test]
        public void NonSquare_512x256_DetailDimensions()
        {
            // 512x256, 3 levels
            // Level 3 (shallowest) decomposes 512x256:
            //   HL: floor(512/2) x ceil(256/2) = 256 x 128
            //   LH: ceil(512/2) x floor(256/2) = 256 x 128
            //   HH: floor(512/2) x floor(256/2) = 256 x 128
            //
            // Level 2 decomposes 256x128 (the LL from level 3):
            //   HL: floor(256/2) x ceil(128/2) = 128 x 64
            //
            // Level 1 decomposes 128x64:
            //   HL: floor(128/2) x ceil(64/2) = 64 x 32
            //
            // LL = 64x32
            var subbands = SubbandPartitioner.GetSubbands(512, 256, 3, 64, 64);

            Assert.That(subbands[0].Width, Is.EqualTo(64), "LL width");
            Assert.That(subbands[0].Height, Is.EqualTo(32), "LL height");

            // Level 3 detail (shallowest)
            int idx3 = 1 + 3 * (3 - 1);
            Assert.That(subbands[idx3].Width, Is.EqualTo(256), "HL level 3 width");
            Assert.That(subbands[idx3].Height, Is.EqualTo(128), "HL level 3 height");

            // Level 1 detail (deepest)
            Assert.That(subbands[1].Width, Is.EqualTo(64), "HL level 1 width");
            Assert.That(subbands[1].Height, Is.EqualTo(32), "HL level 1 height");
        }

        [Test]
        public void NonSquare_512x256_OriginPositions()
        {
            var subbands = SubbandPartitioner.GetSubbands(512, 256, 1, 64, 64);

            // Single level: decomposes 512x256
            // LL: (0,0) 256x128
            // HL: (256,0) 256x128
            // LH: (0,128) 256x128
            // HH: (256,128) 256x128
            Assert.That(subbands[0].OriginX, Is.EqualTo(0), "LL originX");
            Assert.That(subbands[0].OriginY, Is.EqualTo(0), "LL originY");

            Assert.That(subbands[1].OriginX, Is.EqualTo(256), "HL originX");
            Assert.That(subbands[1].OriginY, Is.EqualTo(0), "HL originY");

            Assert.That(subbands[2].OriginX, Is.EqualTo(0), "LH originX");
            Assert.That(subbands[2].OriginY, Is.EqualTo(128), "LH originY");

            Assert.That(subbands[3].OriginX, Is.EqualTo(256), "HH originX");
            Assert.That(subbands[3].OriginY, Is.EqualTo(128), "HH originY");
        }

        #endregion

        #region Code-block grid

        [Test]
        public void CodeBlockGrid_256x256_64x64_1Level()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 1, 64, 64);

            // LL: 128x128, grid = 2x2
            Assert.That(subbands[0].CodeBlockGridWidth, Is.EqualTo(2));
            Assert.That(subbands[0].CodeBlockGridHeight, Is.EqualTo(2));

            // HL: 128x128, grid = 2x2
            Assert.That(subbands[1].CodeBlockGridWidth, Is.EqualTo(2));
            Assert.That(subbands[1].CodeBlockGridHeight, Is.EqualTo(2));
        }

        [Test]
        public void CodeBlockGrid_255x255_CeilDivision()
        {
            var subbands = SubbandPartitioner.GetSubbands(255, 255, 1, 64, 64);

            // HL: 127x128, grid = ceil(127/64) x ceil(128/64) = 2x2
            Assert.That(subbands[1].CodeBlockGridWidth, Is.EqualTo(2));
            Assert.That(subbands[1].CodeBlockGridHeight, Is.EqualTo(2));

            // LH: 128x127, grid = ceil(128/64) x ceil(127/64) = 2x2
            Assert.That(subbands[2].CodeBlockGridWidth, Is.EqualTo(2));
            Assert.That(subbands[2].CodeBlockGridHeight, Is.EqualTo(2));
        }

        [Test]
        public void CodeBlockGrid_SmallSubband_SingleCodeBlock()
        {
            // 8x8 image with 64x64 code-blocks: each subband fits in one code-block
            var subbands = SubbandPartitioner.GetSubbands(8, 8, 1, 64, 64);

            // LL: 4x4, grid = 1x1
            Assert.That(subbands[0].CodeBlockGridWidth, Is.EqualTo(1));
            Assert.That(subbands[0].CodeBlockGridHeight, Is.EqualTo(1));
        }

        #endregion

        #region GetSubbandForCodeBlock

        [Test]
        public void GetSubbandForCodeBlock_ReturnsCorrectType()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 3, 64, 64);

            // LL (index 0)
            Assert.That(SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, 0),
                Is.EqualTo(SubbandType.LL));

            // HL at level 1 (index 1)
            Assert.That(SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, 1),
                Is.EqualTo(SubbandType.HL));

            // LH at level 1 (index 2)
            Assert.That(SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, 2),
                Is.EqualTo(SubbandType.LH));

            // HH at level 1 (index 3)
            Assert.That(SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, 3),
                Is.EqualTo(SubbandType.HH));
        }

        [Test]
        public void GetSubbandForCodeBlock_OutOfRange_Throws()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 1, 64, 64);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, -1));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbandForCodeBlock(subbands, 0, 0, subbands.Length));

            // Code-block position out of range
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbandForCodeBlock(subbands, 99, 0, 0));
        }

        #endregion

        #region FindSubbandAt

        [Test]
        public void FindSubbandAt_ReturnsCorrectSubband()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 1, 64, 64);

            // At level 1 (the detail level), origin for HL is (128, 0)
            var found = SubbandPartitioner.FindSubbandAt(subbands, 128, 0, 1);
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Value.Type, Is.EqualTo(SubbandType.HL));

            // LH origin is (0, 128)
            found = SubbandPartitioner.FindSubbandAt(subbands, 0, 128, 1);
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Value.Type, Is.EqualTo(SubbandType.LH));
        }

        [Test]
        public void FindSubbandAt_OutOfBounds_ReturnsNull()
        {
            var subbands = SubbandPartitioner.GetSubbands(256, 256, 1, 64, 64);
            var found = SubbandPartitioner.FindSubbandAt(subbands, 999, 999, 1);
            Assert.That(found, Is.Null);
        }

        #endregion

        #region Argument Validation

        [Test]
        public void InvalidArguments_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbands(0, 256, 5, 64, 64));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbands(256, 0, 5, 64, 64));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbands(256, 256, -1, 64, 64));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbands(256, 256, 5, 0, 64));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandPartitioner.GetSubbands(256, 256, 5, 64, 0));
        }

        #endregion

        #region Dimensional consistency

        [Test]
        public void AllSubbands_WidthAndHeightSumConsistent([Values(255, 256, 512, 127, 1024)] int size)
        {
            // For each decomposition, LL_w + HL_w = parent_w and LL_h + LH_h = parent_h
            var subbands = SubbandPartitioner.GetSubbands(size, size, 3, 64, 64);

            // The shallowest detail (level 3) decomposes the full image
            int idx3 = 1 + 3 * (3 - 1);
            var hl3 = subbands[idx3];
            var lh3 = subbands[idx3 + 1];

            // LL_w = ceil(size/2), HL_w = floor(size/2), sum should = size
            int llW = (size + 1) / 2;
            Assert.That(hl3.Width + llW, Is.EqualTo(size),
                $"HL_w({hl3.Width}) + LL_w({llW}) should equal {size}");

            int llH = (size + 1) / 2;
            Assert.That(lh3.Height + llH, Is.EqualTo(size),
                $"LH_h({lh3.Height}) + LL_h({llH}) should equal {size}");
        }

        #endregion
    }
}
