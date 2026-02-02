namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Options for JPEG-LS encoding.
    /// </summary>
    /// <param name="Near">The NEAR parameter (0 for lossless, &gt;0 for near-lossless). Maximum allowed error.</param>
    /// <param name="InterleaveMode">The interleave mode for color images.</param>
    /// <param name="GenerateBasicOffsetTable">Whether to generate a Basic Offset Table for multi-frame images.</param>
    public readonly record struct JpegLsCodecOptions(
        int Near,
        JlsInterleaveMode InterleaveMode,
        bool GenerateBasicOffsetTable)
    {
        /// <summary>
        /// Default options for lossless JPEG-LS encoding (NEAR=0).
        /// </summary>
        public static JpegLsCodecOptions Default { get; } = new(0, JlsInterleaveMode.None, true);

        /// <summary>
        /// Options for visually lossless encoding (NEAR=2, typically imperceptible loss).
        /// </summary>
        public static JpegLsCodecOptions VisuallyLossless { get; } = new(2, JlsInterleaveMode.None, true);

        /// <summary>
        /// Options for high compression near-lossless encoding (NEAR=5).
        /// </summary>
        public static JpegLsCodecOptions HighCompression { get; } = new(5, JlsInterleaveMode.None, true);
    }

    /// <summary>
    /// JPEG-LS interleave mode for color images.
    /// </summary>
    public enum JlsInterleaveMode
    {
        /// <summary>
        /// Non-interleaved (component by component).
        /// </summary>
        None = 0,

        /// <summary>
        /// Line interleaved (ILV=1).
        /// </summary>
        Line = 1,

        /// <summary>
        /// Sample interleaved (ILV=2).
        /// </summary>
        Sample = 2
    }
}
