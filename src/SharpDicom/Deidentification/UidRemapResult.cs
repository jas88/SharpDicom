using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Result of a UID reference walking operation.
    /// </summary>
    /// <remarks>
    /// Contains statistics about the traversal including the number of UIDs remapped,
    /// sequence items visited, and the specific tags that were remapped.
    /// </remarks>
    public sealed class UidRemapResult
    {
        /// <summary>
        /// Gets or sets the number of UID components that were remapped.
        /// </summary>
        /// <remarks>
        /// For multi-valued UIDs (backslash-separated), each component that changes
        /// is counted individually.
        /// </remarks>
        public int UidsRemapped { get; set; }

        /// <summary>
        /// Gets or sets the number of sequence items traversed during the walk.
        /// </summary>
        public int SequenceItemsTraversed { get; set; }

        /// <summary>
        /// Gets the tags that had UIDs remapped during the walk.
        /// </summary>
        /// <remarks>
        /// Useful for diagnostics and auditing which elements were modified.
        /// A tag appears once per element that was modified, regardless of how many
        /// components in a multi-valued UID were remapped.
        /// </remarks>
        public List<DicomTag> RemappedTags { get; } = new();
    }
}
