using System;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Interface for storing and retrieving UID mappings.
    /// </summary>
    public interface IUidStore : IDisposable
    {
        /// <summary>
        /// Gets or creates a mapped UID for the original UID.
        /// </summary>
        /// <param name="originalUid">The original UID to map.</param>
        /// <param name="context">Optional context (e.g., patient ID) for consistent mapping.</param>
        /// <returns>The mapped UID (new or existing if already mapped).</returns>
        string GetOrCreateMapping(string originalUid, string? context = null);

        /// <summary>
        /// Tries to get the mapped UID for an original UID.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="mappedUid">The mapped UID if found.</param>
        /// <returns>True if a mapping exists, false otherwise.</returns>
        bool TryGetMapped(string originalUid, out string mappedUid);

        /// <summary>
        /// Tries to get the original UID for a mapped UID (reverse lookup).
        /// </summary>
        /// <param name="mappedUid">The mapped UID.</param>
        /// <param name="originalUid">The original UID if found.</param>
        /// <returns>True if the mapping exists, false otherwise.</returns>
        bool TryGetOriginal(string mappedUid, out string originalUid);

        /// <summary>
        /// Gets the number of mappings stored.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Clears all mappings.
        /// </summary>
        void Clear();
    }
}
