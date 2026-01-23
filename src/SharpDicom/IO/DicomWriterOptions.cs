using System;
using SharpDicom.Data;

namespace SharpDicom.IO
{
    /// <summary>
    /// Specifies how sequence lengths should be encoded when writing DICOM files.
    /// </summary>
    public enum SequenceLengthEncoding
    {
        /// <summary>
        /// Use undefined length with delimiters (FFFE,E00D/E0DD).
        /// This is more widely compatible and allows streaming writes.
        /// </summary>
        Undefined,

        /// <summary>
        /// Calculate and write explicit lengths.
        /// This requires knowing the total sequence length before writing.
        /// </summary>
        Defined
    }

    /// <summary>
    /// Options for configuring DICOM file writing behavior.
    /// </summary>
    public class DicomWriterOptions
    {
        /// <summary>
        /// Gets or sets the transfer syntax for the dataset.
        /// File Meta Information is always written with Explicit VR Little Endian.
        /// Default is ExplicitVRLittleEndian.
        /// </summary>
        public TransferSyntax TransferSyntax { get; init; } = TransferSyntax.ExplicitVRLittleEndian;

        /// <summary>
        /// Gets or sets how sequence lengths should be encoded.
        /// Default is Undefined (uses delimiters).
        /// </summary>
        public SequenceLengthEncoding SequenceLength { get; init; } = SequenceLengthEncoding.Undefined;

        /// <summary>
        /// Gets or sets the buffer size for stream operations.
        /// Default is 81920 bytes (80KB).
        /// </summary>
        public int BufferSize { get; init; } = 81920;

        // File Meta Information options

        /// <summary>
        /// Gets or sets whether to auto-generate File Meta Information.
        /// When true, FMI is generated from the dataset. When false, you must provide FMI.
        /// Default is true.
        /// </summary>
        public bool AutoGenerateFmi { get; init; } = true;

        /// <summary>
        /// Gets or sets the Implementation Class UID to use in FMI.
        /// If null, uses SharpDicomInfo.ImplementationClassUID.
        /// </summary>
        public DicomUID? ImplementationClassUID { get; init; }

        /// <summary>
        /// Gets or sets the Implementation Version Name to use in FMI.
        /// If null, uses SharpDicomInfo.ImplementationVersionName.
        /// </summary>
        public string? ImplementationVersionName { get; init; } = SharpDicomInfo.ImplementationVersionName;

        // Preamble options

        /// <summary>
        /// Gets or sets the 128-byte preamble to write.
        /// If null, writes 128 zero bytes.
        /// </summary>
        public ReadOnlyMemory<byte>? Preamble { get; init; }

        // Validation options

        /// <summary>
        /// Gets or sets whether to validate that SOPClassUID and SOPInstanceUID are present in the dataset.
        /// When true, throws if these required UIDs are missing.
        /// Default is true.
        /// </summary>
        public bool ValidateFmiUids { get; init; } = true;

        /// <summary>
        /// Gets the default writer options.
        /// </summary>
        public static readonly DicomWriterOptions Default = new();
    }
}
