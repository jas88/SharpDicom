using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// Detects PHI regions using modality-specific heuristics (no OCR).
/// </summary>
/// <remarks>
/// This detector identifies common PHI-prone regions based on modality-specific
/// patterns without performing actual text recognition. It returns regions that
/// are likely to contain burned-in text based on equipment manufacturer defaults.
///
/// For more accurate detection, consider using an OCR-based detector.
/// </remarks>
public sealed class HeuristicPhiDetector : IBurnedInPhiDetector
{
    /// <summary>
    /// Detects PHI regions based on modality-specific patterns.
    /// </summary>
    /// <param name="pixelData">Raw pixel data bytes (unused by heuristic detector).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="bitsAllocated">Bits per pixel (unused by heuristic detector).</param>
    /// <param name="samplesPerPixel">Samples per pixel (unused by heuristic detector).</param>
    /// <param name="modality">DICOM modality code (US, CT, SC, etc).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detection result with regions.</returns>
    public ValueTask<PhiDetectionResult> DetectAsync(
        ReadOnlyMemory<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel,
        string? modality,
        CancellationToken ct = default)
    {
        // Get modality-specific regions
        var regions = BurnedInPhiRegions.GetRegions(modality, width, height);
        var isHighRisk = HighRiskModalities.IsHighRisk(modality);

        var result = new PhiDetectionResult
        {
            Regions = regions,
            HasHighRiskModality = isHighRisk,
            Modality = modality,
            BurnedInAnnotationPresent = false, // Caller should check tag
            BurnedInAnnotationValue = null
        };

#if NETSTANDARD2_0
        return new ValueTask<PhiDetectionResult>(result);
#else
        return ValueTask.FromResult(result);
#endif
    }
}
