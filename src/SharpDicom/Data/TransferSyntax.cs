namespace SharpDicom.Data
{
    /// <summary>
    /// Represents a DICOM Transfer Syntax with encoding properties.
    /// </summary>
    /// <remarks>
    /// Transfer Syntaxes define how DICOM data is encoded, including byte order,
    /// VR encoding (explicit or implicit), and pixel data compression.
    /// </remarks>
    public readonly partial record struct TransferSyntax
    {
        /// <summary>
        /// Gets the UID of this transfer syntax.
        /// </summary>
        public DicomUID UID { get; init; }

        /// <summary>
        /// Gets a value indicating whether this transfer syntax uses Explicit VR encoding.
        /// </summary>
        public bool IsExplicitVR { get; init; }

        /// <summary>
        /// Gets a value indicating whether this transfer syntax uses Little Endian byte order.
        /// </summary>
        public bool IsLittleEndian { get; init; }

        /// <summary>
        /// Gets a value indicating whether this transfer syntax uses encapsulated (compressed) pixel data.
        /// </summary>
        public bool IsEncapsulated { get; init; }

        /// <summary>
        /// Gets a value indicating whether this transfer syntax uses lossy compression.
        /// </summary>
        public bool IsLossy { get; init; }

        /// <summary>
        /// Gets the compression type used by this transfer syntax.
        /// </summary>
        public CompressionType Compression { get; init; }

        /// <summary>
        /// Gets a value indicating whether this is a known standard transfer syntax.
        /// </summary>
        public bool IsKnown { get; init; }

        /// <summary>
        /// Returns a transfer syntax for the specified UID.
        /// </summary>
        /// <param name="uid">The transfer syntax UID.</param>
        /// <returns>A TransferSyntax instance for the UID, with IsKnown=false if unrecognized.</returns>
        public static TransferSyntax FromUID(DicomUID uid)
        {
            // Check well-known transfer syntaxes
            if (uid == ImplicitVRLittleEndian.UID)
                return ImplicitVRLittleEndian;
            if (uid == ExplicitVRLittleEndian.UID)
                return ExplicitVRLittleEndian;
            if (uid == ExplicitVRBigEndian.UID)
                return ExplicitVRBigEndian;
            if (uid == JPEGBaseline.UID)
                return JPEGBaseline;
            if (uid == JPEG2000Lossless.UID)
                return JPEG2000Lossless;
            if (uid == RLELossless.UID)
                return RLELossless;
            if (uid == DeflatedExplicitVRLittleEndian.UID)
                return DeflatedExplicitVRLittleEndian;

            // Unknown transfer syntax - return with IsKnown=false
            return new TransferSyntax
            {
                UID = uid,
                IsExplicitVR = true, // Most common default
                IsLittleEndian = true,
                IsEncapsulated = false,
                IsLossy = false,
                Compression = CompressionType.None,
                IsKnown = false
            };
        }

        // Well-known transfer syntaxes

        /// <summary>
        /// Implicit VR Little Endian: Default Transfer Syntax for DICOM (1.2.840.10008.1.2).
        /// </summary>
        public static readonly TransferSyntax ImplicitVRLittleEndian = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2"),
            IsExplicitVR = false,
            IsLittleEndian = true,
            IsEncapsulated = false,
            IsLossy = false,
            Compression = CompressionType.None,
            IsKnown = true
        };

        /// <summary>
        /// Explicit VR Little Endian (1.2.840.10008.1.2.1).
        /// </summary>
        public static readonly TransferSyntax ExplicitVRLittleEndian = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.1"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = false,
            IsLossy = false,
            Compression = CompressionType.None,
            IsKnown = true
        };

        /// <summary>
        /// Explicit VR Big Endian (1.2.840.10008.1.2.2) - RETIRED.
        /// </summary>
        public static readonly TransferSyntax ExplicitVRBigEndian = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.2"),
            IsExplicitVR = true,
            IsLittleEndian = false,
            IsEncapsulated = false,
            IsLossy = false,
            Compression = CompressionType.None,
            IsKnown = true
        };

        /// <summary>
        /// JPEG Baseline (Process 1): Default Transfer Syntax for Lossy JPEG 8 Bit Image Compression (1.2.840.10008.1.2.4.50).
        /// </summary>
        public static readonly TransferSyntax JPEGBaseline = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.50"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.JPEGBaseline,
            IsKnown = true
        };

        /// <summary>
        /// JPEG 2000 Image Compression (Lossless Only) (1.2.840.10008.1.2.4.90).
        /// </summary>
        public static readonly TransferSyntax JPEG2000Lossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.90"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.JPEG2000Lossless,
            IsKnown = true
        };

        /// <summary>
        /// RLE Lossless (1.2.840.10008.1.2.5).
        /// </summary>
        public static readonly TransferSyntax RLELossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.5"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.RLE,
            IsKnown = true
        };

        /// <summary>
        /// Deflated Explicit VR Little Endian (1.2.840.10008.1.2.1.99).
        /// </summary>
        /// <remarks>
        /// The dataset (after File Meta Information) is deflate-compressed at the stream level.
        /// Pixel data is not separately encapsulated.
        /// </remarks>
        public static readonly TransferSyntax DeflatedExplicitVRLittleEndian = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.1.99"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = false,  // Deflate is at stream level, not pixel data encapsulation
            IsLossy = false,
            Compression = CompressionType.None,  // No pixel-level compression
            IsKnown = true
        };
    }
}
