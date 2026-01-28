using System;
using SharpDicom.Data;
using SharpDicom.Validation;

namespace SharpDicom.IO
{
    /// <summary>
    /// Configuration options for DICOM file reading.
    /// </summary>
    public sealed class DicomReaderOptions
    {
        /// <summary>
        /// Gets how to handle the preamble and DICM prefix.
        /// </summary>
        public FilePreambleHandling Preamble { get; init; } = FilePreambleHandling.Optional;

        /// <summary>
        /// Gets how to handle File Meta Information.
        /// </summary>
        public FileMetaInfoHandling FileMetaInfo { get; init; } = FileMetaInfoHandling.Optional;

        /// <summary>
        /// Gets how to handle invalid VRs.
        /// </summary>
        public InvalidVRHandling InvalidVR { get; init; } = InvalidVRHandling.MapToUN;

        /// <summary>
        /// Gets the maximum element length to accept (prevents OOM on malformed files).
        /// </summary>
        public uint MaxElementLength { get; init; } = 256 * 1024 * 1024; // 256 MB

        /// <summary>
        /// Gets the maximum allowed sequence nesting depth.
        /// </summary>
        /// <remarks>
        /// This limit prevents stack overflow and denial-of-service attacks from deeply nested sequences.
        /// Real-world DICOM files rarely exceed 10 levels; RT/SR may reach 5-6 levels.
        /// Default is 128 which is very conservative.
        /// </remarks>
        public int MaxSequenceDepth { get; init; } = 128;

        /// <summary>
        /// Gets the maximum total number of sequence items across all sequences.
        /// </summary>
        /// <remarks>
        /// This limit prevents memory exhaustion from files with excessive sequence items.
        /// Applies across all sequences at all nesting levels.
        /// Default is 100,000 which handles legitimate large datasets.
        /// </remarks>
        public int MaxTotalItems { get; init; } = 100_000;

        /// <summary>
        /// Gets how to handle pixel data during parsing.
        /// </summary>
        /// <remarks>
        /// Determines whether pixel data is loaded immediately, lazily, or skipped entirely.
        /// Default is <see cref="Data.PixelDataHandling.LoadInMemory"/> for backwards compatibility.
        /// </remarks>
        public PixelDataHandling PixelDataHandling { get; init; } = PixelDataHandling.LoadInMemory;

        /// <summary>
        /// Gets the callback to determine pixel data handling per instance.
        /// </summary>
        /// <remarks>
        /// Only called when <see cref="PixelDataHandling"/> is set to <see cref="Data.PixelDataHandling.Callback"/>.
        /// The callback receives context about the pixel data and returns the handling mode.
        /// </remarks>
        public Func<PixelDataContext, PixelDataHandling>? PixelDataCallback { get; init; }

        /// <summary>
        /// Gets the directory for temporary files when buffering non-seekable streams.
        /// </summary>
        /// <remarks>
        /// Defaults to the system temp directory if null.
        /// Only used when lazy loading from non-seekable streams requires buffering.
        /// </remarks>
        public string? TempDirectory { get; init; }

        /// <summary>
        /// Gets whether to retain private tags with unknown creators.
        /// </summary>
        /// <remarks>
        /// If false, private tags with unrecognized creators are discarded during parsing.
        /// Default is true to preserve all data.
        /// </remarks>
        public bool RetainUnknownPrivateTags { get; init; } = true;

        /// <summary>
        /// Gets whether to fail when orphan private elements are detected.
        /// </summary>
        /// <remarks>
        /// Orphan elements are private data elements without a corresponding creator.
        /// Set to true for strict mode validation.
        /// Default is false for maximum compatibility.
        /// </remarks>
        public bool FailOnOrphanPrivateElements { get; init; }

        /// <summary>
        /// Gets whether to fail when duplicate private creator slots are detected.
        /// </summary>
        /// <remarks>
        /// Duplicate slots occur when the same slot is assigned to different creators.
        /// This is a DICOM violation but occurs in some vendor implementations.
        /// Set to true for strict mode validation.
        /// Default is false for maximum compatibility.
        /// </remarks>
        public bool FailOnDuplicatePrivateSlots { get; init; }

        /// <summary>
        /// Gets the validation profile to use during parsing.
        /// </summary>
        /// <remarks>
        /// When set, validation rules run during element parsing.
        /// Issues are collected in DicomFile.ValidationResult and/or passed to ValidationCallback.
        /// Default is null (no validation).
        /// </remarks>
        public ValidationProfile? ValidationProfile { get; init; }

        /// <summary>
        /// Gets a callback invoked for each validation issue.
        /// </summary>
        /// <remarks>
        /// Return false to abort parsing (strict mode behavior).
        /// Return true to continue parsing (lenient mode behavior).
        /// If not set, behavior is determined by ValidationProfile.DefaultBehavior.
        /// </remarks>
        public Func<ValidationIssue, bool>? ValidationCallback { get; init; }

        /// <summary>
        /// Gets whether to collect validation issues in result.
        /// </summary>
        /// <remarks>
        /// When true, issues are accumulated in DicomFile.ValidationResult.
        /// When false, only callback is invoked (saves memory for streaming).
        /// Default is true.
        /// </remarks>
        public bool CollectValidationIssues { get; init; } = true;

        /// <summary>
        /// Gets the strict preset: requires valid preamble and FMI.
        /// </summary>
        public static DicomReaderOptions Strict { get; } = new()
        {
            Preamble = FilePreambleHandling.Require,
            FileMetaInfo = FileMetaInfoHandling.Require,
            InvalidVR = InvalidVRHandling.Throw,
            MaxSequenceDepth = 128,
            MaxTotalItems = 100_000,
            PixelDataHandling = PixelDataHandling.LoadInMemory,
            FailOnOrphanPrivateElements = true,
            FailOnDuplicatePrivateSlots = true,
            ValidationProfile = ValidationProfile.Strict,
            CollectValidationIssues = true
        };

        /// <summary>
        /// Gets the lenient preset: accepts variations.
        /// </summary>
        public static DicomReaderOptions Lenient { get; } = new()
        {
            Preamble = FilePreambleHandling.Optional,
            FileMetaInfo = FileMetaInfoHandling.Optional,
            InvalidVR = InvalidVRHandling.MapToUN,
            MaxSequenceDepth = 128,
            MaxTotalItems = 100_000,
            PixelDataHandling = PixelDataHandling.LoadInMemory,
            ValidationProfile = ValidationProfile.Lenient,
            CollectValidationIssues = true
        };

        /// <summary>
        /// Gets the permissive preset: maximum compatibility.
        /// </summary>
        public static DicomReaderOptions Permissive { get; } = new()
        {
            Preamble = FilePreambleHandling.Ignore,
            FileMetaInfo = FileMetaInfoHandling.Ignore,
            InvalidVR = InvalidVRHandling.Preserve,
            MaxSequenceDepth = 256,
            MaxTotalItems = 500_000,
            PixelDataHandling = PixelDataHandling.LoadInMemory,
            ValidationProfile = ValidationProfile.Permissive,
            CollectValidationIssues = false  // Performance optimization
        };

        /// <summary>
        /// Gets the default options (lenient parsing without validation for backward compatibility).
        /// </summary>
        /// <remarks>
        /// Default options use lenient parsing rules but do not enable validation.
        /// This ensures existing code that doesn't expect validation continues to work.
        /// Use Lenient or Strict for explicit validation.
        /// </remarks>
        public static DicomReaderOptions Default { get; } = new()
        {
            Preamble = FilePreambleHandling.Optional,
            FileMetaInfo = FileMetaInfoHandling.Optional,
            InvalidVR = InvalidVRHandling.MapToUN,
            MaxSequenceDepth = 128,
            MaxTotalItems = 100_000,
            PixelDataHandling = PixelDataHandling.LoadInMemory,
            ValidationProfile = null  // No validation by default for backward compatibility
        };
    }
}
