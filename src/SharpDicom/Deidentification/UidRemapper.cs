using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Remaps UIDs consistently across a de-identification session.
    /// </summary>
    public sealed class UidRemapper : IDisposable
    {
        private readonly IUidStore _store;
        private readonly bool _ownsStore;

        /// <summary>
        /// Creates a UID remapper with a new in-memory store.
        /// </summary>
        public UidRemapper()
            : this(new InMemoryUidStore(), ownsStore: true)
        {
        }

        /// <summary>
        /// Creates a UID remapper with the specified store.
        /// </summary>
        /// <param name="store">The UID store to use.</param>
        /// <param name="ownsStore">Whether this remapper owns the store and should dispose it.</param>
        public UidRemapper(IUidStore store, bool ownsStore = false)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _ownsStore = ownsStore;
        }

        /// <summary>
        /// Gets the underlying UID store.
        /// </summary>
        public IUidStore Store => _store;

        /// <summary>
        /// Remaps a UID to a new consistent value.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="context">Optional context for consistent mapping (e.g., patient ID).</param>
        /// <returns>The remapped UID.</returns>
        public string Remap(string originalUid, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(originalUid))
            {
                return originalUid;
            }

            return _store.GetOrCreateMapping(originalUid.Trim(), context);
        }

        /// <summary>
        /// Remaps all UID elements in a dataset.
        /// </summary>
        /// <param name="dataset">The dataset to process.</param>
        /// <param name="context">Optional context for consistent mapping.</param>
        /// <returns>List of remapped UID information.</returns>
        public IList<UidRemapInfo> RemapDataset(DicomDataset dataset, string? context = null)
        {
            var remapped = new List<UidRemapInfo>();
            RemapDatasetInternal(dataset, context, remapped);
            return remapped;
        }

        private void RemapDatasetInternal(DicomDataset dataset, string? context, List<UidRemapInfo> remapped)
        {
            // Collect tags to process (avoid modifying during enumeration)
            var tagsToProcess = new List<DicomTag>();
            foreach (var element in dataset)
            {
                tagsToProcess.Add(element.Tag);
            }

            foreach (var tag in tagsToProcess)
            {
                var element = dataset[tag];
                if (element == null) continue;

                // Handle sequences recursively
                if (element is DicomSequence seq)
                {
                    foreach (var item in seq.Items)
                    {
                        RemapDatasetInternal(item, context, remapped);
                    }
                    continue;
                }

                // Handle UI VR elements
                if (element.VR == DicomVR.UI && element is DicomStringElement stringElement)
                {
                    var originalUid = stringElement.GetString(DicomEncoding.Default);
                    if (!string.IsNullOrWhiteSpace(originalUid))
                    {
                        var trimmedUid = originalUid!.Trim();
                        var newUid = Remap(trimmedUid, context);

                        if (newUid != trimmedUid)
                        {
                            // Create new element with remapped UID
                            var bytes = System.Text.Encoding.ASCII.GetBytes(newUid);
                            var newElement = new DicomStringElement(tag, DicomVR.UI, bytes);
                            dataset.Add(newElement);

                            remapped.Add(new UidRemapInfo(tag, trimmedUid, newUid));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the original UID from a remapped UID.
        /// </summary>
        /// <param name="remappedUid">The remapped UID.</param>
        /// <param name="originalUid">The original UID if found.</param>
        /// <returns>True if the mapping was found, false otherwise.</returns>
        public bool TryGetOriginal(string remappedUid, out string originalUid)
        {
#pragma warning disable CS8601 // Possible null reference assignment - handled by return value
            return _store.TryGetOriginal(remappedUid, out originalUid);
#pragma warning restore CS8601
        }

        /// <summary>
        /// Gets the number of UID mappings.
        /// </summary>
        public int MappingCount => _store.Count;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsStore)
            {
                _store.Dispose();
            }
        }
    }
}
