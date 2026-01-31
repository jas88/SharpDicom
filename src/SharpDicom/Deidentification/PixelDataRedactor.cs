using System;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Internal;
using SharpDicom.IO;

namespace SharpDicom.Deidentification;

/// <summary>
/// Redacts rectangular regions in pixel data for burned-in annotation removal.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the Clean Pixel Data Option from DICOM PS3.15,
/// which involves removing identifying information that may be embedded
/// directly in pixel data (burned-in annotations).
/// </para>
/// <para>
/// Common modalities with burned-in annotations include:
/// <list type="bullet">
/// <item><description>US (Ultrasound) - very common</description></item>
/// <item><description>ES (Endoscopy)</description></item>
/// <item><description>SC (Secondary Capture / screenshots)</description></item>
/// <item><description>XC (External-camera Photography)</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class PixelDataRedactor
{
    /// <summary>
    /// Redacts specified regions in the dataset's pixel data.
    /// </summary>
    /// <param name="dataset">The dataset containing pixel data to redact.</param>
    /// <param name="options">Redaction options specifying regions and fill value.</param>
    /// <returns>Result containing statistics and any warnings.</returns>
    /// <exception cref="ArgumentNullException">Dataset or options is null.</exception>
    /// <exception cref="InvalidOperationException">Pixel data is compressed and SkipCompressed is false.</exception>
    public static RedactionResult RedactRegions(DicomDataset dataset, RedactionOptions options)
    {
        ThrowHelpers.ThrowIfNull(dataset, nameof(dataset));
        ThrowHelpers.ThrowIfNull(options, nameof(options));

        var result = new RedactionResult();

        // Get pixel data info
        var info = PixelDataInfo.FromDataset(dataset);
        if (!info.HasImageDimensions)
        {
            result.Warnings.Add("No pixel data dimensions found in dataset (missing Rows/Columns)");
            return result;
        }

        // Get pixel data element
        var pixelDataElement = dataset.GetPixelData();
        if (pixelDataElement == null)
        {
            result.Warnings.Add("No pixel data found in dataset");
            return result;
        }

        // Check for compressed data
        if (pixelDataElement.IsEncapsulated)
        {
            if (options.SkipCompressed)
            {
                result.Warnings.Add("Pixel data is compressed (encapsulated). Decompress before redaction.");
                return result;
            }
            throw new InvalidOperationException("Cannot redact compressed pixel data. Decompress first or set SkipCompressed=true.");
        }

        // Skip if no regions to redact
        if (options.Regions.Count == 0)
        {
            return result;
        }

        // Validate regions against image dimensions
        ValidateRegions(options.Regions, info, result);

        // Get pixel data as a modifiable buffer
        var originalData = pixelDataElement.RawValue;
        var modifiedData = originalData.ToArray();

        // Determine number of frames
        var numberOfFrames = info.NumberOfFrames ?? 1;
        var frameSize = info.FrameSize;
        if (!frameSize.HasValue)
        {
            result.Warnings.Add("Cannot determine frame size - missing SamplesPerPixel or BitsAllocated");
            return result;
        }

        var columns = info.Columns!.Value;
        var rows = info.Rows!.Value;
        var bitsAllocated = info.BitsAllocated ?? 8;
        var samplesPerPixel = info.SamplesPerPixel ?? 1;
        var bytesPerSample = (bitsAllocated + 7) / 8;
        var bytesPerPixel = bytesPerSample * samplesPerPixel;
        var rowStride = columns * bytesPerPixel;

        // Process each frame
        for (int frame = 0; frame < numberOfFrames; frame++)
        {
            // Use long arithmetic to avoid integer overflow for large multi-frame images
            long frameOffsetLong = (long)frame * frameSize.Value;
            if (frameOffsetLong > int.MaxValue)
                throw new InvalidOperationException($"Frame offset exceeds maximum supported size: frame {frame} at offset {frameOffsetLong}");
            var frameOffset = (int)frameOffsetLong;
            var frameSpan = modifiedData.AsSpan(frameOffset, (int)frameSize.Value);

            var modified = RedactFrame(
                frameSpan, columns, rows, bytesPerSample, bytesPerPixel, rowStride,
                options, frame, result);

            if (modified)
            {
                result.FramesModified++;
            }
        }

        // Only update dataset if we made changes
        if (result.RegionsRedacted > 0)
        {
            // Create new pixel data element with modified data
            var newSource = new ImmediatePixelDataSource(modifiedData);
            var newPixelData = new DicomPixelDataElement(
                newSource,
                pixelDataElement.VR,
                info,
                isEncapsulated: false,
                fragments: null);

            // Replace pixel data in dataset
            dataset.Add(newPixelData);

            // Update Burned In Annotation tag
            if (options.UpdateBurnedInAnnotationTag)
            {
                var noBytes = Encoding.ASCII.GetBytes("NO");
                if (noBytes.Length % 2 != 0)
                {
                    var padded = new byte[noBytes.Length + 1];
                    noBytes.CopyTo(padded, 0);
                    padded[padded.Length - 1] = (byte)' ';
                    noBytes = padded;
                }
                dataset.Add(new DicomStringElement(DicomTag.BurnedInAnnotation, DicomVR.CS, noBytes));
            }
        }

        return result;
    }

    private static void ValidateRegions(
        IReadOnlyList<RedactionRegion> regions,
        PixelDataInfo info,
        RedactionResult result)
    {
        var columns = info.Columns!.Value;
        var rows = info.Rows!.Value;

        foreach (var region in regions)
        {
            if (region.X < 0 || region.Y < 0)
            {
                result.Warnings.Add($"Region has negative coordinates: ({region.X}, {region.Y})");
            }

            if (region.X + region.Width > columns)
            {
                result.Warnings.Add($"Region extends beyond image width: X={region.X}, Width={region.Width}, ImageWidth={columns}");
            }

            if (region.Y + region.Height > rows)
            {
                result.Warnings.Add($"Region extends beyond image height: Y={region.Y}, Height={region.Height}, ImageHeight={rows}");
            }

            if (region.Width <= 0 || region.Height <= 0)
            {
                result.Warnings.Add($"Region has non-positive dimensions: {region.Width}x{region.Height}");
            }
        }
    }

    private static bool RedactFrame(
        Span<byte> frameData,
        int columns,
        int rows,
        int bytesPerSample,
        int bytesPerPixel,
        int rowStride,
        RedactionOptions options,
        int frameIndex,
        RedactionResult result)
    {
        var modified = false;

        foreach (var region in options.Regions)
        {
            // Skip if region is for a different frame
            if (region.Frame.HasValue && region.Frame.Value != frameIndex)
                continue;

            // Clamp region to image bounds
            var x1 = Math.Max(0, region.X);
            var y1 = Math.Max(0, region.Y);
            var x2 = Math.Min(columns, region.X + region.Width);
            var y2 = Math.Min(rows, region.Y + region.Height);

            // Skip if clamped region is empty
            if (x2 <= x1 || y2 <= y1)
                continue;

            // Fill the region with the specified value
            for (int y = y1; y < y2; y++)
            {
                var rowOffset = y * rowStride;

                for (int x = x1; x < x2; x++)
                {
                    var pixelOffset = rowOffset + (x * bytesPerPixel);
                    FillPixel(frameData, pixelOffset, options.FillValue, bytesPerSample, bytesPerPixel);
                }
            }

            result.RegionsRedacted++;
            modified = true;
        }

        return modified;
    }

    private static void FillPixel(Span<byte> data, int offset, uint fillValue, int bytesPerSample, int bytesPerPixel)
    {
        // Calculate samples per pixel
        var samplesPerPixel = bytesPerPixel / bytesPerSample;

        // Handle different bit depths
        switch (bytesPerSample)
        {
            case 1:
                // 8-bit: Use lowest byte of fill value for grayscale,
                // or extract R, G, B for color
                if (samplesPerPixel == 1)
                {
                    data[offset] = (byte)(fillValue & 0xFF);
                }
                else if (samplesPerPixel == 3)
                {
                    // RGB: fillValue = R << 16 | G << 8 | B
                    data[offset] = (byte)((fillValue >> 16) & 0xFF);     // R
                    data[offset + 1] = (byte)((fillValue >> 8) & 0xFF); // G
                    data[offset + 2] = (byte)(fillValue & 0xFF);        // B
                }
                else
                {
                    // Other (e.g., 4-sample RGBA)
                    var value = (byte)(fillValue & 0xFF);
                    for (int s = 0; s < samplesPerPixel; s++)
                    {
                        data[offset + s] = value;
                    }
                }
                break;

            case 2:
                // 16-bit (little-endian)
                var value16 = (ushort)(fillValue & 0xFFFF);
                for (int s = 0; s < samplesPerPixel; s++)
                {
                    var sampleOffset = offset + (s * 2);
                    data[sampleOffset] = (byte)(value16 & 0xFF);
                    data[sampleOffset + 1] = (byte)(value16 >> 8);
                }
                break;

            case 4:
                // 32-bit (little-endian)
                for (int s = 0; s < samplesPerPixel; s++)
                {
                    var sampleOffset = offset + (s * 4);
                    data[sampleOffset] = (byte)(fillValue & 0xFF);
                    data[sampleOffset + 1] = (byte)((fillValue >> 8) & 0xFF);
                    data[sampleOffset + 2] = (byte)((fillValue >> 16) & 0xFF);
                    data[sampleOffset + 3] = (byte)((fillValue >> 24) & 0xFF);
                }
                break;

            default:
                // Unsupported bit depth - fill with zeros
                for (int i = 0; i < bytesPerPixel; i++)
                {
                    data[offset + i] = (byte)(fillValue & 0xFF);
                }
                break;
        }
    }
}

/// <summary>
/// Result of a pixel data redaction operation.
/// </summary>
public sealed class RedactionResult
{
    /// <summary>
    /// Gets the number of regions that were successfully redacted.
    /// </summary>
    public int RegionsRedacted { get; internal set; }

    /// <summary>
    /// Gets the number of frames that were modified.
    /// </summary>
    public int FramesModified { get; internal set; }

    /// <summary>
    /// Gets any warnings generated during redaction.
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the redaction was successful (no errors).
    /// </summary>
    /// <remarks>
    /// This returns true even if there were warnings. Check <see cref="Warnings"/>
    /// for non-fatal issues.
    /// </remarks>
    public bool Success => Warnings.Count == 0 || WasModified;

    /// <summary>
    /// Gets a value indicating whether any modifications were made.
    /// </summary>
    public bool WasModified => RegionsRedacted > 0;
}
