using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000
{
    /// <summary>
    /// Tests for the multi-tile J2K pipeline: encoding with tile partitioning,
    /// parallel tile decode, all 5 progression orders, and PLT marker emission.
    /// </summary>
    [TestFixture]
    public class J2kMultiTilePipelineTests
    {
        #region Single Tile Roundtrip (backward compatibility)

        /// <summary>
        /// Single tile roundtrip: 256x256 grayscale, verify codestream structure.
        /// Default options produce a single tile covering the full image.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void SingleTile_DefaultOptions_ProducesValidCodestream()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0), "Should produce output");
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True, "Should be valid J2K");

            // Verify SOC and EOC markers
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span), Is.EqualTo((ushort)0xFF4F));
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(encoded.Span.Slice(encoded.Length - 2)), Is.EqualTo((ushort)0xFFD9));

            // Verify SIZ marker has tile size = image size (single tile)
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.TileWidth, Is.EqualTo(size));
            Assert.That(header.TileHeight, Is.EqualTo(size));
        }

        #endregion

        #region Multi-tile Encoding and Decoding

        /// <summary>
        /// Multi-tile 2x2: 64x64 image with 32x32 tiles (4 full tiles).
        /// Verify SIZ marker reports correct tile size.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void MultiTile_2x2_ProducesMultipleSotMarkers()
        {
            const int imageSize = 64;
            const int tileSize = 32;
            var info = PixelDataInfo.Grayscale8(imageSize, imageSize);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 2,
                CodeBlockWidth = 32,
                CodeBlockHeight = 32
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0));
            Assert.That(J2kDecoder.IsJpeg2000(encoded.Span), Is.True);

            // Parse and verify tile dimensions in SIZ marker
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.TileWidth, Is.EqualTo(tileSize), "SIZ should report tile width");
            Assert.That(header.TileHeight, Is.EqualTo(tileSize), "SIZ should report tile height");

            // Count SOT markers in the codestream (should be 4 for 2x2 tiles)
            int sotCount = CountMarkers(encoded.Span, J2kMarkers.SOT);
            Assert.That(sotCount, Is.EqualTo(4), "Should have 4 SOT markers for 2x2 tiles");

            // Decode should succeed
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
        }

        /// <summary>
        /// Multi-tile edge: non-divisible tile size producing partial edge tiles.
        /// Uses a small image (32x32) with 20x20 tiles: 2x2 tiles, right/bottom partial.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void MultiTile_EdgeTiles_HandlesPartialTiles()
        {
            const int imageSize = 32;
            const int tileSize = 20;
            var info = PixelDataInfo.Grayscale8(imageSize, imageSize);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 1,
                CodeBlockWidth = 16,
                CodeBlockHeight = 16
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);

            Assert.That(encoded.Length, Is.GreaterThan(0));

            // Should have 4 SOT markers (ceil(32/20)=2 rows, ceil(32/20)=2 cols)
            int sotCount = CountMarkers(encoded.Span, J2kMarkers.SOT);
            Assert.That(sotCount, Is.EqualTo(4), "Should have 4 tiles for 32x32 image with 20x20 tiles");

            // Decode should succeed
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
        }

        /// <summary>
        /// Multi-tile large: 64x64 image with 16x16 tiles (16 tiles).
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void MultiTile_4x4_SixteenTiles()
        {
            const int imageSize = 64;
            const int tileSize = 16;
            var info = PixelDataInfo.Grayscale8(imageSize, imageSize);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 1,
                CodeBlockWidth = 16,
                CodeBlockHeight = 16
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            int sotCount = CountMarkers(encoded.Span, J2kMarkers.SOT);
            Assert.That(sotCount, Is.EqualTo(16), "Should have 16 SOT markers for 4x4 tiles");

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True, $"Decode failed: {result.Diagnostic?.Message}");
        }

        /// <summary>
        /// Multi-component RGB: 32x32x3 with 16x16 tiles.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void MultiTile_RGB_EncodesAndDecodes()
        {
            const int size = 32;
            const int tileSize = 16;
            var info = PixelDataInfo.Rgb8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < size * size; i++)
            {
                pixelData[i * 3 + 0] = (byte)(i % 256);
                pixelData[i * 3 + 1] = (byte)((i * 2) % 256);
                pixelData[i * 3 + 2] = (byte)((i * 3) % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 2,
                CodeBlockWidth = 16,
                CodeBlockHeight = 16
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);
            Assert.That(encoded.Length, Is.GreaterThan(0));

            // Should have 4 tiles (2x2)
            int sotCount = CountMarkers(encoded.Span, J2kMarkers.SOT);
            Assert.That(sotCount, Is.EqualTo(4));

            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True, $"RGB decode failed: {result.Diagnostic?.Message}");

            // Verify all components have non-zero data
            bool[] hasData = new bool[3];
            for (int i = 0; i < size * size; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (decoded[i * 3 + c] != 0)
                    {
                        hasData[c] = true;
                    }
                }
            }

            for (int c = 0; c < 3; c++)
            {
                Assert.That(hasData[c], Is.True, $"Component {c} should have non-zero data");
            }
        }

        #endregion

        #region Progression Orders

        /// <summary>
        /// All 5 progression orders produce decodable output.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        [TestCase(ProgressionOrder.LRCP)]
        [TestCase(ProgressionOrder.RLCP)]
        [TestCase(ProgressionOrder.RPCL)]
        [TestCase(ProgressionOrder.PCRL)]
        [TestCase(ProgressionOrder.CPRL)]
        public void ProgressionOrder_ProducesDecodableOutput(ProgressionOrder order)
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                Progression = order,
                DecompositionLevels = 2,
                CodeBlockWidth = 32,
                CodeBlockHeight = 32
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);
            Assert.That(encoded.Length, Is.GreaterThan(0), $"Order {order} should produce output");

            // Verify the COD marker records the correct progression order
            bool parsed = J2kCodestream.TryParse(encoded.Span, out var header, out _);
            Assert.That(parsed, Is.True);
            Assert.That(header!.Progression, Is.EqualTo(order), "COD marker should record correct progression order");

            // Decode should succeed
            var decoded = new byte[info.FrameSize];
            var result = J2kDecoder.DecodeFrame(encoded.Span, info, decoded, 0);
            Assert.That(result.Success, Is.True, $"Decode with {order} failed: {result.Diagnostic?.Message}");
        }

        #endregion

        #region PLT Markers

        /// <summary>
        /// PLT markers are present in encoded output.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void PltMarkers_PresentInOutput()
        {
            const int size = 32;
            var info = PixelDataInfo.Grayscale8(size, size);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, lossless: true);

            // Count PLT markers (0xFF58)
            int pltCount = CountMarkers(encoded.Span, J2kMarkers.PLT);
            Assert.That(pltCount, Is.GreaterThan(0), "Should have at least one PLT marker");
        }

        /// <summary>
        /// Multi-tile output has one PLT marker per tile.
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void PltMarkers_OnePerTile()
        {
            const int imageSize = 64;
            const int tileSize = 32;
            var info = PixelDataInfo.Grayscale8(imageSize, imageSize);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 2,
                CodeBlockWidth = 32,
                CodeBlockHeight = 32
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);

            int sotCount = CountMarkers(encoded.Span, J2kMarkers.SOT);
            int pltCount = CountMarkers(encoded.Span, J2kMarkers.PLT);

            Assert.That(pltCount, Is.EqualTo(sotCount), "Should have one PLT marker per tile");
        }

        #endregion

        #region Parallel Decode

        /// <summary>
        /// Parallel decode (parallelism=4) produces identical output to sequential (parallelism=1).
        /// </summary>
        [Test]
        [Category("Pipeline")]
        public void ParallelDecode_ProducesIdenticalOutput()
        {
            const int imageSize = 64;
            const int tileSize = 32;
            var info = PixelDataInfo.Grayscale8(imageSize, imageSize);
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            var options = new J2kEncoderOptions
            {
                TileWidth = tileSize,
                TileHeight = tileSize,
                DecompositionLevels = 2,
                CodeBlockWidth = 32,
                CodeBlockHeight = 32
            };

            var encoded = J2kEncoder.EncodeFrame(pixelData, info, options, lossless: true);

            // Decode sequentially
            var decoded1 = new byte[info.FrameSize];
            var result1 = J2kDecoder.DecodeFrame(encoded.Span, info, decoded1, 0, null, maxDegreeOfParallelism: 1);
            Assert.That(result1.Success, Is.True, "Sequential decode should succeed");

            // Decode in parallel
            var decoded4 = new byte[info.FrameSize];
            var result4 = J2kDecoder.DecodeFrame(encoded.Span, info, decoded4, 0, null, maxDegreeOfParallelism: 4);
            Assert.That(result4.Success, Is.True, "Parallel decode should succeed");

            // Output must be byte-identical
            Assert.That(decoded4, Is.EqualTo(decoded1),
                "Parallel decode output must be identical to sequential decode output");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Counts occurrences of a specific marker in the codestream.
        /// </summary>
        private static int CountMarkers(ReadOnlySpan<byte> data, ushort marker)
        {
            int count = 0;
            byte high = (byte)(marker >> 8);
            byte low = (byte)(marker & 0xFF);

            for (int i = 0; i < data.Length - 1; i++)
            {
                if (data[i] == high && data[i + 1] == low)
                {
                    count++;
                }
            }

            return count;
        }

        #endregion
    }
}
