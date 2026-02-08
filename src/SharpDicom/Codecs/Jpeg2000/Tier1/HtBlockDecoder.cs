using System;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT (High-Throughput) block decoder for the ITU-T T.814 block coding algorithm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decodes code-blocks encoded by <see cref="HtBlockEncoder"/>. Supports 1 to 6
    /// coding passes arranged in HT Sets:
    /// <list type="bullet">
    ///   <item>Pass 1: <see cref="HtCleanup"/> decoding (always present).</item>
    ///   <item>Pass 2: <see cref="HtSigProp"/> significance propagation refinement.</item>
    ///   <item>Pass 3: <see cref="HtMagRef"/> magnitude refinement.</item>
    ///   <item>Passes 4-6: Second HT Set (SigProp + MagRef at next bitplane).</item>
    /// </list>
    /// </para>
    /// <para>
    /// This class provides a standalone decoder interface. For the full
    /// <see cref="IBlockCoder"/> interface (encode + decode), use
    /// <see cref="HtBlockEncoder"/>.
    /// </para>
    /// </remarks>
    public sealed class HtBlockDecoder
    {
        /// <summary>
        /// Gets the shared singleton instance.
        /// </summary>
        /// <remarks>
        /// The decoder is stateless and safe for concurrent use.
        /// </remarks>
        public static HtBlockDecoder Instance { get; } = new HtBlockDecoder();

        /// <summary>
        /// Decodes a code-block from its encoded HT bitstream.
        /// </summary>
        /// <param name="data">
        /// Encoded bitstream data. For single-pass encoding, this is the raw cleanup
        /// segment. For multi-pass encoding, the data contains an embedded header
        /// with cumulative pass lengths followed by concatenated pass data.
        /// </param>
        /// <param name="numPasses">Number of coding passes to decode (1-6).</param>
        /// <param name="output">
        /// Destination buffer for decoded coefficients in row-major order.
        /// Must contain at least <paramref name="width"/> * <paramref name="height"/> elements.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="msbPosition">
        /// Most significant bit position for reconstruction.
        /// </param>
        /// <param name="subbandType">
        /// Subband type: 0=LL, 1=LH, 2=HL, 3=HH.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when output buffer is too small or parameters are invalid.
        /// </exception>
        // CA1822: Intentionally instance method for API symmetry with HtBlockEncoder.
        // Future implementations may add instance state for decoder configuration.
#pragma warning disable CA1822
        public void DecodeBlock(
            ReadOnlySpan<byte> data,
            int numPasses,
            Span<int> output,
            int width, int height,
            int msbPosition,
            int subbandType)
#pragma warning restore CA1822
        {
            // Delegate to HtBlockEncoder which has the full decode implementation
            HtBlockEncoder.Instance.DecodeBlock(
                data, numPasses, output,
                width, height, msbPosition, subbandType);
        }
    }
}
