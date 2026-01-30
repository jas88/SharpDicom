using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Wavelet
{
    /// <summary>
    /// Reversible 5/3 wavelet transform using the lifting scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Le Gall 5/3 filter is used for lossless compression in JPEG 2000.
    /// It uses integer arithmetic with floor division, ensuring bit-exact reconstruction.
    /// </para>
    /// <para>
    /// Forward lifting steps (ITU-T T.800 Annex F.3.3):
    /// <code>
    /// Step 1 (update odd/high-pass):
    ///   y[2n+1] = x[2n+1] - floor((x[2n] + x[2n+2]) / 2)
    ///
    /// Step 2 (update even/low-pass):
    ///   y[2n] = x[2n] + floor((y[2n-1] + y[2n+1] + 2) / 4)
    /// </code>
    /// </para>
    /// <para>
    /// After lifting, the even samples (y[0], y[2], ...) form the low-pass subband (L),
    /// and the odd samples (y[1], y[3], ...) form the high-pass subband (H).
    /// These are then interleaved in the output: [L0, L1, ..., Ln, H0, H1, ..., Hn].
    /// </para>
    /// </remarks>
    public static class Dwt53
    {
        /// <summary>
        /// Performs forward 2D DWT at a single level.
        /// </summary>
        /// <param name="data">Image data buffer (row-major order).</param>
        /// <param name="stride">Row stride (usually equals total image width).</param>
        /// <param name="width">Width to process.</param>
        /// <param name="height">Height to process.</param>
        public static void Forward2D(Span<int> data, int stride, int width, int height)
        {
            // Horizontal transform on each row
            for (int y = 0; y < height; y++)
            {
                ForwardHorizontal(data.Slice(y * stride, width));
            }

            // Vertical transform on each column
            ForwardVertical(data, stride, width, height);
        }

        /// <summary>
        /// Performs inverse 2D DWT at a single level.
        /// </summary>
        /// <param name="data">Wavelet coefficients buffer (row-major order).</param>
        /// <param name="stride">Row stride (usually equals total image width).</param>
        /// <param name="width">Width to process.</param>
        /// <param name="height">Height to process.</param>
        public static void Inverse2D(Span<int> data, int stride, int width, int height)
        {
            // Vertical inverse transform on each column
            InverseVertical(data, stride, width, height);

            // Horizontal inverse transform on each row
            for (int y = 0; y < height; y++)
            {
                InverseHorizontal(data.Slice(y * stride, width));
            }
        }

        /// <summary>
        /// Performs forward horizontal 1D transform (in-place).
        /// </summary>
        /// <param name="row">Row data to transform.</param>
        public static void ForwardHorizontal(Span<int> row)
        {
            int n = row.Length;
            if (n <= 1)
            {
                return;
            }

            // Apply lifting steps in-place
            // Step 1: Update odd samples (high-pass)
            // y[2n+1] = x[2n+1] - floor((x[2n] + x[2n+2]) / 2)
            for (int i = 1; i < n; i += 2)
            {
                int left = row[i - 1];
                int right = (i + 1 < n) ? row[i + 1] : row[i - 1]; // Symmetric extension
                row[i] -= (left + right) >> 1;
            }

            // Step 2: Update even samples (low-pass)
            // y[2n] = x[2n] + floor((y[2n-1] + y[2n+1] + 2) / 4)
            for (int i = 0; i < n; i += 2)
            {
                int left = (i > 0) ? row[i - 1] : row[1]; // Symmetric extension
                int right = (i + 1 < n) ? row[i + 1] : left; // Symmetric extension
                row[i] += (left + right + 2) >> 2;
            }

            // Deinterleave: separate low and high-pass samples
            Deinterleave(row);
        }

        /// <summary>
        /// Performs inverse horizontal 1D transform (in-place).
        /// </summary>
        /// <param name="row">Wavelet coefficients to reconstruct.</param>
        public static void InverseHorizontal(Span<int> row)
        {
            int n = row.Length;
            if (n <= 1)
            {
                return;
            }

            // Interleave: merge low and high-pass samples
            Interleave(row);

            // Inverse step 2: Undo even sample update
            // x[2n] = y[2n] - floor((y[2n-1] + y[2n+1] + 2) / 4)
            for (int i = 0; i < n; i += 2)
            {
                int left = (i > 0) ? row[i - 1] : row[1]; // Symmetric extension
                int right = (i + 1 < n) ? row[i + 1] : left; // Symmetric extension
                row[i] -= (left + right + 2) >> 2;
            }

            // Inverse step 1: Undo odd sample update
            // x[2n+1] = y[2n+1] + floor((x[2n] + x[2n+2]) / 2)
            for (int i = 1; i < n; i += 2)
            {
                int left = row[i - 1];
                int right = (i + 1 < n) ? row[i + 1] : row[i - 1]; // Symmetric extension
                row[i] += (left + right) >> 1;
            }
        }

        /// <summary>
        /// Performs forward vertical 1D transform (column-wise, in-place).
        /// </summary>
        /// <param name="data">Image data buffer.</param>
        /// <param name="stride">Row stride.</param>
        /// <param name="width">Number of columns to process.</param>
        /// <param name="height">Number of rows to process.</param>
        public static void ForwardVertical(Span<int> data, int stride, int width, int height)
        {
            if (height <= 1)
            {
                return;
            }

            // Process each column
            for (int x = 0; x < width; x++)
            {
                // Step 1: Update odd rows (high-pass)
                for (int y = 1; y < height; y += 2)
                {
                    int top = data[(y - 1) * stride + x];
                    int bottom = (y + 1 < height) ? data[(y + 1) * stride + x] : top; // Symmetric extension
                    data[y * stride + x] -= (top + bottom) >> 1;
                }

                // Step 2: Update even rows (low-pass)
                for (int y = 0; y < height; y += 2)
                {
                    int top = (y > 0) ? data[(y - 1) * stride + x] : data[stride + x]; // Symmetric extension
                    int bottom = (y + 1 < height) ? data[(y + 1) * stride + x] : top; // Symmetric extension
                    data[y * stride + x] += (top + bottom + 2) >> 2;
                }
            }

            // Deinterleave vertically
            DeinterleaveVertical(data, stride, width, height);
        }

        /// <summary>
        /// Performs inverse vertical 1D transform (column-wise, in-place).
        /// </summary>
        /// <param name="data">Wavelet coefficients buffer.</param>
        /// <param name="stride">Row stride.</param>
        /// <param name="width">Number of columns to process.</param>
        /// <param name="height">Number of rows to process.</param>
        public static void InverseVertical(Span<int> data, int stride, int width, int height)
        {
            if (height <= 1)
            {
                return;
            }

            // Interleave vertically
            InterleaveVertical(data, stride, width, height);

            // Process each column
            for (int x = 0; x < width; x++)
            {
                // Inverse step 2: Undo even row update
                for (int y = 0; y < height; y += 2)
                {
                    int top = (y > 0) ? data[(y - 1) * stride + x] : data[stride + x]; // Symmetric extension
                    int bottom = (y + 1 < height) ? data[(y + 1) * stride + x] : top; // Symmetric extension
                    data[y * stride + x] -= (top + bottom + 2) >> 2;
                }

                // Inverse step 1: Undo odd row update
                for (int y = 1; y < height; y += 2)
                {
                    int top = data[(y - 1) * stride + x];
                    int bottom = (y + 1 < height) ? data[(y + 1) * stride + x] : top; // Symmetric extension
                    data[y * stride + x] += (top + bottom) >> 1;
                }
            }
        }

        /// <summary>
        /// Deinterleaves a row: [x0, x1, x2, x3, ...] -> [x0, x2, ..., x1, x3, ...]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Deinterleave(Span<int> row)
        {
            int n = row.Length;
            int lowCount = (n + 1) / 2;

            // Use temporary buffer for deinterleaving
            Span<int> temp = n <= 256 ? stackalloc int[n] : new int[n];

            // Copy even indices to first half
            for (int i = 0; i < lowCount; i++)
            {
                temp[i] = row[i * 2];
            }

            // Copy odd indices to second half
            int highCount = n / 2;
            for (int i = 0; i < highCount; i++)
            {
                temp[lowCount + i] = row[i * 2 + 1];
            }

            temp.CopyTo(row);
        }

        /// <summary>
        /// Interleaves a row: [L0, L1, ..., H0, H1, ...] -> [L0, H0, L1, H1, ...]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Interleave(Span<int> row)
        {
            int n = row.Length;
            int lowCount = (n + 1) / 2;

            // Use temporary buffer for interleaving
            Span<int> temp = n <= 256 ? stackalloc int[n] : new int[n];

            // Copy from deinterleaved to interleaved positions
            for (int i = 0; i < lowCount; i++)
            {
                temp[i * 2] = row[i];
            }

            int highCount = n / 2;
            for (int i = 0; i < highCount; i++)
            {
                temp[i * 2 + 1] = row[lowCount + i];
            }

            temp.CopyTo(row);
        }

        /// <summary>
        /// Deinterleaves vertically: even rows to top half, odd rows to bottom half.
        /// </summary>
        private static void DeinterleaveVertical(Span<int> data, int stride, int width, int height)
        {
            int lowCount = (height + 1) / 2;
            int highCount = height / 2;

            // Temporary buffer for one column at a time
            Span<int> temp = height <= 256 ? stackalloc int[height] : new int[height];

            for (int x = 0; x < width; x++)
            {
                // Copy even rows to first half
                for (int y = 0; y < lowCount; y++)
                {
                    temp[y] = data[(y * 2) * stride + x];
                }

                // Copy odd rows to second half
                for (int y = 0; y < highCount; y++)
                {
                    temp[lowCount + y] = data[(y * 2 + 1) * stride + x];
                }

                // Copy back
                for (int y = 0; y < height; y++)
                {
                    data[y * stride + x] = temp[y];
                }
            }
        }

        /// <summary>
        /// Interleaves vertically: top half to even rows, bottom half to odd rows.
        /// </summary>
        private static void InterleaveVertical(Span<int> data, int stride, int width, int height)
        {
            int lowCount = (height + 1) / 2;
            int highCount = height / 2;

            // Temporary buffer for one column at a time
            Span<int> temp = height <= 256 ? stackalloc int[height] : new int[height];

            for (int x = 0; x < width; x++)
            {
                // Copy from deinterleaved to interleaved positions
                for (int y = 0; y < lowCount; y++)
                {
                    temp[y * 2] = data[y * stride + x];
                }

                for (int y = 0; y < highCount; y++)
                {
                    temp[y * 2 + 1] = data[(lowCount + y) * stride + x];
                }

                // Copy back
                for (int y = 0; y < height; y++)
                {
                    data[y * stride + x] = temp[y];
                }
            }
        }
    }
}
