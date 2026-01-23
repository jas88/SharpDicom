namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Represents a DICOM UID definition parsed from the standard XML.
    /// </summary>
    internal readonly struct UidDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UidDefinition"/> struct.
        /// </summary>
        public UidDefinition(string value, string keyword, string name, string type, bool isRetired)
        {
            Value = value;
            Keyword = keyword;
            Name = name;
            Type = type;
            IsRetired = isRetired;
        }

        /// <summary>Gets the UID value.</summary>
        public string Value { get; }

        /// <summary>Gets the UID keyword.</summary>
        public string Keyword { get; }

        /// <summary>Gets the UID name.</summary>
        public string Name { get; }

        /// <summary>Gets the UID type.</summary>
        public string Type { get; }

        /// <summary>Gets whether the UID is retired.</summary>
        public bool IsRetired { get; }
    }
}
