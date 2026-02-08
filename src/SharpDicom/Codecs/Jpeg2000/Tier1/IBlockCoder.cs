using System;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Unified interface for JPEG 2000 tier-1 block coding algorithms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Block coders operate on individual code-blocks of wavelet coefficients,
    /// performing entropy coding (encode) and decoding (decode) using context-adaptive
    /// arithmetic coding.
    /// </para>
    /// <para>
    /// Two implementations are supported:
    /// <list type="bullet">
    ///   <item><see cref="EbcotBlockCoder"/>: Traditional EBCOT (ITU-T T.800 Annex D).</item>
    ///   <item>HT block coder (future): High Throughput (ITU-T T.814) for HTJ2K.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IBlockCoder
    {
        /// <summary>
        /// Encodes a single code-block of wavelet coefficients.
        /// </summary>
        /// <param name="coefficients">
        /// Wavelet coefficients in row-major order.
        /// Must contain at least <paramref name="width"/> * <paramref name="height"/> elements.
        /// </param>
        /// <param name="width">Code-block width in coefficients.</param>
        /// <param name="height">Code-block height in coefficients.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</param>
        /// <param name="msbPosition">
        /// Hint for the most significant bit position.
        /// Implementations may ignore this if they compute it internally (as EBCOT does).
        /// Pass -1 to let the implementation determine it automatically.
        /// </param>
        /// <returns>Encoded code-block data with pass and length information.</returns>
        CodeBlockData EncodeBlock(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int subbandType,
            int msbPosition);

        /// <summary>
        /// Decodes a single code-block from its encoded bitstream.
        /// </summary>
        /// <param name="data">Encoded bitstream data.</param>
        /// <param name="numPasses">Number of coding passes to decode.</param>
        /// <param name="output">
        /// Destination buffer for decoded coefficients in row-major order.
        /// Must contain at least <paramref name="width"/> * <paramref name="height"/> elements.
        /// </param>
        /// <param name="width">Code-block width in coefficients.</param>
        /// <param name="height">Code-block height in coefficients.</param>
        /// <param name="msbPosition">Most significant bit position for reconstruction.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</param>
        void DecodeBlock(
            ReadOnlySpan<byte> data,
            int numPasses,
            Span<int> output,
            int width, int height,
            int msbPosition,
            int subbandType);
    }
}
