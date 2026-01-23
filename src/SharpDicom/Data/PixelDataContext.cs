using System;

namespace SharpDicom.Data;

/// <summary>
/// Provides context information about pixel data for callback-based decisions.
/// </summary>
/// <remarks>
/// This struct is passed to the pixel data callback function when using
/// <see cref="PixelDataHandling.Callback"/> mode, allowing the callback to
/// make per-instance decisions about how to handle pixel data.
/// </remarks>
public readonly struct PixelDataContext
{
    /// <summary>
    /// Gets the number of rows (height) in pixels. Tag (0028,0010).
    /// </summary>
    public ushort? Rows { get; init; }

    /// <summary>
    /// Gets the number of columns (width) in pixels. Tag (0028,0011).
    /// </summary>
    public ushort? Columns { get; init; }

    /// <summary>
    /// Gets the number of bits allocated for each pixel sample. Tag (0028,0100).
    /// </summary>
    public ushort? BitsAllocated { get; init; }

    /// <summary>
    /// Gets the number of frames in the pixel data. Tag (0028,0008).
    /// </summary>
    public int? NumberOfFrames { get; init; }

    /// <summary>
    /// Gets the transfer syntax of the DICOM file.
    /// </summary>
    public TransferSyntax TransferSyntax { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pixel data is encapsulated (compressed).
    /// </summary>
    public bool IsEncapsulated { get; init; }

    /// <summary>
    /// Gets the declared length of the pixel data value in the element header.
    /// </summary>
    /// <remarks>
    /// May be 0xFFFFFFFF for undefined length (encapsulated pixel data).
    /// </remarks>
    public long ValueLength { get; init; }

    /// <summary>
    /// Gets the number of samples per pixel. Tag (0028,0002).
    /// </summary>
    public ushort? SamplesPerPixel { get; init; }

    /// <summary>
    /// Gets the estimated total size of pixel data in bytes.
    /// </summary>
    /// <remarks>
    /// Calculated as Rows * Columns * BytesPerSample * NumberOfFrames * SamplesPerPixel.
    /// Returns null if any required dimension is missing.
    /// </remarks>
    public long? EstimatedSize
    {
        get
        {
            if (!Rows.HasValue || !Columns.HasValue || !BitsAllocated.HasValue)
            {
                return null;
            }

            int bytesPerSample = (BitsAllocated.Value + 7) / 8;
            int samples = SamplesPerPixel.GetValueOrDefault(1);
            int frames = NumberOfFrames.GetValueOrDefault(1);

            return (long)Rows.Value * Columns.Value * bytesPerSample * samples * frames;
        }
    }

    /// <summary>
    /// Gets a value indicating whether both Rows and Columns are present.
    /// </summary>
    public bool HasImageDimensions => Rows.HasValue && Columns.HasValue;

    /// <summary>
    /// Creates a <see cref="PixelDataContext"/> from a DICOM dataset.
    /// </summary>
    /// <param name="dataset">The dataset to extract context from.</param>
    /// <param name="transferSyntax">The transfer syntax of the file.</param>
    /// <param name="isEncapsulated">Whether the pixel data is encapsulated.</param>
    /// <param name="valueLength">The declared value length from the element header.</param>
    /// <returns>A new <see cref="PixelDataContext"/> with values from the dataset.</returns>
    /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
    public static PixelDataContext FromDataset(
        DicomDataset dataset,
        TransferSyntax transferSyntax,
        bool isEncapsulated,
        long valueLength)
    {
        if (dataset is null)
        {
            throw new ArgumentNullException(nameof(dataset));
        }

        return new PixelDataContext
        {
            Rows = GetUInt16(dataset, DicomTag.Rows),
            Columns = GetUInt16(dataset, DicomTag.Columns),
            BitsAllocated = GetUInt16(dataset, DicomTag.BitsAllocated),
            NumberOfFrames = GetInt32FromString(dataset, DicomTag.NumberOfFrames),
            SamplesPerPixel = GetUInt16(dataset, DicomTag.SamplesPerPixel),
            TransferSyntax = transferSyntax,
            IsEncapsulated = isEncapsulated,
            ValueLength = valueLength
        };
    }

    private static ushort? GetUInt16(DicomDataset dataset, DicomTag tag)
    {
        var element = dataset[tag];
        if (element is DicomNumericElement numeric)
        {
            return numeric.GetUInt16();
        }
        return null;
    }

    private static int? GetInt32FromString(DicomDataset dataset, DicomTag tag)
    {
        // NumberOfFrames is IS (Integer String) VR
        var str = dataset.GetString(tag)?.Trim();
        if (string.IsNullOrEmpty(str))
        {
            return null;
        }

        if (int.TryParse(str, out var value))
        {
            return value;
        }

        return null;
    }
}
