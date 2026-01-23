using System;

namespace SharpDicom.Codecs
{
    /// <summary>
    /// Describes the capabilities of a pixel data codec.
    /// </summary>
    /// <param name="CanEncode">Whether the codec supports encoding (compression).</param>
    /// <param name="CanDecode">Whether the codec supports decoding (decompression).</param>
    /// <param name="IsLossy">Whether the codec uses lossy compression.</param>
    /// <param name="SupportsMultiFrame">Whether the codec supports multi-frame images.</param>
    /// <param name="SupportsParallelEncode">Whether the codec can encode frames in parallel.</param>
    /// <param name="SupportedBitDepths">Bit depths the codec supports (e.g., 8, 12, 16).</param>
    /// <param name="SupportedSamplesPerPixel">Samples per pixel values the codec supports (e.g., 1 for grayscale, 3 for RGB).</param>
    public readonly record struct CodecCapabilities(
        bool CanEncode,
        bool CanDecode,
        bool IsLossy,
        bool SupportsMultiFrame,
        bool SupportsParallelEncode,
        int[] SupportedBitDepths,
        int[] SupportedSamplesPerPixel)
    {
        /// <summary>
        /// Creates capabilities for a lossless decode-only codec.
        /// </summary>
        /// <param name="supportedBitDepths">Bit depths the codec supports.</param>
        /// <param name="supportedSamplesPerPixel">Samples per pixel values the codec supports.</param>
        /// <returns>A CodecCapabilities instance.</returns>
        public static CodecCapabilities DecodeOnly(int[] supportedBitDepths, int[] supportedSamplesPerPixel) =>
            new(
                CanEncode: false,
                CanDecode: true,
                IsLossy: false,
                SupportsMultiFrame: true,
                SupportsParallelEncode: false,
                SupportedBitDepths: supportedBitDepths ?? Array.Empty<int>(),
                SupportedSamplesPerPixel: supportedSamplesPerPixel ?? Array.Empty<int>());

        /// <summary>
        /// Creates capabilities for a full encode/decode codec.
        /// </summary>
        /// <param name="isLossy">Whether the codec uses lossy compression.</param>
        /// <param name="supportedBitDepths">Bit depths the codec supports.</param>
        /// <param name="supportedSamplesPerPixel">Samples per pixel values the codec supports.</param>
        /// <returns>A CodecCapabilities instance.</returns>
        public static CodecCapabilities Full(bool isLossy, int[] supportedBitDepths, int[] supportedSamplesPerPixel) =>
            new(
                CanEncode: true,
                CanDecode: true,
                IsLossy: isLossy,
                SupportsMultiFrame: true,
                SupportsParallelEncode: true,
                SupportedBitDepths: supportedBitDepths ?? Array.Empty<int>(),
                SupportedSamplesPerPixel: supportedSamplesPerPixel ?? Array.Empty<int>());
    }
}
