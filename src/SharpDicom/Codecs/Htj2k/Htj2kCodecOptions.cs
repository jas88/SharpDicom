namespace SharpDicom.Codecs.Htj2k
{
    /// <summary>
    /// Options for HTJ2K (High Throughput JPEG 2000) encoding.
    /// </summary>
    /// <param name="UseLossless">Whether to use lossless compression.</param>
    /// <param name="DecompositionLevels">Number of wavelet decomposition levels (typically 5).</param>
    /// <param name="UseRpcl">Whether to use RPCL (Resolution Position Component Layer) progression.</param>
    /// <param name="GenerateBasicOffsetTable">Whether to generate a Basic Offset Table for multi-frame images.</param>
    public readonly record struct Htj2kCodecOptions(
        bool UseLossless,
        int DecompositionLevels,
        bool UseRpcl,
        bool GenerateBasicOffsetTable)
    {
        /// <summary>
        /// Default options for lossless HTJ2K encoding.
        /// </summary>
        public static Htj2kCodecOptions Default { get; } = new(true, 5, false, true);

        /// <summary>
        /// Options for lossless HTJ2K with RPCL progression (optimized for streaming/progressive decode).
        /// </summary>
        public static Htj2kCodecOptions LosslessRpcl { get; } = new(true, 5, true, true);

        /// <summary>
        /// Options for lossy HTJ2K encoding.
        /// </summary>
        public static Htj2kCodecOptions Lossy { get; } = new(false, 5, false, true);
    }
}
