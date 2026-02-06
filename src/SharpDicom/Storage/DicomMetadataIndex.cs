using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SharpDicom.Data;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Storage
{
    /// <summary>
    /// SQLite-backed metadata index for DICOM instances, supporting Q/R queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maintains a 4-table schema (patients, studies, series, instances) with WAL mode
    /// for concurrent read/write access. Provides DICOM-aware query support including
    /// wildcard matching, date range queries, and case-insensitive patient name matching.
    /// </para>
    /// <para>
    /// Thread safety: write operations are serialized via <see cref="SemaphoreSlim"/>.
    /// Read operations can proceed concurrently thanks to WAL mode.
    /// All ADO.NET operations are synchronous per SQLite best practices.
    /// </para>
    /// </remarks>
    public sealed class DicomMetadataIndex : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomMetadataIndex"/> class.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database file. Created if it does not exist.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="databasePath"/> is null.</exception>
        public DicomMetadataIndex(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentNullException(nameof(databasePath));

            _connection = new SqliteConnection($"Data Source={databasePath}");
            _connection.Open();
            EnsureSchema();
        }

        /// <summary>
        /// Creates the database schema if it does not already exist.
        /// </summary>
        private void EnsureSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS patients (
    patient_id TEXT PRIMARY KEY,
    patient_name TEXT,
    patient_birth_date TEXT,
    patient_sex TEXT
);

CREATE TABLE IF NOT EXISTS studies (
    study_instance_uid TEXT PRIMARY KEY,
    patient_id TEXT NOT NULL,
    study_date TEXT,
    study_time TEXT,
    study_description TEXT,
    accession_number TEXT,
    referring_physician TEXT,
    modalities_in_study TEXT
);

CREATE TABLE IF NOT EXISTS series (
    series_instance_uid TEXT PRIMARY KEY,
    study_instance_uid TEXT NOT NULL,
    modality TEXT,
    series_number TEXT,
    series_description TEXT,
    body_part_examined TEXT
);

CREATE TABLE IF NOT EXISTS instances (
    sop_instance_uid TEXT PRIMARY KEY,
    series_instance_uid TEXT NOT NULL,
    sop_class_uid TEXT NOT NULL,
    instance_number TEXT,
    file_path TEXT NOT NULL,
    file_size INTEGER,
    transfer_syntax_uid TEXT,
    indexed_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_patients_name ON patients(patient_name);
CREATE INDEX IF NOT EXISTS idx_studies_date ON studies(study_date);
CREATE INDEX IF NOT EXISTS idx_studies_accession ON studies(accession_number);
CREATE INDEX IF NOT EXISTS idx_studies_patient ON studies(patient_id);
CREATE INDEX IF NOT EXISTS idx_series_study ON series(study_instance_uid);
CREATE INDEX IF NOT EXISTS idx_series_modality ON series(modality);
CREATE INDEX IF NOT EXISTS idx_instances_series ON instances(series_instance_uid);
CREATE INDEX IF NOT EXISTS idx_instances_sop_class ON instances(sop_class_uid);
";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Indexes a DICOM instance by extracting metadata from the dataset and upserting into all tables.
        /// </summary>
        /// <param name="dataset">The DICOM dataset containing metadata to index.</param>
        /// <param name="relativePath">The relative file path (from the store root directory).</param>
        /// <param name="fileSize">The file size in bytes.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataset"/> is null.</exception>
        public void IndexInstance(DicomDataset dataset, string relativePath, long fileSize)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
#endif

            // Extract metadata
            var patientId = dataset.GetString(DicomTag.PatientID) ?? "UNKNOWN";
            var patientName = dataset.GetString(DicomTag.PatientName);
            var patientBirthDate = dataset.GetString(DicomTag.PatientBirthDate);
            var patientSex = dataset.GetString(DicomTag.PatientSex);

            var studyInstanceUid = dataset.GetString(DicomTag.StudyInstanceUID) ?? "";
            var studyDate = dataset.GetString(DicomTag.StudyDate);
            var studyTime = dataset.GetString(DicomTag.StudyTime);
            var studyDescription = dataset.GetString(DicomTag.StudyDescription);
            var accessionNumber = dataset.GetString(DicomTag.AccessionNumber);
            var referringPhysician = dataset.GetString(DicomTag.ReferringPhysicianName);

            var seriesInstanceUid = dataset.GetString(DicomTag.SeriesInstanceUID) ?? "";
            var modality = dataset.GetString(DicomTag.Modality);
            var seriesNumber = dataset.GetString(DicomTag.SeriesNumber);
            var seriesDescription = dataset.GetString(DicomTag.SeriesDescription);
            var bodyPartExamined = dataset.GetString(DicomTag.BodyPartExamined);

            var sopInstanceUid = dataset.GetString(DicomTag.SOPInstanceUID) ?? "";
            var sopClassUid = dataset.GetString(DicomTag.SOPClassUID) ?? "";
            var instanceNumber = dataset.GetString(DicomTag.InstanceNumber);
            var transferSyntaxUid = dataset.GetString(DicomTag.TransferSyntaxUID);

            var indexedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            _writeLock.Wait();
            try
            {
                using var transaction = _connection.BeginTransaction();

                // Upsert patient
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT OR REPLACE INTO patients (patient_id, patient_name, patient_birth_date, patient_sex)
VALUES ($patient_id, $patient_name, $patient_birth_date, $patient_sex)";
                    cmd.Parameters.AddWithValue("$patient_id", patientId);
                    cmd.Parameters.AddWithValue("$patient_name", (object?)patientName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$patient_birth_date", (object?)patientBirthDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$patient_sex", (object?)patientSex ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                // Upsert study (handle ModalitiesInStudy merge)
                string? existingModalities = null;
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT modalities_in_study FROM studies WHERE study_instance_uid = $uid";
                    cmd.Parameters.AddWithValue("$uid", studyInstanceUid);
                    existingModalities = cmd.ExecuteScalar() as string;
                }

                var modalitiesInStudy = MergeModalities(existingModalities, modality);

                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT OR REPLACE INTO studies
(study_instance_uid, patient_id, study_date, study_time, study_description, accession_number, referring_physician, modalities_in_study)
VALUES ($study_instance_uid, $patient_id, $study_date, $study_time, $study_description, $accession_number, $referring_physician, $modalities_in_study)";
                    cmd.Parameters.AddWithValue("$study_instance_uid", studyInstanceUid);
                    cmd.Parameters.AddWithValue("$patient_id", patientId);
                    cmd.Parameters.AddWithValue("$study_date", (object?)studyDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$study_time", (object?)studyTime ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$study_description", (object?)studyDescription ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$accession_number", (object?)accessionNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$referring_physician", (object?)referringPhysician ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$modalities_in_study", (object?)modalitiesInStudy ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                // Upsert series
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT OR REPLACE INTO series
(series_instance_uid, study_instance_uid, modality, series_number, series_description, body_part_examined)
VALUES ($series_instance_uid, $study_instance_uid, $modality, $series_number, $series_description, $body_part_examined)";
                    cmd.Parameters.AddWithValue("$series_instance_uid", seriesInstanceUid);
                    cmd.Parameters.AddWithValue("$study_instance_uid", studyInstanceUid);
                    cmd.Parameters.AddWithValue("$modality", (object?)modality ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$series_number", (object?)seriesNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$series_description", (object?)seriesDescription ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$body_part_examined", (object?)bodyPartExamined ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                // Upsert instance
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"INSERT OR REPLACE INTO instances
(sop_instance_uid, series_instance_uid, sop_class_uid, instance_number, file_path, file_size, transfer_syntax_uid, indexed_at)
VALUES ($sop_instance_uid, $series_instance_uid, $sop_class_uid, $instance_number, $file_path, $file_size, $transfer_syntax_uid, $indexed_at)";
                    cmd.Parameters.AddWithValue("$sop_instance_uid", sopInstanceUid);
                    cmd.Parameters.AddWithValue("$series_instance_uid", seriesInstanceUid);
                    cmd.Parameters.AddWithValue("$sop_class_uid", sopClassUid);
                    cmd.Parameters.AddWithValue("$instance_number", (object?)instanceNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$file_path", relativePath);
                    cmd.Parameters.AddWithValue("$file_size", fileSize);
                    cmd.Parameters.AddWithValue("$transfer_syntax_uid", (object?)transferSyntaxUid ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$indexed_at", indexedAt);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Queries the index using a DICOM C-FIND query identifier dataset.
        /// </summary>
        /// <param name="queryIdentifier">
        /// The query identifier containing QueryRetrieveLevel, matching keys (non-empty values),
        /// and return keys (zero-length values).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An async enumerable of matching datasets.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryIdentifier"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when QueryRetrieveLevel is missing from the identifier.</exception>
        public async IAsyncEnumerable<DicomDataset> FindAsync(
            DicomDataset queryIdentifier,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(queryIdentifier);
#else
            if (queryIdentifier == null)
                throw new ArgumentNullException(nameof(queryIdentifier));
#endif

            var qrLevelStr = queryIdentifier.GetString(DicomTag.QueryRetrieveLevel);
            if (string.IsNullOrWhiteSpace(qrLevelStr))
                throw new ArgumentException("QueryRetrieveLevel is required in the query identifier.");

            var level = QueryRetrieveLevelExtensions.Parse(qrLevelStr!);

            // Build SQL query and parameters
            var (sql, parameters) = BuildFindQuery(queryIdentifier, level);

            // Execute synchronously (SQLite is sync) and yield results
            // We yield synchronously from this async iterator since no actual async work is needed
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var result = BuildResultDataset(reader, queryIdentifier, level);
                yield return result;
            }

            // Suppress CS1998 - this method is intentionally async for IAsyncEnumerable even though
            // SQLite operations are synchronous
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the relative file path for a SOP Instance UID.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID to look up.</param>
        /// <returns>The relative file path, or null if not found.</returns>
        public string? GetFilePath(string sopInstanceUid)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT file_path FROM instances WHERE sop_instance_uid = $uid";
            cmd.Parameters.AddWithValue("$uid", sopInstanceUid);
            return cmd.ExecuteScalar() as string;
        }

        /// <summary>
        /// Removes an instance from the index.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID to remove.</param>
        public void RemoveInstance(string sopInstanceUid)
        {
            _writeLock.Wait();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM instances WHERE sop_instance_uid = $uid";
                cmd.Parameters.AddWithValue("$uid", sopInstanceUid);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Gets the total number of indexed instances.
        /// </summary>
        /// <returns>The instance count.</returns>
        public int GetInstanceCount()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM instances";
            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds a SQL query from a DICOM C-FIND query identifier.
        /// </summary>
        private static (string Sql, List<(string Name, object Value)> Parameters) BuildFindQuery(
            DicomDataset queryIdentifier,
            QueryRetrieveLevel level)
        {
            var sb = new StringBuilder();
            var parameters = new List<(string Name, object Value)>();
            var whereClauses = new List<string>();
            int paramIndex = 0;

            // Build FROM/JOIN based on level
            switch (level)
            {
                case QueryRetrieveLevel.Patient:
                    sb.Append("SELECT DISTINCT p.* FROM patients p");
                    break;
                case QueryRetrieveLevel.Study:
                    sb.Append("SELECT DISTINCT p.patient_id, p.patient_name, p.patient_birth_date, p.patient_sex, ");
                    sb.Append("s.study_instance_uid, s.study_date, s.study_time, s.study_description, ");
                    sb.Append("s.accession_number, s.referring_physician, s.modalities_in_study ");
                    sb.Append("FROM studies s JOIN patients p ON s.patient_id = p.patient_id");
                    break;
                case QueryRetrieveLevel.Series:
                    sb.Append("SELECT DISTINCT p.patient_id, p.patient_name, ");
                    sb.Append("s.study_instance_uid, ");
                    sb.Append("sr.series_instance_uid, sr.modality, sr.series_number, sr.series_description, sr.body_part_examined ");
                    sb.Append("FROM series sr ");
                    sb.Append("JOIN studies s ON sr.study_instance_uid = s.study_instance_uid ");
                    sb.Append("JOIN patients p ON s.patient_id = p.patient_id");
                    break;
                case QueryRetrieveLevel.Image:
                    sb.Append("SELECT DISTINCT p.patient_id, p.patient_name, ");
                    sb.Append("s.study_instance_uid, ");
                    sb.Append("sr.series_instance_uid, ");
                    sb.Append("i.sop_instance_uid, i.sop_class_uid, i.instance_number ");
                    sb.Append("FROM instances i ");
                    sb.Append("JOIN series sr ON i.series_instance_uid = sr.series_instance_uid ");
                    sb.Append("JOIN studies s ON sr.study_instance_uid = s.study_instance_uid ");
                    sb.Append("JOIN patients p ON s.patient_id = p.patient_id");
                    break;
            }

            // Add WHERE clauses for matching keys (non-empty values)
            foreach (var element in queryIdentifier)
            {
                if (element.Tag == DicomTag.QueryRetrieveLevel)
                    continue;

                // Skip return keys (empty values)
                if (element.IsEmpty)
                    continue;

                var stringValue = (element as DicomStringElement)?.GetString(DicomEncoding.Default);
                if (string.IsNullOrEmpty(stringValue))
                    continue;

                var columnInfo = GetColumnForTag(element.Tag, level);
                if (columnInfo == null)
                    continue;

                var (columnName, isPN, isDA) = columnInfo.Value;
                var paramName = $"$p{paramIndex++}";

#if NETSTANDARD2_0
                if (isDA && stringValue!.IndexOf('-') >= 0)
#else
                if (isDA && stringValue!.Contains('-'))
#endif
                {
                    // Date range query
                    var range = DicomDateRange.Parse(stringValue);
                    if (!range.IsUniversal)
                    {
                        if (range.From.HasValue)
                        {
                            var fromParam = $"$p{paramIndex++}";
                            whereClauses.Add($"{columnName} >= {fromParam}");
                            parameters.Add((fromParam, range.From.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                        }
                        if (range.To.HasValue)
                        {
                            var toParam = $"$p{paramIndex++}";
                            whereClauses.Add($"{columnName} <= {toParam}");
                            parameters.Add((toParam, range.To.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                        }
                    }
                }
                else if (DicomQueryMatcher.HasDicomWildcard(stringValue))
                {
                    // Wildcard query
                    var (sqlPattern, _) = DicomQueryMatcher.DicomWildcardToSqlLike(stringValue!);
                    if (isPN)
                    {
                        whereClauses.Add($"{columnName} LIKE {paramName} ESCAPE '\\' COLLATE NOCASE");
                    }
                    else
                    {
                        whereClauses.Add($"{columnName} LIKE {paramName} ESCAPE '\\'");
                    }
                    parameters.Add((paramName, sqlPattern));
                }
                else
                {
                    // Exact match
                    if (isPN)
                    {
                        whereClauses.Add($"{columnName} = {paramName} COLLATE NOCASE");
                    }
                    else
                    {
                        whereClauses.Add($"{columnName} = {paramName}");
                    }
                    parameters.Add((paramName, stringValue!));
                }
            }

            if (whereClauses.Count > 0)
            {
                sb.Append(" WHERE ");
                sb.Append(string.Join(" AND ", whereClauses));
            }

            return (sb.ToString(), parameters);
        }

        /// <summary>
        /// Maps a DICOM tag to its database column name for a given query level.
        /// </summary>
        /// <returns>Tuple of (columnName, isPersonName, isDate), or null if not mapped.</returns>
        private static (string ColumnName, bool IsPN, bool IsDA)? GetColumnForTag(DicomTag tag, QueryRetrieveLevel level)
        {
            // Patient-level columns
            if (tag == DicomTag.PatientID)
                return ("p.patient_id", false, false);
            if (tag == DicomTag.PatientName)
                return ("p.patient_name", true, false);
            if (tag == DicomTag.PatientBirthDate)
                return ("p.patient_birth_date", false, true);
            if (tag == DicomTag.PatientSex)
                return ("p.patient_sex", false, false);

            // Study-level columns (available at Study level and below)
            if (level >= QueryRetrieveLevel.Study)
            {
                if (tag == DicomTag.StudyInstanceUID)
                    return ("s.study_instance_uid", false, false);
                if (tag == DicomTag.StudyDate)
                    return ("s.study_date", false, true);
                if (tag == DicomTag.StudyTime)
                    return ("s.study_time", false, false);
                if (tag == DicomTag.StudyDescription)
                    return ("s.study_description", false, false);
                if (tag == DicomTag.AccessionNumber)
                    return ("s.accession_number", false, false);
                if (tag == DicomTag.ReferringPhysicianName)
                    return ("s.referring_physician", true, false);
                if (tag == DicomTag.ModalitiesInStudy)
                    return ("s.modalities_in_study", false, false);
            }

            // Series-level columns
            if (level >= QueryRetrieveLevel.Series)
            {
                if (tag == DicomTag.SeriesInstanceUID)
                    return ("sr.series_instance_uid", false, false);
                if (tag == DicomTag.Modality)
                    return ("sr.modality", false, false);
                if (tag == DicomTag.SeriesNumber)
                    return ("sr.series_number", false, false);
                if (tag == DicomTag.SeriesDescription)
                    return ("sr.series_description", false, false);
                if (tag == DicomTag.BodyPartExamined)
                    return ("sr.body_part_examined", false, false);
            }

            // Image-level columns
            if (level >= QueryRetrieveLevel.Image)
            {
                if (tag == DicomTag.SOPInstanceUID)
                    return ("i.sop_instance_uid", false, false);
                if (tag == DicomTag.SOPClassUID)
                    return ("i.sop_class_uid", false, false);
                if (tag == DicomTag.InstanceNumber)
                    return ("i.instance_number", false, false);
            }

            return null;
        }

        /// <summary>
        /// Builds a result dataset from a database reader row.
        /// </summary>
        private static DicomDataset BuildResultDataset(
            SqliteDataReader reader,
            DicomDataset queryIdentifier,
            QueryRetrieveLevel level)
        {
            var result = new DicomDataset();

            // Always include QueryRetrieveLevel
            result.Add(new DicomStringElement(
                DicomTag.QueryRetrieveLevel,
                DicomVR.CS,
                Encoding.ASCII.GetBytes(level.ToDicomValue())));

            // For each requested tag in the query identifier, populate if available
            foreach (var element in queryIdentifier)
            {
                if (element.Tag == DicomTag.QueryRetrieveLevel)
                    continue;

                var value = GetValueFromReader(reader, element.Tag, level);
                if (value != null)
                {
                    var vr = GetVRForTag(element.Tag);
                    var bytes = Encoding.UTF8.GetBytes(value);
                    // Pad to even length
                    if (bytes.Length % 2 != 0)
                    {
                        var padded = new byte[bytes.Length + 1];
                        bytes.CopyTo(padded, 0);
                        padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)0 : (byte)' ';
                        bytes = padded;
                    }
                    result.Add(new DicomStringElement(element.Tag, vr, bytes));
                }
                else
                {
                    // Return zero-length element
                    var vr = GetVRForTag(element.Tag);
                    result.Add(new DicomStringElement(element.Tag, vr, Array.Empty<byte>()));
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a column value from the reader for a specific DICOM tag.
        /// </summary>
        private static string? GetValueFromReader(SqliteDataReader reader, DicomTag tag, QueryRetrieveLevel level)
        {
            var columnName = GetReaderColumnName(tag, level);
            if (columnName == null)
                return null;

            try
            {
                var ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>
        /// Maps a DICOM tag to the database column name (without table alias) for reader access.
        /// </summary>
        private static string? GetReaderColumnName(DicomTag tag, QueryRetrieveLevel level)
        {
            if (tag == DicomTag.PatientID) return "patient_id";
            if (tag == DicomTag.PatientName) return "patient_name";
            if (tag == DicomTag.PatientBirthDate) return "patient_birth_date";
            if (tag == DicomTag.PatientSex) return "patient_sex";

            if (level >= QueryRetrieveLevel.Study)
            {
                if (tag == DicomTag.StudyInstanceUID) return "study_instance_uid";
                if (tag == DicomTag.StudyDate) return "study_date";
                if (tag == DicomTag.StudyTime) return "study_time";
                if (tag == DicomTag.StudyDescription) return "study_description";
                if (tag == DicomTag.AccessionNumber) return "accession_number";
                if (tag == DicomTag.ReferringPhysicianName) return "referring_physician";
                if (tag == DicomTag.ModalitiesInStudy) return "modalities_in_study";
            }

            if (level >= QueryRetrieveLevel.Series)
            {
                if (tag == DicomTag.SeriesInstanceUID) return "series_instance_uid";
                if (tag == DicomTag.Modality) return "modality";
                if (tag == DicomTag.SeriesNumber) return "series_number";
                if (tag == DicomTag.SeriesDescription) return "series_description";
                if (tag == DicomTag.BodyPartExamined) return "body_part_examined";
            }

            if (level >= QueryRetrieveLevel.Image)
            {
                if (tag == DicomTag.SOPInstanceUID) return "sop_instance_uid";
                if (tag == DicomTag.SOPClassUID) return "sop_class_uid";
                if (tag == DicomTag.InstanceNumber) return "instance_number";
            }

            return null;
        }

        /// <summary>
        /// Gets the VR for well-known DICOM tags used in Q/R queries.
        /// </summary>
        private static DicomVR GetVRForTag(DicomTag tag)
        {
            if (tag == DicomTag.PatientID) return DicomVR.LO;
            if (tag == DicomTag.PatientName) return DicomVR.PN;
            if (tag == DicomTag.PatientBirthDate) return DicomVR.DA;
            if (tag == DicomTag.PatientSex) return DicomVR.CS;
            if (tag == DicomTag.StudyInstanceUID) return DicomVR.UI;
            if (tag == DicomTag.StudyDate) return DicomVR.DA;
            if (tag == DicomTag.StudyTime) return DicomVR.TM;
            if (tag == DicomTag.StudyDescription) return DicomVR.LO;
            if (tag == DicomTag.AccessionNumber) return DicomVR.SH;
            if (tag == DicomTag.ReferringPhysicianName) return DicomVR.PN;
            if (tag == DicomTag.ModalitiesInStudy) return DicomVR.CS;
            if (tag == DicomTag.SeriesInstanceUID) return DicomVR.UI;
            if (tag == DicomTag.Modality) return DicomVR.CS;
            if (tag == DicomTag.SeriesNumber) return DicomVR.IS;
            if (tag == DicomTag.SeriesDescription) return DicomVR.LO;
            if (tag == DicomTag.BodyPartExamined) return DicomVR.CS;
            if (tag == DicomTag.SOPInstanceUID) return DicomVR.UI;
            if (tag == DicomTag.SOPClassUID) return DicomVR.UI;
            if (tag == DicomTag.InstanceNumber) return DicomVR.IS;
            if (tag == DicomTag.QueryRetrieveLevel) return DicomVR.CS;

            // Default to LO for unknown tags
            return DicomVR.LO;
        }

        /// <summary>
        /// Merges a new modality into an existing comma-separated list of modalities, deduplicating.
        /// </summary>
        private static string? MergeModalities(string? existing, string? newModality)
        {
            if (string.IsNullOrWhiteSpace(newModality))
                return existing;

            if (string.IsNullOrWhiteSpace(existing))
                return newModality!.Trim();

            var modalities = existing!.Split(',')
                .Select(m => m.Trim())
                .Where(m => m.Length > 0)
                .ToList();

            var trimmed = newModality!.Trim();
            bool found = false;
            foreach (var m in modalities)
            {
                if (string.Equals(m, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                modalities.Add(trimmed);
            }

            return string.Join(",", modalities);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _writeLock.Dispose();
                _connection.Dispose();
            }
        }
    }
}
