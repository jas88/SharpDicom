namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Specifies the format used for DICOM tag keys in BSON documents.
/// </summary>
public enum BsonTagKeyFormat
{
    /// <summary>
    /// Eight-character hexadecimal, e.g. "00100010".
    /// This is the default format, compact and widely used in MongoDB DICOM stores.
    /// </summary>
    Hex8 = 0,

    /// <summary>
    /// Dotted hexadecimal, e.g. "0010.0010".
    /// Matches the common DICOM display convention with a group-element separator.
    /// </summary>
    Dotted = 1,

    /// <summary>
    /// Dictionary keyword, e.g. "PatientName".
    /// Falls back to <see cref="Hex8"/> for tags not found in the dictionary.
    /// </summary>
    Keyword = 2,
}
