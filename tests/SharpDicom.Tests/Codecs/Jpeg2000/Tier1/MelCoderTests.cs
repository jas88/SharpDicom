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

        #region Bit-Stuffing Tests

        [Test]
        public void MelDecoder_BitStuffing_After0xFF_Only7BitsUsed()
        {
            // Construct raw MEL data where bit-stuffing matters.
            // Decoder reads backward, so byte[2] is read first, then byte[1], then byte[0].
            //
            // Byte layout (read order):
            //   byte[2] = 0xFF  -> 8 bits available (no previous 0xFF), sets _prevWasFF = true
            //   byte[1] = 0x7F  -> 7 bits available (bit-stuffing: MSB skipped), _prevWasFF = false
            //   byte[0] = 0xAA  -> 8 bits available (previous was not 0xFF)
            //
            // Without bit-stuffing fix, byte[1] would contribute 8 bits, corrupting decode.
            //
            // Bit stream (MSB-first from each byte, skipping stuffing bit):
            //   From 0xFF: 1111 1111 (8 bits)
            //   From 0x7F with stuffing: [skip MSB=0] 111 1111 (7 bits)
            //   From 0xAA: 1010 1010 (8 bits)
            // Total: 8 + 7 + 8 = 23 data bits
            //
            // At state 0, MelE[0]=0: each decode reads 1 bit.
            //   bit=1 -> significant (state stays 0)
            // So the first 15 bits (all 1s from 0xFF + 0x7F) give 15 significant quads.
            // Then from 0xAA = 1010 1010:
            //   bit=1 -> significant
            //   bit=0 -> insignificant (run of 1, state -> 1)
            //   At state 1, MelE[1]=0: bit=1 -> significant (state -> 0)
            //   bit=0 -> insignificant (run of 1, state -> 1)
            //   bit=1 -> significant (state -> 0)
            //   bit=0 -> insignificant (run of 1, state -> 1)
            //   bit=1 -> significant (state -> 0)
            //   bit=0 -> insignificant (run of 1, state -> 1)

            byte[] data = { 0xAA, 0x7F, 0xFF };
            var decoder = new MelDecoder(data, data.Length);

            // First 15 bits are all 1s (8 from 0xFF + 7 from 0x7F): 15 significant quads
            for (int i = 0; i < 15; i++)
            {
                bool sig = decoder.DecodeQuadSignificance();
                Assert.That(sig, Is.True, $"Quad {i}: expected significant (from 0xFF/0x7F all-1 bits)");
            }

            // Then from 0xAA (10101010): alternating significant/insignificant
            bool[] expectedFromAA = { true, false, true, false, true, false, true, false };
            for (int i = 0; i < expectedFromAA.Length; i++)
            {
                bool sig = decoder.DecodeQuadSignificance();
                Assert.That(sig, Is.EqualTo(expectedFromAA[i]),
                    $"Quad {15 + i}: expected {expectedFromAA[i]} (from 0xAA pattern)");
            }
        }

        [Test]
        public void MelDecoder_BitStuffing_Consecutive0xFF_Each7Bits()
        {
            // Two consecutive 0xFF bytes followed by a normal byte.
            // Read order (backward): byte[2]=0xFF, byte[1]=0xFF, byte[0]=0x80
            //
            // Bit contributions:
            //   byte[2] = 0xFF: 8 bits (no previous 0xFF), _prevWasFF = true
            //   byte[1] = 0xFF: 7 bits (bit-stuffing), _prevWasFF = true
            //   byte[0] = 0x80: 7 bits (bit-stuffing after 0xFF), _prevWasFF = false
            //
            // Total data bits: 8 + 7 + 7 = 22 bits
            // From 0xFF (8 bits): 11111111 -> 8 significant quads
            // From 0xFF (7 bits, skip MSB): 1111111 -> 7 significant quads
            // From 0x80 (7 bits, skip MSB): 0000000 -> 7 insignificant quads
            // Total: 15 significant, then 7 insignificant

            byte[] data = { 0x80, 0xFF, 0xFF };
            var decoder = new MelDecoder(data, data.Length);

            // First 15 quads should be significant (8 + 7 all-1 bits)
            for (int i = 0; i < 15; i++)
            {
                bool sig = decoder.DecodeQuadSignificance();
                Assert.That(sig, Is.True, $"Quad {i}: expected significant");
            }

            // Next 7 quads should be insignificant (0x80 with MSB skipped = 0000000)
            // At this point state is 0 (decremented 15 times from 0, clamped at 0)
            // Actually, state transitions matter:
            // After 15 significant quads at state 0, state stays at max(0-1,0)=0 each time
            // A 0-bit at state 0: run of 1 insig quad, state -> 1
            // A 0-bit at state 1: run of 1 insig quad, state -> 2
            // ... and so on for 7 zero bits
            for (int i = 0; i < 7; i++)
            {
                bool sig = decoder.DecodeQuadSignificance();
                Assert.That(sig, Is.False, $"Quad {15 + i}: expected insignificant");
            }
        }

        [Test]
        public void MelRoundtrip_WithBitStuffing_LongSignificantRun()
        {
            // Encode enough significant quads to produce 0xFF bytes in the MEL stream,
            // then decode and verify. This ensures encoder and decoder agree on bit-stuffing.
            int count = 30; // Enough to produce multiple 0xFF bytes

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    encoder.EncodeQuadSignificance(true);
                }
                var data = encoder.Flush();

                // Verify that the encoded data actually contains 0xFF bytes
                bool has0xFF = false;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == 0xFF)
                    {
                        has0xFF = true;
                        break;
                    }
                }
                Assert.That(has0xFF, Is.True,
                    "Encoded data should contain 0xFF bytes to exercise bit-stuffing");

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
                    $"Quad {i}: should be significant (roundtrip with bit-stuffing)");
            }
        }

        [Test]
        public void MelRoundtrip_MixedPattern_With0xFFBytes()
        {
            // A pattern designed to produce 0xFF in the stream (many 1-bits)
            // followed by a pattern change, to ensure bit-stuffing boundaries are handled.
            bool[] pattern = new bool[40];

            // First 16: all significant (produces 1-bits, will create 0xFF bytes)
            for (int i = 0; i < 16; i++)
                pattern[i] = true;

            // Then alternating to change the bit pattern after the 0xFF boundary
            for (int i = 16; i < 40; i++)
                pattern[i] = (i % 3) != 0; // true, true, false, true, true, false, ...

            var encoder = new MelEncoder(64);
            byte[] reversed;
            try
            {
                foreach (bool sig in pattern)
                {
                    encoder.EncodeQuadSignificance(sig);
                }
                var data = encoder.Flush();
                reversed = ReverseArray(data);
            }
            finally
            {
                encoder.Dispose();
            }

            var decoder = new MelDecoder(reversed, reversed.Length);

            for (int i = 0; i < pattern.Length; i++)
            {
                bool decoded = decoder.DecodeQuadSignificance();
                Assert.That(decoded, Is.EqualTo(pattern[i]),
                    $"Quad {i}: expected {pattern[i]}, got {decoded}");
            }
        }

        #endregion
    }
}
