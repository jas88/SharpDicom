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
            if (uid == JPEGLossless.UID)
                return JPEGLossless;
            if (uid == JPEG2000Lossless.UID)
                return JPEG2000Lossless;
            if (uid == JPEG2000Lossy.UID)
                return JPEG2000Lossy;
            if (uid == RLELossless.UID)
                return RLELossless;
            if (uid == DeflatedExplicitVRLittleEndian.UID)
                return DeflatedExplicitVRLittleEndian;
            if (uid == JPEGLSLossless.UID)
                return JPEGLSLossless;
            if (uid == JPEGLSNearLossless.UID)
                return JPEGLSNearLossless;
            if (uid == HTJ2KLossless.UID)
                return HTJ2KLossless;
            if (uid == HTJ2KLosslessRPCL.UID)
                return HTJ2KLosslessRPCL;
            if (uid == HTJ2KLossy.UID)
                return HTJ2KLossy;
            if (uid == JPEGExtended.UID)
                return JPEGExtended;
            if (uid == MPEG2MainML.UID)
                return MPEG2MainML;
            if (uid == MPEG2MainHL.UID)
                return MPEG2MainHL;
            if (uid == H264HighProfile41.UID)
                return H264HighProfile41;
            if (uid == H264BDCompatible41.UID)
                return H264BDCompatible41;
            if (uid == H264HighProfile42_2D.UID)
                return H264HighProfile42_2D;
            if (uid == H264HighProfile42_3D.UID)
                return H264HighProfile42_3D;
            if (uid == H264StereoHighProfile42.UID)
                return H264StereoHighProfile42;
            if (uid == HEVCMainProfile51.UID)
                return HEVCMainProfile51;
            if (uid == HEVCMain10Profile51.UID)
                return HEVCMain10Profile51;

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

        /// <summary>
        /// JPEG Lossless, Non-Hierarchical, First-Order Prediction (Process 14, Selection Value 1) (1.2.840.10008.1.2.4.70).
        /// </summary>
        /// <remarks>
        /// This is the default DICOM lossless JPEG transfer syntax using horizontal prediction (predictor 1).
        /// Supports 2-16 bit samples.
        /// </remarks>
        public static readonly TransferSyntax JPEGLossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.70"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.JPEGLossless,
            IsKnown = true
        };

        /// <summary>
        /// JPEG 2000 Image Compression (1.2.840.10008.1.2.4.91).
        /// </summary>
        /// <remarks>
        /// JPEG 2000 lossy compression using irreversible wavelet transform (9/7 filter) and ICT color transform.
        /// </remarks>
        public static readonly TransferSyntax JPEG2000Lossy = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.91"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.JPEG2000Lossy,
            IsKnown = true
        };

        /// <summary>
        /// JPEG-LS Lossless Image Compression (1.2.840.10008.1.2.4.80).
        /// </summary>
        public static readonly TransferSyntax JPEGLSLossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.80"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.JPEGLSLossless,
            IsKnown = true
        };

        /// <summary>
        /// JPEG-LS Lossy (Near-Lossless) Image Compression (1.2.840.10008.1.2.4.81).
        /// </summary>
        public static readonly TransferSyntax JPEGLSNearLossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.81"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.JPEGLSNearLossless,
            IsKnown = true
        };

        /// <summary>
        /// HTJ2K (High Throughput JPEG 2000) Lossless (1.2.840.10008.1.2.4.201).
        /// </summary>
        public static readonly TransferSyntax HTJ2KLossless = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.201"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.HTJ2KLossless,
            IsKnown = true
        };

        /// <summary>
        /// HTJ2K (High Throughput JPEG 2000) Lossless RPCL (1.2.840.10008.1.2.4.202).
        /// </summary>
        public static readonly TransferSyntax HTJ2KLosslessRPCL = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.202"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = false,
            Compression = CompressionType.HTJ2KLossless,
            IsKnown = true
        };

        /// <summary>
        /// HTJ2K (High Throughput JPEG 2000) Lossy (1.2.840.10008.1.2.4.203).
        /// </summary>
        public static readonly TransferSyntax HTJ2KLossy = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.203"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.HTJ2KLossy,
            IsKnown = true
        };

        /// <summary>
        /// JPEG Extended (Process 2 and 4) for 8-bit and 12-bit lossy compression (1.2.840.10008.1.2.4.51).
        /// </summary>
        public static readonly TransferSyntax JPEGExtended = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.51"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.JPEGExtended,
            IsKnown = true
        };

        /// <summary>
        /// MPEG2 Main Profile / Main Level (1.2.840.10008.1.2.4.100).
        /// </summary>
        public static readonly TransferSyntax MPEG2MainML = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.100"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.MPEG2,
            IsKnown = true
        };

        /// <summary>
        /// MPEG2 Main Profile / High Level (1.2.840.10008.1.2.4.101).
        /// </summary>
        public static readonly TransferSyntax MPEG2MainHL = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.101"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.MPEG2,
            IsKnown = true
        };

        /// <summary>
        /// MPEG-4 AVC/H.264 High Profile / Level 4.1 (1.2.840.10008.1.2.4.102).
        /// </summary>
        public static readonly TransferSyntax H264HighProfile41 = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.102"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.H264,
            IsKnown = true
        };

        /// <summary>
        /// MPEG-4 AVC/H.264 BD-compatible High Profile / Level 4.1 (1.2.840.10008.1.2.4.103).
        /// </summary>
        public static readonly TransferSyntax H264BDCompatible41 = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.103"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.H264,
            IsKnown = true
        };

        /// <summary>
        /// MPEG-4 AVC/H.264 High Profile / Level 4.2 For 2D Video (1.2.840.10008.1.2.4.104).
        /// </summary>
        public static readonly TransferSyntax H264HighProfile42_2D = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.104"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.H264,
            IsKnown = true
        };

        /// <summary>
        /// MPEG-4 AVC/H.264 High Profile / Level 4.2 For 3D Video (1.2.840.10008.1.2.4.105).
        /// </summary>
        public static readonly TransferSyntax H264HighProfile42_3D = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.105"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.H264,
            IsKnown = true
        };

        /// <summary>
        /// MPEG-4 AVC/H.264 Stereo High Profile / Level 4.2 (1.2.840.10008.1.2.4.106).
        /// </summary>
        public static readonly TransferSyntax H264StereoHighProfile42 = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.106"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.H264,
            IsKnown = true
        };

        /// <summary>
        /// HEVC/H.265 Main Profile / Level 5.1 (1.2.840.10008.1.2.4.107).
        /// </summary>
        public static readonly TransferSyntax HEVCMainProfile51 = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.107"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.HEVC,
            IsKnown = true
        };

        /// <summary>
        /// HEVC/H.265 Main 10 Profile / Level 5.1 (1.2.840.10008.1.2.4.108).
        /// </summary>
        public static readonly TransferSyntax HEVCMain10Profile51 = new()
        {
            UID = new DicomUID("1.2.840.10008.1.2.4.108"),
            IsExplicitVR = true,
            IsLittleEndian = true,
            IsEncapsulated = true,
            IsLossy = true,
            Compression = CompressionType.HEVC,
            IsKnown = true
        };
    }
}
