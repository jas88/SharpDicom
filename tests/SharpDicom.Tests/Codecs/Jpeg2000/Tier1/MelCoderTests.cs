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

        #region MelDecoder State Transition Tests

        [Test]
        public void MelDecoder_InitialState_IsZero()
        {
            // Create a decoder with some data
            Span<byte> data = stackalloc byte[] { 0x00 };
            var decoder = new MelDecoder(data, data.Length);

            Assert.That(decoder.State, Is.EqualTo(0));
            Assert.That(decoder.Run, Is.EqualTo(0));
        }

        [Test]
        public void MelDecoder_RunExtension_IncreasesState()
        {
            // A 0-bit indicates run extension, which should increase the state
            // At state 0, MelE[0]=0 so run length = 1
            // A 0-bit means: run of 1 insignificant quad, state goes to 1
            Span<byte> data = stackalloc byte[] { 0x00 }; // all zero bits
            var decoder = new MelDecoder(data, data.Length);

            bool sig = decoder.DecodeQuadSignificance();
            Assert.That(sig, Is.False, "Should be insignificant (run extension)");
            Assert.That(decoder.State, Is.EqualTo(1), "State should increase from 0 to 1");
        }

        [Test]
        public void MelDecoder_RunBreak_DecreasesState()
        {
            // A 1-bit breaks the run, decreasing the state
            // Start at state 0, a 1-bit means significant, state goes to max(0-1, 0) = 0
            Span<byte> data = stackalloc byte[] { 0x80 }; // bit 7 = 1
            var decoder = new MelDecoder(data, data.Length);

            bool sig = decoder.DecodeQuadSignificance();
            Assert.That(sig, Is.True, "Should be significant (run break)");
            Assert.That(decoder.State, Is.EqualTo(0), "State should stay at 0 (can't go below 0)");
        }

        [Test]
        public void MelDecoder_AllInsignificant_ReachesHighState()
        {
            // Feed many 0-bits to drive state to maximum
            // With all zeros, the decoder will see continuous run extensions
            byte[] data = new byte[32];
            // All zeros = continuous 0-bits
            var decoder = new MelDecoder(data, data.Length);

            // Decode many insignificant quads
            int maxQuads = 200; // more than enough to reach max state
            for (int i = 0; i < maxQuads; i++)
            {
                decoder.DecodeQuadSignificance();
            }

            // State should be at or near maximum (12)
            Assert.That(decoder.State, Is.GreaterThanOrEqualTo(10),
                "After many insignificant quads, state should be high");
        }

        [Test]
        public void MelDecoder_AllSignificant_StaysAtZero()
        {
            // Feed all 1-bits to keep state at 0
            byte[] data = new byte[32];
            for (int i = 0; i < data.Length; i++)
                data[i] = 0xFF; // all 1-bits

            var decoder = new MelDecoder(data, data.Length);

            // Each decode should return significant and state stays at 0
            for (int i = 0; i < 20; i++)
            {
                bool sig = decoder.DecodeQuadSignificance();
                Assert.That(sig, Is.True, $"Quad {i} should be significant");
                Assert.That(decoder.State, Is.EqualTo(0), $"State should stay at 0 after quad {i}");
            }
        }

        #endregion

        #region MelDecoder Run Length Tests

        [Test]
        public void MelDecoder_State0_RunLength1()
        {
            // At state 0, MelE[0]=0, run length = 2^0 = 1
            // A 0-bit should produce exactly 1 insignificant quad
            Span<byte> data = stackalloc byte[] { 0x00 };
            var decoder = new MelDecoder(data, data.Length);

            bool sig1 = decoder.DecodeQuadSignificance();
            Assert.That(sig1, Is.False, "First quad should be insignificant (run of 1)");
            // The run count should now be 0 (1 - 1 = 0)
            Assert.That(decoder.Run, Is.EqualTo(0), "Run should be exhausted");
        }

        [Test]
        public void MelDecoder_State3_RunLength2()
        {
            // MelE[3]=1, run length = 2^1 = 2
            // Need to get decoder to state 3 first by feeding 3 run extensions
            byte[] data = new byte[16]; // all zeros
            var decoder = new MelDecoder(data, data.Length);

            // Drive state from 0 to 3 by feeding run extensions
            // State 0: MelE[0]=0, run=1, decode 1 insig -> state 1
            decoder.DecodeQuadSignificance(); // state -> 1
            // State 1: MelE[1]=0, run=1, decode 1 insig -> state 2
            decoder.DecodeQuadSignificance(); // state -> 2
            // State 2: MelE[2]=0, run=1, decode 1 insig -> state 3
            decoder.DecodeQuadSignificance(); // state -> 3

            Assert.That(decoder.State, Is.EqualTo(3));

            // Now at state 3: MelE[3]=1, run = 2^1 = 2
            // Next 0-bit should produce 2 insignificant quads
            bool sig1 = decoder.DecodeQuadSignificance(); // first of run
            Assert.That(sig1, Is.False, "First quad in run of 2");

            bool sig2 = decoder.DecodeQuadSignificance(); // second (from _run counter)
            Assert.That(sig2, Is.False, "Second quad in run of 2");

            Assert.That(decoder.Run, Is.EqualTo(0), "Run should be exhausted after 2 quads");
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
                // Encoding one insignificant completes the run and emits a 0-bit
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
                encoder.EncodeQuadSignificance(false); // emit 0-bit
                encoder.EncodeQuadSignificance(true);  // emit 1-bit

                var data = encoder.Flush();
                Assert.That(data.Length, Is.GreaterThan(0), "Flushed data should not be empty");
            }
            finally
            {
                encoder.Dispose();
            }
        }

        #endregion

        #region Roundtrip Tests

        [Test]
        public void MelRoundtrip_AlternatingPattern()
        {
            // Encode alternating significant/insignificant quads
            bool[] pattern = { false, true, false, true, false, false, true, true };

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                foreach (bool sig in pattern)
                {
                    encoder.EncodeQuadSignificance(sig);
                }
                var data = encoder.Flush();
                // Reverse the encoded data: encoder writes forward, decoder reads backward
                reversed = ReverseArray(data);
            }
            finally
            {
                encoder.Dispose();
            }

            // Decode from the reversed data (decoder reads backward)
            var decoder = new MelDecoder(reversed, reversed.Length);

            for (int i = 0; i < pattern.Length; i++)
            {
                bool decoded = decoder.DecodeQuadSignificance();
                Assert.That(decoded, Is.EqualTo(pattern[i]),
                    $"Quad {i}: expected {pattern[i]}, got {decoded}");
            }
        }

        [Test]
        public void MelRoundtrip_AllInsignificant()
        {
            // Encode all insignificant quads
            int count = 50;

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.EncodeQuadSignificance(false);
                }
                var data = encoder.Flush();
                reversed = ReverseArray(data);
            }
            finally
            {
                encoder.Dispose();
            }

            var decoder = new MelDecoder(reversed, reversed.Length);

            for (int i = 0; i < count; i++)
            {
                bool decoded = decoder.DecodeQuadSignificance();
                Assert.That(decoded, Is.False,
                    $"Quad {i}: should be insignificant");
            }
        }

        [Test]
        public void MelRoundtrip_AllSignificant()
        {
            // Encode all significant quads
            int count = 20;

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.EncodeQuadSignificance(true);
                }
                var data = encoder.Flush();
                reversed = ReverseArray(data);
            }
            finally
            {
                encoder.Dispose();
            }

            var decoder = new MelDecoder(reversed, reversed.Length);

            for (int i = 0; i < count; i++)
            {
                bool decoded = decoder.DecodeQuadSignificance();
                Assert.That(decoded, Is.True,
                    $"Quad {i}: should be significant");
            }
        }

        [Test]
        public void MelRoundtrip_LongInsignificantRun_ThenSignificant()
        {
            // 32 insignificant then 1 significant (tests high state run breaking)
            int insigCount = 32;

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                for (int i = 0; i < insigCount; i++)
                {
                    encoder.EncodeQuadSignificance(false);
                }
                encoder.EncodeQuadSignificance(true);
                var data = encoder.Flush();
                reversed = ReverseArray(data);
            }
            finally
            {
                encoder.Dispose();
            }

            var decoder = new MelDecoder(reversed, reversed.Length);

            for (int i = 0; i < insigCount; i++)
            {
                bool decoded = decoder.DecodeQuadSignificance();
                Assert.That(decoded, Is.False,
                    $"Quad {i}: should be insignificant");
            }

            bool lastDecoded = decoder.DecodeQuadSignificance();
            Assert.That(lastDecoded, Is.True, "Last quad should be significant");
        }

        /// <summary>
        /// Reverses a span of bytes into a new array.
        /// The MEL encoder writes forward but the decoder reads backward,
        /// so the encoded data must be reversed for standalone roundtrip tests.
        /// In the real cleanup segment, this reversal happens during Finalize.
        /// </summary>
        private static byte[] ReverseArray(ReadOnlySpan<byte> data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = data[data.Length - 1 - i];
            }
            return result;
        }

        #endregion
    }
}
