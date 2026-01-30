using System;

namespace SharpDicom.Codecs
{
    /// <summary>
    /// Color space conversion utilities for DICOM image codecs.
    /// </summary>
    /// <remarks>
    /// This class provides conversions between RGB and YCbCr color spaces as used by JPEG,
    /// and RCT/ICT color transforms as used by JPEG 2000.
    ///
    /// The YCbCr conversion uses ITU-R BT.601 coefficients as specified by DICOM PS3.3 C.7.6.3.1.2.
    /// </remarks>
    public static class ColorConversion
    {
        // ITU-R BT.601 coefficients for RGB to YCbCr conversion
        // These are the standard coefficients used by JPEG and DICOM
        // Y  =  0.299R + 0.587G + 0.114B
        // Cb = -0.169R - 0.331G + 0.500B + 128
        // Cr =  0.500R - 0.419G - 0.081B + 128
        private const float Kr = 0.299f;
        private const float Kg = 0.587f;
        private const float Kb = 0.114f;

        // Pre-computed coefficients for inverse conversion (YCbCr to RGB)
        // R = Y + 1.402(Cr - 128)
        // G = Y - 0.344136(Cb - 128) - 0.714136(Cr - 128)
        // B = Y + 1.772(Cb - 128)
        private const float CrToR = 1.402f;        // 2 * (1 - Kr)
        private const float CbToG = 0.344136f;     // 2 * Kb * (1 - Kb) / Kg
        private const float CrToG = 0.714136f;     // 2 * Kr * (1 - Kr) / Kg
        private const float CbToB = 1.772f;        // 2 * (1 - Kb)

        /// <summary>
        /// Converts YCbCr pixel data to RGB using ITU-R BT.601 coefficients.
        /// </summary>
        /// <param name="y">The Y (luminance) component values.</param>
        /// <param name="cb">The Cb (blue-difference chroma) component values.</param>
        /// <param name="cr">The Cr (red-difference chroma) component values.</param>
        /// <param name="rgb">The output buffer for interleaved RGB values (must be 3x the input length).</param>
        /// <remarks>
        /// This conversion is used when decoding JPEG images with JPEG/JFIF color space.
        /// The Cb and Cr values have a 128 offset (level shift) which is removed during conversion.
        /// Output values are clamped to the valid range [0, 255].
        /// </remarks>
        public static void YCbCrToRgb(
            ReadOnlySpan<byte> y,
            ReadOnlySpan<byte> cb,
            ReadOnlySpan<byte> cr,
            Span<byte> rgb)
        {
            if (y.Length != cb.Length || y.Length != cr.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            if (rgb.Length < y.Length * 3)
            {
                throw new ArgumentException("Output RGB buffer must be at least 3x the input length.", nameof(rgb));
            }

            for (int i = 0; i < y.Length; i++)
            {
                int yVal = y[i];
                int cbVal = cb[i] - 128;
                int crVal = cr[i] - 128;

                // ITU-R BT.601 inverse conversion
                int r = (int)(yVal + CrToR * crVal + 0.5f);
                int g = (int)(yVal - CbToG * cbVal - CrToG * crVal + 0.5f);
                int b = (int)(yVal + CbToB * cbVal + 0.5f);

                // Clamp to [0, 255]
                rgb[i * 3 + 0] = ClampToByte(r);
                rgb[i * 3 + 1] = ClampToByte(g);
                rgb[i * 3 + 2] = ClampToByte(b);
            }
        }

        /// <summary>
        /// Converts interleaved RGB pixel data to separate YCbCr components.
        /// </summary>
        /// <param name="rgb">The interleaved RGB input values.</param>
        /// <param name="y">The output Y (luminance) component buffer.</param>
        /// <param name="cb">The output Cb (blue-difference chroma) component buffer.</param>
        /// <param name="cr">The output Cr (red-difference chroma) component buffer.</param>
        /// <remarks>
        /// This conversion is used when encoding JPEG images.
        /// The Cb and Cr values have a 128 offset (level shift) added.
        /// </remarks>
        public static void RgbToYCbCr(
            ReadOnlySpan<byte> rgb,
            Span<byte> y,
            Span<byte> cb,
            Span<byte> cr)
        {
            int pixelCount = rgb.Length / 3;

            if (y.Length < pixelCount || cb.Length < pixelCount || cr.Length < pixelCount)
            {
                throw new ArgumentException("Output component buffers must be large enough for all pixels.");
            }

            for (int i = 0; i < pixelCount; i++)
            {
                int r = rgb[i * 3 + 0];
                int g = rgb[i * 3 + 1];
                int b = rgb[i * 3 + 2];

                // ITU-R BT.601 forward conversion
                int yVal = (int)(Kr * r + Kg * g + Kb * b + 0.5f);
                int cbVal = (int)(128 + 0.5f * (b - yVal) / (1 - Kb) + 0.5f);
                int crVal = (int)(128 + 0.5f * (r - yVal) / (1 - Kr) + 0.5f);

                // Clamp to [0, 255]
                y[i] = ClampToByte(yVal);
                cb[i] = ClampToByte(cbVal);
                cr[i] = ClampToByte(crVal);
            }
        }

        /// <summary>
        /// Applies the JPEG 2000 Reversible Color Transform (RCT) forward transform.
        /// </summary>
        /// <param name="r">The red component values (modified in-place to Y).</param>
        /// <param name="g">The green component values (modified in-place to Cb).</param>
        /// <param name="b">The blue component values (modified in-place to Cr).</param>
        /// <remarks>
        /// RCT is the lossless color transform used in JPEG 2000:
        /// Y  = floor((R + 2G + B) / 4)
        /// Cb = B - G
        /// Cr = R - G
        ///
        /// This transform is perfectly reversible with integer arithmetic.
        /// Input buffers are modified in-place to avoid additional allocations.
        /// </remarks>
        public static void ForwardRct(Span<int> r, Span<int> g, Span<int> b)
        {
            if (r.Length != g.Length || r.Length != b.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            for (int i = 0; i < r.Length; i++)
            {
                int rVal = r[i];
                int gVal = g[i];
                int bVal = b[i];

                int y = (rVal + 2 * gVal + bVal) >> 2;  // Floor division by 4
                int cb = bVal - gVal;
                int cr = rVal - gVal;

                r[i] = y;   // Y stored in R buffer
                g[i] = cb;  // Cb stored in G buffer
                b[i] = cr;  // Cr stored in B buffer
            }
        }

        /// <summary>
        /// Applies the JPEG 2000 Reversible Color Transform (RCT) inverse transform.
        /// </summary>
        /// <param name="y">The Y component values (modified in-place to R).</param>
        /// <param name="cb">The Cb component values (modified in-place to G).</param>
        /// <param name="cr">The Cr component values (modified in-place to B).</param>
        /// <remarks>
        /// RCT inverse transform:
        /// G = Y - floor((Cb + Cr) / 4)
        /// R = Cr + G
        /// B = Cb + G
        ///
        /// This transform perfectly recovers the original RGB values.
        /// Input buffers are modified in-place to avoid additional allocations.
        /// </remarks>
        public static void InverseRct(Span<int> y, Span<int> cb, Span<int> cr)
        {
            if (y.Length != cb.Length || y.Length != cr.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            for (int i = 0; i < y.Length; i++)
            {
                int yVal = y[i];
                int cbVal = cb[i];
                int crVal = cr[i];

                int g = yVal - ((cbVal + crVal) >> 2);
                int r = crVal + g;
                int b = cbVal + g;

                y[i] = r;   // R stored in Y buffer
                cb[i] = g;  // G stored in Cb buffer
                cr[i] = b;  // B stored in Cr buffer
            }
        }

        /// <summary>
        /// Applies the JPEG 2000 Irreversible Color Transform (ICT) forward transform.
        /// </summary>
        /// <param name="r">The red component values (modified in-place to Y).</param>
        /// <param name="g">The green component values (modified in-place to Cb).</param>
        /// <param name="b">The blue component values (modified in-place to Cr).</param>
        /// <remarks>
        /// ICT is the lossy color transform used in JPEG 2000 (identical to ITU-R BT.601):
        /// Y  =  0.299R + 0.587G + 0.114B
        /// Cb = -0.16875R - 0.33126G + 0.5B
        /// Cr =  0.5R - 0.41869G - 0.08131B
        ///
        /// Unlike RCT, ICT uses floating-point arithmetic and is not perfectly reversible.
        /// Input buffers are modified in-place to avoid additional allocations.
        /// </remarks>
        public static void ForwardIct(Span<float> r, Span<float> g, Span<float> b)
        {
            if (r.Length != g.Length || r.Length != b.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            for (int i = 0; i < r.Length; i++)
            {
                float rVal = r[i];
                float gVal = g[i];
                float bVal = b[i];

                float y = Kr * rVal + Kg * gVal + Kb * bVal;
                float cb = -0.16875f * rVal - 0.33126f * gVal + 0.5f * bVal;
                float cr = 0.5f * rVal - 0.41869f * gVal - 0.08131f * bVal;

                r[i] = y;
                g[i] = cb;
                b[i] = cr;
            }
        }

        /// <summary>
        /// Applies the JPEG 2000 Irreversible Color Transform (ICT) inverse transform.
        /// </summary>
        /// <param name="y">The Y component values (modified in-place to R).</param>
        /// <param name="cb">The Cb component values (modified in-place to G).</param>
        /// <param name="cr">The Cr component values (modified in-place to B).</param>
        /// <remarks>
        /// ICT inverse transform:
        /// R = Y + 1.402Cr
        /// G = Y - 0.34413Cb - 0.71414Cr
        /// B = Y + 1.772Cb
        ///
        /// Input buffers are modified in-place to avoid additional allocations.
        /// </remarks>
        public static void InverseIct(Span<float> y, Span<float> cb, Span<float> cr)
        {
            if (y.Length != cb.Length || y.Length != cr.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            for (int i = 0; i < y.Length; i++)
            {
                float yVal = y[i];
                float cbVal = cb[i];
                float crVal = cr[i];

                float r = yVal + 1.402f * crVal;
                float g = yVal - 0.34413f * cbVal - 0.71414f * crVal;
                float b = yVal + 1.772f * cbVal;

                y[i] = r;
                cb[i] = g;
                cr[i] = b;
            }
        }

        /// <summary>
        /// Converts YCbCr pixel data to RGB for 16-bit samples.
        /// </summary>
        /// <param name="y">The Y (luminance) component values.</param>
        /// <param name="cb">The Cb (blue-difference chroma) component values.</param>
        /// <param name="cr">The Cr (red-difference chroma) component values.</param>
        /// <param name="rgb">The output buffer for interleaved RGB values.</param>
        /// <param name="maxValue">The maximum sample value (e.g., 4095 for 12-bit, 65535 for 16-bit).</param>
        /// <remarks>
        /// This overload handles high bit-depth samples common in medical imaging.
        /// The level shift for Cb/Cr is half the maximum value (e.g., 2048 for 12-bit).
        /// </remarks>
        public static void YCbCrToRgb(
            ReadOnlySpan<ushort> y,
            ReadOnlySpan<ushort> cb,
            ReadOnlySpan<ushort> cr,
            Span<ushort> rgb,
            int maxValue)
        {
            if (y.Length != cb.Length || y.Length != cr.Length)
            {
                throw new ArgumentException("Input component arrays must have the same length.");
            }

            if (rgb.Length < y.Length * 3)
            {
                throw new ArgumentException("Output RGB buffer must be at least 3x the input length.", nameof(rgb));
            }

            int halfRange = maxValue / 2;

            for (int i = 0; i < y.Length; i++)
            {
                float yVal = y[i];
                float cbVal = cb[i] - halfRange;
                float crVal = cr[i] - halfRange;

                int r = (int)(yVal + CrToR * crVal + 0.5f);
                int g = (int)(yVal - CbToG * cbVal - CrToG * crVal + 0.5f);
                int b = (int)(yVal + CbToB * cbVal + 0.5f);

                rgb[i * 3 + 0] = ClampToRange(r, maxValue);
                rgb[i * 3 + 1] = ClampToRange(g, maxValue);
                rgb[i * 3 + 2] = ClampToRange(b, maxValue);
            }
        }

        private static byte ClampToByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private static ushort ClampToRange(int value, int maxValue)
        {
            if (value < 0) return 0;
            if (value > maxValue) return (ushort)maxValue;
            return (ushort)value;
        }
    }
}
