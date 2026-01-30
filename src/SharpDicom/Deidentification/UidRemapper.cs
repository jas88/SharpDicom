using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Remaps UIDs consistently across a de-identification session.
    /// </summary>
    /// <remarks>
    /// Preserves standard DICOM UIDs (Transfer Syntax, SOP Class, etc.) while
    /// remapping instance-specific UIDs (Study, Series, SOP Instance, etc.).
    /// </remarks>
    public sealed class UidRemapper : IDisposable
    {
        private readonly IUidStore _store;
        private readonly bool _ownsStore;
        private readonly HashSet<string> _additionalStandardUids = new(StringComparer.Ordinal);

        /// <summary>
        /// The root prefix for all DICOM-defined UIDs that should never be remapped.
        /// </summary>
        public const string DicomRoot = "1.2.840.10008.";

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
        /// Adds a UID to the list of standard UIDs that should never be remapped.
        /// </summary>
        /// <param name="uid">The UID to preserve.</param>
        public void AddStandardUid(string uid)
        {
            if (!string.IsNullOrWhiteSpace(uid))
            {
                _additionalStandardUids.Add(uid.Trim());
            }
        }

        /// <summary>
        /// Checks if a UID is a standard DICOM UID that should not be remapped.
        /// </summary>
        /// <param name="uid">The UID to check.</param>
        /// <returns>True if the UID is a standard UID that should be preserved.</returns>
        public bool IsStandardUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
            {
                return false;
            }

            var trimmed = uid.Trim();

            // Check DICOM-defined UID root (Transfer Syntax, SOP Class, etc.)
            if (trimmed.StartsWith(DicomRoot, StringComparison.Ordinal))
            {
                return true;
            }

            // Check additional standard UIDs added by user
            return _additionalStandardUids.Contains(trimmed);
        }

        /// <summary>
        /// Remaps a UID to a new consistent value.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="context">Optional context for consistent mapping (e.g., patient ID).</param>
        /// <returns>The remapped UID, or the original if it's a standard UID.</returns>
        public string Remap(string originalUid, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(originalUid))
            {
                return originalUid;
            }

            var trimmed = originalUid.Trim();

            // Never remap standard UIDs
            if (IsStandardUid(trimmed))
            {
                return trimmed;
            }

            return _store.GetOrCreateMapping(trimmed, context);
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

                        // Skip standard UIDs
                        if (IsStandardUid(trimmedUid))
                        {
                            continue;
                        }

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
