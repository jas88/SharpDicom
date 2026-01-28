using System;
using SharpDicom.Internal;

namespace SharpDicom.Data;

/// <summary>
/// Contains metadata about pixel data extracted from a DICOM dataset.
/// </summary>
/// <remarks>
/// This struct provides computed properties for frame size calculations and
/// a factory method to extract pixel data information from a DicomDataset.
/// All properties are nullable to handle cases where tags are missing.
/// </remarks>
public readonly struct PixelDataInfo
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
    /// <remarks>
    /// Common values are 8, 12, or 16 bits.
    /// </remarks>
    public ushort? BitsAllocated { get; init; }

    /// <summary>
    /// Gets the number of bits stored within BitsAllocated. Tag (0028,0101).
    /// </summary>
    /// <remarks>
    /// BitsStored is always less than or equal to BitsAllocated.
    /// </remarks>
    public ushort? BitsStored { get; init; }

    /// <summary>
    /// Gets the most significant bit position. Tag (0028,0102).
    /// </summary>
    public ushort? HighBit { get; init; }

    /// <summary>
    /// Gets the number of samples per pixel. Tag (0028,0002).
    /// </summary>
    /// <remarks>
    /// 1 for grayscale, 3 for RGB/YBR color images.
    /// </remarks>
    public ushort? SamplesPerPixel { get; init; }

    /// <summary>
    /// Gets the number of frames in the pixel data. Tag (0028,0008).
    /// </summary>
    /// <remarks>
    /// Single-frame images may not have this tag; defaults to 1 if missing.
    /// </remarks>
    public int? NumberOfFrames { get; init; }

    /// <summary>
    /// Gets the planar configuration. Tag (0028,0006).
    /// </summary>
    /// <remarks>
    /// 0 = color-by-pixel (R1G1B1R2G2B2...), 1 = color-by-plane (R1R2...G1G2...B1B2...).
    /// Only meaningful when SamplesPerPixel > 1.
    /// </remarks>
    public ushort? PlanarConfiguration { get; init; }

    /// <summary>
    /// Gets the pixel representation. Tag (0028,0103).
    /// </summary>
    /// <remarks>
    /// 0 = unsigned integer, 1 = two's complement (signed) integer.
    /// </remarks>
    public ushort? PixelRepresentation { get; init; }

    /// <summary>
    /// Gets the photometric interpretation. Tag (0028,0004).
    /// </summary>
    /// <remarks>
    /// Describes the intended interpretation of pixel data (e.g., MONOCHROME1, MONOCHROME2, RGB).
    /// </remarks>
    public string? PhotometricInterpretation { get; init; }

    /// <summary>
    /// Gets the number of bytes per sample based on BitsAllocated.
    /// </summary>
    /// <remarks>
    /// Calculated as (BitsAllocated + 7) / 8, rounded up. Defaults to 2 if BitsAllocated is not specified.
    /// </remarks>
    public int BytesPerSample => (BitsAllocated.GetValueOrDefault(16) + 7) / 8;

    /// <summary>
    /// Gets the size in bytes of a single frame, or null if dimensions are incomplete.
    /// </summary>
    /// <remarks>
    /// Calculated as Rows * Columns * SamplesPerPixel * BytesPerSample.
    /// Returns null if Rows, Columns, or SamplesPerPixel is missing.
    /// </remarks>
    public long? FrameSize
    {
        get
        {
            if (!Rows.HasValue || !Columns.HasValue || !SamplesPerPixel.HasValue)
            {
                return null;
            }

            return (long)Rows.Value * Columns.Value * SamplesPerPixel.Value * BytesPerSample;
        }
    }

    /// <summary>
    /// Gets the total size in bytes of all pixel data, or null if dimensions are incomplete.
    /// </summary>
    /// <remarks>
    /// Calculated as FrameSize * NumberOfFrames. Returns null if FrameSize cannot be computed.
    /// If NumberOfFrames is not specified, assumes 1 frame.
    /// </remarks>
    public long? TotalSize
    {
        get
        {
            var frameSize = FrameSize;
            if (!frameSize.HasValue)
            {
                return null;
            }

            return frameSize.Value * NumberOfFrames.GetValueOrDefault(1);
        }
    }

    /// <summary>
    /// Gets a value indicating whether all required dimensions are present.
    /// </summary>
    public bool HasImageDimensions => Rows.HasValue && Columns.HasValue;

    /// <summary>
    /// Creates a <see cref="PixelDataInfo"/> from a DICOM dataset.
    /// </summary>
    /// <param name="dataset">The dataset to extract pixel data information from.</param>
    /// <returns>A new <see cref="PixelDataInfo"/> containing values from the dataset.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataset"/> is null.</exception>
    public static PixelDataInfo FromDataset(DicomDataset dataset)
    {
        ThrowHelpers.ThrowIfNull(dataset, nameof(dataset));

        return new PixelDataInfo
        {
            Rows = GetUInt16(dataset, DicomTag.Rows),
            Columns = GetUInt16(dataset, DicomTag.Columns),
            BitsAllocated = GetUInt16(dataset, DicomTag.BitsAllocated),
            BitsStored = GetUInt16(dataset, DicomTag.BitsStored),
            HighBit = GetUInt16(dataset, DicomTag.HighBit),
            SamplesPerPixel = GetUInt16(dataset, DicomTag.SamplesPerPixel),
            NumberOfFrames = GetInt32FromString(dataset, DicomTag.NumberOfFrames),
            PlanarConfiguration = GetUInt16(dataset, DicomTag.PlanarConfiguration),
            PixelRepresentation = GetUInt16(dataset, DicomTag.PixelRepresentation),
            PhotometricInterpretation = dataset.GetString(DicomTag.PhotometricInterpretation)?.Trim()
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
