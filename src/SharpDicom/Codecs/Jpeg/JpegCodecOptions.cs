using System;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Options for JPEG baseline encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control how JPEG baseline encoding is performed. The most
    /// important setting is <see cref="Quality"/>, which affects quantization
    /// and therefore the trade-off between file size and image quality.
    /// </para>
    /// <para>
    /// For medical imaging, consider using <see cref="MedicalImaging"/> preset
    /// which uses quality 90 and no chroma subsampling to preserve diagnostic quality.
    /// </para>
    /// </remarks>
    public sealed class JpegCodecOptions
    {
        private int _quality = 75;

        /// <summary>
        /// Gets or sets the quality level (1-100, default 75).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Quality affects quantization table scaling:
        /// - Higher values = less compression, better quality
        /// - Lower values = more compression, lower quality
        /// </para>
        /// <para>
        /// For medical imaging, consider using 90-95 for conservative lossy compression.
        /// Values below 70 may introduce visible artifacts.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if value is not in range 1-100.</exception>
        public int Quality
        {
            get => _quality;
            set
            {
                if (value < 1 || value > 100)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Quality must be between 1 and 100.");
                }
                _quality = value;
            }
        }

        /// <summary>
        /// Gets or sets the chroma subsampling mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Chroma subsampling reduces color resolution to achieve better compression:
        /// - <see cref="ChromaSubsampling.None"/> (4:4:4): Full color resolution
        /// - <see cref="ChromaSubsampling.Horizontal"/> (4:2:2): Halved horizontal color resolution
        /// - <see cref="ChromaSubsampling.Both"/> (4:2:0): Halved horizontal and vertical color resolution
        /// </para>
        /// <para>
        /// Default is <see cref="ChromaSubsampling.None"/> for medical imaging quality.
        /// Subsampling provides better compression but may lose fine color details.
        /// </para>
        /// </remarks>
        public ChromaSubsampling Subsampling { get; set; } = ChromaSubsampling.None;

        /// <summary>
        /// Gets or sets whether to generate JFIF APP0 marker.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The JFIF marker is typically not needed for DICOM images as DICOM provides
        /// its own metadata framework. Default is false.
        /// </para>
        /// <para>
        /// Set to true if the JPEG data will be used outside of DICOM context and
        /// needs to include standard JPEG application markers.
        /// </para>
        /// </remarks>
        public bool IncludeJfifMarker { get; set; }

        /// <summary>
        /// Gets or sets whether to generate optimized Huffman tables.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When true, the encoder will scan the image data and build optimal
        /// Huffman tables tailored to the specific image. This produces smaller
        /// files (typically 2-5% smaller) but requires two passes over the data.
        /// </para>
        /// <para>
        /// When false (default), the encoder uses standard Huffman tables from
        /// ITU-T.81 Annex K. This is faster and produces compliant output.
        /// </para>
        /// </remarks>
        public bool OptimizeHuffmanTables { get; set; }

        /// <summary>
        /// Gets or sets whether to use arithmetic coding instead of Huffman coding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Arithmetic coding typically achieves 5-10% better compression than Huffman
        /// coding but is slower. Not all DICOM viewers support arithmetic-coded JPEG.
        /// </para>
        /// <para>
        /// Default is false (Huffman coding) for maximum compatibility.
        /// </para>
        /// </remarks>
        public bool UseArithmeticCoding { get; set; }

        /// <summary>
        /// Gets or sets the restart interval in MCU (Minimum Coded Unit) rows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Restart markers allow partial decoding and error recovery. A value of 0
        /// means no restart markers are inserted.
        /// </para>
        /// <para>
        /// For large images, consider using restart intervals of 1-4 MCU rows
        /// to enable parallel decoding and improve error resilience.
        /// </para>
        /// </remarks>
        public int RestartInterval { get; set; }

        /// <summary>
        /// Gets or sets whether to use progressive encoding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Progressive JPEG encodes the image in multiple scans, allowing quick
        /// preview at lower quality followed by refinement. This is useful for
        /// network transmission but requires more memory during encoding/decoding.
        /// </para>
        /// <para>
        /// Default is false (sequential encoding) for DICOM compatibility.
        /// Progressive JPEG uses a different transfer syntax in DICOM.
        /// </para>
        /// </remarks>
        public bool ProgressiveEncoding { get; set; }

        /// <summary>
        /// Default options for medical imaging (quality 90, no subsampling).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This preset is optimized for medical imaging where preserving diagnostic
        /// quality is important:
        /// - Quality 90 provides high fidelity with reasonable compression
        /// - No chroma subsampling preserves color accuracy
        /// - Standard Huffman tables for maximum compatibility
        /// </para>
        /// </remarks>
        public static JpegCodecOptions MedicalImaging { get; } = new()
        {
            Quality = 90,
            Subsampling = ChromaSubsampling.None,
            OptimizeHuffmanTables = false,
            UseArithmeticCoding = false
        };

        /// <summary>
        /// Default options (quality 75, no subsampling).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Standard JPEG quality settings suitable for general use:
        /// - Quality 75 provides good balance of size and quality
        /// - No chroma subsampling by default
        /// </para>
        /// </remarks>
        public static JpegCodecOptions Default { get; } = new();

        /// <summary>
        /// High compression options (quality 50, 4:2:0 subsampling).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use with caution for medical imaging. This preset prioritizes
        /// file size over quality and may introduce visible artifacts.
        /// </para>
        /// </remarks>
        public static JpegCodecOptions HighCompression { get; } = new()
        {
            Quality = 50,
            Subsampling = ChromaSubsampling.Both,
            OptimizeHuffmanTables = true
        };

        /// <summary>
        /// Creates a copy of these options with modified quality.
        /// </summary>
        /// <param name="quality">The new quality value (1-100).</param>
        /// <returns>A new JpegCodecOptions instance with the specified quality.</returns>
        public JpegCodecOptions WithQuality(int quality)
        {
            return new JpegCodecOptions
            {
                Quality = quality,
                Subsampling = Subsampling,
                IncludeJfifMarker = IncludeJfifMarker,
                OptimizeHuffmanTables = OptimizeHuffmanTables,
                UseArithmeticCoding = UseArithmeticCoding,
                RestartInterval = RestartInterval,
                ProgressiveEncoding = ProgressiveEncoding
            };
        }

        /// <summary>
        /// Creates a copy of these options with modified subsampling.
        /// </summary>
        /// <param name="subsampling">The new subsampling mode.</param>
        /// <returns>A new JpegCodecOptions instance with the specified subsampling.</returns>
        public JpegCodecOptions WithSubsampling(ChromaSubsampling subsampling)
        {
            return new JpegCodecOptions
            {
                Quality = Quality,
                Subsampling = subsampling,
                IncludeJfifMarker = IncludeJfifMarker,
                OptimizeHuffmanTables = OptimizeHuffmanTables,
                UseArithmeticCoding = UseArithmeticCoding,
                RestartInterval = RestartInterval,
                ProgressiveEncoding = ProgressiveEncoding
            };
        }
    }

    /// <summary>
    /// Chroma subsampling modes for JPEG encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chroma subsampling exploits the human visual system's lower sensitivity
    /// to color details compared to luminance. By storing color (chroma) at
    /// lower resolution than brightness (luma), significant compression can
    /// be achieved with minimal perceptual quality loss.
    /// </para>
    /// </remarks>
    public enum ChromaSubsampling
    {
        /// <summary>
        /// No subsampling (4:4:4) - full color resolution.
        /// </summary>
        /// <remarks>
        /// Each pixel has its own Y, Cb, and Cr values.
        /// Best quality, largest file size.
        /// Recommended for medical imaging.
        /// </remarks>
        None = 0,

        /// <summary>
        /// Horizontal 2:1 subsampling (4:2:2).
        /// </summary>
        /// <remarks>
        /// Chroma resolution halved horizontally.
        /// Two pixels share one Cb/Cr pair.
        /// Good balance of quality and compression.
        /// </remarks>
        Horizontal = 1,

        /// <summary>
        /// Horizontal and vertical 2:1 subsampling (4:2:0).
        /// </summary>
        /// <remarks>
        /// Chroma resolution halved both horizontally and vertically.
        /// Four pixels share one Cb/Cr pair.
        /// Maximum compression, may lose fine color details.
        /// Common for consumer photography and video.
        /// </remarks>
        Both = 2
    }
}
