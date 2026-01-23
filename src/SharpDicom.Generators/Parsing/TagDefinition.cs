namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Represents a DICOM tag definition parsed from the standard XML.
    /// </summary>
    internal readonly struct TagDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TagDefinition"/> struct.
        /// </summary>
        public TagDefinition(ushort group, ushort element, string keyword, string name, string[] vrs, string vm, bool isRetired)
        {
            Group = group;
            Element = element;
            Keyword = keyword;
            Name = name;
            VRs = vrs;
            VM = vm;
            IsRetired = isRetired;
        }

        /// <summary>Gets the tag group number.</summary>
        public ushort Group { get; }

        /// <summary>Gets the tag element number.</summary>
        public ushort Element { get; }

        /// <summary>Gets the tag keyword.</summary>
        public string Keyword { get; }

        /// <summary>Gets the tag name.</summary>
        public string Name { get; }

        /// <summary>Gets the value representations.</summary>
        public string[] VRs { get; }

        /// <summary>Gets the value multiplicity.</summary>
        public string VM { get; }

        /// <summary>Gets whether the tag is retired.</summary>
        public bool IsRetired { get; }
    }
}
