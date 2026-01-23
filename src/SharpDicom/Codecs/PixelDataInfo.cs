namespace SharpDicom.Codecs
{
    /// <summary>
    /// Metadata about pixel data needed for codec operations.
    /// </summary>
    /// <param name="Rows">Number of rows (height) in the image.</param>
    /// <param name="Columns">Number of columns (width) in the image.</param>
    /// <param name="BitsAllocated">Bits allocated per sample (8, 16, or 32).</param>
    /// <param name="BitsStored">Bits actually used per sample.</param>
    /// <param name="HighBit">Most significant bit position (usually BitsStored - 1).</param>
    /// <param name="SamplesPerPixel">Number of samples per pixel (1 for grayscale, 3 for RGB).</param>
    /// <param name="PixelRepresentation">0 for unsigned, 1 for signed values.</param>
    /// <param name="PlanarConfiguration">0 for pixel-interleaved, 1 for plane-separated.</param>
    /// <param name="NumberOfFrames">Number of frames in the image (1 for single-frame).</param>
    public readonly record struct PixelDataInfo(
        ushort Rows,
        ushort Columns,
        ushort BitsAllocated,
        ushort BitsStored,
        ushort HighBit,
        ushort SamplesPerPixel,
        ushort PixelRepresentation,
        ushort PlanarConfiguration,
        int NumberOfFrames)
    {
        /// <summary>
        /// Gets the number of bytes per pixel (BitsAllocated / 8 * SamplesPerPixel).
        /// </summary>
        public int BytesPerPixel => (BitsAllocated / 8) * SamplesPerPixel;

        /// <summary>
        /// Gets the size in bytes of a single uncompressed frame.
        /// </summary>
        public int FrameSize => Rows * Columns * BytesPerPixel;

        /// <summary>
        /// Gets the total size in bytes of all uncompressed frames.
        /// </summary>
        public long TotalSize => (long)FrameSize * NumberOfFrames;

        /// <summary>
        /// Gets whether the pixel values are signed integers.
        /// </summary>
        public bool IsSigned => PixelRepresentation == 1;

        /// <summary>
        /// Gets whether the image is grayscale (single sample per pixel).
        /// </summary>
        public bool IsGrayscale => SamplesPerPixel == 1;

        /// <summary>
        /// Gets whether the image uses planar configuration (samples separated into planes).
        /// </summary>
        public bool IsPlanar => PlanarConfiguration == 1;

        /// <summary>
        /// Gets the number of bytes per sample.
        /// </summary>
        public int BytesPerSample => BitsAllocated / 8;

        /// <summary>
        /// Creates a default pixel data info for a grayscale 8-bit image.
        /// </summary>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="numberOfFrames">Number of frames (default 1).</param>
        /// <returns>A PixelDataInfo for an 8-bit grayscale image.</returns>
        public static PixelDataInfo Grayscale8(ushort rows, ushort columns, int numberOfFrames = 1) =>
            new(rows, columns, 8, 8, 7, 1, 0, 0, numberOfFrames);

        /// <summary>
        /// Creates a default pixel data info for a grayscale 16-bit image.
        /// </summary>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="numberOfFrames">Number of frames (default 1).</param>
        /// <returns>A PixelDataInfo for a 16-bit grayscale image.</returns>
        public static PixelDataInfo Grayscale16(ushort rows, ushort columns, int numberOfFrames = 1) =>
            new(rows, columns, 16, 16, 15, 1, 0, 0, numberOfFrames);

        /// <summary>
        /// Creates a default pixel data info for an RGB 8-bit image.
        /// </summary>
        /// <param name="rows">Number of rows.</param>
        /// <param name="columns">Number of columns.</param>
        /// <param name="numberOfFrames">Number of frames (default 1).</param>
        /// <returns>A PixelDataInfo for an 8-bit RGB image.</returns>
        public static PixelDataInfo Rgb8(ushort rows, ushort columns, int numberOfFrames = 1) =>
            new(rows, columns, 8, 8, 7, 3, 0, 0, numberOfFrames);
    }
}
