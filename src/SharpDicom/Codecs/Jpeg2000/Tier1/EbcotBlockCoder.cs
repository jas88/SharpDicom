using System;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// EBCOT (Embedded Block Coding with Optimal Truncation) block coder
    /// implementing <see cref="IBlockCoder"/> by wrapping the existing
    /// <see cref="EbcotEncoder"/> and <see cref="EbcotDecoder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This adapter provides a unified block coder interface over the existing EBCOT
    /// encoder and decoder without modifying their internals. It enables the J2K pipeline
    /// to switch between EBCOT and future HT block coding via the <see cref="IBlockCoder"/>
    /// abstraction.
    /// </para>
    /// <para>
    /// The <see cref="Instance"/> singleton is safe for sequential use (one code-block at a time)
    /// because the underlying encoder and decoder maintain state arrays that are reset per call.
    /// For concurrent encoding, create separate instances via the constructor.
    /// </para>
    /// </remarks>
    public sealed class EbcotBlockCoder : IBlockCoder, IDisposable
    {
        private readonly EbcotEncoder _encoder;
        private readonly EbcotDecoder _decoder;
        private bool _disposed;

        /// <summary>
        /// Gets a shared singleton instance for sequential (non-concurrent) use.
        /// </summary>
        /// <remarks>
        /// The singleton is suitable for typical single-threaded encode/decode pipelines.
        /// For concurrent code-block processing, create separate instances via the constructor.
        /// </remarks>
        public static EbcotBlockCoder Instance { get; } = new EbcotBlockCoder();

        /// <summary>
        /// Initializes a new <see cref="EbcotBlockCoder"/>.
        /// </summary>
        public EbcotBlockCoder()
        {
            _encoder = new EbcotEncoder();
            _decoder = new EbcotDecoder();
        }

        /// <inheritdoc />
        public CodeBlockData EncodeBlock(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int subbandType,
            int msbPosition)
        {
            // EbcotEncoder computes MSB position internally, so the msbPosition hint is ignored.
            return _encoder.EncodeCodeBlock(coefficients, width, height, subbandType);
        }

        /// <inheritdoc />
        public void DecodeBlock(
            ReadOnlySpan<byte> data,
            int numPasses,
            Span<int> output,
            int width, int height,
            int msbPosition,
            int subbandType)
        {
            int count = width * height;
            if (output.Length < count)
            {
                throw new ArgumentException(
                    $"Output buffer length {output.Length} is less than the required {width}x{height}={count}.",
                    nameof(output));
            }

            int[] decoded = _decoder.DecodeCodeBlock(data, numPasses, width, height, msbPosition, subbandType);

            if (decoded.Length < count)
            {
                throw new InvalidOperationException(
                    $"Decoder returned {decoded.Length} coefficients but {width}x{height}={count} were expected.");
            }

            decoded.AsSpan(0, count).CopyTo(output);
        }

        /// <summary>
        /// Disposes the underlying encoder resources.
        /// </summary>
        /// <remarks>
        /// Do not dispose the <see cref="Instance"/> singleton.
        /// </remarks>
        public void Dispose()
        {
            if (!_disposed)
            {
                _encoder.Dispose();
                _disposed = true;
            }
        }
    }
}
