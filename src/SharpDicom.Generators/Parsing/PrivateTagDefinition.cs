namespace SharpDicom.Generators.Parsing
{
    /// <summary>
    /// Represents a private DICOM tag definition parsed from vendor dictionary XML.
    /// </summary>
    internal readonly struct PrivateTagDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrivateTagDefinition"/> struct.
        /// </summary>
        /// <param name="creator">The private creator string (owner).</param>
        /// <param name="group">The group number.</param>
        /// <param name="elementOffset">The element offset (0x00-0xFF).</param>
        /// <param name="vr">The value representation.</param>
        /// <param name="vm">The value multiplicity.</param>
        /// <param name="name">The human-readable name.</param>
        /// <param name="keyword">The generated keyword.</param>
        public PrivateTagDefinition(
            string creator,
            ushort group,
            byte elementOffset,
            string vr,
            string vm,
            string name,
            string keyword)
        {
            Creator = creator;
            Group = group;
            ElementOffset = elementOffset;
            VR = vr;
            VM = vm;
            Name = name;
            Keyword = keyword;
        }

        /// <summary>Gets the private creator string (owner).</summary>
        public string Creator { get; }

        /// <summary>Gets the group number.</summary>
        public ushort Group { get; }

        /// <summary>Gets the element offset (0x00-0xFF).</summary>
        public byte ElementOffset { get; }

        /// <summary>Gets the value representation.</summary>
        public string VR { get; }

        /// <summary>Gets the value multiplicity.</summary>
        public string VM { get; }

        /// <summary>Gets the human-readable name.</summary>
        public string Name { get; }

        /// <summary>Gets the generated keyword (PascalCase from name).</summary>
        public string Keyword { get; }
    }
}
