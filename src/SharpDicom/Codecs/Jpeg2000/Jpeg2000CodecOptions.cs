namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// Options for JPEG 2000 encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control the encoding parameters for JPEG 2000 compression.
    /// For lossless compression, most parameters still apply except compression ratio.
    /// </para>
    /// <para>
    /// For DICOM compliance, use the <see cref="MedicalImaging"/> preset which
    /// uses conservative settings appropriate for diagnostic imaging.
    /// </para>
    /// </remarks>
    public sealed class Jpeg2000CodecOptions
    {
        /// <summary>
        /// Gets or sets the target compression ratio for lossy encoding (e.g., 10 = 10:1 compression).
        /// </summary>
        /// <remarks>
        /// This option is ignored for lossless encoding.
        /// Higher values mean more compression but lower quality.
        /// </remarks>
        public int CompressionRatio { get; set; } = 10;

        /// <summary>
        /// Gets or sets the number of decomposition levels (typically 5).
        /// </summary>
        /// <remarks>
        /// More decomposition levels provide better progressive transmission
        /// and potentially better compression, but require more processing.
        /// Valid range is 0-32.
        /// </remarks>
        public int DecompositionLevels { get; set; } = 5;

        /// <summary>
        /// Gets or sets the number of quality layers (1 for single quality, more for progressive).
        /// </summary>
        /// <remarks>
        /// Multiple quality layers allow progressive refinement of the image.
        /// For most DICOM applications, 1 layer is sufficient.
        /// </remarks>
        public int QualityLayers { get; set; } = 1;

        /// <summary>
        /// Gets or sets the code-block size (default 64x64).
        /// </summary>
        /// <remarks>
        /// Must be a power of 2 between 4 and 1024.
        /// The product of width and height must not exceed 4096.
        /// 64x64 is the standard default for JPEG 2000.
        /// </remarks>
        public int CodeBlockSize { get; set; } = 64;

        /// <summary>
        /// Gets or sets whether to generate a Basic Offset Table for multi-frame images.
        /// </summary>
        public bool GenerateBasicOffsetTable { get; set; } = true;

        /// <summary>
        /// Gets the default options for medical imaging.
        /// </summary>
        /// <remarks>
        /// Uses conservative compression settings appropriate for diagnostic imaging:
        /// - Lower compression ratio (5:1) for better quality
        /// - Standard decomposition levels (5)
        /// - Single quality layer
        /// </remarks>
        public static Jpeg2000CodecOptions MedicalImaging { get; } = new()
        {
            CompressionRatio = 5,  // Conservative for medical
            DecompositionLevels = 5,
            QualityLayers = 1,
            CodeBlockSize = 64,
            GenerateBasicOffsetTable = true
        };

        /// <summary>
        /// Gets the default options.
        /// </summary>
        public static Jpeg2000CodecOptions Default { get; } = new();
    }
}
