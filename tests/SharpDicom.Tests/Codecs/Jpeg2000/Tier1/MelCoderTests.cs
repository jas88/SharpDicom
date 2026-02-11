using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for the MEL (Modular Embedded Lossless) run-length coder.
    /// </summary>
    [TestFixture]
    public class MelCoderTests
    {
        #region MEL State Table Tests

        [Test]
        public void MelE_Has13Entries()
        {
            Assert.That(MelCoder.MelE.Length, Is.EqualTo(13));
        }

        [Test]
        public void MelE_Values_MatchSpec()
        {
            // ITU-T T.814: MelE = {0,0,0,1,1,1,2,2,2,3,3,4,5}
            ReadOnlySpan<int> expected = new int[] { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5 };
            var actual = MelCoder.MelE;

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]),
                    $"MelE[{i}] should be {expected[i]}, got {actual[i]}");
            }
        }

        [Test]
        public void NumStates_Is13()
        {
            Assert.That(MelCoder.NumStates, Is.EqualTo(13));
        }

        [Test]
        public void MaxState_Is12()
        {
            Assert.That(MelCoder.MaxState, Is.EqualTo(12));
        }

        [Test]
        [TestCase(0, 1)]    // 2^0 = 1
        [TestCase(1, 1)]    // 2^0 = 1
        [TestCase(2, 1)]    // 2^0 = 1
        [TestCase(3, 2)]    // 2^1 = 2
        [TestCase(4, 2)]    // 2^1 = 2
        [TestCase(5, 2)]    // 2^1 = 2
        [TestCase(6, 4)]    // 2^2 = 4
        [TestCase(7, 4)]    // 2^2 = 4
        [TestCase(8, 4)]    // 2^2 = 4
        [TestCase(9, 8)]    // 2^3 = 8
        [TestCase(10, 8)]   // 2^3 = 8
        [TestCase(11, 16)]  // 2^4 = 16
        [TestCase(12, 32)]  // 2^5 = 32
        public void RunLength_Matches2PowerMelE(int state, int expectedRunLength)
        {
            int runLength = 1 << MelCoder.MelE[state];
            Assert.That(runLength, Is.EqualTo(expectedRunLength),
                $"State {state}: run length should be {expectedRunLength}");
        }

        #endregion

        #region MelEncoder Tests

        [Test]
        public void MelEncoder_InitialState_IsZero()
        {
            var encoder = new MelEncoder(64);
            try
            {
                Assert.That(encoder.State, Is.EqualTo(0));
                Assert.That(encoder.BytesWritten, Is.EqualTo(0));
            }
            finally
            {
                encoder.Dispose();
            }
        }

        [Test]
        public void MelEncoder_EncodeInsignificant_IncreasesState()
        {
            var encoder = new MelEncoder(64);
            try
            {
                // At state 0, run length = 1
                // Encoding one insignificant completes the run and emits a 1-bit
                encoder.EncodeQuadSignificance(false);
                Assert.That(encoder.State, Is.EqualTo(1), "State should increase after run completion");
            }
            finally
            {
                encoder.Dispose();
            }
        }

        [Test]
        public void MelEncoder_EncodeSignificant_DecreasesState()
        {
            var encoder = new MelEncoder(64);
            try
            {
                // First drive state up
                encoder.EncodeQuadSignificance(false); // state 0 -> 1

                // Now encode significant, state should decrease
                encoder.EncodeQuadSignificance(true);
                Assert.That(encoder.State, Is.EqualTo(0), "State should decrease after significance");
            }
            finally
            {
                encoder.Dispose();
            }
        }

        [Test]
        public void MelEncoder_Flush_ReturnsNonEmptyData()
        {
            var encoder = new MelEncoder(64);
            try
            {
                encoder.EncodeQuadSignificance(false); // emit 1-bit (run complete)
                encoder.EncodeQuadSignificance(true);  // emit 0-bit (run break)

                var data = encoder.Flush();
                Assert.That(data.Length, Is.GreaterThan(0), "Flushed data should not be empty");
            }
            finally
            {
                encoder.Dispose();
            }
        }

        #endregion

        #region Roundtrip Tests via HtCleanupWriter/Reader

        /// <summary>
        /// Tests MEL encode/decode roundtrip by going through HtCleanupWriter and HtCleanupReader.
        /// The standalone MelDecoder reads from a cleanup segment, so the only correct way
        /// to test encoder/decoder roundtrips is through the full cleanup segment pipeline.
        /// </summary>
        [Test]
        public void MelRoundtrip_AlternatingPattern()
        {
            bool[] pattern = { false, true, false, true, false, false, true, true };
            VerifyMelRoundtrip(pattern);
        }

        [Test]
        public void MelRoundtrip_AllInsignificant()
        {
            bool[] pattern = new bool[50];
            // All false (insignificant)
            VerifyMelRoundtrip(pattern);
        }

        [Test]
        public void MelRoundtrip_AllSignificant()
        {
            bool[] pattern = new bool[20];
            for (int i = 0; i < pattern.Length; i++)
                pattern[i] = true;
            VerifyMelRoundtrip(pattern);
        }

        [Test]
        public void MelRoundtrip_LongInsignificantRun_ThenSignificant()
        {
            // 32 insignificant then 1 significant (tests high state run breaking)
            bool[] pattern = new bool[33];
            pattern[32] = true;
            VerifyMelRoundtrip(pattern);
        }

        [Test]
        public void MelRoundtrip_MixedPattern_Long()
        {
            bool[] pattern = new bool[40];

            // First 16: all significant (produces 1-bits, will create 0xFF bytes)
            for (int i = 0; i < 16; i++)
                pattern[i] = true;

            // Then alternating to change the bit pattern after the 0xFF boundary
            for (int i = 16; i < 40; i++)
                pattern[i] = (i % 3) != 0; // true, true, false, true, true, false, ...

            VerifyMelRoundtrip(pattern);
        }

        [Test]
        public void MelRoundtrip_WithBitStuffing_LongSignificantRun()
        {
            // Encode enough significant quads to produce 0xFF bytes in the MEL stream
            int count = 30;
            bool[] pattern = new bool[count];
            for (int i = 0; i < count; i++)
                pattern[i] = true;
            VerifyMelRoundtrip(pattern);
        }

        /// <summary>
        /// Helper method that encodes MEL events via HtCleanupWriter, finalizes a segment,
        /// then decodes via HtCleanupReader and verifies the roundtrip.
        /// </summary>
        private static void VerifyMelRoundtrip(bool[] pattern)
        {
            var writer = new HtCleanupWriter(128);
            byte[] segment;
            try
            {
                foreach (bool sig in pattern)
                {
                    writer.EncodeMel(sig);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2),
                "Segment must have at least the ILW bytes");

            var reader = new HtCleanupReader(segment);

            for (int i = 0; i < pattern.Length; i++)
            {
                bool decoded = reader.DecodeMelSignificance();
                Assert.That(decoded, Is.EqualTo(pattern[i]),
                    $"Quad {i}: expected {pattern[i]}, got {decoded}");
            }
        }

        #endregion

        #region MelDecoder State Transition Tests via Full Segment

        [Test]
        public void MelDecoder_AllSignificant_FromSegment()
        {
            // Use HtCleanupWriter to encode all-significant MEL pattern,
            // then verify all come back significant.
            const int count = 20;
            var writer = new HtCleanupWriter(128);
            byte[] segment;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    writer.EncodeMel(true);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            for (int i = 0; i < count; i++)
            {
                bool sig = reader.DecodeMelSignificance();
                Assert.That(sig, Is.True, $"Quad {i} should be significant");
            }
        }

        [Test]
        public void MelDecoder_AllInsignificant_FromSegment()
        {
            // All insignificant quads - should reach high state
            const int count = 50;
            var writer = new HtCleanupWriter(128);
            byte[] segment;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    writer.EncodeMel(false);
                }
                segment = writer.Finalize();
            }
            finally
            {
                writer.Dispose();
            }

            var reader = new HtCleanupReader(segment);
            for (int i = 0; i < count; i++)
            {
                bool sig = reader.DecodeMelSignificance();
                Assert.That(sig, Is.False, $"Quad {i}: should be insignificant");
            }
        }

        #endregion
    }
}
