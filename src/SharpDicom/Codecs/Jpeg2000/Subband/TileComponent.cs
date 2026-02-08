using System;
using System.Buffers;

namespace SharpDicom.Codecs.Jpeg2000.Subband
{
    /// <summary>
    /// Manages DWT coefficient data and code-block access for a single tile and component
    /// within a JPEG 2000 image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After DWT decomposition, a tile-component's coefficients are organized into subbands.
    /// This class provides indexed access to individual code-blocks within each subband,
    /// supporting both encoder (read code-block data for EBCOT) and decoder (write
    /// code-block data during reconstruction) workflows.
    /// </para>
    /// <para>
    /// Coefficient storage uses <see cref="ArrayPool{T}"/> to reduce GC pressure for large tiles.
    /// Callers must dispose the instance to return the pooled buffer.
    /// </para>
    /// </remarks>
    public sealed class TileComponent : IDisposable
    {
        private int[]? _coefficients;
        private readonly bool _pooled;
        private bool _disposed;

        /// <summary>Gets the zero-based tile index.</summary>
        public int TileIndex { get; }

        /// <summary>Gets the zero-based component index.</summary>
        public int ComponentIndex { get; }

        /// <summary>Gets the tile width in pixels/coefficients.</summary>
        public int TileWidth { get; }

        /// <summary>Gets the tile height in pixels/coefficients.</summary>
        public int TileHeight { get; }

        /// <summary>Gets the number of DWT decomposition levels.</summary>
        public int DecompositionLevels { get; }

        /// <summary>Gets the nominal code-block width.</summary>
        public int CodeBlockWidth { get; }

        /// <summary>Gets the nominal code-block height.</summary>
        public int CodeBlockHeight { get; }

        /// <summary>Gets the subband descriptors for this tile-component's decomposition.</summary>
        public SubbandDescriptor[] Subbands { get; }

        /// <summary>
        /// Initializes a new <see cref="TileComponent"/>.
        /// </summary>
        /// <param name="tileIndex">Zero-based tile index.</param>
        /// <param name="componentIndex">Zero-based component index.</param>
        /// <param name="tileWidth">Tile width (may be smaller than nominal at image edges).</param>
        /// <param name="tileHeight">Tile height (may be smaller than nominal at image edges).</param>
        /// <param name="decompositionLevels">Number of DWT decomposition levels.</param>
        /// <param name="codeBlockWidth">Nominal code-block width.</param>
        /// <param name="codeBlockHeight">Nominal code-block height.</param>
        /// <exception cref="ArgumentOutOfRangeException">If any parameter is invalid.</exception>
        public TileComponent(
            int tileIndex,
            int componentIndex,
            int tileWidth,
            int tileHeight,
            int decompositionLevels,
            int codeBlockWidth,
            int codeBlockHeight)
        {
            if (tileIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileIndex), "Must be non-negative.");
            }

            if (componentIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(componentIndex), "Must be non-negative.");
            }

            if (tileWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileWidth), "Must be positive.");
            }

            if (tileHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileHeight), "Must be positive.");
            }

            if (decompositionLevels < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(decompositionLevels), "Must be non-negative.");
            }

            if (codeBlockWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockWidth), "Must be positive.");
            }

            if (codeBlockHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockHeight), "Must be positive.");
            }

            TileIndex = tileIndex;
            ComponentIndex = componentIndex;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            DecompositionLevels = decompositionLevels;
            CodeBlockWidth = codeBlockWidth;
            CodeBlockHeight = codeBlockHeight;

            Subbands = SubbandPartitioner.GetSubbands(
                tileWidth, tileHeight, decompositionLevels, codeBlockWidth, codeBlockHeight);

            // Use ArrayPool for buffers 1024+ elements; small buffers are just allocated
            int size = tileWidth * tileHeight;
            if (size >= 1024)
            {
                _coefficients = ArrayPool<int>.Shared.Rent(size);
                _pooled = true;
                // Clear the rented array (it may contain stale data)
                Array.Clear(_coefficients, 0, size);
            }
            else
            {
                _coefficients = new int[size];
                _pooled = false;
            }
        }

        /// <summary>
        /// Gets a span over the raw coefficient data for this tile-component.
        /// The span length is exactly <see cref="TileWidth"/> * <see cref="TileHeight"/>.
        /// </summary>
        /// <exception cref="ObjectDisposedException">If the instance has been disposed.</exception>
        public Span<int> Coefficients
        {
            get
            {
                ThrowIfDisposed();
                return new Span<int>(_coefficients, 0, TileWidth * TileHeight);
            }
        }

        /// <summary>
        /// Extracts code-block coefficients for a specific code-block within a subband,
        /// copying them into the provided destination buffer in row-major order.
        /// </summary>
        /// <param name="subbandIndex">Index into <see cref="Subbands"/>.</param>
        /// <param name="cbX">Horizontal code-block index within the subband.</param>
        /// <param name="cbY">Vertical code-block index within the subband.</param>
        /// <param name="destination">
        /// Destination buffer. Must be at least <see cref="CodeBlockWidth"/> * <see cref="CodeBlockHeight"/> elements.
        /// Only the actual code-block region is written; excess area is zeroed.
        /// </param>
        /// <returns>The actual (width, height) of the code-block (may be smaller at subband edges).</returns>
        /// <exception cref="ObjectDisposedException">If the instance has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If indices are out of range.</exception>
        /// <exception cref="ArgumentException">If destination is too small.</exception>
        public (int Width, int Height) GetCodeBlockCoefficients(
            int subbandIndex, int cbX, int cbY, Span<int> destination)
        {
            ThrowIfDisposed();
            ValidateCodeBlockArgs(subbandIndex, cbX, cbY);

            int requiredSize = CodeBlockWidth * CodeBlockHeight;
            if (destination.Length < requiredSize)
            {
                throw new ArgumentException(
                    $"Destination must be at least {requiredSize} elements.", nameof(destination));
            }

            var sb = Subbands[subbandIndex];

            // Calculate the pixel region in the coefficient array
            int startX = sb.OriginX + cbX * CodeBlockWidth;
            int startY = sb.OriginY + cbY * CodeBlockHeight;
            int actualWidth = Math.Min(CodeBlockWidth, sb.OriginX + sb.Width - startX);
            int actualHeight = Math.Min(CodeBlockHeight, sb.OriginY + sb.Height - startY);

            // Clear destination first
            destination.Slice(0, requiredSize).Clear();

            // Copy from coefficient array (row-major, stride = TileWidth)
            for (int y = 0; y < actualHeight; y++)
            {
                int srcOffset = (startY + y) * TileWidth + startX;
                int dstOffset = y * CodeBlockWidth;

                for (int x = 0; x < actualWidth; x++)
                {
                    destination[dstOffset + x] = _coefficients![srcOffset + x];
                }
            }

            return (actualWidth, actualHeight);
        }

        /// <summary>
        /// Writes code-block coefficients back into the coefficient array for a specific
        /// code-block within a subband. Used during decoding/reconstruction.
        /// </summary>
        /// <param name="subbandIndex">Index into <see cref="Subbands"/>.</param>
        /// <param name="cbX">Horizontal code-block index within the subband.</param>
        /// <param name="cbY">Vertical code-block index within the subband.</param>
        /// <param name="source">
        /// Source buffer containing code-block coefficients in row-major order.
        /// Must be at least <see cref="CodeBlockWidth"/> * <see cref="CodeBlockHeight"/> elements.
        /// </param>
        /// <exception cref="ObjectDisposedException">If the instance has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If indices are out of range.</exception>
        /// <exception cref="ArgumentException">If source is too small.</exception>
        public void SetCodeBlockCoefficients(
            int subbandIndex, int cbX, int cbY, ReadOnlySpan<int> source)
        {
            ThrowIfDisposed();
            ValidateCodeBlockArgs(subbandIndex, cbX, cbY);

            int requiredSize = CodeBlockWidth * CodeBlockHeight;
            if (source.Length < requiredSize)
            {
                throw new ArgumentException(
                    $"Source must be at least {requiredSize} elements.", nameof(source));
            }

            var sb = Subbands[subbandIndex];

            int startX = sb.OriginX + cbX * CodeBlockWidth;
            int startY = sb.OriginY + cbY * CodeBlockHeight;
            int actualWidth = Math.Min(CodeBlockWidth, sb.OriginX + sb.Width - startX);
            int actualHeight = Math.Min(CodeBlockHeight, sb.OriginY + sb.Height - startY);

            for (int y = 0; y < actualHeight; y++)
            {
                int dstOffset = (startY + y) * TileWidth + startX;
                int srcOffset = y * CodeBlockWidth;

                for (int x = 0; x < actualWidth; x++)
                {
                    _coefficients![dstOffset + x] = source[srcOffset + x];
                }
            }
        }

        /// <summary>
        /// Releases the pooled coefficient buffer.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_pooled && _coefficients != null)
                {
                    ArrayPool<int>.Shared.Return(_coefficients);
                }

                _coefficients = null;
                _disposed = true;
            }
        }

        private void ValidateCodeBlockArgs(int subbandIndex, int cbX, int cbY)
        {
            if (subbandIndex < 0 || subbandIndex >= Subbands.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(subbandIndex),
                    $"Must be between 0 and {Subbands.Length - 1}.");
            }

            var sb = Subbands[subbandIndex];

            if (cbX < 0 || cbX >= sb.CodeBlockGridWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(cbX),
                    $"Must be between 0 and {sb.CodeBlockGridWidth - 1} for subband {sb.Type} at level {sb.ResolutionLevel}.");
            }

            if (cbY < 0 || cbY >= sb.CodeBlockGridHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(cbY),
                    $"Must be between 0 and {sb.CodeBlockGridHeight - 1} for subband {sb.Type} at level {sb.ResolutionLevel}.");
            }
        }

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TileComponent));
            }
#endif
        }
    }
}
