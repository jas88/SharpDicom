namespace SharpDicom.Data
{
    /// <summary>
    /// Represents metadata for a DICOM tag from the data dictionary.
    /// </summary>
    public readonly record struct DicomDictionaryEntry
    {
        /// <summary>
        /// Gets the DICOM tag.
        /// </summary>
        public DicomTag Tag { get; init; }

        /// <summary>
        /// Gets the keyword (e.g., "PatientName", "StudyDate").
        /// </summary>
        public string Keyword { get; init; }

        /// <summary>
        /// Gets the human-readable name (e.g., "Patient's Name", "Study Date").
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the allowed Value Representations for this tag.
        /// </summary>
        /// <remarks>
        /// Some tags allow multiple VRs depending on context (e.g., Pixel Data can be OB or OW).
        /// The first VR in the array is the default.
        /// </remarks>
        public DicomVR[] ValueRepresentations { get; init; }

        /// <summary>
        /// Gets the Value Multiplicity specification.
        /// </summary>
        public ValueMultiplicity VM { get; init; }

        /// <summary>
        /// Gets a value indicating whether this tag is retired.
        /// </summary>
        public bool IsRetired { get; init; }

        /// <summary>
        /// Gets the default (first) Value Representation for this tag.
        /// </summary>
        public DicomVR DefaultVR => ValueRepresentations != null && ValueRepresentations.Length > 0
            ? ValueRepresentations[0]
            : DicomVR.UN;

        /// <summary>
        /// Gets a value indicating whether this tag has multiple allowed Value Representations.
        /// </summary>
        public bool HasMultipleVRs => ValueRepresentations != null && ValueRepresentations.Length > 1;
    }
}
