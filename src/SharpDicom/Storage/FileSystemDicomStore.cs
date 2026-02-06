using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.IO;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Storage
{
    /// <summary>
    /// Integrated DICOM store and serve implementation backed by the file system and SQLite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FileSystemDicomStore provides a turnkey mini-PACS that can receive DICOM files via
    /// C-STORE, index their metadata in SQLite, and serve C-FIND/C-MOVE/C-GET queries.
    /// Usable out-of-the-box with just a root directory path.
    /// </para>
    /// <para>
    /// Files are stored in a hierarchical layout:
    /// <c>{RootDirectory}/{PatientID}/{StudyInstanceUID}/{SeriesInstanceUID}/{SOPInstanceUID}.dcm</c>
    /// </para>
    /// <para>
    /// Use <see cref="CreateServerOptions"/> to get a fully wired <see cref="DicomServerOptions"/>
    /// for one-line mini-PACS setup.
    /// </para>
    /// </remarks>
    public sealed class FileSystemDicomStore : IDisposable
    {
        private readonly FileSystemDicomStoreOptions _storeOptions;
        private readonly DicomMetadataIndex _index;
        private readonly string _rootDirectory;

        /// <summary>
        /// Characters that are invalid in file path components.
        /// </summary>
        private static readonly char[] InvalidPathChars = { '/', '\\', ':', '*', '?', '<', '>', '|', '"' };

        /// <summary>
        /// Maximum length for a single path component (directory or file name).
        /// </summary>
        private const int MaxPathComponentLength = 200;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSystemDicomStore"/> class.
        /// </summary>
        /// <param name="options">The store configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options are invalid.</exception>
        public FileSystemDicomStore(FileSystemDicomStoreOptions options)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options == null)
                throw new ArgumentNullException(nameof(options));
#endif

            options.Validate();

            _storeOptions = options;
            _rootDirectory = options.RootDirectory;

            // Ensure root directory exists
            Directory.CreateDirectory(_rootDirectory);

            // Create the metadata index
            _index = new DicomMetadataIndex(options.EffectiveDatabasePath);
        }

        /// <summary>
        /// Stores a DICOM dataset to disk and indexes its metadata.
        /// </summary>
        /// <param name="context">The C-STORE request context.</param>
        /// <param name="dataset">The DICOM dataset to store.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="DicomStatus"/> indicating the result of the store operation.</returns>
        public ValueTask<DicomStatus> StoreAsync(
            CStoreRequestContext context,
            DicomDataset dataset,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // Extract UIDs for path construction
                var patientId = dataset.GetString(DicomTag.PatientID);
                var studyUid = dataset.GetString(DicomTag.StudyInstanceUID);
                var seriesUid = dataset.GetString(DicomTag.SeriesInstanceUID);
                var sopUid = dataset.GetString(DicomTag.SOPInstanceUID);

                // Build hierarchical relative path
                var sanitizedPatient = SanitizePathComponent(patientId, "UNKNOWN");
                var sanitizedStudy = SanitizePathComponent(studyUid, "NO_STUDY");
                var sanitizedSeries = SanitizePathComponent(seriesUid, "NO_SERIES");
                var sanitizedSop = SanitizePathComponent(sopUid, "NO_INSTANCE");

                var relativePath = Path.Combine(
                    sanitizedPatient,
                    sanitizedStudy,
                    sanitizedSeries,
                    sanitizedSop + ".dcm");

                var fullPath = Path.Combine(_rootDirectory, relativePath);

                // Create directories as needed
                var directoryPath = Path.GetDirectoryName(fullPath);
                if (directoryPath != null)
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // Write the DICOM file
                var file = new DicomFile(dataset);
                file.Save(fullPath);

                // Get file size
                var fileInfo = new FileInfo(fullPath);
                var fileSize = fileInfo.Length;

                // Index the metadata
                _index.IndexInstance(dataset, relativePath, fileSize);

                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Do not catch general exception types -- DIMSE requires status code response
            catch (Exception)
            {
                return new ValueTask<DicomStatus>(DicomStatus.ProcessingFailure);
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Queries the metadata index for matching DICOM instances.
        /// </summary>
        /// <param name="queryIdentifier">
        /// The C-FIND query identifier containing QueryRetrieveLevel and matching/return keys.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An async enumerable of matching datasets.</returns>
        public IAsyncEnumerable<DicomDataset> FindAsync(
            DicomDataset queryIdentifier,
            CancellationToken ct)
        {
            return _index.FindAsync(queryIdentifier, ct);
        }

        /// <summary>
        /// Retrieves a stored DICOM file for a matched instance.
        /// </summary>
        /// <param name="matchedInstance">
        /// A dataset from <see cref="FindAsync"/> containing at least SOPInstanceUID.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The DICOM file, or null if the file cannot be retrieved.</returns>
        public ValueTask<DicomFile?> RetrieveAsync(
            DicomDataset matchedInstance,
            CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var sopInstanceUid = matchedInstance.GetString(DicomTag.SOPInstanceUID);
                if (string.IsNullOrEmpty(sopInstanceUid))
                    return new ValueTask<DicomFile?>((DicomFile?)null);

                var relativePath = _index.GetFilePath(sopInstanceUid!);
                if (relativePath == null)
                    return new ValueTask<DicomFile?>((DicomFile?)null);

                var fullPath = Path.Combine(_rootDirectory, relativePath);
                if (!File.Exists(fullPath))
                    return new ValueTask<DicomFile?>((DicomFile?)null);

                var file = DicomFile.Open(fullPath);
                return new ValueTask<DicomFile?>(file);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Do not catch general exception types -- DIMSE requires null for failed retrieval
            catch (Exception)
            {
                return new ValueTask<DicomFile?>((DicomFile?)null);
            }
#pragma warning restore CA1031
        }

        /// <summary>
        /// Creates a fully wired <see cref="DicomServerOptions"/> for one-line mini-PACS setup.
        /// </summary>
        /// <returns>
        /// A <see cref="DicomServerOptions"/> with C-STORE, C-FIND, C-MOVE, and C-GET handlers
        /// all configured. <see cref="DicomServerOptions.OnResolveMoveDestination"/> is left null
        /// and must be set by the caller if C-MOVE is needed.
        /// </returns>
        public DicomServerOptions CreateServerOptions()
        {
            return new DicomServerOptions
            {
                AETitle = _storeOptions.AETitle,
                Port = _storeOptions.Port,
                OnCStoreRequest = (ctx, dataset, ct) => StoreAsync(ctx, dataset, ct),
                OnCFind = (query, ct) => FindAsync(query, ct),
                OnCMoveRetrieve = (match, ct) => RetrieveAsync(match, ct),
                OnCGetRetrieve = (match, ct) => RetrieveAsync(match, ct),
            };
        }

        /// <summary>
        /// Gets the total number of indexed instances.
        /// </summary>
        /// <returns>The instance count.</returns>
        public int GetInstanceCount()
        {
            return _index.GetInstanceCount();
        }

        /// <summary>
        /// Sanitizes a string for use as a file path component.
        /// </summary>
        /// <param name="value">The value to sanitize.</param>
        /// <param name="fallback">The fallback value if the input is null or empty.</param>
        /// <returns>A sanitized path component.</returns>
        private static string SanitizePathComponent(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            var sanitized = value!.Trim();

            // Replace invalid path characters with underscores
            foreach (var c in InvalidPathChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Also replace any OS-specific invalid characters
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Truncate if too long
            if (sanitized.Length > MaxPathComponentLength)
            {
                sanitized = sanitized.Substring(0, MaxPathComponentLength);
            }

            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _index.Dispose();
        }
    }
}
