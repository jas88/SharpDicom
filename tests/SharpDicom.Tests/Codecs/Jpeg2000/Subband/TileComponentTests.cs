using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Subband;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Subband
{
    /// <summary>
    /// Tests for <see cref="TileComponent"/> verifying coefficient access,
    /// code-block extraction/insertion, edge handling, and disposal.
    /// </summary>
    [TestFixture]
    public class TileComponentTests
    {
        #region Construction

        [Test]
        public void Constructor_SetsPropertiesCorrectly()
        {
            using var tc = new TileComponent(0, 1, 256, 256, 3, 64, 64);

            Assert.That(tc.TileIndex, Is.EqualTo(0));
            Assert.That(tc.ComponentIndex, Is.EqualTo(1));
            Assert.That(tc.TileWidth, Is.EqualTo(256));
            Assert.That(tc.TileHeight, Is.EqualTo(256));
            Assert.That(tc.DecompositionLevels, Is.EqualTo(3));
            Assert.That(tc.CodeBlockWidth, Is.EqualTo(64));
            Assert.That(tc.CodeBlockHeight, Is.EqualTo(64));
            Assert.That(tc.Subbands.Length, Is.EqualTo(10)); // 1 + 3*3
        }

        [Test]
        public void Constructor_InvalidArgs_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(-1, 0, 64, 64, 1, 64, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, -1, 64, 64, 1, 64, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, 0, 0, 64, 1, 64, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, 0, 64, 0, 1, 64, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, 0, 64, 64, -1, 64, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, 0, 64, 64, 1, 0, 64));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TileComponent(0, 0, 64, 64, 1, 64, 0));
        }

        [Test]
        public void Coefficients_InitiallyZero()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var coeffs = tc.Coefficients;
            Assert.That(coeffs.Length, Is.EqualTo(64 * 64));
            for (int i = 0; i < coeffs.Length; i++)
            {
                Assert.That(coeffs[i], Is.EqualTo(0));
            }
        }

        #endregion

        #region GetCodeBlockCoefficients

        [Test]
        public void GetCodeBlockCoefficients_ExtractsCorrectRegion()
        {
            // 64x64 tile, 1 decomposition level, 32x32 code-blocks
            // LL = 32x32 at (0,0), HL = 32x32 at (32,0), LH = 32x32 at (0,32), HH = 32x32 at (32,32)
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            // Fill the coefficient array with identifiable values
            var coeffs = tc.Coefficients;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    coeffs[y * 64 + x] = y * 1000 + x;
                }
            }

            // Extract HL subband (index 1, origin at (32, 0))
            var buffer = new int[32 * 32];
            var (w, h) = tc.GetCodeBlockCoefficients(1, 0, 0, buffer);

            Assert.That(w, Is.EqualTo(32));
            Assert.That(h, Is.EqualTo(32));

            // The HL origin is at (32, 0), so first row should be:
            // coefficients at (32,0), (33,0), ... (63,0)
            Assert.That(buffer[0], Is.EqualTo(0 * 1000 + 32));
            Assert.That(buffer[1], Is.EqualTo(0 * 1000 + 33));
            Assert.That(buffer[31], Is.EqualTo(0 * 1000 + 63));

            // Second row
            Assert.That(buffer[32], Is.EqualTo(1 * 1000 + 32));
        }

        [Test]
        public void GetCodeBlockCoefficients_LLSubband_StartsAtOriginZero()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var coeffs = tc.Coefficients;
            for (int i = 0; i < coeffs.Length; i++)
            {
                coeffs[i] = i;
            }

            var buffer = new int[32 * 32];
            tc.GetCodeBlockCoefficients(0, 0, 0, buffer);

            // LL origin is (0,0), so buffer[0] should be coefficient at (0,0)
            Assert.That(buffer[0], Is.EqualTo(0));
            Assert.That(buffer[1], Is.EqualTo(1));
            // Second row: stride is TileWidth=64, but LL width is 32.
            // buffer[32] = coefficient at (0, 1) = 1*64 + 0 = 64
            Assert.That(buffer[32], Is.EqualTo(64));
        }

        [Test]
        public void GetCodeBlockCoefficients_HHSubband_CorrectOrigin()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var coeffs = tc.Coefficients;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    coeffs[y * 64 + x] = y * 100 + x;
                }
            }

            // HH is index 3, origin at (32, 32)
            var buffer = new int[32 * 32];
            tc.GetCodeBlockCoefficients(3, 0, 0, buffer);

            Assert.That(buffer[0], Is.EqualTo(32 * 100 + 32), "HH(0,0) = coeff at (32,32)");
        }

        #endregion

        #region SetCodeBlockCoefficients

        [Test]
        public void SetCodeBlockCoefficients_WritesCorrectRegion()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            // Write known pattern into HL subband (index 1, origin at (32, 0))
            var source = new int[32 * 32];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = 42 + i;
            }

            tc.SetCodeBlockCoefficients(1, 0, 0, source);

            // Verify the coefficients were written to the correct position
            var coeffs = tc.Coefficients;
            // HL origin = (32, 0). First element: (32, 0) in the coefficient array
            Assert.That(coeffs[0 * 64 + 32], Is.EqualTo(42), "First HL coeff at (32,0)");
            Assert.That(coeffs[0 * 64 + 33], Is.EqualTo(43), "Second HL coeff at (33,0)");
            Assert.That(coeffs[1 * 64 + 32], Is.EqualTo(42 + 32), "First coeff of row 1");
        }

        [Test]
        public void SetAndGet_Roundtrip()
        {
            using var tc = new TileComponent(0, 0, 128, 128, 2, 32, 32);

            // Pick a subband and code-block
            // Level 2 detail: index 4 (HL), 5 (LH), 6 (HH)
            int subbandIdx = 4;
            var sb = tc.Subbands[subbandIdx];

            if (sb.TotalCodeBlocks == 0)
            {
                Assert.Pass("Subband has no code-blocks (degenerate case).");
                return;
            }

            // Write pattern
            var source = new int[32 * 32];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = (i * 7 + 13) % 1000;
            }

            tc.SetCodeBlockCoefficients(subbandIdx, 0, 0, source);

            // Read it back
            var dest = new int[32 * 32];
            var (w, h) = tc.GetCodeBlockCoefficients(subbandIdx, 0, 0, dest);

            // Only the actual code-block region should match
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Assert.That(dest[y * 32 + x], Is.EqualTo(source[y * 32 + x]),
                        $"Mismatch at ({x},{y})");
                }
            }
        }

        #endregion

        #region Edge code-blocks (partial)

        [Test]
        public void EdgeCodeBlock_PartialWidth()
        {
            // 100x64, 1 level, 64x64 code-blocks
            // LL: 50x32 at (0,0) -> grid = ceil(50/64)xceil(32/64) = 1x1
            // HL: 50x32 at (50,0) -> grid = ceil(50/64)xceil(32/64) = 1x1
            // Actually: LL width=ceil(100/2)=50, HL width=floor(100/2)=50,
            //           LL height=ceil(64/2)=32, LH height=floor(64/2)=32
            using var tc = new TileComponent(0, 0, 100, 64, 1, 64, 64);

            var coeffs = tc.Coefficients;
            for (int i = 0; i < coeffs.Length; i++)
            {
                coeffs[i] = i + 1;
            }

            // LL subband (index 0): 50x32, one code-block
            var buffer = new int[64 * 64];
            var (w, h) = tc.GetCodeBlockCoefficients(0, 0, 0, buffer);

            Assert.That(w, Is.EqualTo(50), "LL width should be 50 (partial code-block)");
            Assert.That(h, Is.EqualTo(32), "LL height should be 32 (partial code-block)");

            // Verify that beyond the actual width, buffer is zeroed
            Assert.That(buffer[50], Is.EqualTo(0), "Beyond actual width should be zero");
        }

        [Test]
        public void EdgeCodeBlock_MultipleCodeBlocks_LastIsPartial()
        {
            // 96x96, 0 levels (single LL band), 64x64 code-blocks
            // LL = 96x96, grid = ceil(96/64) x ceil(96/64) = 2x2
            using var tc = new TileComponent(0, 0, 96, 96, 0, 64, 64);

            var coeffs = tc.Coefficients;
            for (int y = 0; y < 96; y++)
            {
                for (int x = 0; x < 96; x++)
                {
                    coeffs[y * 96 + x] = y * 100 + x;
                }
            }

            // Code-block (1, 1) is partial: covers x=64..95, y=64..95 -> 32x32
            var buffer = new int[64 * 64];
            var (w, h) = tc.GetCodeBlockCoefficients(0, 1, 1, buffer);

            Assert.That(w, Is.EqualTo(32), "Partial CB width");
            Assert.That(h, Is.EqualTo(32), "Partial CB height");

            Assert.That(buffer[0], Is.EqualTo(64 * 100 + 64), "Top-left of partial CB");
        }

        #endregion

        #region Disposal

        [Test]
        public void Dispose_ReleasesBuffer()
        {
            var tc = new TileComponent(0, 0, 256, 256, 3, 64, 64);
            tc.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = tc.Coefficients);
        }

        [Test]
        public void Dispose_Multiple_DoesNotThrow()
        {
            var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);
            tc.Dispose();
            Assert.DoesNotThrow(() => tc.Dispose());
        }

        [Test]
        public void AfterDispose_GetCodeBlock_Throws()
        {
            var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);
            tc.Dispose();

            var buffer = new int[32 * 32];
            Assert.Throws<ObjectDisposedException>(() =>
                tc.GetCodeBlockCoefficients(0, 0, 0, buffer));
        }

        [Test]
        public void AfterDispose_SetCodeBlock_Throws()
        {
            var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);
            tc.Dispose();

            var source = new int[32 * 32];
            Assert.Throws<ObjectDisposedException>(() =>
                tc.SetCodeBlockCoefficients(0, 0, 0, source));
        }

        #endregion

        #region Small tile (non-pooled)

        [Test]
        public void SmallTile_UsesDirectAllocation()
        {
            // < 1024 elements: 16*16 = 256 elements
            using var tc = new TileComponent(0, 0, 16, 16, 1, 8, 8);

            Assert.That(tc.Coefficients.Length, Is.EqualTo(256));

            // Should still work correctly
            var coeffs = tc.Coefficients;
            coeffs[0] = 42;
            Assert.That(tc.Coefficients[0], Is.EqualTo(42));
        }

        [Test]
        public void LargeTile_UsesPooling()
        {
            // 256*256 = 65536 elements, should use pool
            using var tc = new TileComponent(0, 0, 256, 256, 1, 64, 64);

            Assert.That(tc.Coefficients.Length, Is.EqualTo(256 * 256));
        }

        #endregion

        #region Argument validation

        [Test]
        public void GetCodeBlock_InvalidSubbandIndex_Throws()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var buffer = new int[32 * 32];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tc.GetCodeBlockCoefficients(-1, 0, 0, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tc.GetCodeBlockCoefficients(tc.Subbands.Length, 0, 0, buffer));
        }

        [Test]
        public void GetCodeBlock_InvalidCBPosition_Throws()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var buffer = new int[32 * 32];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tc.GetCodeBlockCoefficients(0, -1, 0, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tc.GetCodeBlockCoefficients(0, 0, -1, buffer));
        }

        [Test]
        public void GetCodeBlock_DestinationTooSmall_Throws()
        {
            using var tc = new TileComponent(0, 0, 64, 64, 1, 32, 32);

            var buffer = new int[10]; // too small
            Assert.Throws<ArgumentException>(() =>
                tc.GetCodeBlockCoefficients(0, 0, 0, buffer));
        }

        #endregion
    }
}
