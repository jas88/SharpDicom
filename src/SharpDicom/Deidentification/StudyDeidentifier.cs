using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Deidentification.PixelCleaner;

namespace SharpDicom.Deidentification;

/// <summary>
/// Processes multiple DICOM files as a study with shared de-identification context.
/// Ensures consistent UID remapping and date shifting across all files in the study.
/// </summary>
/// <remarks>
/// <para>
/// Use StudyDeidentifier when processing multiple related DICOM files that should
/// share consistent de-identification mappings:
/// </para>
/// <list type="bullet">
/// <item>UID references remain valid across files (e.g., ReferencedSOPInstanceUID)</item>
/// <item>Date shifts are consistent per patient or study</item>
/// <item>Patient identifiers are remapped consistently</item>
/// </list>
/// <para>
/// Example usage:
/// </para>
/// <code>
/// var options = new DeidentificationOptions
/// {
///     Profile = DeidentificationProfile.Basic,
///     DateShiftStrategy = DateShiftStrategy.PerPatient
/// };
///
/// await using var deidentifier = new StudyDeidentifier(options);
///
/// await foreach (var result in deidentifier.ProcessDirectoryAsync(inputDir, outputDir))
/// {
///     if (!result.Success)
///         Console.WriteLine($"Failed: {result.Input}: {result.Error?.Message}");
/// }
///
/// Console.WriteLine($"Processed {deidentifier.FilesProcessed} files, {deidentifier.WarningsRaised} warnings");
/// </code>
/// </remarks>
public sealed class StudyDeidentifier : IAsyncDisposable
{
    private readonly DicomDeidentifier _deidentifier;
    private readonly DeidentificationOptions _options;
    private int _filesProcessed;
    private int _warningsRaised;

    /// <summary>
    /// Creates a new study de-identifier with the given options.
    /// </summary>
    /// <param name="options">The de-identification options to apply to all files.</param>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    public StudyDeidentifier(DeidentificationOptions options)
    {
#if NETSTANDARD2_0
        if (options == null)
            throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(options);
#endif
        _options = options;
        _deidentifier = new DicomDeidentifier(options);
        _deidentifier.HighRiskModalityDetected += OnHighRiskModality;
    }

    /// <summary>
    /// Creates a study de-identifier using an existing context for resuming processing.
    /// </summary>
    /// <param name="options">The de-identification options to apply to all files.</param>
    /// <param name="context">Existing context with UID/date mappings to preserve consistency.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or context is null.</exception>
    public StudyDeidentifier(DeidentificationOptions options, DeidentificationContext context)
    {
#if NETSTANDARD2_0
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (context == null)
            throw new ArgumentNullException(nameof(context));
#else
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
#endif
        _options = options;
        _deidentifier = new DicomDeidentifier(options, context);
        _deidentifier.HighRiskModalityDetected += OnHighRiskModality;
    }

    /// <summary>
    /// Gets the shared de-identification context.
    /// </summary>
    /// <remarks>
    /// The context maintains UID and date offset mappings across all processed files.
    /// Save this context using <see cref="SaveContextAsync"/> to resume processing later.
    /// </remarks>
    public DeidentificationContext Context => _deidentifier.Context;

    /// <summary>
    /// Gets the number of files successfully processed.
    /// </summary>
    public int FilesProcessed => _filesProcessed;

    /// <summary>
    /// Gets the number of high-risk modality warnings raised.
    /// </summary>
    public int WarningsRaised => _warningsRaised;

    /// <summary>
    /// Raised when a warning occurs during processing.
    /// </summary>
    /// <remarks>
    /// The first parameter is the modality code, the second is the SOP Instance UID (may be null).
    /// </remarks>
    public event Action<string, string?>? Warning;

    /// <summary>
    /// Processes a single DICOM file.
    /// </summary>
    /// <param name="inputPath">Path to the input DICOM file.</param>
    /// <param name="outputPath">Path where the de-identified file will be saved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when inputPath or outputPath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the input file does not exist.</exception>
    public async ValueTask ProcessFileAsync(
        string inputPath,
        string outputPath,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (inputPath == null)
            throw new ArgumentNullException(nameof(inputPath));
        if (outputPath == null)
            throw new ArgumentNullException(nameof(outputPath));
#else
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
#endif

        var file = await DicomFile.OpenAsync(inputPath, ct: ct).ConfigureAwait(false);
        await _deidentifier.ApplyAsync(file.Dataset, ct).ConfigureAwait(false);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await file.SaveAsync(outputPath, ct: ct).ConfigureAwait(false);
        Interlocked.Increment(ref _filesProcessed);
    }

    /// <summary>
    /// Processes a directory of DICOM files.
    /// </summary>
    /// <param name="inputDir">Path to the input directory.</param>
    /// <param name="outputDir">Path to the output directory.</param>
    /// <param name="searchPattern">File search pattern (default "*.dcm").</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of processing results for each file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when inputDir or outputDir is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the input directory does not exist.</exception>
    public async IAsyncEnumerable<(string Input, string Output, bool Success, Exception? Error)>
        ProcessDirectoryAsync(
            string inputDir,
            string outputDir,
            string searchPattern = "*.dcm",
            bool recursive = true,
            [EnumeratorCancellation] CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (inputDir == null)
            throw new ArgumentNullException(nameof(inputDir));
        if (outputDir == null)
            throw new ArgumentNullException(nameof(outputDir));
#else
        ArgumentNullException.ThrowIfNull(inputDir);
        ArgumentNullException.ThrowIfNull(outputDir);
#endif

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");

        Directory.CreateDirectory(outputDir);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(inputDir, searchPattern, searchOption);

        foreach (var inputPath in files)
        {
            ct.ThrowIfCancellationRequested();

            // Preserve relative directory structure
            var relativePath = GetRelativePath(inputDir, inputPath);
            var outputPath = Path.Combine(outputDir, relativePath);
            var outputSubDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputSubDir) && !Directory.Exists(outputSubDir))
            {
                Directory.CreateDirectory(outputSubDir);
            }

            Exception? error = null;
            var success = false;

            try
            {
                await ProcessFileAsync(inputPath, outputPath, ct).ConfigureAwait(false);
                success = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex;
            }

            yield return (inputPath, outputPath, success, error);
        }
    }

    /// <summary>
    /// Processes files in parallel for better throughput.
    /// </summary>
    /// <param name="files">Collection of input/output path pairs.</param>
    /// <param name="maxDegreeOfParallelism">Maximum concurrent file operations.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Thrown when files is null.</exception>
    public async Task ProcessParallelAsync(
        IEnumerable<(string Input, string Output)> files,
        int maxDegreeOfParallelism = 4,
        IProgress<(int Processed, int Total)>? progress = null,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (files == null)
            throw new ArgumentNullException(nameof(files));
#else
        ArgumentNullException.ThrowIfNull(files);
#endif

        var fileList = new List<(string Input, string Output)>(files);
        var total = fileList.Count;
        var processed = 0;

#if NET6_0_OR_GREATER
        await Parallel.ForEachAsync(
            fileList,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = ct
            },
            async (file, token) =>
            {
                await ProcessFileAsync(file.Input, file.Output, token).ConfigureAwait(false);
                var current = Interlocked.Increment(ref processed);
                progress?.Report((current, total));
            }).ConfigureAwait(false);
#else
        // netstandard2.0 fallback: process sequentially
        foreach (var file in fileList)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessFileAsync(file.Input, file.Output, ct).ConfigureAwait(false);
            processed++;
            progress?.Report((processed, total));
        }
#endif
    }

    /// <summary>
    /// Saves the context for later resumption.
    /// </summary>
    /// <param name="path">Path to save the context file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The context file contains UID mappings and date offsets that must be
    /// preserved to maintain consistency when resuming processing.
    /// </remarks>
    public async Task SaveContextAsync(string path, CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (path == null)
            throw new ArgumentNullException(nameof(path));
#else
        ArgumentNullException.ThrowIfNull(path);
#endif

        using var stream = File.Create(path);
        await Context.SaveAsync(stream, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a previously saved context and creates a new StudyDeidentifier.
    /// </summary>
    /// <param name="contextPath">Path to the saved context file.</param>
    /// <param name="options">De-identification options to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new StudyDeidentifier with the loaded context.</returns>
    /// <exception cref="ArgumentNullException">Thrown when contextPath or options is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the context file does not exist.</exception>
    public static async Task<StudyDeidentifier> LoadAsync(
        string contextPath,
        DeidentificationOptions options,
        CancellationToken ct = default)
    {
#if NETSTANDARD2_0
        if (contextPath == null)
            throw new ArgumentNullException(nameof(contextPath));
        if (options == null)
            throw new ArgumentNullException(nameof(options));
#else
        ArgumentNullException.ThrowIfNull(contextPath);
        ArgumentNullException.ThrowIfNull(options);
#endif

        using var stream = File.OpenRead(contextPath);
        var context = await DeidentificationContext.LoadAsync(stream, options, ct).ConfigureAwait(false);
        return new StudyDeidentifier(options, context);
    }

    private void OnHighRiskModality(string modality, DicomDataset dataset)
    {
        Interlocked.Increment(ref _warningsRaised);
        var sopInstance = dataset[DicomTag.SOPInstanceUID] is DicomStringElement se
            ? se.GetString() : null;
        Warning?.Invoke(modality, sopInstance);
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
#if NET6_0_OR_GREATER
        return Path.GetRelativePath(basePath, fullPath);
#else
        // netstandard2.0 fallback
        if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            basePath += Path.DirectorySeparatorChar;

        var baseUri = new Uri(basePath);
        var fullUri = new Uri(fullPath);
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        return Uri.UnescapeDataString(relativeUri.ToString())
            .Replace('/', Path.DirectorySeparatorChar);
#endif
    }

    /// <summary>
    /// Disposes the study de-identifier and its context.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _deidentifier.HighRiskModalityDetected -= OnHighRiskModality;
        Context.Dispose();
#if NETSTANDARD2_0
        return default;
#else
        return ValueTask.CompletedTask;
#endif
    }
}
