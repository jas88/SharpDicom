using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Central UID mapping coordination with standard UID preservation.
    /// </summary>
    /// <remarks>
    /// This facade provides consistent UID remapping while ensuring that
    /// standard DICOM UIDs (Transfer Syntax, SOP Class, Coding Scheme, etc.)
    /// are never remapped, as per PS3.15 requirements.
    /// </remarks>
    public sealed class UidMapper : IDisposable
    {
        /// <summary>
        /// The root prefix for all DICOM-defined UIDs.
        /// </summary>
        public const string DicomRoot = "1.2.840.10008.";

        /// <summary>
        /// Common Transfer Syntax UID prefix.
        /// </summary>
        public const string TransferSyntaxRoot = "1.2.840.10008.1.2";

        /// <summary>
        /// Common SOP Class UID prefix.
        /// </summary>
        public const string SopClassRoot = "1.2.840.10008.5.1.4";

        /// <summary>
        /// Storage SOP Class UID prefix.
        /// </summary>
        public const string StorageSopClassRoot = "1.2.840.10008.5.1.4.1";

        private readonly IUidMappingStore _store;
        private readonly bool _ownsStore;
        private readonly UidMappingScope _defaultScope;
        private readonly HashSet<string> _additionalStandardUids;

        /// <summary>
        /// Creates a UID mapper with a new in-memory store.
        /// </summary>
        /// <param name="defaultScope">Default scope for mappings.</param>
        public UidMapper(UidMappingScope defaultScope = UidMappingScope.Batch)
            : this(new InMemoryUidStore(), ownsStore: true, defaultScope)
        {
        }

        /// <summary>
        /// Creates a UID mapper with the specified store.
        /// </summary>
        /// <param name="store">The UID mapping store to use.</param>
        /// <param name="ownsStore">Whether this mapper owns the store and should dispose it.</param>
        /// <param name="defaultScope">Default scope for mappings.</param>
        public UidMapper(IUidMappingStore store, bool ownsStore = false, UidMappingScope defaultScope = UidMappingScope.Batch)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _ownsStore = ownsStore;
            _defaultScope = defaultScope;
            _additionalStandardUids = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the underlying UID mapping store.
        /// </summary>
        public IUidMappingStore Store => _store;

        /// <summary>
        /// Gets the default scope for mappings.
        /// </summary>
        public UidMappingScope DefaultScope => _defaultScope;

        /// <summary>
        /// Gets the count of UID mappings.
        /// </summary>
        public int MappingCount => _store.Count;

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

            // Check DICOM-defined UID root
            if (trimmed.StartsWith(DicomRoot, StringComparison.Ordinal))
            {
                return true;
            }

            // Check additional standard UIDs
            return _additionalStandardUids.Contains(trimmed);
        }

        /// <summary>
        /// Maps a UID to a new value, preserving standard UIDs.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="scope">Optional scope for the mapping. Uses default scope if null.</param>
        /// <returns>The mapped UID (or original if it's a standard UID).</returns>
        public string Map(string originalUid, string? scope = null)
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

            var effectiveScope = scope ?? ScopeToString(_defaultScope);
            return _store.GetOrCreateMapping(trimmed, effectiveScope);
        }

        /// <summary>
        /// Tries to get the original UID from a remapped UID.
        /// </summary>
        /// <param name="remappedUid">The remapped UID.</param>
        /// <param name="originalUid">The original UID if found.</param>
        /// <returns>True if the mapping was found, false otherwise.</returns>
        public bool TryGetOriginal(string remappedUid, out string? originalUid)
        {
            return _store.TryGetOriginal(remappedUid, out originalUid);
        }

        /// <summary>
        /// Tries to get the remapped UID from an original UID.
        /// </summary>
        /// <param name="originalUid">The original UID.</param>
        /// <param name="remappedUid">The remapped UID if found.</param>
        /// <returns>True if the mapping was found, false otherwise.</returns>
        public bool TryGetMapping(string originalUid, out string? remappedUid)
        {
            return _store.TryGetMapping(originalUid, out remappedUid);
        }

        /// <summary>
        /// Exports all mappings to JSON for auditing and reversibility.
        /// </summary>
        /// <param name="output">The stream to write JSON to.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task ExportMappingsAsync(Stream output, CancellationToken ct = default)
        {
            return _store.ExportToJsonAsync(output, ct);
        }

        /// <summary>
        /// Exports all mappings to a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the output file.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task ExportMappingsAsync(string filePath, CancellationToken ct = default)
        {
            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await _store.ExportToJsonAsync(stream, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds multiple mappings in a batch operation.
        /// </summary>
        /// <param name="mappings">The mappings to add.</param>
        public void AddMappings(IEnumerable<(string Original, string Remapped, string Scope)> mappings)
        {
            _store.AddMappings(mappings);
        }

        private static string ScopeToString(UidMappingScope scope)
        {
            return scope switch
            {
                UidMappingScope.Dataset => "dataset",
                UidMappingScope.Study => "study",
                UidMappingScope.Batch => "batch",
                UidMappingScope.Global => "global",
                _ => "batch"
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ownsStore && _store is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
