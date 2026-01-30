using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Storage abstraction for UID mappings with bidirectional lookup and persistence support.
    /// </summary>
    /// <remarks>
    /// Extends the basic IUidStore interface with scope-aware mapping, batch operations,
    /// and JSON export for auditing and reversibility.
    /// </remarks>
    public interface IUidMappingStore : IDisposable
    {
        /// <summary>
        /// Gets or creates a mapping for the original UID.
        /// </summary>
        /// <param name="originalUid">The original UID to map.</param>
        /// <param name="scope">The scope for consistent mapping (e.g., study ID, batch ID).</param>
        /// <returns>The mapped UID (new or existing if already mapped).</returns>
        string GetOrCreateMapping(string originalUid, string scope);

        /// <summary>
        /// Tries to get existing mapping for original UID.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="remappedUid">The remapped UID if found.</param>
        /// <returns>True if a mapping exists, false otherwise.</returns>
        bool TryGetMapping(string originalUid, out string? remappedUid);

        /// <summary>
        /// Tries to get original UID from remapped UID (reverse lookup).
        /// </summary>
        /// <param name="remappedUid">The remapped UID.</param>
        /// <param name="originalUid">The original UID if found.</param>
        /// <returns>True if the mapping exists, false otherwise.</returns>
        bool TryGetOriginal(string remappedUid, out string? originalUid);

        /// <summary>
        /// Adds multiple mappings in a batch operation.
        /// </summary>
        /// <param name="mappings">The mappings to add (Original, Remapped, Scope).</param>
        void AddMappings(IEnumerable<(string Original, string Remapped, string Scope)> mappings);

        /// <summary>
        /// Exports all mappings to JSON format for auditing and reversibility.
        /// </summary>
        /// <param name="output">The stream to write JSON to.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        Task ExportToJsonAsync(Stream output, CancellationToken ct = default);

        /// <summary>
        /// Gets the count of stored mappings.
        /// </summary>
        int Count { get; }
    }

    /// <summary>
    /// Scope for UID mapping consistency.
    /// </summary>
    public enum UidMappingScope
    {
        /// <summary>Each dataset is mapped independently.</summary>
        Dataset,

        /// <summary>All datasets in a study share consistent mappings.</summary>
        Study,

        /// <summary>All datasets in a batch/session share consistent mappings.</summary>
        Batch,

        /// <summary>Global mapping across all operations.</summary>
        Global
    }
}
