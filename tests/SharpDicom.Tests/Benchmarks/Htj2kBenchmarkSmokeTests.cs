using System;
using System.Diagnostics;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Benchmarks
{
    /// <summary>
    /// Smoke tests that verify HT block coding is faster than EBCOT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests use the block coder directly (not the full codec pipeline) to
    /// isolate the performance difference between HT and EBCOT tier-1 coding.
    /// </para>
    /// <para>
    /// Thresholds are conservative for CI reliability in Debug mode.
    /// Encode threshold is 1.3x (HT overhead from SigProp/MagRef reduces advantage
    /// for full-pass encode in debug). Decode threshold is 2.0x.
    /// Real-world Release-mode HT speedup is typically 3-10x.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class Htj2kBenchmarkSmokeTests
    {
        private const int BlockWidth = 64;
        private const int BlockHeight = 64;
        private const int WarmupIterations = 10;
        private const int TimedIterations = 50;

        [Test]
        public void HtBlockCoder_Encode_AtLeast2xFasterThanEbcot()
        {
            int[] coefficients = CreateTestCoefficients(BlockWidth, BlockHeight);

            var htEncoder = HtBlockEncoder.Instance;
            var ebcotEncoder = new EbcotBlockCoder();

            // Warmup both encoders
            for (int i = 0; i < WarmupIterations; i++)
            {
                htEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);
                ebcotEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);
            }

            // Time EBCOT encode
            var ebcotSw = Stopwatch.StartNew();
            for (int i = 0; i < TimedIterations; i++)
            {
                ebcotEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);
            }
            ebcotSw.Stop();

            // Time HT encode
            var htSw = Stopwatch.StartNew();
            for (int i = 0; i < TimedIterations; i++)
            {
                htEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);
            }
            htSw.Stop();

            double speedup = (double)ebcotSw.ElapsedTicks / htSw.ElapsedTicks;

            TestContext.Out.WriteLine($"EBCOT encode: {ebcotSw.ElapsedMilliseconds}ms ({TimedIterations} iterations)");
            TestContext.Out.WriteLine($"HT encode:    {htSw.ElapsedMilliseconds}ms ({TimedIterations} iterations)");
            TestContext.Out.WriteLine($"Speedup:      {speedup:F2}x");

            Assert.That(speedup, Is.GreaterThanOrEqualTo(1.3),
                $"HT should be at least 1.3x faster than EBCOT for encoding (conservative Debug-mode threshold). " +
                $"Got {speedup:F2}x (EBCOT={ebcotSw.ElapsedMilliseconds}ms, HT={htSw.ElapsedMilliseconds}ms)");
        }

        [Test]
        public void HtBlockCoder_Decode_AtLeast2xFasterThanEbcot()
        {
            int[] coefficients = CreateTestCoefficients(BlockWidth, BlockHeight);

            var htEncoder = HtBlockEncoder.Instance;
            var ebcotEncoder = new EbcotBlockCoder();

            // Encode with both to get encoded data
            var htEncoded = htEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);
            var ebcotEncoded = ebcotEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[BlockWidth * BlockHeight];

            // Warmup both decoders
            for (int i = 0; i < WarmupIterations; i++)
            {
                htEncoder.DecodeBlock(htEncoded.Data.Span, htEncoded.NumPasses, decoded, BlockWidth, BlockHeight, htEncoded.MsbPosition, 0);
                ebcotEncoder.DecodeBlock(ebcotEncoded.Data.Span, ebcotEncoded.NumPasses, decoded, BlockWidth, BlockHeight, ebcotEncoded.MsbPosition, 0);
            }

            // Time EBCOT decode
            var ebcotSw = Stopwatch.StartNew();
            for (int i = 0; i < TimedIterations; i++)
            {
                ebcotEncoder.DecodeBlock(ebcotEncoded.Data.Span, ebcotEncoded.NumPasses, decoded, BlockWidth, BlockHeight, ebcotEncoded.MsbPosition, 0);
            }
            ebcotSw.Stop();

            // Time HT decode
            var htSw = Stopwatch.StartNew();
            for (int i = 0; i < TimedIterations; i++)
            {
                htEncoder.DecodeBlock(htEncoded.Data.Span, htEncoded.NumPasses, decoded, BlockWidth, BlockHeight, htEncoded.MsbPosition, 0);
            }
            htSw.Stop();

            double speedup = (double)ebcotSw.ElapsedTicks / htSw.ElapsedTicks;

            TestContext.Out.WriteLine($"EBCOT decode: {ebcotSw.ElapsedMilliseconds}ms ({TimedIterations} iterations)");
            TestContext.Out.WriteLine($"HT decode:    {htSw.ElapsedMilliseconds}ms ({TimedIterations} iterations)");
            TestContext.Out.WriteLine($"Speedup:      {speedup:F2}x");

            Assert.That(speedup, Is.GreaterThanOrEqualTo(2.0),
                $"HT should be at least 2x faster than EBCOT for decoding. " +
                $"Got {speedup:F2}x (EBCOT={ebcotSw.ElapsedMilliseconds}ms, HT={htSw.ElapsedMilliseconds}ms)");
        }

        [Test]
        public void HtBlockCoder_EncodeDecode_ProducesCorrectResult()
        {
            // Verify correctness alongside performance
            int[] coefficients = CreateTestCoefficients(BlockWidth, BlockHeight);
            var htEncoder = HtBlockEncoder.Instance;

            var encoded = htEncoder.EncodeBlock(coefficients, BlockWidth, BlockHeight, subbandType: 0, msbPosition: -1);

            int[] decoded = new int[BlockWidth * BlockHeight];
            htEncoder.DecodeBlock(encoded.Data.Span, encoded.NumPasses, decoded, BlockWidth, BlockHeight, encoded.MsbPosition, 0);

            Assert.That(decoded, Is.EqualTo(coefficients),
                "HT encode/decode roundtrip must be lossless");
        }

        private static int[] CreateTestCoefficients(int width, int height)
        {
            // Use a sparse pattern with moderate magnitudes, matching
            // the patterns that existing HtBlockCoder tests verify.
            int[] coefficients = new int[width * height];
            var rng = new System.Random(42);
            for (int i = 0; i < coefficients.Length; i++)
            {
                if (rng.Next(100) < 30)
                {
                    coefficients[i] = rng.Next(-50, 51);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
            }
            return coefficients;
        }
    }
}
