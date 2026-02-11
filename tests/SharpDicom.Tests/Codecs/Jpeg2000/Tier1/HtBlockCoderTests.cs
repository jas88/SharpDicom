using System;
using NUnit.Framework;
using FsCheck;
using FsCheck.Fluent;
using SharpDicom.Codecs.Jpeg2000.Tier1;

using Random = System.Random;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for <see cref="HtBlockEncoder"/> and <see cref="HtBlockDecoder"/>,
    /// verifying roundtrip correctness across pass counts, block sizes,
    /// subband types, and edge cases.
    /// </summary>
    [TestFixture]
    public class HtBlockCoderTests
    {
        #region Cleanup-Only Roundtrip (1 Pass)

        [Test]
        public void CleanupOnly_SingleValue_Roundtrip()
        {
            int width = 4, height = 4;
            int[] input = new int[width * height];
            input[0] = 1;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1),
                "Single +-1 value should produce cleanup-only (1 pass)");

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void CleanupOnly_NegativeOne_Roundtrip()
        {
            int width = 4, height = 4;
            int[] input = new int[width * height];
            input[5] = -1;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void CleanupOnly_AllOnes_Roundtrip()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = (i % 2 == 0) ? 1 : -1;
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        #endregion

        #region Lossless Roundtrip (1 Cleanup Pass)

        [Test]
        public void SmallValues_Roundtrip()
        {
            int width = 4, height = 4;
            int[] input = new int[width * height];
            input[0] = 3;
            input[1] = -2;
            input[5] = 2;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1),
                "Lossless HTJ2K always produces 1 cleanup pass");

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void SmallValues_8x8_Roundtrip()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            var rng = new Random(42);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-3, 4);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void LargerValues_Roundtrip()
        {
            int width = 4, height = 4;
            int[] input = new int[width * height];
            input[0] = 5;
            input[1] = -7;
            input[4] = 4;
            input[5] = 6;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1),
                "Lossless HTJ2K always produces 1 cleanup pass");

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void LargerValues_16x16_Roundtrip()
        {
            int width = 16, height = 16;
            int[] input = new int[width * height];
            var rng = new Random(99);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-100, 101);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        #endregion

        #region IBlockCoder Interface Conformance

        [Test]
        public void Instance_ReturnsNonNullSingleton()
        {
            var instance = HtBlockEncoder.Instance;

            Assert.That(instance, Is.Not.Null, "Instance should not be null");
            Assert.That(instance, Is.SameAs(HtBlockEncoder.Instance),
                "Instance should return the same object");
        }

        [Test]
        public void Instance_ImplementsIBlockCoder()
        {
            IBlockCoder blockCoder = HtBlockEncoder.Instance;
            Assert.That(blockCoder, Is.Not.Null);
            Assert.That(blockCoder, Is.InstanceOf<IBlockCoder>());
        }

        [Test]
        public void Decoder_Instance_ReturnsNonNullSingleton()
        {
            var instance = HtBlockDecoder.Instance;

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance, Is.SameAs(HtBlockDecoder.Instance));
        }

        [Test]
        public void Decoder_ProducesSameResult_AsEncoder()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            input[0] = 5;
            input[1] = -3;
            input[9] = 7;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decodedViaEncoder = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decodedViaEncoder, width, height,
                encoded.MsbPosition, subbandType: 0);

            int[] decodedViaDecoder = new int[width * height];
            HtBlockDecoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decodedViaDecoder, width, height,
                encoded.MsbPosition, subbandType: 0);

            Assert.That(decodedViaDecoder, Is.EqualTo(decodedViaEncoder),
                "Decoder should produce identical results to encoder's decode");
        }

        #endregion

        #region Various Code-Block Sizes

        [Test]
        [TestCase(4, 4)]
        [TestCase(16, 16)]
        [TestCase(32, 32)]
        [TestCase(64, 64)]
        public void VariousBlockSizes_Roundtrip(int width, int height)
        {
            int[] input = new int[width * height];
            var rng = new Random(42 + width * height);
            for (int i = 0; i < input.Length; i++)
            {
                if (rng.Next(100) < 30)
                {
                    input[i] = rng.Next(-50, 51);
                    if (input[i] == 0) input[i] = 1;
                }
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input),
                $"Roundtrip failed for {width}x{height}");
        }

        #endregion

        #region Various Subband Types

        [Test]
        [TestCase(0)]  // LL
        [TestCase(1)]  // LH
        [TestCase(2)]  // HL
        [TestCase(3)]  // HH
        public void SubbandTypes_Roundtrip(int subbandType)
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            var rng = new Random(42 + subbandType);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-30, 31);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType);

            Assert.That(decoded, Is.EqualTo(input),
                $"Roundtrip failed for subband type {subbandType}");
        }

        #endregion

        #region Edge Cases

        [Test]
        public void AllZero_ReturnsEmpty()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(0),
                "All-zero block should have no passes");
            Assert.That(encoded.Data.IsEmpty, Is.True,
                "All-zero block should have empty data");

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        [Test]
        public void SingleNonZero_Roundtrip()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            input[35] = 42;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded[35], Is.EqualTo(42));
            // All other positions should be zero
            for (int i = 0; i < decoded.Length; i++)
            {
                if (i != 35)
                {
                    Assert.That(decoded[i], Is.EqualTo(0),
                        $"Position {i} should be zero");
                }
            }
        }

        [Test]
        public void MaximumMagnitude_Roundtrip()
        {
            int width = 4, height = 4;
            int[] input = new int[width * height];
            input[0] = 32767;
            input[1] = -32768;

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input));
        }

        #endregion

        #region Pass Count Validation

        [Test]
        public void PassCount_MsbZero_ProducesOnePas()
        {
            // MSB = 0 means max magnitude is 1
            int[] input = { 1, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, 4, 4, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));
            Assert.That(encoded.MsbPosition, Is.EqualTo(0));
        }

        [Test]
        public void PassCount_MsbOne_ProducesOnePass()
        {
            // MSB = 1 means max magnitude is 2 or 3
            // Lossless HTJ2K always produces 1 cleanup pass
            int[] input = { 2, 0, 0, 0, -3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, 4, 4, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));
            Assert.That(encoded.MsbPosition, Is.EqualTo(1));
        }

        [Test]
        public void PassCount_MsbTwo_ProducesOnePass()
        {
            // MSB = 2 means max magnitude is 4-7
            // Lossless HTJ2K always produces 1 cleanup pass
            int[] input = { 4, 0, 0, 0, -7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, 4, 4, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.NumPasses, Is.EqualTo(1));
            Assert.That(encoded.MsbPosition, Is.EqualTo(2));
        }

        #endregion

        #region PassLengths Consistency

        [Test]
        public void PassLengths_AreMonotonicallyIncreasing()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            var rng = new Random(77);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-100, 101);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded.PassLengths.Length, Is.EqualTo(encoded.NumPasses));

            for (int i = 1; i < encoded.PassLengths.Length; i++)
            {
                Assert.That(encoded.PassLengths[i],
                    Is.GreaterThanOrEqualTo(encoded.PassLengths[i - 1]),
                    $"PassLengths[{i}] must be >= PassLengths[{i - 1}]");
            }

            // Last pass length should equal total data length
            Assert.That(encoded.PassLengths[encoded.PassLengths.Length - 1],
                Is.EqualTo(encoded.Data.Length),
                "Last pass length should equal total data length");
        }

        #endregion

        #region Random Data Property Tests

        [Test]
        [TestCase(100)]
        [TestCase(200)]
        [TestCase(300)]
        [TestCase(400)]
        [TestCase(500)]
        public void RandomData_Roundtrip(int seed)
        {
            var rng = new Random(seed);
            int width = rng.Next(2, 33);
            int height = rng.Next(2, 33);
            int[] input = new int[width * height];

            for (int i = 0; i < input.Length; i++)
            {
                int r = rng.Next(100);
                if (r < 40)
                {
                    input[i] = 0;
                }
                else if (r < 70)
                {
                    input[i] = rng.Next(-10, 11);
                    if (input[i] == 0) input[i] = 1;
                }
                else
                {
                    input[i] = rng.Next(-1000, 1001);
                    if (input[i] == 0) input[i] = 1;
                }
            }

            int subbandType = rng.Next(4);

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType);

            Assert.That(decoded, Is.EqualTo(input),
                $"Roundtrip failed for seed={seed}, {width}x{height}, subband={subbandType}");
        }

        [Test]
        public void FsCheckProperty_RandomCoefficients_RoundtripLosslessly()
        {
            // Use FsCheck to generate random coefficient arrays
            Prop.ForAll(
                Arb.From(
                    Gen.Choose(2, 16).SelectMany(w =>
                    Gen.Choose(2, 16).SelectMany(h =>
                    Gen.Choose(-500, 500).ArrayOf(w * h).Select(arr =>
                        (Width: w, Height: h, Coefficients: arr)
                    )))),
                tuple =>
                {
                    int width = tuple.Width;
                    int height = tuple.Height;
                    int[] input = tuple.Coefficients;

                    var encoded = HtBlockEncoder.Instance.EncodeBlock(
                        input, width, height, subbandType: 0, msbPosition: -1);

                    int[] decoded = new int[width * height];
                    HtBlockEncoder.Instance.DecodeBlock(
                        encoded.Data.Span, encoded.NumPasses,
                        decoded, width, height, encoded.MsbPosition, subbandType: 0);

                    for (int i = 0; i < width * height; i++)
                    {
                        if (decoded[i] != input[i])
                        {
                            return false.ToProperty();
                        }
                    }

                    return true.ToProperty();
                })
                .QuickCheckThrowOnFailure();
        }

        #endregion

        #region Determinism Tests

        [Test]
        public void ConsecutiveEncodes_ProduceSameResult()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            var rng = new Random(55);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-50, 51);
            }

            var encoded1 = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);
            var encoded2 = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            Assert.That(encoded1.NumPasses, Is.EqualTo(encoded2.NumPasses));
            Assert.That(encoded1.Data.ToArray(), Is.EqualTo(encoded2.Data.ToArray()));
            Assert.That(encoded1.PassLengths, Is.EqualTo(encoded2.PassLengths));
        }

        [Test]
        public void MultipleDecodes_ProduceSameResult()
        {
            int width = 8, height = 8;
            int[] input = new int[width * height];
            var rng = new Random(66);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-50, 51);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded1 = new int[width * height];
            int[] decoded2 = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded1, width, height, encoded.MsbPosition, subbandType: 0);
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded2, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded1, Is.EqualTo(decoded2));
        }

        #endregion

        #region Odd Dimensions

        [Test]
        [TestCase(5, 5)]
        [TestCase(7, 3)]
        [TestCase(3, 7)]
        [TestCase(9, 5)]
        public void OddDimensions_Roundtrip(int width, int height)
        {
            int[] input = new int[width * height];
            var rng = new Random(width * 100 + height);
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = rng.Next(-20, 21);
            }

            var encoded = HtBlockEncoder.Instance.EncodeBlock(
                input, width, height, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[width * height];
            HtBlockEncoder.Instance.DecodeBlock(
                encoded.Data.Span, encoded.NumPasses,
                decoded, width, height, encoded.MsbPosition, subbandType: 0);

            Assert.That(decoded, Is.EqualTo(input),
                $"Roundtrip failed for {width}x{height}");
        }

        #endregion
    }
}
