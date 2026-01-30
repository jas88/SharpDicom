using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Processes directories of DICOM files for batch de-identification.
    /// </summary>
    /// <remarks>
    /// Provides parallel processing, progress reporting, and consistent UID mapping
    /// across all files in a batch.
    /// </remarks>
#if NET5_0_OR_GREATER
    public sealed class BatchDeidentifier : IAsyncDisposable
#else
    public sealed class BatchDeidentifier : IDisposable
#endif
    {
        private readonly DeidentificationContext _context;
        private readonly DicomDeidentifier _deidentifier;
        private readonly BatchDeidentificationOptions _options;
        private bool _disposed;

        /// <summary>
        /// Creates a batch de-identifier with persistent SQLite storage.
        /// </summary>
        /// <param name="contextDbPath">Path to the SQLite database for UID mappings.</param>
        /// <param name="config">De-identification configuration.</param>
        /// <param name="options">Batch processing options.</param>
        public BatchDeidentifier(
            string contextDbPath,
            DeidentificationConfig config,
            BatchDeidentificationOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(contextDbPath))
            {
                throw new ArgumentNullException(nameof(contextDbPath));
            }

#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(config);
#else
            if (config == null) throw new ArgumentNullException(nameof(config));
#endif

            _context = new DeidentificationContext(contextDbPath);
            _options = options ?? BatchDeidentificationOptions.Default;

#if NET6_0_OR_GREATER
            var builder = DeidentificationConfigLoader.CreateBuilder(config);
#else
            var builder = new DicomDeidentifierBuilder().WithBasicProfile();
#endif
            _deidentifier = builder
                .WithUidRemapper(_context.UidRemapper)
                .Build();
        }

        /// <summary>
        /// Creates a batch de-identifier with the specified context and deidentifier.
        /// </summary>
        /// <param name="context">The de-identification context.</param>
        /// <param name="deidentifier">The deidentifier to use.</param>
        /// <param name="options">Batch processing options.</param>
        public BatchDeidentifier(
            DeidentificationContext context,
            DicomDeidentifier deidentifier,
            BatchDeidentificationOptions? options = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(deidentifier);
#else
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (deidentifier == null) throw new ArgumentNullException(nameof(deidentifier));
#endif

            _context = context;
            _deidentifier = deidentifier;
            _options = options ?? BatchDeidentificationOptions.Default;
        }

        /// <summary>
        /// Gets the de-identification context used by this batch processor.
        /// </summary>
        public DeidentificationContext Context => _context;

        /// <summary>
        /// De-identifies all DICOM files in a directory.
        /// </summary>
        /// <param name="inputDir">Input directory containing DICOM files.</param>
        /// <param name="outputDir">Output directory for de-identified files.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Result containing statistics and any errors.</returns>
        public async Task<BatchDeidentificationResult> ProcessDirectoryAsync(
            string inputDir,
            string outputDir,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(inputDir))
            {
                throw new ArgumentNullException(nameof(inputDir));
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                throw new ArgumentNullException(nameof(outputDir));
            }
            if (!Directory.Exists(inputDir))
            {
                throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");
            }

            var result = new BatchDeidentificationResult();

            // Ensure output directory exists
            Directory.CreateDirectory(outputDir);

            // Find all files
            var searchOption = _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.EnumerateFiles(inputDir, _options.SearchPattern, searchOption).ToList();
            result.TotalFiles = files.Count;

            if (files.Count == 0)
            {
                return result;
            }

            // Determine parallelism
            var parallelism = _options.MaxParallelism > 0
                ? _options.MaxParallelism
                : Environment.ProcessorCount;

            // Process files in parallel with throttling
            using var semaphore = new SemaphoreSlim(parallelism);
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await ProcessFileAsync(file, inputDir, outputDir, result, ct).ConfigureAwait(false);

                    var processed = Interlocked.Increment(ref result.ProcessedFilesCounter);
                    _options.Progress?.Report(new BatchProgress
                    {
                        ProcessedFiles = processed,
                        TotalFiles = result.TotalFiles,
                        CurrentFile = Path.GetFileName(file),
                        SuccessCount = result.SuccessCount,
                        ErrorCount = result.ErrorCount
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Export mappings
#if NET6_0_OR_GREATER
            if (_options.ExportMappings)
            {
                var mappingsPath = Path.Combine(outputDir, _options.MappingsFileName);
                await _context.ExportMappingsAsync(mappingsPath, ct).ConfigureAwait(false);
                result.MappingsExportPath = mappingsPath;
            }
#endif

            return result;
        }

        private async Task ProcessFileAsync(
            string inputPath,
            string inputDir,
            string outputDir,
            BatchDeidentificationResult result,
            CancellationToken ct)
        {
            try
            {
                // Load file
                var file = await DicomFile.OpenAsync(inputPath, ct: ct).ConfigureAwait(false);

                // De-identify
                var deidResult = _deidentifier.Deidentify(file.Dataset);

                // Determine output path
                var relativePath = GetRelativePath(inputDir, inputPath);
                var outputPath = _options.PreserveDirectoryStructure
                    ? Path.Combine(outputDir, relativePath)
                    : Path.Combine(outputDir, Path.GetFileName(inputPath));

                // Ensure output subdirectory exists
                var outputSubDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputSubDir))
                {
                    Directory.CreateDirectory(outputSubDir);
                }

                // Save de-identified file
                await file.SaveAsync(outputPath, ct: ct).ConfigureAwait(false);

                // Update stats
                Interlocked.Increment(ref result.SuccessCountCounter);
                Interlocked.Add(ref result.TotalAttributesRemovedCounter, deidResult.Summary.AttributesRemoved);
                Interlocked.Add(ref result.TotalAttributesReplacedCounter, deidResult.Summary.AttributesReplaced);
                Interlocked.Add(ref result.TotalUidsRemappedCounter, deidResult.Summary.UidsRemapped);
            }
            catch (Exception ex) when (_options.ContinueOnError)
            {
                Interlocked.Increment(ref result.ErrorCountCounter);
                lock (result.Errors)
                {
                    result.Errors.Add(new BatchError
                    {
                        FilePath = inputPath,
                        Message = ex.Message,
                        Exception = ex
                    });
                }
            }
        }

        /// <summary>
        /// Processes a list of specific files.
        /// </summary>
        /// <param name="inputFiles">List of input file paths.</param>
        /// <param name="outputDir">Output directory for de-identified files.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Result containing statistics and any errors.</returns>
        public async Task<BatchDeidentificationResult> ProcessFilesAsync(
            IEnumerable<string> inputFiles,
            string outputDir,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(inputFiles);
#else
            if (inputFiles == null) throw new ArgumentNullException(nameof(inputFiles));
#endif
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                throw new ArgumentNullException(nameof(outputDir));
            }

            var result = new BatchDeidentificationResult();
            var files = inputFiles.ToList();
            result.TotalFiles = files.Count;

            if (files.Count == 0)
            {
                return result;
            }

            Directory.CreateDirectory(outputDir);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var baseDir = Path.GetDirectoryName(file) ?? string.Empty;
                await ProcessFileAsync(file, baseDir, outputDir, result, ct).ConfigureAwait(false);
                result.ProcessedFilesCounter++;

                _options.Progress?.Report(new BatchProgress
                {
                    ProcessedFiles = result.ProcessedFiles,
                    TotalFiles = result.TotalFiles,
                    CurrentFile = Path.GetFileName(file),
                    SuccessCount = result.SuccessCount,
                    ErrorCount = result.ErrorCount
                });
            }

#if NET6_0_OR_GREATER
            if (_options.ExportMappings)
            {
                var mappingsPath = Path.Combine(outputDir, _options.MappingsFileName);
                await _context.ExportMappingsAsync(mappingsPath, ct).ConfigureAwait(false);
                result.MappingsExportPath = mappingsPath;
            }
#endif

            return result;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
#if NETSTANDARD2_0
            // Manual implementation for netstandard2.0
            var baseUri = new Uri(EnsureTrailingSlash(basePath));
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
#else
            return Path.GetRelativePath(basePath, fullPath);
#endif
        }

#if NETSTANDARD2_0
        private static string EnsureTrailingSlash(string path)
        {
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
                !path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path + Path.DirectorySeparatorChar;
            }
            return path;
        }
#endif

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BatchDeidentifier));
            }
#endif
        }

#if NET5_0_OR_GREATER
        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _deidentifier.Dispose();
                await _context.DisposeAsync().ConfigureAwait(false);
            }
        }
#else
        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _deidentifier.Dispose();
                _context.Dispose();
            }
        }
#endif
    }
}
