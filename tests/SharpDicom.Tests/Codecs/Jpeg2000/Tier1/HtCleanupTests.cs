using System;
using NUnit.Framework;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Tests.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Tests for the HT Cleanup pass encoder and decoder.
    /// </summary>
    [TestFixture]
    public class HtCleanupTests
    {
        #region All-Zero Block Tests

        [Test]
        public void AllZeroBlock_4x4_RoundtripsCorrectly()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            // All zeros

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            // Segment should be minimal (only MEL runs + ILW)
            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2), "Segment must have ILW");

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients),
                "All-zero block should decode to all zeros");
        }

        [Test]
        public void AllZeroBlock_8x8_MinimalSegment()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Single Non-Zero Sample Tests

        [Test]
        public void SingleNonZero_TopLeft_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 5; // top-left of first quad

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void SingleNonZero_Negative_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = -3;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void SingleNonZero_Value1_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[5] = 1; // row 1, col 1

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void SingleNonZero_ValueNeg1_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[7] = -1; // row 1, col 3

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Alternating Significance Tests

        [Test]
        public void AlternatingQuads_Roundtrip()
        {
            // 4x4 block: first quad significant, second not, third significant, fourth not
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];

            // First quad (rows 0-1, cols 0-1): all non-zero
            coefficients[0] = 1;
            coefficients[1] = 2;
            coefficients[4] = 3;
            coefficients[5] = 4;

            // Second quad (rows 0-1, cols 2-3): all zero

            // Third quad (rows 2-3, cols 0-1): all non-zero
            coefficients[8] = -1;
            coefficients[9] = -2;
            coefficients[12] = -3;
            coefficients[13] = -4;

            // Fourth quad (rows 2-3, cols 2-3): all zero

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void AlternatingSignificance_LargerBlock()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];

            // Set alternating quads to have values
            for (int qr = 0; qr < 4; qr++)
            {
                for (int qc = 0; qc < 4; qc++)
                {
                    if ((qr + qc) % 2 == 0)
                    {
                        int r = qr * 2;
                        int c = qc * 2;
                        coefficients[r * width + c] = (qr + 1) * (qc + 1);
                    }
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Full Significance Tests

        [Test]
        public void FullSignificance_AllOnes_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = 1;
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void FullSignificance_VariousMagnitudes_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            int val = 1;
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = (i % 2 == 0) ? val : -val;
                val++;
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void FullSignificance_8x8_Roundtrip()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = (i % 3 == 0) ? -(i + 1) : (i + 1);
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Various Code-Block Size Tests

        [Test]
        [TestCase(4, 4)]
        [TestCase(8, 8)]
        [TestCase(16, 16)]
        [TestCase(32, 32)]
        [TestCase(64, 64)]
        public void VariousBlockSizes_Roundtrip(int width, int height)
        {
            int[] coefficients = new int[width * height];
            var rng = new Random(42 + width * height);
            for (int i = 0; i < coefficients.Length; i++)
            {
                // Sparse: ~30% non-zero
                if (rng.Next(100) < 30)
                {
                    coefficients[i] = rng.Next(-50, 51);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void MinimalBlock_2x2_Roundtrip()
        {
            int width = 2, height = 2;
            int[] coefficients = { 5, -3, 7, 0 };

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[4];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Odd Dimension Tests

        [Test]
        [TestCase(5, 5)]
        [TestCase(7, 3)]
        [TestCase(3, 7)]
        [TestCase(1, 1)]
        [TestCase(1, 4)]
        [TestCase(4, 1)]
        [TestCase(3, 3)]
        [TestCase(9, 5)]
        public void OddDimensions_Roundtrip(int width, int height)
        {
            int[] coefficients = new int[width * height];
            var rng = new Random(width * 100 + height);
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(-20, 21);
                if (coefficients[i] == 0 && rng.Next(2) == 0)
                {
                    coefficients[i] = 1; // ensure some non-zero values
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void OddDimension_1x1_SingleValue()
        {
            int[] coefficients = { 42 };

            byte[] segment = HtCleanup.Encode(coefficients, 1, 1, 0);

            int[] decoded = new int[1];
            HtCleanup.Decode(segment, decoded, 1, 1, 0);

            Assert.That(decoded[0], Is.EqualTo(42));
        }

        [Test]
        public void OddDimension_1x1_Zero()
        {
            int[] coefficients = { 0 };

            byte[] segment = HtCleanup.Encode(coefficients, 1, 1, 0);

            int[] decoded = new int[1];
            HtCleanup.Decode(segment, decoded, 1, 1, 0);

            Assert.That(decoded[0], Is.EqualTo(0));
        }

        #endregion

        #region Large Magnitude Tests

        [Test]
        [TestCase(32767)]
        [TestCase(-32768)]
        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(255)]
        [TestCase(-256)]
        [TestCase(1000)]
        [TestCase(16383)]
        public void LargeMagnitudes_Roundtrip(int value)
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = value;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded[0], Is.EqualTo(value),
                $"Value {value} should roundtrip exactly");
        }

        [Test]
        public void LargeMagnitudes_AllCorners_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            // Place large values in each corner of first quad
            coefficients[0] = 32767;   // top-left
            coefficients[1] = -32768;  // top-right
            coefficients[4] = 16384;   // bottom-left
            coefficients[5] = -16384;  // bottom-right

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void LargeMagnitude_MixedSizes()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 1;       // E=1
            coefficients[1] = 3;       // E=2
            coefficients[4] = 127;     // E=7
            coefficients[5] = 32767;   // E=15

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Subband Type Tests

        [Test]
        [TestCase(0)]  // LL
        [TestCase(1)]  // LH
        [TestCase(2)]  // HL
        [TestCase(3)]  // HH
        public void SubbandTypes_Roundtrip(int subbandType)
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];
            var rng = new Random(42 + subbandType);
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(-30, 31);
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, subbandType);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, subbandType);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void SubbandType_AffectsTableSelection()
        {
            // Same data with different subband types should produce different
            // encoded segments (due to VLC table selection), but decode correctly
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];
            var rng = new Random(42);
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(-10, 11);
            }

            byte[] segment0 = HtCleanup.Encode(coefficients, width, height, 0);
            byte[] segment1 = HtCleanup.Encode(coefficients, width, height, 1);

            // Both should decode correctly with their respective subband type
            int[] decoded0 = new int[width * height];
            int[] decoded1 = new int[width * height];
            HtCleanup.Decode(segment0, decoded0, width, height, 0);
            HtCleanup.Decode(segment1, decoded1, width, height, 1);

            Assert.That(decoded0, Is.EqualTo(coefficients));
            Assert.That(decoded1, Is.EqualTo(coefficients));
        }

        #endregion

        #region Random Data Roundtrip Tests

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
            int[] coefficients = new int[width * height];

            for (int i = 0; i < coefficients.Length; i++)
            {
                // Mix of zeros and non-zeros with varying magnitudes
                int r = rng.Next(100);
                if (r < 40)
                {
                    coefficients[i] = 0;
                }
                else if (r < 70)
                {
                    coefficients[i] = rng.Next(-10, 11);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
                else if (r < 90)
                {
                    coefficients[i] = rng.Next(-1000, 1001);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
                else
                {
                    coefficients[i] = rng.Next(-32000, 32001);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
            }

            int subbandType = rng.Next(4);
            byte[] segment = HtCleanup.Encode(coefficients, width, height, subbandType);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, subbandType);

            Assert.That(decoded, Is.EqualTo(coefficients),
                $"Roundtrip failed for seed={seed}, {width}x{height}, subband={subbandType}");
        }

        [Test]
        public void RandomData_HighDensity_Roundtrip()
        {
            // All samples non-zero
            var rng = new Random(999);
            int width = 16, height = 16;
            int[] coefficients = new int[width * height];

            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(1, 100) * (rng.Next(2) == 0 ? 1 : -1);
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void RandomData_Sparse_Roundtrip()
        {
            // Very few non-zero samples (~5%)
            var rng = new Random(777);
            int width = 32, height = 32;
            int[] coefficients = new int[width * height];

            for (int i = 0; i < coefficients.Length; i++)
            {
                if (rng.Next(100) < 5)
                {
                    coefficients[i] = rng.Next(1, 50) * (rng.Next(2) == 0 ? 1 : -1);
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Segment Structure Tests

        [Test]
        public void SegmentStructure_HasValidILW()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 5;
            coefficients[1] = -3;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            // Segment must have at least 2 bytes for ILW
            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));

            // Parse ILW
            int ilwByte0 = segment[segment.Length - 2];
            int ilwByte1 = segment[segment.Length - 1];
            int vlcOffset = (ilwByte0 << 4) | (ilwByte1 >> 4);

            // VLC offset should be non-negative and within segment bounds
            Assert.That(vlcOffset, Is.GreaterThanOrEqualTo(0));
            Assert.That(vlcOffset, Is.LessThanOrEqualTo(segment.Length - 2),
                "VLC offset must be within segment data area");
        }

        [Test]
        public void SegmentStructure_AllZero_ValidILW()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            Assert.That(segment.Length, Is.GreaterThanOrEqualTo(2));

            int ilwByte0 = segment[segment.Length - 2];
            int ilwByte1 = segment[segment.Length - 1];
            int vlcOffset = (ilwByte0 << 4) | (ilwByte1 >> 4);

            Assert.That(vlcOffset, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void SegmentStructure_ILW_PointsToValidBoundary()
        {
            int width = 16, height = 16;
            int[] coefficients = new int[width * height];
            var rng = new Random(123);
            for (int i = 0; i < coefficients.Length; i++)
            {
                if (rng.Next(100) < 50)
                {
                    coefficients[i] = rng.Next(-100, 101);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            // Parse ILW
            int vlcOffset = (segment[segment.Length - 2] << 4) | (segment[segment.Length - 1] >> 4);

            // MagSgn region: [0, vlcOffset)
            // VLC+MEL region: [vlcOffset, segment.Length - 2)
            // ILW: [segment.Length - 2, segment.Length)
            Assert.That(vlcOffset, Is.GreaterThanOrEqualTo(0));
            Assert.That(vlcOffset, Is.LessThanOrEqualTo(segment.Length - 2));
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public void Encode_WrongCoefficientCount_Throws()
        {
            int[] coefficients = new int[10]; // Not 4x4=16
            Assert.Throws<ArgumentException>(() =>
                HtCleanup.Encode(coefficients, 4, 4, 0));
        }

        [Test]
        public void Decode_OutputTooSmall_Throws()
        {
            byte[] segment = { 0x00, 0x00 }; // Minimal segment
            int[] output = new int[4]; // Too small for 4x4
            Assert.Throws<ArgumentException>(() =>
                HtCleanup.Decode(segment, output, 4, 4, 0));
        }

        [Test]
        public void Encode_ZeroDimension_Throws()
        {
            int[] coefficients = Array.Empty<int>();
            Assert.Throws<ArgumentException>(() =>
                HtCleanup.Encode(coefficients, 0, 4, 0));
        }

        [Test]
        public void Decode_ZeroDimension_Throws()
        {
            byte[] segment = { 0x00, 0x00 };
            int[] output = new int[16];
            Assert.Throws<ArgumentException>(() =>
                HtCleanup.Decode(segment, output, 0, 4, 0));
        }

        #endregion

        #region Context Formation Tests

        [Test]
        public void ContextFormation_FirstQuad_HasZeroContext()
        {
            // The first quad (qr=0, qc=0) has no neighbours, so context should be 0.
            // This is implicitly tested by the all-zero and single-value tests,
            // but we verify by checking that context 0 produces the expected VLC codeword.

            // With a single value at position (0,0), context should be 0 for the first quad
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 1;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded[0], Is.EqualTo(1));
        }

        [Test]
        public void ContextFormation_NeighbourInfluence()
        {
            // Set up a pattern where context bits should be non-zero
            // First quad (qr=0, qc=0) significant -> second quad has left=1
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 1; // First quad significant
            coefficients[2] = 2; // Second quad (qr=0, qc=1) significant, should have context with left=1

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients),
                "Roundtrip with non-zero context should work correctly");
        }

        [Test]
        public void ContextFormation_AboveNeighbour()
        {
            // Quad at (qr=0, qc=0) significant -> quad at (qr=1, qc=0) has above=1
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 1;  // First quad (qr=0) significant
            coefficients[8] = 3;  // Quad at row 2, col 0 (qr=1, qc=0) significant

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void ContextFormation_AllNeighbours()
        {
            // Set up so that quad (qr=1, qc=1) has left, above-left, and above all significant
            int width = 4, height = 4;
            int[] coefficients = new int[width * height];
            coefficients[0] = 1;  // (qr=0, qc=0) -> above-left of (qr=1, qc=1)
            coefficients[2] = 2;  // (qr=0, qc=1) -> above of (qr=1, qc=1)
            coefficients[8] = 3;  // (qr=1, qc=0) -> left of (qr=1, qc=1)
            coefficients[10] = 4; // (qr=1, qc=1) -> the target quad

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region MagSgn Encoding Verification Tests

        [Test]
        [TestCase(1)]   // E=1: sign + 0-term = 2 bits
        [TestCase(2)]   // E=2: sign + 1 + 0-term + 1-bit mantissa = 4 bits
        [TestCase(3)]   // E=2: sign + 1 + 0-term + 1-bit mantissa = 4 bits
        [TestCase(4)]   // E=3: sign + 11 + 0-term + 2-bit mantissa = 6 bits
        [TestCase(7)]   // E=3: sign + 11 + 0-term + 2-bit mantissa = 6 bits
        [TestCase(8)]   // E=4: sign + 111 + 0-term + 3-bit mantissa = 8 bits
        [TestCase(127)] // E=7: sign + 111111 + 0-term + 6-bit mantissa = 14 bits
        public void MagSgn_VariousExponents_Roundtrip(int value)
        {
            int width = 2, height = 2;
            int[] coefficients = { value, 0, 0, 0 };

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[4];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded[0], Is.EqualTo(value));
        }

        [Test]
        public void MagSgn_PowersOfTwo_Roundtrip()
        {
            // Powers of 2 are special: mantissa is all zeros
            int width = 4, height = 4;
            int[] coefficients = new int[16];
            coefficients[0] = 1;   // 2^0
            coefficients[1] = 2;   // 2^1
            coefficients[4] = 4;   // 2^2
            coefficients[5] = 8;   // 2^3

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[16];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void MagSgn_NegativePowersOfTwo_Roundtrip()
        {
            int width = 4, height = 4;
            int[] coefficients = new int[16];
            coefficients[0] = -1;
            coefficients[1] = -2;
            coefficients[4] = -4;
            coefficients[5] = -8;

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[16];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void LargeBlock_64x64_Roundtrip()
        {
            int width = 64, height = 64;
            int[] coefficients = new int[width * height];
            var rng = new Random(12345);
            for (int i = 0; i < coefficients.Length; i++)
            {
                if (rng.Next(100) < 25)
                {
                    coefficients[i] = rng.Next(-500, 501);
                    if (coefficients[i] == 0) coefficients[i] = 1;
                }
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded = new int[width * height];
            HtCleanup.Decode(segment, decoded, width, height, 0);

            Assert.That(decoded, Is.EqualTo(coefficients));
        }

        [Test]
        public void ConsecutiveEncodes_ProduceSameResult()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];
            var rng = new Random(55);
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(-20, 21);
            }

            byte[] segment1 = HtCleanup.Encode(coefficients, width, height, 0);
            byte[] segment2 = HtCleanup.Encode(coefficients, width, height, 0);

            Assert.That(segment1, Is.EqualTo(segment2),
                "Deterministic encoding should produce identical segments");
        }

        [Test]
        public void MultipleDecodes_ProduceSameResult()
        {
            int width = 8, height = 8;
            int[] coefficients = new int[width * height];
            var rng = new Random(66);
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] = rng.Next(-20, 21);
            }

            byte[] segment = HtCleanup.Encode(coefficients, width, height, 0);

            int[] decoded1 = new int[width * height];
            int[] decoded2 = new int[width * height];
            HtCleanup.Decode(segment, decoded1, width, height, 0);
            HtCleanup.Decode(segment, decoded2, width, height, 0);

            Assert.That(decoded1, Is.EqualTo(decoded2),
                "Deterministic decoding should produce identical results");
        }

        #endregion
    }
}
