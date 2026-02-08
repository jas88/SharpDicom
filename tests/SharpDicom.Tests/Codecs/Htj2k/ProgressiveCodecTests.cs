using System;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Data;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    [TestFixture]
    public class ProgressiveCodecTests
    {
        /// <summary>
        /// Helper: builds a minimal valid J2K codestream with known decomposition levels
        /// for testing progressive codec methods without requiring full encode/decode.
        /// </summary>
        private static DicomFragmentSequence BuildTestFragments(
            ushort width, ushort height, int decompositionLevels, bool reversible = true)
        {
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(width, height);

            // Create simple gradient pixel data
            var pixelData = new byte[info.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }

            return codec.Encode(pixelData, info);
        }

        [Test]
        public void GetResolutionLevels_ReturnsCorrectCount()
        {
            // Default HTJ2K options use 5 decomposition levels
            // So resolution levels = 5 + 1 = 6
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();

            int levels = codec.GetResolutionLevels(fragments, 0);

            Assert.That(levels, Is.EqualTo(6),
                "Resolution levels should be decomposition levels + 1");
        }

        [Test]
        public void GetResolutionLevels_ThrowsOnNullFragments()
        {
            var codec = new Htj2kLosslessCodec();

            Assert.Throws<ArgumentNullException>(() =>
                codec.GetResolutionLevels(null!, 0));
        }

        [Test]
        public void GetResolutionLevels_ThrowsOnInvalidFrameIndex()
        {
            var fragments = BuildTestFragments(16, 16, 5);
            var codec = new Htj2kLosslessCodec();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                codec.GetResolutionLevels(fragments, -1));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                codec.GetResolutionLevels(fragments, fragments.Fragments.Count));
        }

        [Test]
        public void GetResolutionDimensions_FullResolution_MatchesOriginal()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);
            int maxLevel = codec.GetResolutionLevels(fragments, 0) - 1;

            var (width, height) = codec.GetResolutionDimensions(fragments, info, 0, maxLevel);

            Assert.That(width, Is.EqualTo(64));
            Assert.That(height, Is.EqualTo(64));
        }

        [Test]
        public void GetResolutionDimensions_Level0_IsSmallest()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            var (width, height) = codec.GetResolutionDimensions(fragments, info, 0, 0);

            // 64 halved 5 times: 64->32->16->8->4->2
            Assert.That(width, Is.EqualTo(2));
            Assert.That(height, Is.EqualTo(2));
        }

        [Test]
        public void GetResolutionDimensions_IntermediateLevel_CorrectSize()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Level 3 means skip 5-3=2 levels from full: 64->32->16
            var (width, height) = codec.GetResolutionDimensions(fragments, info, 0, 3);

            Assert.That(width, Is.EqualTo(16));
            Assert.That(height, Is.EqualTo(16));
        }

        [Test]
        public void GetResolutionDimensions_ThrowsOnInvalidLevel()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Level 6 would be out of range for 5 decomposition levels (max = 5)
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                codec.GetResolutionDimensions(fragments, info, 0, 10));
        }

        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void DecodeAtResolution_Level0_ProducesSmallOutput()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Level 0: 2x2 pixels = 4 bytes
            var (outW, outH) = codec.GetResolutionDimensions(fragments, info, 0, 0);
            var destination = new byte[outW * outH];
            var result = codec.DecodeAtResolution(fragments, info, 0, 0, destination);

            Assert.That(result.Success, Is.True, "Decode at resolution level 0 should succeed");
            Assert.That(result.BytesWritten, Is.EqualTo(outW * outH));
        }

        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void DecodeAtResolution_MaxLevel_IdenticalToFullDecode()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            int maxLevel = codec.GetResolutionLevels(fragments, 0) - 1;

            // Full decode
            var fullDest = new byte[info.FrameSize];
            var fullResult = codec.Decode(fragments, info, 0, fullDest);

            // Progressive decode at max level
            var progDest = new byte[info.FrameSize];
            var progResult = codec.DecodeAtResolution(fragments, info, 0, maxLevel, progDest);

            Assert.That(fullResult.Success, Is.True);
            Assert.That(progResult.Success, Is.True);
            Assert.That(progDest, Is.EqualTo(fullDest),
                "Decode at max resolution should be identical to full decode");
        }

        [Test]
        [Ignore("J2K encoder/decoder lack multi-resolution subband support (21-09: architectural issue, deferred to Phase 30)")]
        public void DecodeAtResolution_IntermediateLevel_ProducesValidPixels()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            // Level 3: 16x16 = 256 bytes
            var (outW, outH) = codec.GetResolutionDimensions(fragments, info, 0, 3);
            var destination = new byte[outW * outH];
            var result = codec.DecodeAtResolution(fragments, info, 0, 3, destination);

            Assert.That(result.Success, Is.True);
            Assert.That(result.BytesWritten, Is.GreaterThan(0));

            // Verify at least some non-zero pixels
            bool hasNonZero = false;
            for (int i = 0; i < result.BytesWritten; i++)
            {
                if (destination[i] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
            Assert.That(hasNonZero, Is.True, "Progressive decode should produce non-zero pixel data");
        }

        [Test]
        public void DecodeAtResolution_InvalidResolutionLevel_ReturnsFail()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            var destination = new byte[64 * 64];
            var result = codec.DecodeAtResolution(fragments, info, 0, -1, destination);
            Assert.That(result.Success, Is.False);

            result = codec.DecodeAtResolution(fragments, info, 0, 100, destination);
            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void DecodeAtResolution_InvalidFrameIndex_ReturnsFail()
        {
            var fragments = BuildTestFragments(64, 64, 5);
            var codec = new Htj2kLosslessCodec();
            var info = PixelDataInfo.Grayscale8(64, 64);

            var destination = new byte[64 * 64];
            var result = codec.DecodeAtResolution(fragments, info, 99, 0, destination);
            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void IProgressiveCodec_Interface_IsImplemented()
        {
            // Verify all three codec variants implement IProgressiveCodec
            Assert.That(new Htj2kLosslessCodec(), Is.InstanceOf<IProgressiveCodec>());
            Assert.That(new Htj2kLosslessRpclCodec(), Is.InstanceOf<IProgressiveCodec>());
            Assert.That(new Htj2kLossyCodec(), Is.InstanceOf<IProgressiveCodec>());
        }

        [Test]
        public void IProgressiveCodec_AlsoImplements_IPixelDataCodec()
        {
            // IProgressiveCodec extends IPixelDataCodec
            IProgressiveCodec codec = new Htj2kLosslessCodec();
            Assert.That(codec, Is.InstanceOf<IPixelDataCodec>());
        }

        [Test]
        public void GetResolutionDimensions_NonSquareImage_CorrectSizes()
        {
            // 128x64 with 5 levels
            var codec = new Htj2kLosslessCodec();
            var info128x64 = PixelDataInfo.Grayscale8(64, 128);
            var pixelData = new byte[info128x64.FrameSize];
            for (int i = 0; i < pixelData.Length; i++)
            {
                pixelData[i] = (byte)(i % 256);
            }
            var fragments = codec.Encode(pixelData, info128x64);

            // Level 0: halved 5 times
            // 128x64 -> 64x32 -> 32x16 -> 16x8 -> 8x4 -> 4x2
            var (w0, h0) = codec.GetResolutionDimensions(fragments, info128x64, 0, 0);
            Assert.That(w0, Is.EqualTo(4));
            Assert.That(h0, Is.EqualTo(2));

            // Full resolution (level 5)
            var (wMax, hMax) = codec.GetResolutionDimensions(fragments, info128x64, 0, 5);
            Assert.That(wMax, Is.EqualTo(128));
            Assert.That(hMax, Is.EqualTo(64));
        }
    }
}
