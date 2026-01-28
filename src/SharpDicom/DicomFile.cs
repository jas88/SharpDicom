using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;
using SharpDicom.IO;
using SharpDicom.Validation;

namespace SharpDicom;

/// <summary>
/// Represents a DICOM Part 10 file.
/// </summary>
/// <remarks>
/// DicomFile is the main entry point for working with DICOM files. Use the static
/// <see cref="Open(string, DicomReaderOptions?)"/> or <see cref="OpenAsync(string, DicomReaderOptions?, CancellationToken)"/>
/// methods to read DICOM files, or create new instances with <see cref="DicomFile(DicomDataset, TransferSyntax?)"/>.
/// </remarks>
public sealed class DicomFile
{
    /// <summary>
    /// Gets the 128-byte preamble (may be empty if file had no preamble).
    /// </summary>
    public ReadOnlyMemory<byte> Preamble { get; }

    /// <summary>
    /// Gets the File Meta Information (Group 0002 elements).
    /// </summary>
    public DicomDataset FileMetaInfo { get; }

    /// <summary>
    /// Gets the main dataset content.
    /// </summary>
    public DicomDataset Dataset { get; }

    /// <summary>
    /// Gets the Transfer Syntax used for the dataset.
    /// </summary>
    public TransferSyntax TransferSyntax { get; }

    /// <summary>
    /// Gets the validation result from parsing, if validation was enabled.
    /// </summary>
    /// <remarks>
    /// Contains all validation issues collected during parsing when
    /// DicomReaderOptions.CollectValidationIssues is true and a
    /// ValidationProfile is configured. Null if validation was not enabled
    /// or if no issues were found and collection was disabled.
    /// </remarks>
    public ValidationResult? ValidationResult { get; internal set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomFile"/> class from a dataset.
    /// </summary>
    /// <param name="dataset">The dataset to wrap.</param>
    /// <param name="transferSyntax">The transfer syntax (defaults to Explicit VR Little Endian).</param>
    /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
    public DicomFile(DicomDataset dataset, TransferSyntax? transferSyntax = null)
    {
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        TransferSyntax = transferSyntax ?? TransferSyntax.ExplicitVRLittleEndian;
        FileMetaInfo = new DicomDataset();
        Preamble = new byte[128];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomFile"/> class with explicit components.
    /// </summary>
    /// <param name="preamble">The 128-byte preamble.</param>
    /// <param name="fileMetaInfo">The File Meta Information dataset.</param>
    /// <param name="dataset">The main dataset.</param>
    /// <param name="transferSyntax">The transfer syntax.</param>
    internal DicomFile(
        ReadOnlyMemory<byte> preamble,
        DicomDataset fileMetaInfo,
        DicomDataset dataset,
        TransferSyntax transferSyntax)
    {
        Preamble = preamble;
        FileMetaInfo = fileMetaInfo;
        Dataset = dataset;
        TransferSyntax = transferSyntax;
    }

    /// <summary>
    /// Opens a DICOM file from the specified path.
    /// </summary>
    /// <param name="path">The path to the DICOM file.</param>
    /// <param name="options">Reader options, or null for defaults.</param>
    /// <returns>The opened DICOM file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static DicomFile Open(string path, DicomReaderOptions? options = null)
    {
        ThrowHelpers.ThrowIfNull(path, nameof(path));

        using var stream = File.OpenRead(path);
        return Open(stream, options);
    }

    /// <summary>
    /// Opens a DICOM file from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="options">Reader options, or null for defaults.</param>
    /// <returns>The opened DICOM file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static DicomFile Open(Stream stream, DicomReaderOptions? options = null)
    {
        ThrowHelpers.ThrowIfNull(stream, nameof(stream));

        return OpenAsync(stream, options, CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Opens a DICOM file asynchronously from the specified path.
    /// </summary>
    /// <param name="path">The path to the DICOM file.</param>
    /// <param name="options">Reader options, or null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task containing the opened DICOM file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when path is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static async ValueTask<DicomFile> OpenAsync(
        string path,
        DicomReaderOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowHelpers.ThrowIfNull(path, nameof(path));

#if NETSTANDARD2_0
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        try
        {
            return await OpenAsync(stream, options, ct).ConfigureAwait(false);
        }
        finally
        {
            stream.Dispose();
        }
#else
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await OpenAsync(stream, options, ct).ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Opens a DICOM file asynchronously from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="options">Reader options, or null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task containing the opened DICOM file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async ValueTask<DicomFile> OpenAsync(
        Stream stream,
        DicomReaderOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowHelpers.ThrowIfNull(stream, nameof(stream));

#if NETSTANDARD2_0
        var reader = new DicomFileReader(stream, options, leaveOpen: true);
        try
        {
            await reader.ReadFileMetaInfoAsync(ct).ConfigureAwait(false);
            var dataset = await reader.ReadDatasetAsync(ct).ConfigureAwait(false);

            var file = new DicomFile(
                reader.Preamble,
                reader.FileMetaInfo ?? new DicomDataset(),
                dataset,
                reader.TransferSyntax)
            {
                ValidationResult = reader.ValidationResult
            };
            return file;
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
#else
        await using var reader = new DicomFileReader(stream, options, leaveOpen: true);

        await reader.ReadFileMetaInfoAsync(ct).ConfigureAwait(false);
        var dataset = await reader.ReadDatasetAsync(ct).ConfigureAwait(false);

        var file = new DicomFile(
            reader.Preamble,
            reader.FileMetaInfo ?? new DicomDataset(),
            dataset,
            reader.TransferSyntax)
        {
            ValidationResult = reader.ValidationResult
        };
        return file;
#endif
    }

    /// <summary>
    /// Gets a string value from the dataset.
    /// </summary>
    /// <param name="tag">The tag to retrieve.</param>
    /// <returns>The string value, or null if not found or not a string.</returns>
    public string? GetString(DicomTag tag)
        => Dataset.GetString(tag);

    /// <summary>
    /// Checks if a tag exists in the dataset.
    /// </summary>
    /// <param name="tag">The tag to check.</param>
    /// <returns>True if the tag exists; otherwise, false.</returns>
    public bool Contains(DicomTag tag)
        => Dataset.Contains(tag);

    /// <summary>
    /// Gets an element from the dataset by tag.
    /// </summary>
    /// <param name="tag">The tag to retrieve.</param>
    /// <returns>The element, or null if not found.</returns>
    public IDicomElement? this[DicomTag tag]
        => Dataset[tag];

    /// <summary>
    /// Gets the pixel data element from the dataset.
    /// </summary>
    /// <returns>The pixel data element, or null if not present.</returns>
    /// <remarks>
    /// This is a convenience property that delegates to <see cref="DicomDataset.GetPixelData"/>.
    /// Returns the element at tag (7FE0,0010) as a <see cref="DicomPixelDataElement"/>.
    /// The pixel data may be native (uncompressed) or encapsulated (compressed).
    /// </remarks>
    public DicomPixelDataElement? PixelData => Dataset.GetPixelData();

    /// <summary>
    /// Gets a value indicating whether this file contains pixel data.
    /// </summary>
    public bool HasPixelData => Dataset.HasPixelData;

    /// <summary>
    /// Saves the DICOM file to the specified path.
    /// </summary>
    /// <param name="path">The path to save to.</param>
    /// <param name="options">Writer options, or null for defaults.</param>
    public void Save(string path, DicomWriterOptions? options = null)
    {
        ThrowHelpers.ThrowIfNull(path, nameof(path));

        using var stream = File.Create(path);
        Save(stream, options);
    }

    /// <summary>
    /// Saves the DICOM file to a stream.
    /// </summary>
    /// <param name="stream">The stream to save to.</param>
    /// <param name="options">Writer options, or null for defaults.</param>
    public void Save(Stream stream, DicomWriterOptions? options = null)
    {
        ThrowHelpers.ThrowIfNull(stream, nameof(stream));

        using var writer = new DicomFileWriter(stream, options, leaveOpen: true);
        writer.Write(this);
    }

    /// <summary>
    /// Asynchronously saves the DICOM file to the specified path.
    /// </summary>
    /// <param name="path">The path to save to.</param>
    /// <param name="options">Writer options, or null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask SaveAsync(
        string path,
        DicomWriterOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowHelpers.ThrowIfNull(path, nameof(path));

#if NETSTANDARD2_0
        var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        try
        {
            await SaveAsync(stream, options, ct).ConfigureAwait(false);
        }
        finally
        {
            stream.Dispose();
        }
#else
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await SaveAsync(stream, options, ct).ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Asynchronously saves the DICOM file to a stream.
    /// </summary>
    /// <param name="stream">The stream to save to.</param>
    /// <param name="options">Writer options, or null for defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask SaveAsync(
        Stream stream,
        DicomWriterOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowHelpers.ThrowIfNull(stream, nameof(stream));

#if NETSTANDARD2_0
        var writer = new DicomFileWriter(stream, options, leaveOpen: true);
        try
        {
            await writer.WriteAsync(this, ct).ConfigureAwait(false);
        }
        finally
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
#else
        await using var writer = new DicomFileWriter(stream, options, leaveOpen: true);
        await writer.WriteAsync(this, ct).ConfigureAwait(false);
#endif
    }
}
