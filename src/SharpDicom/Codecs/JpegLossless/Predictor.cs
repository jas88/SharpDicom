using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.JpegLossless
{
    /// <summary>
    /// JPEG Lossless DPCM predictors per ITU-T.81 Table H.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JPEG Lossless compression uses Differential Pulse Code Modulation (DPCM)
    /// with seven predictor modes. The predictor computes an estimate of the
    /// current sample based on neighboring samples, and only the difference
    /// (prediction error) is encoded.
    /// </para>
    /// <para>
    /// The neighboring samples are:
    /// <code>
    ///   C | B
    ///  ---+---
    ///   A | X
    /// </code>
    /// Where X is the current sample, A is to the left, B is above, and C is
    /// above-left diagonal.
    /// </para>
    /// </remarks>
    public static class Predictor
    {
        /// <summary>
        /// Computes the predicted value using the specified selection value.
        /// </summary>
        /// <param name="selectionValue">Predictor selection (0-7).</param>
        /// <param name="a">Sample immediately to the left (Ra).</param>
        /// <param name="b">Sample immediately above (Rb).</param>
        /// <param name="c">Sample above and to the left (Rc).</param>
        /// <returns>The predicted value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="selectionValue"/> is not in range 0-7.
        /// </exception>
        /// <remarks>
        /// <para>
        /// DICOM Transfer Syntax 1.2.840.10008.1.2.4.70 MUST use Selection Value 1.
        /// </para>
        /// <para>
        /// Predictor formulas per ITU-T.81 Table H.1:
        /// <list type="table">
        /// <listheader>
        /// <term>Selection</term>
        /// <description>Prediction</description>
        /// </listheader>
        /// <item><term>0</term><description>No prediction (hierarchical only)</description></item>
        /// <item><term>1</term><description>Ra (horizontal)</description></item>
        /// <item><term>2</term><description>Rb (vertical)</description></item>
        /// <item><term>3</term><description>Rc (diagonal)</description></item>
        /// <item><term>4</term><description>Ra + Rb - Rc</description></item>
        /// <item><term>5</term><description>Ra + (Rb - Rc) / 2</description></item>
        /// <item><term>6</term><description>Rb + (Ra - Rc) / 2</description></item>
        /// <item><term>7</term><description>(Ra + Rb) / 2</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Predict(int selectionValue, int a, int b, int c)
        {
            return selectionValue switch
            {
                0 => 0,                    // No prediction (hierarchical only)
                1 => a,                    // Ra (horizontal)
                2 => b,                    // Rb (vertical)
                3 => c,                    // Rc (diagonal)
                4 => a + b - c,           // Ra + Rb - Rc
                5 => a + ((b - c) >> 1),  // Ra + (Rb - Rc) / 2
                6 => b + ((a - c) >> 1),  // Rb + (Ra - Rc) / 2
                7 => (a + b) >> 1,        // (Ra + Rb) / 2
                _ => throw new ArgumentOutOfRangeException(nameof(selectionValue),
                    $"Selection value must be 0-7, got {selectionValue}")
            };
        }

        /// <summary>
        /// Gets the neighboring samples for prediction.
        /// </summary>
        /// <param name="data">Image data array.</param>
        /// <param name="x">Current column (0-based).</param>
        /// <param name="y">Current row (0-based).</param>
        /// <param name="width">Image width in samples.</param>
        /// <param name="defaultValue">Default value for boundary conditions (typically 2^(P-1)).</param>
        /// <param name="a">Output: left neighbor (Ra).</param>
        /// <param name="b">Output: above neighbor (Rb).</param>
        /// <param name="c">Output: above-left neighbor (Rc).</param>
        /// <remarks>
        /// <para>
        /// Per ITU-T.81 Section H.1.1, boundary conditions are:
        /// </para>
        /// <list type="bullet">
        /// <item>First row: Ra = left neighbor; Rb and Rc = default value (except first sample uses Ra = default)</item>
        /// <item>First column: Ra = Rc = Rb = above neighbor (prediction uses vertical predictor behavior)</item>
        /// <item>First sample (0,0): all neighbors = default value, prediction = 2^(P-Pt-1)</item>
        /// </list>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetNeighbors(
            ReadOnlySpan<int> data,
            int x,
            int y,
            int width,
            int defaultValue,
            out int a,
            out int b,
            out int c)
        {
            // Handle boundary conditions per ITU-T.81 Section H.1.1
            if (x == 0 && y == 0)
            {
                // First sample: use default prediction value for all neighbors
                a = defaultValue;
                b = defaultValue;
                c = defaultValue;
            }
            else if (y == 0)
            {
                // First row: use left neighbor for prediction, above is default
                a = data[x - 1];
                b = defaultValue;
                c = defaultValue;
            }
            else if (x == 0)
            {
                // First column: use above neighbor for all (effective vertical prediction)
                int aboveValue = data[(y - 1) * width];
                a = aboveValue;
                b = aboveValue;
                c = aboveValue;
            }
            else
            {
                // Interior: use actual neighbors
                a = data[y * width + x - 1];
                b = data[(y - 1) * width + x];
                c = data[(y - 1) * width + x - 1];
            }
        }

        /// <summary>
        /// Gets the default prediction value for the first sample.
        /// </summary>
        /// <param name="precision">Sample precision P in bits (2-16).</param>
        /// <param name="pointTransform">Point transform Pt (0 to precision-1).</param>
        /// <returns>The default value: 2^(P-Pt-1).</returns>
        /// <remarks>
        /// The default value represents the midpoint of the valid sample range
        /// after accounting for point transform. For typical DICOM images with
        /// no point transform (Pt=0), this equals 2^(P-1), i.e., 128 for 8-bit
        /// or 32768 for 16-bit images.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetDefaultValue(int precision, int pointTransform = 0)
        {
            return 1 << (precision - pointTransform - 1);
        }
    }
}
