using System;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Predictor modes for JPEG-LS encoding per ITU-T T.87 Section 4.2.
    /// </summary>
    internal enum PredictorMode
    {
        /// <summary>
        /// Reserved (use mode 1 instead).
        /// </summary>
        Reserved = 0,

        /// <summary>
        /// Horizontal prediction: a (left neighbor).
        /// </summary>
        Horizontal = 1,

        /// <summary>
        /// Vertical prediction: b (above neighbor).
        /// </summary>
        Vertical = 2,

        /// <summary>
        /// Diagonal prediction: c (above-left neighbor).
        /// </summary>
        Diagonal = 3,

        /// <summary>
        /// Linear prediction: a + b - c.
        /// </summary>
        Linear = 4,

        /// <summary>
        /// Gradient predictor 1: a + (b - c) / 2.
        /// </summary>
        Gradient1 = 5,

        /// <summary>
        /// Gradient predictor 2: b + (a - c) / 2.
        /// </summary>
        Gradient2 = 6,

        /// <summary>
        /// Average predictor: (a + b) / 2.
        /// </summary>
        Average = 7
    }

    /// <summary>
    /// JPEG-LS predictor implementations per ITU-T T.87 Section 4.2.
    /// </summary>
    /// <remarks>
    /// Provides all 8 predictor modes and the Median Edge Detection (MED) algorithm
    /// for automatic mode selection based on local gradients.
    /// </remarks>
    internal static class JpegLsPredictor
    {
        /// <summary>
        /// Computes prediction using the specified mode.
        /// </summary>
        /// <param name="mode">The predictor mode.</param>
        /// <param name="a">Left neighbor sample value.</param>
        /// <param name="b">Above neighbor sample value.</param>
        /// <param name="c">Above-left neighbor sample value.</param>
        /// <returns>The predicted sample value.</returns>
        public static int Predict(PredictorMode mode, int a, int b, int c)
        {
            return mode switch
            {
                PredictorMode.Reserved => a, // Mode 0 reserved, use mode 1
                PredictorMode.Horizontal => a,
                PredictorMode.Vertical => b,
                PredictorMode.Diagonal => c,
                PredictorMode.Linear => a + b - c,
                PredictorMode.Gradient1 => a + ((b - c) >> 1),
                PredictorMode.Gradient2 => b + ((a - c) >> 1),
                PredictorMode.Average => (a + b) >> 1,
                _ => a
            };
        }

        /// <summary>
        /// Median Edge Detection (MED) predictor - automatic mode selection.
        /// </summary>
        /// <remarks>
        /// This is the standard JPEG-LS predictor that selects the prediction mode
        /// based on local gradients. Per ITU-T T.87 Section 4.2:
        /// - If c is maximum of {a,b,c}, predict min(a,b)
        /// - If c is minimum of {a,b,c}, predict max(a,b)
        /// - Otherwise predict a + b - c (linear prediction)
        /// </remarks>
        /// <param name="a">Left neighbor sample value.</param>
        /// <param name="b">Above neighbor sample value.</param>
        /// <param name="c">Above-left neighbor sample value.</param>
        /// <returns>The predicted sample value.</returns>
        public static int MedianEdgeDetection(int a, int b, int c)
        {
            // Edge detection based on gradient analysis
            if (c >= Math.Max(a, b))
            {
                // c is maximum - sharp edge, predict minimum neighbor
                return Math.Min(a, b);
            }
            else if (c <= Math.Min(a, b))
            {
                // c is minimum - sharp edge, predict maximum neighbor
                return Math.Max(a, b);
            }
            else
            {
                // Smooth region - use linear prediction
                return a + b - c;
            }
        }

        /// <summary>
        /// Computes default quantization thresholds per ITU-T T.87, C.2.4.1.1.1.
        /// </summary>
        /// <param name="maxVal">Maximum sample value ((1 &lt;&lt; bitsPerSample) - 1).</param>
        /// <param name="near">The NEAR parameter (0 for lossless).</param>
        /// <param name="t1">Output: first threshold.</param>
        /// <param name="t2">Output: second threshold.</param>
        /// <param name="t3">Output: third threshold.</param>
        public static void ComputeDefaultThresholds(int maxVal, int near, out int t1, out int t2, out int t3)
        {
            const int basicT1 = 3;
            const int basicT2 = 7;
            const int basicT3 = 21;

            if (maxVal >= 128)
            {
                int factor = (Math.Min(maxVal, 4095) + 128) / 256;
                t1 = ClampThreshold(factor * (basicT1 - 2) + 2 + 3 * near, near + 1, maxVal);
                t2 = ClampThreshold(factor * (basicT2 - 3) + 3 + 5 * near, t1, maxVal);
                t3 = ClampThreshold(factor * (basicT3 - 4) + 4 + 7 * near, t2, maxVal);
            }
            else
            {
                int factor = 256 / (maxVal + 1);
                t1 = ClampThreshold(Math.Max(2, basicT1 / factor + 3 * near), near + 1, maxVal);
                t2 = ClampThreshold(Math.Max(3, basicT2 / factor + 5 * near), t1, maxVal);
                t3 = ClampThreshold(Math.Max(4, basicT3 / factor + 7 * near), t2, maxVal);
            }
        }

        private static int ClampThreshold(int value, int min, int max)
        {
            if (value > max || value < min) return min;
            return value;
        }

        /// <summary>
        /// Quantizes a gradient value for context selection per ITU-T T.87 Section 4.3.
        /// </summary>
        /// <param name="gradient">The gradient value (difference between neighboring samples).</param>
        /// <param name="near">The NEAR parameter (0 for lossless, >0 for near-lossless).</param>
        /// <param name="t1">First quantization threshold.</param>
        /// <param name="t2">Second quantization threshold.</param>
        /// <param name="t3">Third quantization threshold.</param>
        /// <returns>Quantized gradient in range [-4, 4].</returns>
        public static int QuantizeGradient(int gradient, int near, int t1, int t2, int t3)
        {
            // Map gradient to quantization region
            if (gradient <= -t3) return -4;
            if (gradient <= -t2) return -3;
            if (gradient <= -t1) return -2;
            if (gradient < -near) return -1;
            if (gradient <= near) return 0;
            if (gradient < t1) return 1;
            if (gradient < t2) return 2;
            if (gradient < t3) return 3;
            return 4;
        }

        /// <summary>
        /// Computes the context index from quantized gradients.
        /// </summary>
        /// <remarks>
        /// Per ITU-T T.87 Section 4.3, the context index is computed from three
        /// quantized gradients (q1, q2, q3) after sign normalization.
        /// Index = (q1 * 9 + q2) * 9 + q3 + 364
        /// Valid range is [0, 364] for the 365-element context array.
        /// </remarks>
        /// <param name="q1">First quantized gradient (d - b).</param>
        /// <param name="q2">Second quantized gradient (b - c).</param>
        /// <param name="q3">Third quantized gradient (c - a).</param>
        /// <returns>Context index in range [0, 364].</returns>
        public static int ComputeContextIndex(int q1, int q2, int q3)
        {
            // Formula from ITU-T T.87 Section 4.3
            // After sign normalization, q1, q2, q3 are in range [0, 4]
            int index = (q1 * 9 + q2) * 9 + q3;

            // Clamp to valid range as defensive measure
            return Clamp(index, 0, 364);
        }

        /// <summary>
        /// Normalizes quantized gradients for context selection.
        /// </summary>
        /// <remarks>
        /// Per ITU-T T.87 Section 4.3, if the first non-zero gradient is negative,
        /// all gradients are negated to normalize the sign.
        /// </remarks>
        /// <param name="q1">First quantized gradient.</param>
        /// <param name="q2">Second quantized gradient.</param>
        /// <param name="q3">Third quantized gradient.</param>
        /// <returns>True if sign was flipped, false otherwise.</returns>
        public static bool NormalizeGradients(ref int q1, ref int q2, ref int q3)
        {
            // Sign normalization: negate all if first non-zero is negative
            if (q1 < 0 || (q1 == 0 && q2 < 0) || (q1 == 0 && q2 == 0 && q3 < 0))
            {
                q1 = -q1;
                q2 = -q2;
                q3 = -q3;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clamps a value to the specified range.
        /// </summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
