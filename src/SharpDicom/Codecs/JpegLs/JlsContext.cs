namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Context state for JPEG-LS encoding/decoding per ITU-T T.87 Section 4.3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JPEG-LS uses 365 context states (indexed 0-364) for adaptive prediction error modeling.
    /// Each context maintains:
    /// - A: accumulated absolute prediction error
    /// - B: accumulated signed prediction error (bias)
    /// - C: bias correction factor
    /// - N: sample count
    /// </para>
    /// <para>
    /// The context state adapts during encoding/decoding to track local image statistics
    /// and compute optimal Golomb-Rice coding parameters.
    /// </para>
    /// </remarks>
    internal struct JlsContext
    {
        /// <summary>
        /// Accumulated absolute prediction error.
        /// </summary>
        public int A;

        /// <summary>
        /// Accumulated signed prediction error (bias).
        /// </summary>
        public int B;

        /// <summary>
        /// Bias correction factor (range [-128, 127]).
        /// </summary>
        public int C;

        /// <summary>
        /// Sample count (number of samples processed in this context).
        /// </summary>
        public int N;

        /// <summary>
        /// Initializes the context state for a given value range.
        /// </summary>
        /// <param name="range">The sample value range (maxVal + 1).</param>
        public void Initialize(int range)
        {
            // Initial A value per ITU-T T.87 Section 4.3
            A = System.Math.Max(2, (range + 32) / 64);
            B = 0;
            C = 0;
            N = 1;
        }

        /// <summary>
        /// Computes the Golomb-Rice parameter k for entropy coding.
        /// </summary>
        /// <remarks>
        /// Per ITU-T T.87 Section A.5.1, k is the smallest value such that
        /// N * 2^k >= A, i.e. 2^k >= A/N.
        /// </remarks>
        /// <returns>The Golomb-Rice parameter k.</returns>
        public int ComputeK()
        {
            int k = 0;

            // Find smallest k such that N * 2^k >= A
            while ((N << k) < A && k < 16)
            {
                k++;
            }

            return k;
        }

        /// <summary>
        /// Updates the context state with a new prediction error.
        /// </summary>
        /// <remarks>
        /// Per ITU-T T.87 code segments A.12 and A.13.
        /// Order: accumulate A and B, check reset, increment N, then bias correction.
        /// </remarks>
        /// <param name="error">The prediction error.</param>
        /// <param name="near">The NEAR parameter (0 for lossless).</param>
        /// <param name="reset">The reset threshold (typically 64).</param>
        public void Update(int error, int near, int reset)
        {
            // Accumulate error statistics (ITU-T T.87, A.12)
            int absError = error < 0 ? -error : error;
            A += absError;
            B += error * (2 * near + 1);

            // Periodic reset to prevent overflow (check BEFORE increment per CharLS/T.87)
            if (N == reset)
            {
                A >>= 1;
                B >>= 1;
                N >>= 1;
            }

            N++;

            // Bias correction per ITU-T T.87 code segment A.13
            if (B + N <= 0)
            {
                B += N;
                if (B <= -N)
                {
                    B = -N + 1;
                }
                if (C > -128) C--;
            }
            else if (B > 0)
            {
                B -= N;
                if (B > 0)
                {
                    B = 0;
                }
                if (C < 127) C++;
            }
        }

        /// <summary>
        /// Gets the error correction value for error mapping per ITU-T T.87, A.5.2.
        /// </summary>
        /// <remarks>
        /// When k=0, returns bit_wise_sign(2*B + N - 1), which is 0 or -1.
        /// This is XORed with the error value before mapping to improve coding efficiency.
        /// </remarks>
        /// <param name="k">The Golomb-Rice parameter.</param>
        /// <returns>0 when k != 0; 0 or -1 when k == 0.</returns>
        public int GetErrorCorrection(int k)
        {
            if (k != 0)
                return 0;
            return (2 * B + N - 1) >> 31; // arithmetic right shift gives 0 or -1
        }
    }
}
