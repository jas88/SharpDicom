using System;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Options for batch de-identification processing.
    /// </summary>
    public sealed class BatchDeidentificationOptions
    {
        /// <summary>
        /// Gets or sets the search pattern for DICOM files.
        /// </summary>
        /// <remarks>
        /// Supports standard file system wildcards. Default is "*.dcm".
        /// </remarks>
        public string SearchPattern { get; init; } = "*.dcm";

        /// <summary>
        /// Gets or sets whether to search subdirectories.
        /// </summary>
        public bool Recursive { get; init; } = true;

        /// <summary>
        /// Gets or sets whether to preserve directory structure in output.
        /// </summary>
        public bool PreserveDirectoryStructure { get; init; } = true;

        /// <summary>
        /// Gets or sets the maximum degree of parallelism.
        /// </summary>
        /// <remarks>
        /// A value of 0 or less uses Environment.ProcessorCount.
        /// </remarks>
        public int MaxParallelism { get; init; }

        /// <summary>
        /// Gets or sets whether to continue processing on individual file errors.
        /// </summary>
        public bool ContinueOnError { get; init; } = true;

        /// <summary>
        /// Gets or sets the progress callback.
        /// </summary>
        public IProgress<BatchProgress>? Progress { get; init; }

        /// <summary>
        /// Gets or sets whether to export UID mappings after completion.
        /// </summary>
        public bool ExportMappings { get; init; } = true;

        /// <summary>
        /// Gets or sets the filename for mappings export (relative to output directory).
        /// </summary>
        public string MappingsFileName { get; init; } = "uid-mappings.json";

        /// <summary>
        /// Default options for batch processing.
        /// </summary>
        public static BatchDeidentificationOptions Default { get; } = new();
    }

    /// <summary>
    /// Progress information for batch de-identification.
    /// </summary>
    public readonly struct BatchProgress
    {
        /// <summary>
        /// Gets the number of files processed so far.
        /// </summary>
        public int ProcessedFiles { get; init; }

        /// <summary>
        /// Gets the total number of files to process.
        /// </summary>
        public int TotalFiles { get; init; }

        /// <summary>
        /// Gets the name of the current file being processed.
        /// </summary>
        public string? CurrentFile { get; init; }

        /// <summary>
        /// Gets the number of files successfully processed.
        /// </summary>
        public int SuccessCount { get; init; }

        /// <summary>
        /// Gets the number of files that had errors.
        /// </summary>
        public int ErrorCount { get; init; }

        /// <summary>
        /// Gets the completion percentage.
        /// </summary>
        public double PercentComplete => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
    }
}
