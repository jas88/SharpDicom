using System.Linq;

namespace SharpDicom.Data
{
    /// <summary>
    /// Provides access to the DICOM data dictionary for tag metadata lookups.
    /// </summary>
    /// <remarks>
    /// The dictionary is populated by generated code from the DICOM standard.
    /// Uses the generated GeneratedDictionaryData class for lookups.
    /// Instance methods are used (rather than static) to allow future extension
    /// with custom dictionaries and to support dependency injection/mocking.
    /// </remarks>
#pragma warning disable CA1822 // Mark members as static - instance pattern intentional for extensibility
    public sealed class DicomDictionary
    {
        private static readonly DicomDictionary _default = new();

        /// <summary>
        /// Gets the default singleton instance of the DICOM dictionary.
        /// </summary>
        public static DicomDictionary Default => _default;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomDictionary"/> class.
        /// </summary>
        internal DicomDictionary()
        {
        }

        /// <summary>
        /// Gets the dictionary entry for the specified tag.
        /// </summary>
        /// <param name="tag">The DICOM tag to look up.</param>
        /// <returns>The dictionary entry if found; otherwise, null.</returns>
        public DicomDictionaryEntry? GetEntry(DicomTag tag)
        {
            var entry = GeneratedDictionaryData.GetTag(tag.Group, tag.Element);
            if (entry == null)
                return null;

            return ConvertTagEntry(entry.Value);
        }

        /// <summary>
        /// Gets the dictionary entry for the specified keyword.
        /// </summary>
        /// <param name="keyword">The keyword to look up (case-insensitive).</param>
        /// <returns>The dictionary entry if found; otherwise, null.</returns>
        public DicomDictionaryEntry? GetEntryByKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return null;

            var entry = GeneratedDictionaryData.GetTag(keyword);
            if (entry == null)
                return null;

            return ConvertTagEntry(entry.Value);
        }

        /// <summary>
        /// Determines whether the dictionary contains an entry for the specified tag.
        /// </summary>
        /// <param name="tag">The DICOM tag to check.</param>
        /// <returns>true if an entry exists; otherwise, false.</returns>
        public bool Contains(DicomTag tag)
        {
            return GeneratedDictionaryData.GetTag(tag.Group, tag.Element) != null;
        }

        private static DicomDictionaryEntry ConvertTagEntry(DicomTagEntry entry)
        {
            return new DicomDictionaryEntry
            {
                Tag = new DicomTag(entry.Group, entry.Element),
                Keyword = entry.Keyword,
                Name = entry.Name,
                ValueRepresentations = entry.VRs.Select(vr => new DicomVR(vr)).ToArray(),
                VM = ValueMultiplicity.Parse(entry.VM),
                IsRetired = entry.IsRetired
            };
        }
    }
#pragma warning restore CA1822
}
