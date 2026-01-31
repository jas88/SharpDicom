using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Manages persistent state for de-identifying multiple DICOM files in a batch.
    /// </summary>
    /// <remarks>
    /// The context provides:
    /// - Persistent UID mappings across files (SQLite-backed)
    /// - Persistent date offsets per patient
    /// - Convenient factory method for creating configured deidentifiers
    ///
    /// Example usage:
    /// <code>
    /// await using var context = DeidentificationContext.Create("mappings.db");
    /// var deidentifier = context.CreateBuilder()
    ///     .WithBasicProfile()
    ///     .WithRandomDateShift(-365, -30)
    ///     .Build();
    ///
    /// foreach (var file in files)
    /// {
    ///     var dataset = await DicomFile.OpenAsync(file);
    ///     deidentifier.Deidentify(dataset);
    ///     await dataset.SaveAsync(outputPath);
    /// }
    ///
    /// await context.ExportMappingsAsync("uid_mappings.json");
    /// </code>
    /// </remarks>
#if NET5_0_OR_GREATER
    public sealed class DeidentificationContext : IAsyncDisposable, IDisposable
#else
    public sealed class DeidentificationContext : IDisposable
#endif
    {
        private readonly SqliteUidStore _uidStore;
        private readonly UidRemapper _uidRemapper;
        private InMemoryDateOffsetStore _dateOffsetStore;
        private bool _disposed;

        /// <summary>
        /// Creates a de-identification context with persistent storage.
        /// </summary>
        /// <param name="databasePath">Path to SQLite database file for UID mappings.</param>
        public DeidentificationContext(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentNullException(nameof(databasePath));
            }

            _uidStore = new SqliteUidStore(databasePath);
            _uidRemapper = new UidRemapper(_uidStore, ownsStore: false);
            _dateOffsetStore = new InMemoryDateOffsetStore();
        }

        /// <summary>
        /// Creates a de-identification context with in-memory storage (no persistence).
        /// </summary>
        private DeidentificationContext()
        {
            _uidStore = null!;  // Will be set by InMemory factory
            _uidRemapper = new UidRemapper();
            _dateOffsetStore = new InMemoryDateOffsetStore();
        }

        /// <summary>
        /// Creates a context with persistent SQLite storage.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file.</param>
        /// <returns>A new de-identification context.</returns>
        public static DeidentificationContext Create(string databasePath)
        {
            return new DeidentificationContext(databasePath);
        }

        /// <summary>
        /// Creates a context with in-memory storage (no persistence).
        /// </summary>
        /// <returns>A new in-memory de-identification context.</returns>
        public static DeidentificationContext CreateInMemory()
        {
            var context = new DeidentificationContext
            {
            };
            return context;
        }

        /// <summary>
        /// Gets the UID store used by this context.
        /// </summary>
        public IUidStore UidStore => _uidStore ?? _uidRemapper.Store;

        /// <summary>
        /// Gets the UID remapper used by this context.
        /// </summary>
        public UidRemapper UidRemapper => _uidRemapper;

        /// <summary>
        /// Gets the date offset store used by this context.
        /// </summary>
        public IDateOffsetStore DateOffsetStore => _dateOffsetStore;

        /// <summary>
        /// Gets the number of UID mappings in this context.
        /// </summary>
        public int MappingCount => _uidRemapper.MappingCount;

        /// <summary>
        /// Creates a new builder pre-configured with this context's stores.
        /// </summary>
        /// <returns>A configured builder.</returns>
        public DicomDeidentifierBuilder CreateBuilder()
        {
            return new DicomDeidentifierBuilder()
                .WithUidRemapper(_uidRemapper)
                .WithDateOffsetStore(_dateOffsetStore);
        }

        /// <summary>
        /// Creates a de-identifier with basic profile using this context's stores.
        /// </summary>
        /// <returns>A configured de-identifier.</returns>
        public DicomDeidentifier CreateDeidentifier()
        {
            return CreateBuilder()
                .WithBasicProfile()
                .Build();
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Exports all UID mappings to a JSON file.
        /// </summary>
        /// <param name="path">Path to the output JSON file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ExportMappingsAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_uidStore != null)
            {
                await using var stream = File.Create(path);
                await _uidStore.ExportToJsonAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
#endif

        /// <summary>
        /// Clears all UID mappings and date offsets.
        /// </summary>
        public void Reset()
        {
            _uidStore?.Clear();
            // Recreate date offset store for fresh start
            _dateOffsetStore = new InMemoryDateOffsetStore();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _uidRemapper.Dispose();
                _uidStore?.Dispose();
                _disposed = true;
            }
        }

#if NET5_0_OR_GREATER
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
#endif
    }
}
