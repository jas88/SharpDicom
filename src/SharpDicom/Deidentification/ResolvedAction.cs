namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Concrete actions after resolving compound de-identification actions.
    /// </summary>
    public enum ResolvedAction
    {
        /// <summary>Keep the element unchanged.</summary>
        Keep,

        /// <summary>Remove the element from the dataset.</summary>
        Remove,

        /// <summary>Replace with empty/zero-length value.</summary>
        ReplaceWithEmpty,

        /// <summary>Replace with a VR-appropriate dummy value.</summary>
        ReplaceWithDummy,

        /// <summary>Clean the element (context-aware replacement).</summary>
        Clean,

        /// <summary>Remap the UID to a new consistent value.</summary>
        RemapUid
    }

    /// <summary>
    /// DICOM attribute type for IOD conformance.
    /// </summary>
    public enum DicomAttributeType
    {
        /// <summary>Type 1: Required, must have non-empty value.</summary>
        Type1,

        /// <summary>Type 1C: Conditionally required.</summary>
        Type1C,

        /// <summary>Type 2: Required, may be empty.</summary>
        Type2,

        /// <summary>Type 2C: Conditionally required, may be empty.</summary>
        Type2C,

        /// <summary>Type 3: Optional.</summary>
        Type3
    }
}
