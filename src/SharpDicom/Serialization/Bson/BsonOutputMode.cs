namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Specifies the output mode for BSON serialization.
/// </summary>
public enum BsonOutputMode
{
    /// <summary>
    /// Native MongoDB-optimized output. Dual-stores numeric/date VRs for efficient
    /// querying while preserving original DICOM string values in Raw fields.
    /// This is the default mode.
    /// </summary>
    MongoNative = 0,

    /// <summary>
    /// DICOM JSON output per PS3.18 Annex F (DICOMweb).
    /// Produces strict DICOM JSON Model objects suitable for DICOMweb STOW/WADO-RS.
    /// Handled in a separate plan.
    /// </summary>
    DicomJson = 1,
}
