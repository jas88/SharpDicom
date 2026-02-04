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
        /// Per ITU-T T.87 Section 4.5, k is chosen such that 2^k ≈ A/N.
        /// This gives optimal Golomb-Rice coding efficiency for the current context.
        /// </remarks>
        /// <param name="limit">Maximum k value (typically 32).</param>
        /// <returns>The Golomb-Rice parameter k.</returns>
        public int ComputeK(int limit)
        {
            int k = 0;
            int nTimesA = N * A;

            // Find smallest k such that N * 2^k >= N * A
            while ((N << k) < nTimesA && k < limit)
            {
                k++;
            }

            return k;
        }

        /// <summary>
        /// Updates the context state with a new prediction error.
        /// </summary>
        /// <remarks>
        /// Per ITU-T T.87 Section 4.3, the context state is updated after each sample:
        /// - A accumulates absolute error
        /// - B accumulates signed error
        /// - N increments
        /// - Periodic reset when N reaches threshold
        /// - Bias correction adjusts C based on accumulated bias
        /// </remarks>
        /// <param name="error">The prediction error.</param>
        /// <param name="reset">The reset threshold (typically 64).</param>
        /// <param name="range">The sample value range.</param>
        public void Update(int error, int reset, int range)
        {
            // Accumulate error statistics
            int absError = error < 0 ? -error : error;
            A += absError;
            B += error;
            N++;

            // Periodic reset to prevent overflow
            if (N == reset)
            {
                // Halve all counters
                A = (A + 1) >> 1;
                B = (B + 1) >> 1;
                N = (N + 1) >> 1;
            }

            // Bias correction per ITU-T T.87 Section 4.3
            // Adjusts C to correct for systematic bias in prediction errors
            if (B <= -N)
            {
                // Negative bias detected
                B = System.Math.Max(B + N, 1 - N);
                if (C > -128) C--;
            }
            else if (B > 0)
            {
                // Positive bias detected
                B = System.Math.Min(B - N, 0);
                if (C < 127) C++;
            }
        }

        /// <summary>
        /// Gets the bias-corrected prediction value.
        /// </summary>
        /// <remarks>
        /// The bias correction (C + N/2) / N is added to the raw prediction
        /// to compensate for systematic errors.
        /// </remarks>
        /// <returns>The bias correction value.</returns>
        public int GetBiasCorrection()
        {
            return (C + (N >> 1)) / N;
        }
    }
}
