using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// In-memory UID store for single-session de-identification.
    /// </summary>
    /// <remarks>
    /// Use this for simple scenarios where persistence isn't needed.
    /// For batch processing with persistence, use SqliteUidStore.
    /// Thread-safe for concurrent access.
    /// </remarks>
    public sealed class InMemoryUidStore : IUidStore, IUidMappingStore
    {
        // Nested dictionary: scope -> (originalUid -> entry)
        // Empty string scope is used for global/scopeless mappings
        private readonly Dictionary<string, Dictionary<string, UidMappingEntry>> _scopedMappings = new();
        private readonly Dictionary<string, string> _mappedToOriginal = new();
        private readonly object _lock = new();

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var scopeDict in _scopedMappings.Values)
                    {
                        count += scopeDict.Count;
                    }
                    return count;
                }
            }
        }

        /// <inheritdoc cref="IUidStore.GetOrCreateMapping"/>
        public string GetOrCreateMapping(string originalUid, string? context = null)
        {
            return GetOrCreateMappingInternal(originalUid, context ?? string.Empty);
        }

        /// <inheritdoc cref="IUidMappingStore.GetOrCreateMapping"/>
        string IUidMappingStore.GetOrCreateMapping(string originalUid, string scope)
        {
            return GetOrCreateMappingInternal(originalUid, scope);
        }

        private string GetOrCreateMappingInternal(string originalUid, string scope)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(originalUid);
            ArgumentException.ThrowIfNullOrEmpty(originalUid);
#else
            if (originalUid == null)
            {
                throw new ArgumentNullException(nameof(originalUid));
            }

            if (originalUid.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(originalUid));
            }
#endif

            // Use empty string as key for null/empty scope
            var scopeKey = scope ?? string.Empty;

            lock (_lock)
            {
                // Get or create the scope dictionary
                if (!_scopedMappings.TryGetValue(scopeKey, out var scopeDict))
                {
                    scopeDict = new Dictionary<string, UidMappingEntry>();
                    _scopedMappings[scopeKey] = scopeDict;
                }

                // Check if mapping already exists for this scope
                if (scopeDict.TryGetValue(originalUid, out var existing))
                {
                    return existing.RemappedUid;
                }

                // Generate new UID using UUID-derived 2.25.xxx format
                var newUid = UidGenerator.GenerateUid();
                var entry = new UidMappingEntry(originalUid, newUid, scopeKey, DateTime.UtcNow);

                scopeDict[originalUid] = entry;
                _mappedToOriginal[newUid] = originalUid;

                return newUid;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMapped(string originalUid, out string mappedUid)
        {
            lock (_lock)
            {
                // Search across all scopes for a mapping (returns first match)
                foreach (var scopeDict in _scopedMappings.Values)
                {
                    if (scopeDict.TryGetValue(originalUid, out var entry))
                    {
                        mappedUid = entry.RemappedUid;
                        return true;
                    }
                }

                mappedUid = null!;
                return false;
            }
        }

        /// <inheritdoc cref="IUidMappingStore.TryGetMapping"/>
        public bool TryGetMapping(string originalUid, out string? remappedUid)
        {
            lock (_lock)
            {
                // Search across all scopes for a mapping (returns first match)
                foreach (var scopeDict in _scopedMappings.Values)
                {
                    if (scopeDict.TryGetValue(originalUid, out var entry))
                    {
                        remappedUid = entry.RemappedUid;
                        return true;
                    }
                }

                remappedUid = null;
                return false;
            }
        }

        /// <inheritdoc cref="IUidStore.TryGetOriginal"/>
        public bool TryGetOriginal(string mappedUid, out string originalUid)
        {
            lock (_lock)
            {
                if (_mappedToOriginal.TryGetValue(mappedUid, out var result))
                {
                    originalUid = result;
                    return true;
                }

                originalUid = null!;
                return false;
            }
        }

        /// <inheritdoc cref="IUidMappingStore.TryGetOriginal"/>
        bool IUidMappingStore.TryGetOriginal(string remappedUid, out string? originalUid)
        {
            lock (_lock)
            {
                if (_mappedToOriginal.TryGetValue(remappedUid, out var result))
                {
                    originalUid = result;
                    return true;
                }

                originalUid = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public void AddMappings(IEnumerable<(string Original, string Remapped, string Scope)> mappings)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(mappings);
#else
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
#endif

            lock (_lock)
            {
                foreach (var (original, remapped, scope) in mappings)
                {
                    var scopeKey = scope ?? string.Empty;

                    // Get or create the scope dictionary
                    if (!_scopedMappings.TryGetValue(scopeKey, out var scopeDict))
                    {
                        scopeDict = new Dictionary<string, UidMappingEntry>();
                        _scopedMappings[scopeKey] = scopeDict;
                    }

                    var entry = new UidMappingEntry(original, remapped, scopeKey, DateTime.UtcNow);
                    scopeDict[original] = entry;
                    _mappedToOriginal[remapped] = original;
                }
            }
        }

        /// <inheritdoc/>
        public async Task ExportToJsonAsync(Stream output, CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(output);
#else
            if (output == null) throw new ArgumentNullException(nameof(output));
#endif

            List<UidMappingEntry> entries;
            lock (_lock)
            {
                entries = new List<UidMappingEntry>();
                foreach (var scopeDict in _scopedMappings.Values)
                {
                    entries.AddRange(scopeDict.Values);
                }
            }

            // Serialize manually to JSON format (avoids System.Text.Json dependency on netstandard2.0
            // and avoids IL2026/IL3050 trim/AOT warnings on modern .NET)
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.Append("  \"exportedAt\": \"").Append(DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).AppendLine("\",");
            sb.Append("  \"mappingCount\": ").Append(entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(",");
            sb.AppendLine("  \"mappings\": [");

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.Append("    {");
                sb.Append("\"originalUid\": \"").Append(EscapeJsonString(entry.OriginalUid)).Append("\", ");
                sb.Append("\"remappedUid\": \"").Append(EscapeJsonString(entry.RemappedUid)).Append("\", ");
                sb.Append("\"scope\": \"").Append(EscapeJsonString(entry.Scope)).Append("\", ");
                sb.Append("\"createdAt\": \"").Append(entry.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Append('"');
                sb.Append(i < entries.Count - 1 ? "}," : "}");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
#if NETSTANDARD2_0
            await output.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
#else
            await output.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
#endif
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            lock (_lock)
            {
                _scopedMappings.Clear();
                _mappedToOriginal.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // No resources to dispose for in-memory store
        }

        /// <summary>
        /// Internal mapping entry with metadata.
        /// </summary>
        private sealed class UidMappingEntry
        {
            public string OriginalUid { get; }
            public string RemappedUid { get; }
            public string Scope { get; }
            public DateTime CreatedAt { get; }

            public UidMappingEntry(string originalUid, string remappedUid, string scope, DateTime createdAt)
            {
                OriginalUid = originalUid;
                RemappedUid = remappedUid;
                Scope = scope;
                CreatedAt = createdAt;
            }
        }

    }
}
