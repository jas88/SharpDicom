namespace SharpDicom.Deidentification;

/// <summary>
/// De-identification action codes per DICOM PS3.15 Section E.1.
/// </summary>
/// <remarks>
/// These action codes define what should be done with each DICOM attribute
/// during de-identification. The numeric values correspond to ASCII codes
/// of the single-letter action codes used in PS3.15 Table E.1-1.
/// </remarks>
public enum DeidentificationAction : byte
{
    /// <summary>
    /// None - no action defined, attribute not in standard profile.
    /// Used for attributes that are not covered by the de-identification profile
    /// and should be handled according to the RemovePrivateTags or other settings.
    /// </summary>
    None = 0,

    /// <summary>
    /// D - replace with a non-zero length dummy value consistent with the VR.
    /// This is the default action for Type 1/2 attributes that must retain a value.
    /// </summary>
    Dummy = (byte)'D',

    /// <summary>
    /// Z - replace with a zero-length value if possible (Type 2/3 only).
    /// For Type 1 attributes, use Dummy instead.
    /// </summary>
    Zero = (byte)'Z',

    /// <summary>
    /// X - remove the attribute entirely.
    /// Used for attributes that should not be present in de-identified datasets.
    /// </summary>
    Remove = (byte)'X',

    /// <summary>
    /// K - keep the attribute unchanged.
    /// The attribute is retained as-is. Nested sequences will still be processed.
    /// </summary>
    Keep = (byte)'K',

    /// <summary>
    /// C - clean (replace with non-identifying value preserving meaning).
    /// Used for attributes like descriptions that may contain PHI but should
    /// retain their semantic content.
    /// </summary>
    Clean = (byte)'C',

    /// <summary>
    /// U - replace UID with a consistently remapped UID.
    /// All references to the same UID within a study will be replaced with
    /// the same new UID to maintain referential integrity.
    /// </summary>
    UidRemap = (byte)'U'
}
