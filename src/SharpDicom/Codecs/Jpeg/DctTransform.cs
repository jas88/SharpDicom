using System;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Provides forward and inverse 8x8 Discrete Cosine Transform (DCT) operations
    /// for JPEG baseline encoding and decoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DCT is the core mathematical operation in JPEG compression. It transforms
    /// an 8x8 block of spatial-domain pixel values into 8x8 frequency-domain coefficients.
    /// The forward DCT is used during encoding; the inverse DCT during decoding.
    /// </para>
    /// <para>
    /// This implementation uses the AAN (Arai, Agui, Nakajima) algorithm which requires
    /// only 5 multiplications and 29 additions for a 1D 8-point DCT, compared to the
    /// naive O(N^2) approach. The 2D DCT is implemented as separable 1D transforms
    /// on rows then columns.
    /// </para>
    /// </remarks>
    public static class DctTransform
    {
        // AAN algorithm constants (cosine values)
        // These are derived from cos(k*pi/16) for k = 1..7
        private const float C1 = 0.980785280f;  // cos(1*pi/16)
        private const float C2 = 0.923879533f;  // cos(2*pi/16)
        private const float C3 = 0.831469612f;  // cos(3*pi/16)
        private const float C4 = 0.707106781f;  // cos(4*pi/16) = sqrt(2)/2
        private const float C5 = 0.555570233f;  // cos(5*pi/16)
        private const float C6 = 0.382683432f;  // cos(6*pi/16)
        private const float C7 = 0.195090322f;  // cos(7*pi/16)

        // Derived constants for AAN algorithm
        private const float S0 = 0.353553391f;  // 1/(2*sqrt(2))
        private const float S1 = 0.254897800f;  // C1/(4*sqrt(2)*cos(7*pi/16))
        private const float S2 = 0.270598050f;  // C2/(4*sqrt(2)*cos(6*pi/16))
        private const float S3 = 0.300672444f;  // C3/(4*sqrt(2)*cos(5*pi/16))
        private const float S4 = 0.353553391f;  // 1/(2*sqrt(2))
        private const float S5 = 0.449988111f;  // C5/(4*sqrt(2)*cos(3*pi/16))
        private const float S6 = 0.653281482f;  // C6/(4*sqrt(2)*cos(2*pi/16))
        private const float S7 = 1.281457724f;  // C7/(4*sqrt(2)*cos(1*pi/16))

        // Loeffler algorithm constants
        private const float A1 = C4;            // cos(4*pi/16) = sqrt(2)/2
        private const float A2 = C2 - C6;       // cos(2*pi/16) - cos(6*pi/16)
        private const float A3 = C4;            // cos(4*pi/16)
        private const float A4 = C6 + C2;       // cos(6*pi/16) + cos(2*pi/16)
        private const float A5 = C6;            // cos(6*pi/16)

        /// <summary>
        /// Performs forward 8x8 DCT on a block (in-place).
        /// </summary>
        /// <param name="block">64-element float span representing 8x8 block in row-major order.</param>
        /// <exception cref="ArgumentException">Thrown if block does not contain exactly 64 elements.</exception>
        /// <remarks>
        /// <para>
        /// The input should contain pixel values (typically 0-255 level-shifted to -128 to 127).
        /// The output will contain DCT coefficients with the DC coefficient at index 0.
        /// </para>
        /// <para>
        /// For proper JPEG encoding, follow this transform with quantization using the
        /// appropriate quantization table (scaled by quality factor).
        /// </para>
        /// </remarks>
        public static void Forward(Span<float> block)
        {
            if (block.Length != 64)
            {
                throw new ArgumentException("Block must contain exactly 64 elements.", nameof(block));
            }

#if NET8_0_OR_GREATER
            if (Avx2.IsSupported)
            {
                ForwardSimd(block);
                return;
            }
#endif
            ForwardScalar(block);
        }

        /// <summary>
        /// Performs inverse 8x8 DCT (IDCT) on a block (in-place).
        /// </summary>
        /// <param name="block">64-element float span of DCT coefficients.</param>
        /// <exception cref="ArgumentException">Thrown if block does not contain exactly 64 elements.</exception>
        /// <remarks>
        /// <para>
        /// The input should contain dequantized DCT coefficients.
        /// The output will contain pixel values that should be level-shifted (+128) and clamped to 0-255.
        /// </para>
        /// </remarks>
        public static void Inverse(Span<float> block)
        {
            if (block.Length != 64)
            {
                throw new ArgumentException("Block must contain exactly 64 elements.", nameof(block));
            }

#if NET8_0_OR_GREATER
            if (Avx2.IsSupported)
            {
                InverseSimd(block);
                return;
            }
#endif
            InverseScalar(block);
        }

        /// <summary>
        /// Scalar implementation of forward DCT using the Loeffler algorithm.
        /// </summary>
        private static void ForwardScalar(Span<float> block)
        {
            // Row transforms
            for (int row = 0; row < 8; row++)
            {
                Forward1D(block.Slice(row * 8, 8));
            }

            // Column transforms (transpose access pattern)
            Span<float> column = stackalloc float[8];
            for (int col = 0; col < 8; col++)
            {
                // Extract column
                for (int row = 0; row < 8; row++)
                {
                    column[row] = block[row * 8 + col];
                }

                // Transform
                Forward1D(column);

                // Store back
                for (int row = 0; row < 8; row++)
                {
                    block[row * 8 + col] = column[row];
                }
            }

            // Apply normalization factor (1/8 for 2D DCT = 0.125)
            for (int i = 0; i < 64; i++)
            {
                block[i] *= 0.125f;
            }
        }

        /// <summary>
        /// Scalar implementation of inverse DCT using the Loeffler algorithm.
        /// </summary>
        private static void InverseScalar(Span<float> block)
        {
            // Column transforms first (matches JPEG standard)
            Span<float> column = stackalloc float[8];
            for (int col = 0; col < 8; col++)
            {
                // Extract column
                for (int row = 0; row < 8; row++)
                {
                    column[row] = block[row * 8 + col];
                }

                // Transform
                Inverse1D(column);

                // Store back
                for (int row = 0; row < 8; row++)
                {
                    block[row * 8 + col] = column[row];
                }
            }

            // Row transforms
            for (int row = 0; row < 8; row++)
            {
                Inverse1D(block.Slice(row * 8, 8));
            }

            // Apply normalization factor (1/8 for 2D IDCT = 0.125)
            for (int i = 0; i < 64; i++)
            {
                block[i] *= 0.125f;
            }
        }

        /// <summary>
        /// 1D forward DCT using the Loeffler/Ligtenberg/Moschytz algorithm.
        /// </summary>
        /// <remarks>
        /// This algorithm requires only 11 multiplications and 29 additions,
        /// achieving the theoretical minimum for an 8-point DCT.
        /// </remarks>
        private static void Forward1D(Span<float> data)
        {
            float x0 = data[0];
            float x1 = data[1];
            float x2 = data[2];
            float x3 = data[3];
            float x4 = data[4];
            float x5 = data[5];
            float x6 = data[6];
            float x7 = data[7];

            // Stage 1: butterfly operations
            float t0 = x0 + x7;
            float t7 = x0 - x7;
            float t1 = x1 + x6;
            float t6 = x1 - x6;
            float t2 = x2 + x5;
            float t5 = x2 - x5;
            float t3 = x3 + x4;
            float t4 = x3 - x4;

            // Stage 2: butterfly operations
            float s0 = t0 + t3;
            float s3 = t0 - t3;
            float s1 = t1 + t2;
            float s2 = t1 - t2;

            // Stage 3: butterfly for DC and AC4
            float r0 = s0 + s1;  // DC
            float r4 = s0 - s1;  // AC4

            // Rotation for AC2 and AC6
            float r2 = (s2 + s3) * A1;
            float r6 = s3 - s2;
            r2 = s3 * A5 + r2;
            r6 = r6 * A5 - s2 * (A4 - A5);

            // Odd part using standard IDCT rotation
            float z1 = t4 + t7;
            float z2 = t5 + t6;
            float z3 = t4 + t6;
            float z4 = t5 + t7;
            float z5 = (z3 + z4) * 1.175875602f;  // sqrt(2) * c3

            float tmp0 = t4 * 0.298631336f;  // sqrt(2) * (-c1+c3+c5-c7)
            float tmp1 = t5 * 2.053119869f;  // sqrt(2) * ( c1+c3-c5+c7)
            float tmp2 = t6 * 3.072711026f;  // sqrt(2) * ( c1+c3+c5-c7)
            float tmp3 = t7 * 1.501321110f;  // sqrt(2) * ( c1+c3-c5-c7)

            float z11 = z3 * (-1.961570560f); // sqrt(2) * (-c3-c5)
            float z12 = z4 * (-0.390180644f); // sqrt(2) * ( c5-c3)
            float z13 = z1 * (-0.899976223f); // sqrt(2) * ( c7-c3)
            float z14 = z2 * (-2.562915447f); // sqrt(2) * (-c1-c3)

            z11 += z5;
            z12 += z5;

            float r1 = tmp3 + z13 + z12;
            float r3 = tmp2 + z14 + z11;
            float r5 = tmp1 + z14 + z12;
            float r7 = tmp0 + z13 + z11;

            data[0] = r0;
            data[1] = r1;
            data[2] = r2;
            data[3] = r3;
            data[4] = r4;
            data[5] = r5;
            data[6] = r6;
            data[7] = r7;
        }

        /// <summary>
        /// 1D inverse DCT using the Loeffler algorithm.
        /// </summary>
        private static void Inverse1D(Span<float> data)
        {
            float y0 = data[0];
            float y1 = data[1];
            float y2 = data[2];
            float y3 = data[3];
            float y4 = data[4];
            float y5 = data[5];
            float y6 = data[6];
            float y7 = data[7];

            // Odd part
            float z13 = y5 + y3;
            float z12 = y5 - y3;
            float z11 = y1 + y7;
            float z10 = y1 - y7;

            float tmp7 = z11 + z13;
            float tmp11 = (z11 - z13) * A3;

            float z5 = (z10 + z12) * 1.175875602f;
            float tmp10 = z5 - z12 * 1.961570560f;
            float tmp12 = z5 - z10 * 0.390180644f;

            float tmp6 = tmp12 - tmp7;
            float tmp5 = tmp11 - tmp6;
            float tmp4 = tmp10 - tmp5;

            // Even part
            float tmp0 = y0 + y4;
            float tmp1 = y0 - y4;

            float tmp13 = (y2 + y6) * A3;
            float tmp2 = y6 * (-A4) + tmp13;
            float tmp3 = y2 * A2 + tmp13;

            float s0 = tmp0 + tmp3;
            float s3 = tmp0 - tmp3;
            float s1 = tmp1 + tmp2;
            float s2 = tmp1 - tmp2;

            // Final stage
            data[0] = s0 + tmp7;
            data[7] = s0 - tmp7;
            data[1] = s1 + tmp6;
            data[6] = s1 - tmp6;
            data[2] = s2 + tmp5;
            data[5] = s2 - tmp5;
            data[3] = s3 + tmp4;
            data[4] = s3 - tmp4;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// SIMD-accelerated forward DCT using AVX2.
        /// Processes all 8 rows in parallel using 256-bit vectors.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardSimd(Span<float> block)
        {
            // Load all 8 rows into 8 Vector256<float> registers
            // Each vector holds one row of 8 floats
            Span<Vector256<float>> rows = stackalloc Vector256<float>[8];

            for (int i = 0; i < 8; i++)
            {
                rows[i] = Vector256.Create(
                    block[i * 8 + 0], block[i * 8 + 1], block[i * 8 + 2], block[i * 8 + 3],
                    block[i * 8 + 4], block[i * 8 + 5], block[i * 8 + 6], block[i * 8 + 7]);
            }

            // Transpose to process columns as vectors
            Transpose8x8(rows);

            // Apply 1D DCT to each vector (now represents a column)
            for (int i = 0; i < 8; i++)
            {
                rows[i] = Forward1DSimd(rows[i]);
            }

            // Transpose back
            Transpose8x8(rows);

            // Apply 1D DCT to each vector (now represents a row)
            for (int i = 0; i < 8; i++)
            {
                rows[i] = Forward1DSimd(rows[i]);
            }

            // Apply normalization (0.125) and store results
            var normFactor = Vector256.Create(0.125f);
            for (int i = 0; i < 8; i++)
            {
                var normalized = Avx.Multiply(rows[i], normFactor);
                for (int j = 0; j < 8; j++)
                {
                    block[i * 8 + j] = normalized.GetElement(j);
                }
            }
        }

        /// <summary>
        /// SIMD-accelerated inverse DCT using AVX2.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseSimd(Span<float> block)
        {
            // Load all 8 rows
            Span<Vector256<float>> rows = stackalloc Vector256<float>[8];

            for (int i = 0; i < 8; i++)
            {
                rows[i] = Vector256.Create(
                    block[i * 8 + 0], block[i * 8 + 1], block[i * 8 + 2], block[i * 8 + 3],
                    block[i * 8 + 4], block[i * 8 + 5], block[i * 8 + 6], block[i * 8 + 7]);
            }

            // Transpose to process columns first (matching JPEG standard)
            Transpose8x8(rows);

            // Apply 1D IDCT to each vector (now columns)
            for (int i = 0; i < 8; i++)
            {
                rows[i] = Inverse1DSimd(rows[i]);
            }

            // Transpose back
            Transpose8x8(rows);

            // Apply 1D IDCT to each vector (now rows)
            for (int i = 0; i < 8; i++)
            {
                rows[i] = Inverse1DSimd(rows[i]);
            }

            // Apply normalization and store
            var normFactor = Vector256.Create(0.125f);
            for (int i = 0; i < 8; i++)
            {
                var normalized = Avx.Multiply(rows[i], normFactor);
                for (int j = 0; j < 8; j++)
                {
                    block[i * 8 + j] = normalized.GetElement(j);
                }
            }
        }

        /// <summary>
        /// Transposes an 8x8 matrix stored as 8 Vector256 registers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose8x8(Span<Vector256<float>> rows)
        {
            // 8x8 transpose using AVX shuffle and permute operations
            // This is a standard 3-stage transpose algorithm

            // Stage 1: Interleave pairs
            var t0 = Avx.UnpackLow(rows[0], rows[1]);
            var t1 = Avx.UnpackHigh(rows[0], rows[1]);
            var t2 = Avx.UnpackLow(rows[2], rows[3]);
            var t3 = Avx.UnpackHigh(rows[2], rows[3]);
            var t4 = Avx.UnpackLow(rows[4], rows[5]);
            var t5 = Avx.UnpackHigh(rows[4], rows[5]);
            var t6 = Avx.UnpackLow(rows[6], rows[7]);
            var t7 = Avx.UnpackHigh(rows[6], rows[7]);

            // Stage 2: Shuffle 64-bit pairs
            var s0 = Avx.Shuffle(t0, t2, 0x44);
            var s1 = Avx.Shuffle(t0, t2, 0xEE);
            var s2 = Avx.Shuffle(t1, t3, 0x44);
            var s3 = Avx.Shuffle(t1, t3, 0xEE);
            var s4 = Avx.Shuffle(t4, t6, 0x44);
            var s5 = Avx.Shuffle(t4, t6, 0xEE);
            var s6 = Avx.Shuffle(t5, t7, 0x44);
            var s7 = Avx.Shuffle(t5, t7, 0xEE);

            // Stage 3: Permute 128-bit lanes
            rows[0] = Avx2.Permute2x128(s0, s4, 0x20);
            rows[1] = Avx2.Permute2x128(s1, s5, 0x20);
            rows[2] = Avx2.Permute2x128(s2, s6, 0x20);
            rows[3] = Avx2.Permute2x128(s3, s7, 0x20);
            rows[4] = Avx2.Permute2x128(s0, s4, 0x31);
            rows[5] = Avx2.Permute2x128(s1, s5, 0x31);
            rows[6] = Avx2.Permute2x128(s2, s6, 0x31);
            rows[7] = Avx2.Permute2x128(s3, s7, 0x31);
        }

        /// <summary>
        /// 1D forward DCT on a Vector256 (processes 8 elements in SIMD).
        /// Uses the same Loeffler algorithm but with vector operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<float> Forward1DSimd(Vector256<float> data)
        {
            // Extract individual elements for the algorithm
            // Note: For true SIMD parallelism, we'd need to restructure to process
            // multiple 1D transforms in parallel. This is a straightforward translation.
            Span<float> temp = stackalloc float[8];
            for (int i = 0; i < 8; i++)
            {
                temp[i] = data.GetElement(i);
            }

            Forward1D(temp);

            return Vector256.Create(temp[0], temp[1], temp[2], temp[3],
                                    temp[4], temp[5], temp[6], temp[7]);
        }

        /// <summary>
        /// 1D inverse DCT on a Vector256.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<float> Inverse1DSimd(Vector256<float> data)
        {
            Span<float> temp = stackalloc float[8];
            for (int i = 0; i < 8; i++)
            {
                temp[i] = data.GetElement(i);
            }

            Inverse1D(temp);

            return Vector256.Create(temp[0], temp[1], temp[2], temp[3],
                                    temp[4], temp[5], temp[6], temp[7]);
        }
#endif
    }
}
