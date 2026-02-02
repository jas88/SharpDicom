using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// Cleans (blacks out/replaces) detected PHI regions in pixel data.
/// </summary>
/// <remarks>
/// <para>
/// This class provides methods to clean burned-in PHI from DICOM pixel data.
/// It supports both immediate span-based cleaning and async dataset-level cleaning
/// that integrates with the IBurnedInPhiDetector infrastructure.
/// </para>
/// <para>
/// Cleaning replaces detected regions with the configured replacement value:
/// <list type="bullet">
/// <item>Black (0) - Most common, clearly indicates redaction</item>
/// <item>White (max value) - Alternative for certain modalities</item>
/// <item>Average - Blends with surrounding pixels for less obvious redaction</item>
/// </list>
/// </para>
/// </remarks>
public static class PixelDataCleaner
{
    /// <summary>
    /// Cleans detected regions in pixel data by replacing with specified value.
    /// </summary>
    /// <param name="pixelData">Pixel data buffer (modified in-place).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="bitsAllocated">Bits per sample (8, 16).</param>
    /// <param name="samplesPerPixel">1 for grayscale, 3 for RGB.</param>
    /// <param name="regions">Regions to clean.</param>
    /// <param name="replacement">Replacement value type.</param>
    public static void Clean(
        Span<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel,
        IReadOnlyList<PhiRegion> regions,
        PixelReplacementValue replacement)
    {
        if (regions.Count == 0)
            return;

        var bytesPerSample = bitsAllocated / 8;
        var bytesPerPixel = bytesPerSample * samplesPerPixel;
        var rowStride = width * bytesPerPixel;

        foreach (var region in regions)
        {
            CleanRegion(pixelData, width, height, bytesPerPixel, rowStride,
                region, replacement, bitsAllocated, samplesPerPixel);
        }
    }

    private static void CleanRegion(
        Span<byte> pixelData,
        int width,
        int height,
        int bytesPerPixel,
        int rowStride,
        PhiRegion region,
        PixelReplacementValue replacement,
        int bitsAllocated,
        int samplesPerPixel)
    {
        // Clamp region to image bounds
        var x1 = Math.Max(0, region.X);
        var y1 = Math.Max(0, region.Y);
        var x2 = Math.Min(width, region.X + region.Width);
        var y2 = Math.Min(height, region.Y + region.Height);

        if (x2 <= x1 || y2 <= y1)
            return;

        // Calculate replacement value
        byte replacementByte;
        ushort replacementShort;

        if (replacement == PixelReplacementValue.AverageOfRegion)
        {
            // Calculate average of region
            (replacementByte, replacementShort) = CalculateRegionAverage(
                pixelData, rowStride, bytesPerPixel, x1, y1, x2, y2, bitsAllocated);
        }
        else
        {
            replacementByte = replacement == PixelReplacementValue.Black ? (byte)0 : (byte)255;
            replacementShort = replacement == PixelReplacementValue.Black ? (ushort)0 : (ushort)65535;
        }

        // Fill region
        for (int y = y1; y < y2; y++)
        {
            var rowStart = y * rowStride + x1 * bytesPerPixel;
            var rowEnd = y * rowStride + x2 * bytesPerPixel;

            if (rowEnd > pixelData.Length)
                rowEnd = pixelData.Length;
            if (rowStart >= pixelData.Length)
                continue;

            if (bitsAllocated == 8)
            {
                pixelData.Slice(rowStart, rowEnd - rowStart).Fill(replacementByte);
            }
            else if (bitsAllocated == 16)
            {
                var row = pixelData.Slice(rowStart, rowEnd - rowStart);
                for (int i = 0; i < row.Length; i += 2)
                {
                    if (i + 1 < row.Length)
                    {
                        row[i] = (byte)(replacementShort & 0xFF);
                        row[i + 1] = (byte)((replacementShort >> 8) & 0xFF);
                    }
                }
            }
        }
    }

    private static (byte Byte, ushort Short) CalculateRegionAverage(
        ReadOnlySpan<byte> pixelData,
        int rowStride,
        int bytesPerPixel,
        int x1, int y1, int x2, int y2,
        int bitsAllocated)
    {
        long sum = 0;
        int count = 0;

        for (int y = y1; y < y2; y++)
        {
            for (int x = x1; x < x2; x++)
            {
                var offset = y * rowStride + x * bytesPerPixel;
                if (offset + bytesPerPixel <= pixelData.Length)
                {
                    if (bitsAllocated == 8)
                    {
                        sum += pixelData[offset];
                    }
                    else if (bitsAllocated == 16 && offset + 1 < pixelData.Length)
                    {
                        sum += pixelData[offset] | (pixelData[offset + 1] << 8);
                    }
                    count++;
                }
            }
        }

        if (count == 0)
            return (0, 0);

        var avg = sum / count;
        return ((byte)Math.Min(255, avg), (ushort)Math.Min(65535, avg));
    }

    /// <summary>
    /// Cleans pixel data in a dataset using the specified detector and options.
    /// </summary>
    /// <param name="dataset">The DICOM dataset containing pixel data.</param>
    /// <param name="detector">The PHI detector to use.</param>
    /// <param name="options">Pixel cleaning options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when cleaning is done.</returns>
    /// <remarks>
    /// This method:
    /// <list type="number">
    /// <item>Retrieves pixel data using <see cref="DicomDataset.GetPixelData"/></item>
    /// <item>Extracts image parameters (width, height, bits allocated, etc.)</item>
    /// <item>Detects PHI regions using the provided detector</item>
    /// <item>Cleans detected regions</item>
    /// <item>Replaces the pixel data element with the cleaned version</item>
    /// </list>
    /// </remarks>
    public static async ValueTask CleanAsync(
        DicomDataset dataset,
        IBurnedInPhiDetector detector,
        PixelCleaningOptions options,
        CancellationToken ct = default)
    {
        // Get pixel data element - use DicomPixelDataElement (NOT DicomOtherElement)
        var pixelDataElement = dataset.GetPixelData();
        if (pixelDataElement == null)
            return;

        // Get image parameters
        var width = GetUShort(dataset, DicomTag.Columns);
        var height = GetUShort(dataset, DicomTag.Rows);
        var bitsAllocated = GetUShort(dataset, DicomTag.BitsAllocated);
        var samplesPerPixel = GetUShort(dataset, DicomTag.SamplesPerPixel);
        var modality = GetString(dataset, DicomTag.Modality);

        if (width == 0 || height == 0 || bitsAllocated == 0)
            return;

        if (samplesPerPixel == 0)
            samplesPerPixel = 1;

        // Get pixel data bytes from DicomPixelDataElement
        // Must load async first since pixel data may be lazy-loaded
        var pixelBytes = await pixelDataElement.LoadAsync(ct).ConfigureAwait(false);
        if (pixelBytes.Length == 0)
            return;

        // Detect PHI regions
        var result = await detector.DetectAsync(
            pixelBytes, width, height, bitsAllocated, samplesPerPixel, modality, ct).ConfigureAwait(false);

        if (result.Regions.Count == 0)
            return;

        // Clean regions (need mutable span)
        var mutableBytes = pixelBytes.ToArray();
        Clean(mutableBytes, width, height, bitsAllocated, samplesPerPixel,
            result.Regions, options.ReplacementValue);

        // Update pixel data - create new binary element with cleaned bytes
        // Use the same VR as the original element
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, pixelDataElement.VR, mutableBytes));
    }

    private static ushort GetUShort(DicomDataset ds, DicomTag tag)
    {
        if (ds[tag] is DicomNumericElement ne)
        {
            var value = ne.GetUInt16();
            return value ?? 0;
        }
        return 0;
    }

    private static string? GetString(DicomDataset ds, DicomTag tag)
    {
        if (ds[tag] is DicomStringElement se)
            return se.GetString();
        return null;
    }
}
