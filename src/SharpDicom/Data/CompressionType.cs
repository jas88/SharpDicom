namespace SharpDicom.Data
{
    /// <summary>
    /// Specifies the type of compression used for DICOM pixel data.
    /// </summary>
    public enum CompressionType
    {
        /// <summary>
        /// No compression (uncompressed pixel data).
        /// </summary>
        None,

        /// <summary>
        /// JPEG Baseline (Process 1) compression.
        /// </summary>
        JPEGBaseline,

        /// <summary>
        /// JPEG Extended (Process 2 and 4) compression.
        /// </summary>
        JPEGExtended,

        /// <summary>
        /// JPEG Lossless compression.
        /// </summary>
        JPEGLossless,

        /// <summary>
        /// JPEG 2000 Lossless compression.
        /// </summary>
        JPEG2000Lossless,

        /// <summary>
        /// JPEG 2000 Lossy compression.
        /// </summary>
        JPEG2000Lossy,

        /// <summary>
        /// JPEG-LS Lossless compression.
        /// </summary>
        JPEGLSLossless,

        /// <summary>
        /// JPEG-LS Near-Lossless compression.
        /// </summary>
        JPEGLSNearLossless,

        /// <summary>
        /// RLE (Run-Length Encoding) compression.
        /// </summary>
        RLE
    }
}
