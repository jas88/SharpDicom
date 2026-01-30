using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// SQLite-backed persistent UID store for batch de-identification.
    /// </summary>
    /// <remarks>
    /// Uses WAL mode for better concurrency and thread-safe read/write.
    /// Supports bidirectional lookup and JSON export for auditing.
    /// </remarks>
    public sealed class SqliteUidStore : IUidMappingStore
    {
        private readonly string _connectionString;
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>
        /// Creates a new SQLite UID store with a database file.
        /// </summary>
        /// <param name="dbPath">Path to the SQLite database file.</param>
        public SqliteUidStore(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                throw new ArgumentException("Database path cannot be null or empty", nameof(dbPath));
            }

            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        /// <summary>
        /// Creates a new SQLite UID store with the specified connection string.
        /// </summary>
        /// <param name="connectionString">SQLite connection string.</param>
        /// <param name="isConnectionString">Dummy parameter to differentiate from path constructor.</param>
        public SqliteUidStore(string connectionString, bool isConnectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
            }

            _connectionString = connectionString;
            _ = isConnectionString; // Unused, just for signature differentiation
            InitializeDatabase();
        }

        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private void InitializeDatabase()
        {
            using var connection = CreateConnection();

            // Enable WAL mode for better concurrency
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }

            // Create table if not exists
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS uid_mappings (
                        original_uid TEXT PRIMARY KEY NOT NULL,
                        remapped_uid TEXT NOT NULL UNIQUE,
                        scope TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS idx_remapped ON uid_mappings(remapped_uid);
                    CREATE INDEX IF NOT EXISTS idx_scope ON uid_mappings(scope, created_at);
                ";
                cmd.ExecuteNonQuery();
            }
        }

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                ThrowIfDisposed();
                lock (_lock)
                {
                    using var connection = CreateConnection();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM uid_mappings;";
                    return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        /// <inheritdoc/>
        public string GetOrCreateMapping(string originalUid, string scope)
        {
            ThrowIfDisposed();
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(originalUid);
#else
            if (originalUid == null) throw new ArgumentNullException(nameof(originalUid));
#endif
            if (originalUid.Length == 0)
            {
                throw new ArgumentException("UID cannot be empty", nameof(originalUid));
            }

            lock (_lock)
            {
                using var connection = CreateConnection();

                // Try to get existing mapping
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT remapped_uid FROM uid_mappings WHERE original_uid = @original;";
                    cmd.Parameters.AddWithValue("@original", originalUid);

                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return (string)result;
                    }
                }

                // Create new mapping
                var newUid = UidGenerator.GenerateUid();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO uid_mappings (original_uid, remapped_uid, scope, created_at)
                        VALUES (@original, @remapped, @scope, @created);
                    ";
                    cmd.Parameters.AddWithValue("@original", originalUid);
                    cmd.Parameters.AddWithValue("@remapped", newUid);
                    cmd.Parameters.AddWithValue("@scope", scope ?? string.Empty);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }

                return newUid;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMapping(string originalUid, out string? remappedUid)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT remapped_uid FROM uid_mappings WHERE original_uid = @original;";
                cmd.Parameters.AddWithValue("@original", originalUid);

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    remappedUid = (string)result;
                    return true;
                }

                remappedUid = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public bool TryGetOriginal(string remappedUid, out string? originalUid)
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT original_uid FROM uid_mappings WHERE remapped_uid = @remapped;";
                cmd.Parameters.AddWithValue("@remapped", remappedUid);

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    originalUid = (string)result;
                    return true;
                }

                originalUid = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public void AddMappings(IEnumerable<(string Original, string Remapped, string Scope)> mappings)
        {
            ThrowIfDisposed();
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(mappings);
#else
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
#endif

            lock (_lock)
            {
                using var connection = CreateConnection();
                using var transaction = connection.BeginTransaction();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO uid_mappings (original_uid, remapped_uid, scope, created_at)
                        VALUES (@original, @remapped, @scope, @created);
                    ";

                    var originalParam = cmd.Parameters.Add("@original", SqliteType.Text);
                    var remappedParam = cmd.Parameters.Add("@remapped", SqliteType.Text);
                    var scopeParam = cmd.Parameters.Add("@scope", SqliteType.Text);
                    var createdParam = cmd.Parameters.Add("@created", SqliteType.Text);

                    var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

                    foreach (var (original, remapped, scope) in mappings)
                    {
                        originalParam.Value = original;
                        remappedParam.Value = remapped;
                        scopeParam.Value = scope ?? string.Empty;
                        createdParam.Value = now;
                        cmd.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task ExportToJsonAsync(Stream output, CancellationToken ct = default)
        {
            ThrowIfDisposed();
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(output);
#else
            if (output == null) throw new ArgumentNullException(nameof(output));
#endif

            var entries = new List<(string OriginalUid, string RemappedUid, string Scope, string CreatedAt)>();

            lock (_lock)
            {
                using var connection = CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT original_uid, remapped_uid, scope, created_at FROM uid_mappings ORDER BY created_at;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    entries.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3)
                    ));
                }
            }

            // Build JSON manually to avoid System.Text.Json AOT issues
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
                sb.Append("\"createdAt\": \"").Append(EscapeJsonString(entry.CreatedAt)).Append('"');
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

        /// <summary>
        /// Clears all mappings from the database.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            lock (_lock)
            {
                using var connection = CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM uid_mappings;";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Gets all mappings for a specific scope.
        /// </summary>
        /// <param name="scope">The scope to filter by.</param>
        /// <returns>Enumerable of mappings in the scope.</returns>
        public IEnumerable<(string OriginalUid, string RemappedUid)> GetMappingsByScope(string scope)
        {
            ThrowIfDisposed();
            var results = new List<(string, string)>();

            lock (_lock)
            {
                using var connection = CreateConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT original_uid, remapped_uid FROM uid_mappings WHERE scope = @scope;";
                cmd.Parameters.AddWithValue("@scope", scope ?? string.Empty);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            return results;
        }

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteUidStore));
            }
#endif
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
