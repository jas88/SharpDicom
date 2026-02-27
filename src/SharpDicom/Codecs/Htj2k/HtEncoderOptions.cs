namespace SharpDicom.Codecs.Htj2k
{
    /// <summary>
    /// Options specific to the HT (High-Throughput) block coding algorithm for HTJ2K encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Controls the number of HT coding passes and quality targets.
    /// An HT Set consists of 3 passes: Cleanup, SigProp, and MagRef.
    /// With 1 set there are up to 3 passes; with 2 sets up to 6 passes.
    /// </para>
    /// <para>
    /// Named presets map common imaging use cases to appropriate quality parameters
    /// per ITU-T T.814 guidelines.
    /// </para>
    /// </remarks>
    /// <param name="HtSetCount">Number of HT coding sets (1 or 2). More sets provide finer progressive refinement.</param>
    /// <param name="IncludeSigProp">Whether to include the Significance Propagation pass within each set.</param>
    /// <param name="IncludeMagRef">Whether to include the Magnitude Refinement pass within each set.</param>
    /// <param name="TargetBpp">Target bits per pixel for rate control. Null means lossless (no rate limit).</param>
    /// <param name="TargetPsnr">Target PSNR in decibels. Null means no PSNR target.</param>
    public readonly record struct HtEncoderOptions(
        int HtSetCount,
        bool IncludeSigProp,
        bool IncludeMagRef,
        float? TargetBpp,
        float? TargetPsnr)
    {
        /// <summary>
        /// Gets the effective number of coding passes based on set count and included passes.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item>Cleanup only: 1 pass per set.</item>
        ///   <item>Cleanup + SigProp: 2 passes per set.</item>
        ///   <item>Cleanup + SigProp + MagRef: 3 passes per set.</item>
        /// </list>
        /// </remarks>
        public int EffectivePassCount
        {
            get
            {
                int passesPerSet = 1; // Cleanup always present
                if (IncludeSigProp)
                {
                    passesPerSet++;
                }
                if (IncludeMagRef)
                {
                    passesPerSet++;
                }
                return passesPerSet * HtSetCount;
            }
        }

        /// <summary>
        /// Gets whether this configuration targets lossless compression (no rate or PSNR constraint).
        /// </summary>
        public bool IsLossless => TargetBpp is null && TargetPsnr is null;

        /// <summary>
        /// Lossless preset: 1 HT Set, cleanup only, no rate target.
        /// </summary>
        /// <remarks>
        /// Standard-compatible lossless encoding. Produces codestreams decodable by any HTJ2K decoder.
        /// HT cleanup encoding is inherently lossless for all coefficient data.
        /// </remarks>
        public static HtEncoderOptions Lossless { get; } = new(
            HtSetCount: 1,
            IncludeSigProp: false,
            IncludeMagRef: false,
            TargetBpp: null,
            TargetPsnr: null);

        /// <summary>
        /// Lossless multi-pass preset: 2 HT Sets with all passes, no rate target.
        /// </summary>
        /// <remarks>
        /// Uses an internal multi-pass format with embedded pass length header.
        /// Only decodable by SharpDicom's own decoder. Provides progressive refinement capability.
        /// </remarks>
        public static HtEncoderOptions LosslessMultiPass { get; } = new(
            HtSetCount: 2,
            IncludeSigProp: true,
            IncludeMagRef: true,
            TargetBpp: null,
            TargetPsnr: null);

        /// <summary>
        /// Diagnostic preset: 1 HT Set, cleanup only, PSNR target of 40 dB.
        /// </summary>
        /// <remarks>
        /// Standard-compatible high-quality lossy compression for diagnostic-grade viewing.
        /// </remarks>
        public static HtEncoderOptions Diagnostic { get; } = new(
            HtSetCount: 1,
            IncludeSigProp: false,
            IncludeMagRef: false,
            TargetBpp: null,
            TargetPsnr: 40f);

        /// <summary>
        /// Archive preset: 2 HT Sets with all passes, PSNR target of 35 dB.
        /// </summary>
        /// <remarks>
        /// Good quality for long-term storage with reduced file size.
        /// </remarks>
        public static HtEncoderOptions Archive { get; } = new(
            HtSetCount: 2,
            IncludeSigProp: true,
            IncludeMagRef: true,
            TargetBpp: null,
            TargetPsnr: 35f);

        /// <summary>
        /// Review preset: 1 HT Set with SigProp and MagRef, PSNR target of 30 dB.
        /// </summary>
        /// <remarks>
        /// Moderate quality for clinical review, balancing quality and speed.
        /// </remarks>
        public static HtEncoderOptions Review { get; } = new(
            HtSetCount: 1,
            IncludeSigProp: true,
            IncludeMagRef: true,
            TargetBpp: null,
            TargetPsnr: 30f);

        /// <summary>
        /// Fast preset: 1 HT Set, cleanup only (no SigProp or MagRef), PSNR target of 25 dB.
        /// </summary>
        /// <remarks>
        /// Fastest encoding with minimal quality, suitable for thumbnails or preview images.
        /// </remarks>
        public static HtEncoderOptions Fast { get; } = new(
            HtSetCount: 1,
            IncludeSigProp: false,
            IncludeMagRef: false,
            TargetBpp: null,
            TargetPsnr: 25f);
    }
}
