using System;

namespace SharpDicom.Codecs.Jpeg2000.Wavelet
{
    /// <summary>
    /// Discrete Wavelet Transform operations for JPEG 2000.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides coordination for DWT operations used in JPEG 2000 encoding and decoding.
    /// It delegates to either the reversible 5/3 transform (for lossless compression) or the
    /// irreversible 9/7 transform (for lossy compression).
    /// </para>
    /// <para>
    /// The transform operates on a 2D grid using separable 1D transforms - first horizontal,
    /// then vertical (or vice versa for inverse). After each level of decomposition, the
    /// low-pass subband (LL) is recursively decomposed.
    /// </para>
    /// </remarks>
    public static class DwtTransform
    {
        /// <summary>
        /// Performs forward 2D DWT decomposition.
        /// </summary>
        /// <param name="data">Image data (modified in-place).</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="levels">Number of decomposition levels.</param>
        /// <param name="reversible">True for 5/3 (lossless), false for 9/7 (lossy).</param>
        /// <exception cref="ArgumentException">Thrown if dimensions are invalid.</exception>
        public static void Forward(Span<int> data, int width, int height, int levels, bool reversible)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Width and height must be positive.");
            }

            // Use long multiplication to avoid overflow before comparing
            if (data.Length < (long)width * height)
            {
                throw new ArgumentException("Data buffer is too small for the specified dimensions.");
            }

            if (levels <= 0)
            {
                return; // No decomposition
            }

            int currentWidth = width;
            int currentHeight = height;

            for (int level = 0; level < levels; level++)
            {
                if (currentWidth <= 1 && currentHeight <= 1)
                {
                    break; // Can't decompose further
                }

                if (reversible)
                {
                    Dwt53.Forward2D(data, width, currentWidth, currentHeight);
                }
                else
                {
                    Dwt97.Forward2D(data, width, currentWidth, currentHeight);
                }

                // Next level operates on the LL subband
                currentWidth = (currentWidth + 1) / 2;
                currentHeight = (currentHeight + 1) / 2;
            }
        }

        /// <summary>
        /// Performs inverse 2D DWT reconstruction.
        /// </summary>
        /// <param name="data">Wavelet coefficients (modified in-place).</param>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="levels">Number of decomposition levels.</param>
        /// <param name="reversible">True for 5/3 (lossless), false for 9/7 (lossy).</param>
        /// <exception cref="ArgumentException">Thrown if dimensions are invalid.</exception>
        public static void Inverse(Span<int> data, int width, int height, int levels, bool reversible)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Width and height must be positive.");
            }

            // Use long multiplication to avoid overflow before comparing
            if (data.Length < (long)width * height)
            {
                throw new ArgumentException("Data buffer is too small for the specified dimensions.");
            }

            if (levels <= 0)
            {
                return; // No reconstruction
            }

            // Calculate dimensions at each level
            Span<int> widths = stackalloc int[levels];
            Span<int> heights = stackalloc int[levels];

            int w = width;
            int h = height;
            for (int i = 0; i < levels; i++)
            {
                widths[i] = w;
                heights[i] = h;
                w = (w + 1) / 2;
                h = (h + 1) / 2;
            }

            // Reconstruct from deepest level to full resolution
            for (int level = levels - 1; level >= 0; level--)
            {
                int currentWidth = widths[level];
                int currentHeight = heights[level];

                if (currentWidth <= 1 && currentHeight <= 1)
                {
                    continue;
                }

                if (reversible)
                {
                    Dwt53.Inverse2D(data, width, currentWidth, currentHeight);
                }
                else
                {
                    Dwt97.Inverse2D(data, width, currentWidth, currentHeight);
                }
            }
        }

        /// <summary>
        /// Gets the dimensions of the LL subband after the specified number of decomposition levels.
        /// </summary>
        /// <param name="width">Original image width.</param>
        /// <param name="height">Original image height.</param>
        /// <param name="levels">Number of decomposition levels.</param>
        /// <returns>The dimensions of the LL subband as (width, height).</returns>
        public static (int Width, int Height) GetLLDimensions(int width, int height, int levels)
        {
            for (int i = 0; i < levels; i++)
            {
                width = (width + 1) / 2;
                height = (height + 1) / 2;
            }
            return (width, height);
        }

        /// <summary>
        /// Gets the dimensions of a subband at a specific resolution level.
        /// </summary>
        /// <param name="width">Original image width.</param>
        /// <param name="height">Original image height.</param>
        /// <param name="level">Resolution level (0 = full, higher = lower resolution).</param>
        /// <param name="subband">Subband type (0=LL, 1=HL, 2=LH, 3=HH).</param>
        /// <returns>The dimensions of the subband as (width, height).</returns>
        public static (int Width, int Height) GetSubbandDimensions(int width, int height, int level, int subband)
        {
            // Get dimensions at this resolution level
            for (int i = 0; i < level; i++)
            {
                width = (width + 1) / 2;
                height = (height + 1) / 2;
            }

            // For LL subband (0), dimensions are halved
            // For HL (1), LH (2), HH (3), they depend on the half size
            if (subband == 0)
            {
                return ((width + 1) / 2, (height + 1) / 2);
            }
            else if (subband == 1) // HL: high horizontal, low vertical
            {
                return (width / 2, (height + 1) / 2);
            }
            else if (subband == 2) // LH: low horizontal, high vertical
            {
                return ((width + 1) / 2, height / 2);
            }
            else // HH: high horizontal, high vertical
            {
                return (width / 2, height / 2);
            }
        }
    }
}
