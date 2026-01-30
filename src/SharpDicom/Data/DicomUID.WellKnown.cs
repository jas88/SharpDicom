namespace SharpDicom.Data
{
    /// <summary>
    /// Well-known DICOM UID constants.
    /// </summary>
    public readonly partial struct DicomUID
    {
        #region Verification SOP Class

        /// <summary>
        /// Verification SOP Class UID (1.2.840.10008.1.1).
        /// </summary>
        /// <remarks>
        /// Used for C-ECHO operations to verify DICOM connectivity.
        /// See DICOM PS3.4 Annex A.4.
        /// </remarks>
        public static readonly DicomUID Verification = new("1.2.840.10008.1.1");

        #endregion

        #region Transfer Syntax UIDs

        /// <summary>
        /// Implicit VR Little Endian Transfer Syntax UID (1.2.840.10008.1.2).
        /// </summary>
        /// <remarks>
        /// The default DICOM transfer syntax.
        /// </remarks>
        public static readonly DicomUID ImplicitVRLittleEndian = new("1.2.840.10008.1.2");

        /// <summary>
        /// Explicit VR Little Endian Transfer Syntax UID (1.2.840.10008.1.2.1).
        /// </summary>
        public static readonly DicomUID ExplicitVRLittleEndian = new("1.2.840.10008.1.2.1");

        /// <summary>
        /// Explicit VR Big Endian Transfer Syntax UID (1.2.840.10008.1.2.2).
        /// </summary>
        /// <remarks>
        /// Retired but still encountered in legacy systems.
        /// </remarks>
        public static readonly DicomUID ExplicitVRBigEndian = new("1.2.840.10008.1.2.2");

        /// <summary>
        /// RLE Lossless Transfer Syntax UID (1.2.840.10008.1.2.5).
        /// </summary>
        public static readonly DicomUID RLELossless = new("1.2.840.10008.1.2.5");

        /// <summary>
        /// JPEG Baseline (Process 1) Transfer Syntax UID (1.2.840.10008.1.2.4.50).
        /// </summary>
        public static readonly DicomUID JPEGBaseline = new("1.2.840.10008.1.2.4.50");

        /// <summary>
        /// JPEG Extended (Process 2 &amp; 4) Transfer Syntax UID (1.2.840.10008.1.2.4.51).
        /// </summary>
        public static readonly DicomUID JPEGExtended = new("1.2.840.10008.1.2.4.51");

        /// <summary>
        /// JPEG Lossless, Non-Hierarchical (Process 14) Transfer Syntax UID (1.2.840.10008.1.2.4.57).
        /// </summary>
        public static readonly DicomUID JPEGLossless = new("1.2.840.10008.1.2.4.57");

        /// <summary>
        /// JPEG Lossless, Non-Hierarchical, First-Order Prediction (Process 14, Selection Value 1)
        /// Transfer Syntax UID (1.2.840.10008.1.2.4.70).
        /// </summary>
        /// <remarks>
        /// Default DICOM lossless JPEG transfer syntax.
        /// </remarks>
        public static readonly DicomUID JPEGLosslessSV1 = new("1.2.840.10008.1.2.4.70");

        /// <summary>
        /// JPEG 2000 Image Compression (Lossless Only) Transfer Syntax UID (1.2.840.10008.1.2.4.90).
        /// </summary>
        public static readonly DicomUID JPEG2000Lossless = new("1.2.840.10008.1.2.4.90");

        /// <summary>
        /// JPEG 2000 Image Compression Transfer Syntax UID (1.2.840.10008.1.2.4.91).
        /// </summary>
        public static readonly DicomUID JPEG2000 = new("1.2.840.10008.1.2.4.91");

        /// <summary>
        /// JPEG-LS Lossless Image Compression Transfer Syntax UID (1.2.840.10008.1.2.4.80).
        /// </summary>
        public static readonly DicomUID JPEGLSLossless = new("1.2.840.10008.1.2.4.80");

        /// <summary>
        /// JPEG-LS Lossy (Near-Lossless) Image Compression Transfer Syntax UID (1.2.840.10008.1.2.4.81).
        /// </summary>
        public static readonly DicomUID JPEGLSNearLossless = new("1.2.840.10008.1.2.4.81");

        #endregion

        #region Query/Retrieve SOP Classes

        /// <summary>
        /// Patient Root Query/Retrieve Information Model - FIND UID (1.2.840.10008.5.1.4.1.2.1.1).
        /// </summary>
        public static readonly DicomUID PatientRootQueryRetrieveFind = new("1.2.840.10008.5.1.4.1.2.1.1");

        /// <summary>
        /// Patient Root Query/Retrieve Information Model - MOVE UID (1.2.840.10008.5.1.4.1.2.1.2).
        /// </summary>
        public static readonly DicomUID PatientRootQueryRetrieveMove = new("1.2.840.10008.5.1.4.1.2.1.2");

        /// <summary>
        /// Patient Root Query/Retrieve Information Model - GET UID (1.2.840.10008.5.1.4.1.2.1.3).
        /// </summary>
        public static readonly DicomUID PatientRootQueryRetrieveGet = new("1.2.840.10008.5.1.4.1.2.1.3");

        /// <summary>
        /// Study Root Query/Retrieve Information Model - FIND UID (1.2.840.10008.5.1.4.1.2.2.1).
        /// </summary>
        public static readonly DicomUID StudyRootQueryRetrieveFind = new("1.2.840.10008.5.1.4.1.2.2.1");

        /// <summary>
        /// Study Root Query/Retrieve Information Model - MOVE UID (1.2.840.10008.5.1.4.1.2.2.2).
        /// </summary>
        public static readonly DicomUID StudyRootQueryRetrieveMove = new("1.2.840.10008.5.1.4.1.2.2.2");

        /// <summary>
        /// Study Root Query/Retrieve Information Model - GET UID (1.2.840.10008.5.1.4.1.2.2.3).
        /// </summary>
        public static readonly DicomUID StudyRootQueryRetrieveGet = new("1.2.840.10008.5.1.4.1.2.2.3");

        #endregion

        #region Storage SOP Classes (Common)

        /// <summary>
        /// Secondary Capture Image Storage UID (1.2.840.10008.5.1.4.1.1.7).
        /// </summary>
        public static readonly DicomUID SecondaryCaptureImageStorage = new("1.2.840.10008.5.1.4.1.1.7");

        /// <summary>
        /// CT Image Storage UID (1.2.840.10008.5.1.4.1.1.2).
        /// </summary>
        public static readonly DicomUID CTImageStorage = new("1.2.840.10008.5.1.4.1.1.2");

        /// <summary>
        /// MR Image Storage UID (1.2.840.10008.5.1.4.1.1.4).
        /// </summary>
        public static readonly DicomUID MRImageStorage = new("1.2.840.10008.5.1.4.1.1.4");

        /// <summary>
        /// Digital X-Ray Image Storage - For Presentation UID (1.2.840.10008.5.1.4.1.1.1.1).
        /// </summary>
        public static readonly DicomUID DigitalXRayImageStorageForPresentation = new("1.2.840.10008.5.1.4.1.1.1.1");

        /// <summary>
        /// Digital X-Ray Image Storage - For Processing UID (1.2.840.10008.5.1.4.1.1.1.1.1).
        /// </summary>
        public static readonly DicomUID DigitalXRayImageStorageForProcessing = new("1.2.840.10008.5.1.4.1.1.1.1.1");

        #endregion
    }
}
