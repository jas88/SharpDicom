using System;
using System.Buffers.Binary;
using FsCheck;
using FsCheck.Fluent;
using NUnit.Framework;
using SharpDicom.Codecs;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Codecs.Jpeg2000;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using PixelDataInfo = SharpDicom.Codecs.PixelDataInfo;

namespace SharpDicom.Tests.Codecs.Htj2k
{
    /// <summary>
    /// Property-based tests for HTJ2K codec using FsCheck.
    /// Verifies codec invariants across arbitrary image dimensions and bit depths.
    /// </summary>
    [TestFixture]
    public class Htj2kPropertyTests
    {
        private const int TestCount = 50;

        /// <summary>
        /// Property 1: Lossless encode/decode roundtrip produces identical pixel data
        /// for arbitrary image dimensions and bit depths.
        /// </summary>
        [Test]
        public void Property_LosslessRoundtrip_PreservesPixelData()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenImageParams()),
                param =>
                {
                    var codec = new Htj2kLosslessCodec();
                    PixelDataInfo info;
                    byte[] original;

                    if (param.BitsStored == 8)
                    {
                        info = PixelDataInfo.Grayscale8((ushort)param.Height, (ushort)param.Width);
                        original = CreateGradient8(param.Width, param.Height);
                    }
                    else
                    {
                        info = new PixelDataInfo(
                            Rows: (ushort)param.Height,
                            Columns: (ushort)param.Width,
                            BitsAllocated: 16,
                            BitsStored: (ushort)param.BitsStored,
                            HighBit: (ushort)(param.BitsStored - 1),
                            SamplesPerPixel: 1,
                            PixelRepresentation: 0,
                            PlanarConfiguration: 0,
                            NumberOfFrames: 1);
                        original = CreateGradient16(param.Width, param.Height, param.BitsStored);
                    }

                    var fragments = codec.Encode(original, info);
                    var decoded = new byte[info.FrameSize];
                    var result = codec.Decode(fragments, info, 0, decoded);

                    if (!result.Success)
                    {
                        return false;
                    }

                    for (int i = 0; i < original.Length; i++)
                    {
                        if (original[i] != decoded[i])
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .Check(config);
        }

        /// <summary>
        /// Property 2: Encoding the same pixel data twice produces identical codestreams.
        /// </summary>
        [Test]
        public void Property_CodecSymmetry_SameInputProducesSameOutput()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenImageParams()),
                param =>
                {
                    var codec = new Htj2kLosslessCodec();
                    PixelDataInfo info;
                    byte[] original;

                    if (param.BitsStored == 8)
                    {
                        info = PixelDataInfo.Grayscale8((ushort)param.Height, (ushort)param.Width);
                        original = CreateGradient8(param.Width, param.Height);
                    }
                    else
                    {
                        info = new PixelDataInfo(
                            Rows: (ushort)param.Height,
                            Columns: (ushort)param.Width,
                            BitsAllocated: 16,
                            BitsStored: (ushort)param.BitsStored,
                            HighBit: (ushort)(param.BitsStored - 1),
                            SamplesPerPixel: 1,
                            PixelRepresentation: 0,
                            PlanarConfiguration: 0,
                            NumberOfFrames: 1);
                        original = CreateGradient16(param.Width, param.Height, param.BitsStored);
                    }

                    var fragments1 = codec.Encode(original, info);
                    var fragments2 = codec.Encode(original, info);

                    var data1 = fragments1.Fragments[0].Span;
                    var data2 = fragments2.Fragments[0].Span;

                    if (data1.Length != data2.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < data1.Length; i++)
                    {
                        if (data1[i] != data2[i])
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .Check(config);
        }

        /// <summary>
        /// Property 3: Cleanup-only encoding (1 pass) produces a valid decodable codestream.
        /// </summary>
        [Test]
        public void Property_CleanupOnlySubset_ProducesValidCodestream()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenSmallImageParams()),
                param =>
                {
                    // Use the HT block encoder directly with cleanup-only
                    int width = param.Width;
                    int height = param.Height;
                    int[] coefficients = new int[width * height];
                    var rng = new System.Random(param.Seed);
                    for (int i = 0; i < coefficients.Length; i++)
                    {
                        coefficients[i] = rng.Next(-100, 101);
                    }

                    var encoded = HtBlockEncoder.Instance.EncodeBlock(
                        coefficients, width, height, subbandType: 0, msbPosition: -1);

                    if (encoded.NumPasses == 0)
                    {
                        // All-zero block, valid
                        return true;
                    }

                    // Verify it can be decoded
                    int[] decoded = new int[width * height];
                    try
                    {
                        // Decode with only the first pass (cleanup)
                        HtBlockEncoder.Instance.DecodeBlock(
                            encoded.Data.Span,
                            Math.Min(encoded.NumPasses, 1),
                            decoded, width, height,
                            encoded.MsbPosition, subbandType: 0);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .Check(config);
        }

        /// <summary>
        /// Property 4: Pass count is always between 1 and 6 (never 0 for non-zero blocks, never > 6).
        /// </summary>
        [Test]
        public void Property_PassCountBounds_AlwaysValid()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenSmallImageParams()),
                param =>
                {
                    int width = param.Width;
                    int height = param.Height;
                    int[] coefficients = new int[width * height];
                    var rng = new System.Random(param.Seed);
                    bool allZero = true;
                    for (int i = 0; i < coefficients.Length; i++)
                    {
                        coefficients[i] = rng.Next(-100, 101);
                        if (coefficients[i] != 0) allZero = false;
                    }

                    var encoded = HtBlockEncoder.Instance.EncodeBlock(
                        coefficients, width, height, subbandType: 0, msbPosition: -1);

                    if (allZero)
                    {
                        // All-zero block can have 0 passes
                        return encoded.NumPasses == 0;
                    }

                    // Non-zero blocks: 1-6 passes
                    return encoded.NumPasses >= 1 && encoded.NumPasses <= 6;
                })
                .Check(config);
        }

        /// <summary>
        /// Property 5: All HT codestreams contain a valid CAP marker.
        /// </summary>
        [Test]
        public void Property_CapMarkerPresent_InAllHtCodestreams()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenImageParams()),
                param =>
                {
                    var codec = new Htj2kLosslessCodec();
                    PixelDataInfo info;
                    byte[] original;

                    if (param.BitsStored == 8)
                    {
                        info = PixelDataInfo.Grayscale8((ushort)param.Height, (ushort)param.Width);
                        original = CreateGradient8(param.Width, param.Height);
                    }
                    else
                    {
                        info = new PixelDataInfo(
                            Rows: (ushort)param.Height,
                            Columns: (ushort)param.Width,
                            BitsAllocated: 16,
                            BitsStored: (ushort)param.BitsStored,
                            HighBit: (ushort)(param.BitsStored - 1),
                            SamplesPerPixel: 1,
                            PixelRepresentation: 0,
                            PlanarConfiguration: 0,
                            NumberOfFrames: 1);
                        original = CreateGradient16(param.Width, param.Height, param.BitsStored);
                    }

                    var fragments = codec.Encode(original, info);
                    var data = fragments.Fragments[0].Span;

                    // Search for CAP marker (0xFF50)
                    for (int i = 0; i < data.Length - 1; i++)
                    {
                        if (data[i] == 0xFF && data[i + 1] == 0x50)
                        {
                            return true;
                        }
                    }

                    return false;
                })
                .Check(config);
        }

        /// <summary>
        /// Property 6: Higher quality presets produce larger or equal output compared to lower quality.
        /// Diagnostic >= Archive >= Review >= Fast (in terms of encoded size).
        /// </summary>
        [Test]
        public void Property_OutputSizeMonotonicity_HigherQualityNotSmaller()
        {
            var config = Config.Quick.WithMaxTest(TestCount);

            Prop.ForAll(
                Arb.From(GenImageParams()),
                param =>
                {
                    var codec = new Htj2kLossyCodec();
                    PixelDataInfo info;
                    byte[] original;

                    if (param.BitsStored == 8)
                    {
                        info = PixelDataInfo.Grayscale8((ushort)param.Height, (ushort)param.Width);
                        original = CreateGradient8(param.Width, param.Height);
                    }
                    else
                    {
                        info = new PixelDataInfo(
                            Rows: (ushort)param.Height,
                            Columns: (ushort)param.Width,
                            BitsAllocated: 16,
                            BitsStored: (ushort)param.BitsStored,
                            HighBit: (ushort)(param.BitsStored - 1),
                            SamplesPerPixel: 1,
                            PixelRepresentation: 0,
                            PlanarConfiguration: 0,
                            NumberOfFrames: 1);
                        original = CreateGradient16(param.Width, param.Height, param.BitsStored);
                    }

                    // Encode with each preset
                    var presets = new[]
                    {
                        HtEncoderOptions.Fast,
                        HtEncoderOptions.Review,
                        HtEncoderOptions.Archive,
                        HtEncoderOptions.Diagnostic
                    };

                    int previousSize = 0;
                    foreach (var preset in presets)
                    {
                        var opts = new Htj2kCodecOptions(false, 5, false, true, preset);
                        var fragments = codec.Encode(original, info, opts);
                        int currentSize = fragments.Fragments[0].Length;

                        // Each preset should produce output >= previous (lower quality) preset
                        if (currentSize < previousSize)
                        {
                            return false;
                        }
                        previousSize = currentSize;
                    }

                    return true;
                })
                .Check(config);
        }

        // ================================================================
        // Generators
        // ================================================================

        /// <summary>
        /// Generates arbitrary image parameters: width/height between 4-512
        /// (restricted to multiples of 4 for DWT alignment), bit depth 8/12/16.
        /// </summary>
        private static Gen<ImageParams> GenImageParams()
        {
            return
                from w in Gen.Choose(1, 32).Select(v => v * 4) // 4 to 128, multiples of 4
                from h in Gen.Choose(1, 32).Select(v => v * 4) // 4 to 128, multiples of 4
                from bits in Gen.Elements(8, 12, 16)
                select new ImageParams(w, h, bits, 0);
        }

        /// <summary>
        /// Generates small image params for block-level tests.
        /// </summary>
        private static Gen<ImageParams> GenSmallImageParams()
        {
            return
                from w in Gen.Choose(2, 16)
                from h in Gen.Choose(2, 16)
                from seed in Gen.Choose(0, 10000)
                select new ImageParams(w, h, 8, seed);
        }

        // ================================================================
        // Data structures
        // ================================================================

        private readonly record struct ImageParams(int Width, int Height, int BitsStored, int Seed);

        // ================================================================
        // Helpers
        // ================================================================

        private static byte[] CreateGradient8(int width, int height)
        {
            var data = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data[y * width + x] = (byte)((x + y) % 256);
                }
            }
            return data;
        }

        private static byte[] CreateGradient16(int width, int height, int bitsStored)
        {
            int maxVal = (1 << bitsStored) - 1;
            var data = new byte[width * height * 2];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ushort value = (ushort)(((x + y) * 17) % (maxVal + 1));
                    int offset = (y * width + x) * 2;
                    data[offset] = (byte)(value & 0xFF);
                    data[offset + 1] = (byte)(value >> 8);
                }
            }
            return data;
        }
    }
}
