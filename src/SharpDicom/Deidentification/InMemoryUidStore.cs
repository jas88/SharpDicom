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
        private readonly Dictionary<string, UidMappingEntry> _originalToMapped = new();
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
            if (string.IsNullOrEmpty(originalUid))
            {
                throw new ArgumentNullException(nameof(originalUid));
            }

            lock (_lock)
            {
                if (_originalToMapped.TryGetValue(originalUid, out var existing))
                {
                    return existing.RemappedUid;
                }

                // Generate new UID using UUID-derived 2.25.xxx format
                var newUid = UidGenerator.GenerateUid();
                var entry = new UidMappingEntry(originalUid, newUid, scope, DateTime.UtcNow);

                _originalToMapped[originalUid] = entry;
                _mappedToOriginal[newUid] = originalUid;

                return newUid;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMapped(string originalUid, out string mappedUid)
        {
            lock (_lock)
            {
                if (_originalToMapped.TryGetValue(originalUid, out var entry))
                {
                    mappedUid = entry.RemappedUid;
                    return true;
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
                if (_originalToMapped.TryGetValue(originalUid, out var entry))
                {
                    remappedUid = entry.RemappedUid;
                    return true;
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
                    var entry = new UidMappingEntry(original, remapped, scope, DateTime.UtcNow);
                    _originalToMapped[original] = entry;
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
                entries = new List<UidMappingEntry>(_originalToMapped.Values);
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
                _originalToMapped.Clear();
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
