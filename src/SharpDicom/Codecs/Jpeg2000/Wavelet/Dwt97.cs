using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Wavelet
{
    /// <summary>
    /// Irreversible 9/7 wavelet transform using the lifting scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Daubechies 9/7 biorthogonal filter is used for lossy compression in JPEG 2000.
    /// It uses floating-point arithmetic, so reconstruction is not bit-exact.
    /// </para>
    /// <para>
    /// Forward lifting steps (ITU-T T.800 Annex F.3.4):
    /// <code>
    /// Step 1: y[2n+1] += alpha * (y[2n] + y[2n+2])
    /// Step 2: y[2n]   += beta  * (y[2n-1] + y[2n+1])
    /// Step 3: y[2n+1] += gamma * (y[2n] + y[2n+2])
    /// Step 4: y[2n]   += delta * (y[2n-1] + y[2n+1])
    /// Finally: scale even by K, odd by 1/K
    /// </code>
    /// </para>
    /// <para>
    /// The coefficients are defined in ITU-T T.800 Table F.4.
    /// </para>
    /// </remarks>
    public static class Dwt97
    {
        // Lifting coefficients from ITU-T T.800 Table F.4
        private const float Alpha = -1.586134342059924f;
        private const float Beta = -0.052980118572961f;
        private const float Gamma = 0.882911075530934f;
        private const float Delta = 0.443506852043971f;
        private const float K = 1.230174104914001f;
        private const float InvK = 1.0f / K; // Approximately 0.812893066115962

        /// <summary>
        /// Performs forward 2D DWT at a single level using integer data.
        /// </summary>
        /// <param name="data">Image data buffer (row-major order).</param>
        /// <param name="stride">Row stride (usually equals total image width).</param>
        /// <param name="width">Width to process.</param>
        /// <param name="height">Height to process.</param>
        /// <remarks>
        /// <para>
        /// The integer data is converted to float internally for processing,
        /// then converted back to integer. This matches the JPEG 2000 workflow
        /// where 9/7 transform results in integer coefficients after quantization.
        /// </para>
        /// <para>
        /// Note: This implementation rounds horizontal transform results to integers
        /// before the vertical transform. A higher-precision implementation would
        /// maintain float values throughout both transforms, only rounding at the end.
        /// The current approach sacrifices some precision for memory efficiency.
        /// </para>
        /// </remarks>
        public static void Forward2D(Span<int> data, int stride, int width, int height)
        {
            // Allocate float buffer for processing
            int size = width > height ? width : height;
            Span<float> tempFloat = size <= 256 ? stackalloc float[size] : new float[size];

            // Horizontal transform on each row
            for (int y = 0; y < height; y++)
            {
                // Copy to float
                Span<int> row = data.Slice(y * stride, width);
                for (int x = 0; x < width; x++)
                {
                    tempFloat[x] = row[x];
                }

                // Transform
                ForwardHorizontal(tempFloat.Slice(0, width));

                // Copy back to int
                for (int x = 0; x < width; x++)
                {
                    row[x] = (int)Math.Round(tempFloat[x]);
                }
            }

            // Vertical transform on each column
            ForwardVertical(data, stride, width, height);
        }

        /// <summary>
        /// Performs inverse 2D DWT at a single level using integer data.
        /// </summary>
        /// <param name="data">Wavelet coefficients buffer (row-major order).</param>
        /// <param name="stride">Row stride (usually equals total image width).</param>
        /// <param name="width">Width to process.</param>
        /// <param name="height">Height to process.</param>
        public static void Inverse2D(Span<int> data, int stride, int width, int height)
        {
            // Allocate float buffer for processing
            int size = width > height ? width : height;
            Span<float> tempFloat = size <= 256 ? stackalloc float[size] : new float[size];

            // Vertical inverse transform
            InverseVertical(data, stride, width, height);

            // Horizontal inverse transform on each row
            for (int y = 0; y < height; y++)
            {
                // Copy to float
                Span<int> row = data.Slice(y * stride, width);
                for (int x = 0; x < width; x++)
                {
                    tempFloat[x] = row[x];
                }

                // Transform
                InverseHorizontal(tempFloat.Slice(0, width));

                // Copy back to int
                for (int x = 0; x < width; x++)
                {
                    row[x] = (int)Math.Round(tempFloat[x]);
                }
            }
        }

        /// <summary>
        /// Performs forward horizontal 1D transform (in-place).
        /// </summary>
        /// <param name="row">Row data to transform.</param>
        public static void ForwardHorizontal(Span<float> row)
        {
            int n = row.Length;
            if (n <= 1)
            {
                return;
            }

            // Step 1: y[2n+1] += alpha * (y[2n] + y[2n+2])
            for (int i = 1; i < n; i += 2)
            {
                float left = row[i - 1];
                float right = (i + 1 < n) ? row[i + 1] : row[i - 1]; // Symmetric extension
                row[i] += Alpha * (left + right);
            }

            // Step 2: y[2n] += beta * (y[2n-1] + y[2n+1])
            for (int i = 0; i < n; i += 2)
            {
                float left = (i > 0) ? row[i - 1] : row[1]; // Symmetric extension
                float right = (i + 1 < n) ? row[i + 1] : left; // Symmetric extension
                row[i] += Beta * (left + right);
            }

            // Step 3: y[2n+1] += gamma * (y[2n] + y[2n+2])
            for (int i = 1; i < n; i += 2)
            {
                float left = row[i - 1];
                float right = (i + 1 < n) ? row[i + 1] : row[i - 1]; // Symmetric extension
                row[i] += Gamma * (left + right);
            }

            // Step 4: y[2n] += delta * (y[2n-1] + y[2n+1])
            for (int i = 0; i < n; i += 2)
            {
                float left = (i > 0) ? row[i - 1] : row[1]; // Symmetric extension
                float right = (i + 1 < n) ? row[i + 1] : left; // Symmetric extension
                row[i] += Delta * (left + right);
            }

            // Scale: even by K, odd by 1/K
            for (int i = 0; i < n; i += 2)
            {
                row[i] *= K;
            }
            for (int i = 1; i < n; i += 2)
            {
                row[i] *= InvK;
            }

            // Deinterleave
            Deinterleave(row);
        }

        /// <summary>
        /// Performs inverse horizontal 1D transform (in-place).
        /// </summary>
        /// <param name="row">Wavelet coefficients to reconstruct.</param>
        public static void InverseHorizontal(Span<float> row)
        {
            int n = row.Length;
            if (n <= 1)
            {
                return;
            }

            // Interleave
            Interleave(row);

            // Inverse scale: even by 1/K, odd by K
            for (int i = 0; i < n; i += 2)
            {
                row[i] *= InvK;
            }
            for (int i = 1; i < n; i += 2)
            {
                row[i] *= K;
            }

            // Inverse step 4: y[2n] -= delta * (y[2n-1] + y[2n+1])
            for (int i = 0; i < n; i += 2)
            {
                float left = (i > 0) ? row[i - 1] : row[1];
                float right = (i + 1 < n) ? row[i + 1] : left;
                row[i] -= Delta * (left + right);
            }

            // Inverse step 3: y[2n+1] -= gamma * (y[2n] + y[2n+2])
            for (int i = 1; i < n; i += 2)
            {
                float left = row[i - 1];
                float right = (i + 1 < n) ? row[i + 1] : row[i - 1];
                row[i] -= Gamma * (left + right);
            }

            // Inverse step 2: y[2n] -= beta * (y[2n-1] + y[2n+1])
            for (int i = 0; i < n; i += 2)
            {
                float left = (i > 0) ? row[i - 1] : row[1];
                float right = (i + 1 < n) ? row[i + 1] : left;
                row[i] -= Beta * (left + right);
            }

            // Inverse step 1: y[2n+1] -= alpha * (y[2n] + y[2n+2])
            for (int i = 1; i < n; i += 2)
            {
                float left = row[i - 1];
                float right = (i + 1 < n) ? row[i + 1] : row[i - 1];
                row[i] -= Alpha * (left + right);
            }
        }

        /// <summary>
        /// Performs forward vertical 1D transform (column-wise).
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

            // Allocate temp buffer for float conversion
            Span<float> temp = height <= 256 ? stackalloc float[height] : new float[height];

            for (int x = 0; x < width; x++)
            {
                // Copy column to temp as float
                for (int y = 0; y < height; y++)
                {
                    temp[y] = data[y * stride + x];
                }

                // Apply lifting steps
                // Step 1
                for (int y = 1; y < height; y += 2)
                {
                    float top = temp[y - 1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] += Alpha * (top + bottom);
                }

                // Step 2
                for (int y = 0; y < height; y += 2)
                {
                    float top = (y > 0) ? temp[y - 1] : temp[1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] += Beta * (top + bottom);
                }

                // Step 3
                for (int y = 1; y < height; y += 2)
                {
                    float top = temp[y - 1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] += Gamma * (top + bottom);
                }

                // Step 4
                for (int y = 0; y < height; y += 2)
                {
                    float top = (y > 0) ? temp[y - 1] : temp[1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] += Delta * (top + bottom);
                }

                // Scale
                for (int y = 0; y < height; y += 2)
                {
                    temp[y] *= K;
                }
                for (int y = 1; y < height; y += 2)
                {
                    temp[y] *= InvK;
                }

                // Copy back to int, deinterleaved
                int lowCount = (height + 1) / 2;
                for (int y = 0; y < lowCount; y++)
                {
                    data[y * stride + x] = (int)Math.Round(temp[y * 2]);
                }
                int highCount = height / 2;
                for (int y = 0; y < highCount; y++)
                {
                    data[(lowCount + y) * stride + x] = (int)Math.Round(temp[y * 2 + 1]);
                }
            }
        }

        /// <summary>
        /// Performs inverse vertical 1D transform (column-wise).
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

            // Allocate temp buffer for float conversion
            Span<float> temp = height <= 256 ? stackalloc float[height] : new float[height];

            int lowCount = (height + 1) / 2;
            int highCount = height / 2;

            for (int x = 0; x < width; x++)
            {
                // Copy column to temp as float, interleaving
                for (int y = 0; y < lowCount; y++)
                {
                    temp[y * 2] = data[y * stride + x];
                }
                for (int y = 0; y < highCount; y++)
                {
                    temp[y * 2 + 1] = data[(lowCount + y) * stride + x];
                }

                // Inverse scale
                for (int y = 0; y < height; y += 2)
                {
                    temp[y] *= InvK;
                }
                for (int y = 1; y < height; y += 2)
                {
                    temp[y] *= K;
                }

                // Inverse step 4
                for (int y = 0; y < height; y += 2)
                {
                    float top = (y > 0) ? temp[y - 1] : temp[1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] -= Delta * (top + bottom);
                }

                // Inverse step 3
                for (int y = 1; y < height; y += 2)
                {
                    float top = temp[y - 1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] -= Gamma * (top + bottom);
                }

                // Inverse step 2
                for (int y = 0; y < height; y += 2)
                {
                    float top = (y > 0) ? temp[y - 1] : temp[1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] -= Beta * (top + bottom);
                }

                // Inverse step 1
                for (int y = 1; y < height; y += 2)
                {
                    float top = temp[y - 1];
                    float bottom = (y + 1 < height) ? temp[y + 1] : top;
                    temp[y] -= Alpha * (top + bottom);
                }

                // Copy back to int
                for (int y = 0; y < height; y++)
                {
                    data[y * stride + x] = (int)Math.Round(temp[y]);
                }
            }
        }

        /// <summary>
        /// Deinterleaves a row: [x0, x1, x2, x3, ...] -> [x0, x2, ..., x1, x3, ...]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Deinterleave(Span<float> row)
        {
            int n = row.Length;
            int lowCount = (n + 1) / 2;

            Span<float> temp = n <= 256 ? stackalloc float[n] : new float[n];

            for (int i = 0; i < lowCount; i++)
            {
                temp[i] = row[i * 2];
            }

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
        private static void Interleave(Span<float> row)
        {
            int n = row.Length;
            int lowCount = (n + 1) / 2;

            Span<float> temp = n <= 256 ? stackalloc float[n] : new float[n];

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
    }
}
