using System;
using SharpDicom.Data;

namespace SharpDicom.Codecs.Htj2k
{
    /// <summary>
    /// Interface for codecs that support progressive (resolution-level) decoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Progressive codecs can decode an image at lower resolution levels without
    /// fully decoding the entire codestream. This is particularly useful for:
    /// <list type="bullet">
    ///   <item>Generating thumbnails quickly from large images.</item>
    ///   <item>Implementing resolution-on-demand viewing in PACS workstations.</item>
    ///   <item>Reducing bandwidth in network transfers using RPCL progression.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Resolution level 0 is the lowest resolution (the LL subband from the deepest
    /// decomposition level). Resolution level N (where N = DecompositionLevels) is the
    /// full resolution image.
    /// </para>
    /// </remarks>
    public interface IProgressiveCodec : IPixelDataCodec
    {
        /// <summary>
        /// Gets the number of resolution levels available in the compressed data.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments.</param>
        /// <param name="frameIndex">Zero-based index of the frame to query.</param>
        /// <returns>
        /// The number of resolution levels (1 + decomposition levels).
        /// Returns 1 if the codestream has no decomposition.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="fragments"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is out of range.</exception>
        int GetResolutionLevels(DicomFragmentSequence fragments, int frameIndex);

        /// <summary>
        /// Decodes a single frame at a specific resolution level.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments.</param>
        /// <param name="info">Pixel data metadata for the full-resolution image.</param>
        /// <param name="frameIndex">Zero-based index of the frame to decode.</param>
        /// <param name="resolutionLevel">
        /// The target resolution level (0 = lowest/thumbnail, max = full resolution).
        /// </param>
        /// <param name="destination">Buffer to write the decompressed pixel data at the target resolution.</param>
        /// <returns>The decode result indicating success or failure with diagnostics.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fragments"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="frameIndex"/> or <paramref name="resolutionLevel"/> is out of range.
        /// </exception>
        DecodeResult DecodeAtResolution(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            int resolutionLevel,
            Memory<byte> destination);

        /// <summary>
        /// Gets the output dimensions for a given resolution level.
        /// </summary>
        /// <param name="fragments">The encapsulated pixel data fragments.</param>
        /// <param name="info">Pixel data metadata for the full-resolution image.</param>
        /// <param name="frameIndex">Zero-based index of the frame to query.</param>
        /// <param name="resolutionLevel">
        /// The target resolution level (0 = lowest/thumbnail, max = full resolution).
        /// </param>
        /// <returns>The width and height of the image at the requested resolution level.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fragments"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="frameIndex"/> or <paramref name="resolutionLevel"/> is out of range.
        /// </exception>
        (int Width, int Height) GetResolutionDimensions(
            DicomFragmentSequence fragments,
            PixelDataInfo info,
            int frameIndex,
            int resolutionLevel);
    }
}
