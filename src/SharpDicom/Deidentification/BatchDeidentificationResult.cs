using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Result of batch de-identification processing.
    /// </summary>
    public sealed class BatchDeidentificationResult
    {
        /// <summary>
        /// Gets or sets the total number of files found.
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Gets the number of files processed (internal counter).
        /// </summary>
        internal int ProcessedFilesCounter;

        /// <summary>
        /// Gets the number of files processed.
        /// </summary>
        public int ProcessedFiles => ProcessedFilesCounter;

        /// <summary>
        /// Gets the number of files successfully processed (internal counter).
        /// </summary>
        internal int SuccessCountCounter;

        /// <summary>
        /// Gets the number of files successfully processed.
        /// </summary>
        public int SuccessCount => SuccessCountCounter;

        /// <summary>
        /// Gets the number of files with errors (internal counter).
        /// </summary>
        internal int ErrorCountCounter;

        /// <summary>
        /// Gets the number of files with errors.
        /// </summary>
        public int ErrorCount => ErrorCountCounter;

        /// <summary>
        /// Gets the total number of attributes removed across all files (internal counter).
        /// </summary>
        internal int TotalAttributesRemovedCounter;

        /// <summary>
        /// Gets the total number of attributes removed across all files.
        /// </summary>
        public int TotalAttributesRemoved => TotalAttributesRemovedCounter;

        /// <summary>
        /// Gets the total number of attributes replaced across all files (internal counter).
        /// </summary>
        internal int TotalAttributesReplacedCounter;

        /// <summary>
        /// Gets the total number of attributes replaced across all files.
        /// </summary>
        public int TotalAttributesReplaced => TotalAttributesReplacedCounter;

        /// <summary>
        /// Gets the total number of UIDs remapped across all files (internal counter).
        /// </summary>
        internal int TotalUidsRemappedCounter;

        /// <summary>
        /// Gets the total number of UIDs remapped across all files.
        /// </summary>
        public int TotalUidsRemapped => TotalUidsRemappedCounter;

        /// <summary>
        /// Gets or sets the path to the exported mappings file, if applicable.
        /// </summary>
        public string? MappingsExportPath { get; set; }

        /// <summary>
        /// Gets the list of errors encountered during processing.
        /// </summary>
        public List<BatchError> Errors { get; } = new();

        /// <summary>
        /// Gets the success rate as a percentage.
        /// </summary>
        public double SuccessRate => TotalFiles > 0 ? (double)SuccessCount / TotalFiles * 100 : 0;

        /// <summary>
        /// Gets a value indicating whether all files were processed successfully.
        /// </summary>
        public bool AllSucceeded => ErrorCount == 0 && SuccessCount == TotalFiles;
    }

    /// <summary>
    /// Information about an error that occurred during batch processing.
    /// </summary>
    public sealed class BatchError
    {
        /// <summary>
        /// Gets or sets the path to the file that had an error.
        /// </summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the error message.
        /// </summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the exception that was thrown, if any.
        /// </summary>
        public Exception? Exception { get; init; }
    }
}
