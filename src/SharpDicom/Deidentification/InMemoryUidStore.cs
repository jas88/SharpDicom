using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// In-memory UID store for single-session de-identification.
    /// </summary>
    /// <remarks>
    /// Use this for simple scenarios where persistence isn't needed.
    /// For batch processing with persistence, use SqliteUidStore.
    /// </remarks>
    public sealed class InMemoryUidStore : IUidStore
    {
        private readonly Dictionary<string, string> _originalToMapped = new();
        private readonly Dictionary<string, string> _mappedToOriginal = new();
        private readonly object _lock = new();

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _originalToMapped.Count;
                }
            }
        }

        /// <inheritdoc/>
        public string GetOrCreateMapping(string originalUid, string? context = null)
        {
            if (string.IsNullOrEmpty(originalUid))
            {
                throw new ArgumentNullException(nameof(originalUid));
            }

            lock (_lock)
            {
                if (_originalToMapped.TryGetValue(originalUid, out var existing))
                {
                    return existing;
                }

                // Generate new UID using UUID-derived 2.25.xxx format
                var newUid = UidGenerator.GenerateUid();

                _originalToMapped[originalUid] = newUid;
                _mappedToOriginal[newUid] = originalUid;

                return newUid;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMapped(string originalUid, out string mappedUid)
        {
            lock (_lock)
            {
#pragma warning disable CS8601 // Possible null reference assignment - handled by return value
                return _originalToMapped.TryGetValue(originalUid, out mappedUid);
#pragma warning restore CS8601
            }
        }

        /// <inheritdoc/>
        public bool TryGetOriginal(string mappedUid, out string originalUid)
        {
            lock (_lock)
            {
#pragma warning disable CS8601 // Possible null reference assignment - handled by return value
                return _mappedToOriginal.TryGetValue(mappedUid, out originalUid);
#pragma warning restore CS8601
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (_lock)
            {
                _originalToMapped.Clear();
                _mappedToOriginal.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // No resources to dispose for in-memory store
        }
    }
}
