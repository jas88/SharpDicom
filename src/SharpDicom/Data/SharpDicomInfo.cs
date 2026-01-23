namespace SharpDicom.Data
{
    /// <summary>
    /// Contains SharpDicom implementation identification information.
    /// </summary>
    /// <remarks>
    /// These values are used in File Meta Information when writing DICOM files.
    /// The Implementation Class UID uses the 2.25 prefix (UUID-derived) format.
    /// </remarks>
    public static class SharpDicomInfo
    {
        /// <summary>
        /// The SharpDicom Implementation Class UID.
        /// </summary>
        /// <remarks>
        /// Uses 2.25 prefix (UUID-derived) format: 2.25.{UUID as decimal}.
        /// This UID uniquely identifies the SharpDicom implementation.
        /// </remarks>
        public static readonly DicomUID ImplementationClassUID =
            new("2.25.336851275958757810911461898545210578371");

        /// <summary>
        /// The SharpDicom Implementation Version Name.
        /// </summary>
        /// <remarks>
        /// Maximum 16 characters per DICOM SH VR specification.
        /// Format: SHARPDICOM_major_minor
        /// </remarks>
        public const string ImplementationVersionName = "SHARPDICOM_1_0";
    }
}
