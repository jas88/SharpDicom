namespace FellowOakDicom
{
    /// <summary>
    /// Wrapper for tag dictionary information, matching fo-dicom's DicomDictionaryEntry.
    /// </summary>
    public sealed class DicomDictionaryEntry
    {
        /// <summary>
        /// Gets the human-readable name of the tag.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the keyword for the tag.
        /// </summary>
        public string Keyword { get; }

        /// <summary>
        /// Gets the tag this entry describes.
        /// </summary>
        public DicomTag Tag { get; }

        internal DicomDictionaryEntry(SharpDicom.Data.DicomDictionaryEntry inner, DicomTag tag)
        {
            Name = inner.Name;
            Keyword = inner.Keyword;
            Tag = tag;
        }

        private DicomDictionaryEntry(string name, string keyword, DicomTag tag)
        {
            Name = name;
            Keyword = keyword;
            Tag = tag;
        }

        /// <summary>
        /// Creates a fallback entry for unknown tags, matching fo-dicom behavior.
        /// </summary>
        internal static DicomDictionaryEntry CreateUnknown(DicomTag tag) =>
            new DicomDictionaryEntry("Unknown Tag", "UnknownTag", tag);
    }
}
