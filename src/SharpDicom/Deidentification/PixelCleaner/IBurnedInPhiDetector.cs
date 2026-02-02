using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// Represents a detected PHI region in pixel data.
/// </summary>
/// <param name="X">X coordinate (left edge).</param>
/// <param name="Y">Y coordinate (top edge).</param>
/// <param name="Width">Region width in pixels.</param>
/// <param name="Height">Region height in pixels.</param>
/// <param name="Confidence">Detection confidence (0.0-1.0).</param>
/// <param name="Source">Detection source (Heuristic, OCR, etc).</param>
public readonly record struct PhiRegion(
    int X,
    int Y,
    int Width,
    int Height,
    float Confidence,
    string Source);

/// <summary>
/// Result of PHI detection scan.
/// </summary>
public sealed class PhiDetectionResult
{
    /// <summary>
    /// Gets the detected PHI regions.
    /// </summary>
    public IReadOnlyList<PhiRegion> Regions { get; init; } = Array.Empty<PhiRegion>();

    /// <summary>
    /// Gets a value indicating whether the modality is high-risk for burned-in PHI.
    /// </summary>
    public bool HasHighRiskModality { get; init; }

    /// <summary>
    /// Gets the DICOM modality code.
    /// </summary>
    public string? Modality { get; init; }

    /// <summary>
    /// Gets a value indicating whether the BurnedInAnnotation tag indicates YES.
    /// </summary>
    public bool BurnedInAnnotationPresent { get; init; }

    /// <summary>
    /// Gets the raw value of the BurnedInAnnotation tag if present.
    /// </summary>
    public string? BurnedInAnnotationValue { get; init; }
}

/// <summary>
/// Interface for burned-in PHI detection in pixel data.
/// </summary>
public interface IBurnedInPhiDetector
{
    /// <summary>
    /// Detects potential PHI regions in the image.
    /// </summary>
    /// <param name="pixelData">Raw pixel data bytes.</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <param name="bitsAllocated">Bits per pixel (8, 16, etc).</param>
    /// <param name="samplesPerPixel">1 for grayscale, 3 for RGB.</param>
    /// <param name="modality">DICOM modality code (US, CT, SC, etc).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detection result with regions.</returns>
    ValueTask<PhiDetectionResult> DetectAsync(
        ReadOnlyMemory<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel,
        string? modality,
        CancellationToken ct = default);
}

/// <summary>
/// High-risk modalities for burned-in PHI per research.
/// </summary>
public static class HighRiskModalities
{
    /// <summary>Ultrasound - 100% risk per research.</summary>
    public const string Ultrasound = "US";

    /// <summary>Secondary Capture - frequently contains burned-in text.</summary>
    public const string SecondaryCapture = "SC";

    /// <summary>X-Ray Angiography - often has patient info overlays.</summary>
    public const string XRayAngiography = "XA";

    /// <summary>Endoscopy - high burned-in text rate.</summary>
    public const string Endoscopy = "ES";

    /// <summary>RF Fluoroscopy - patient info often visible.</summary>
    public const string RfFluoroscopy = "RF";

    /// <summary>
    /// All high-risk modality codes.
    /// </summary>
#if NET5_0_OR_GREATER
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
#else
    public static readonly HashSet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
#endif
    {
        Ultrasound, SecondaryCapture, XRayAngiography, Endoscopy, RfFluoroscopy
    };

    /// <summary>
    /// Checks if a modality is high-risk for burned-in PHI.
    /// </summary>
    /// <param name="modality">The DICOM modality code.</param>
    /// <returns>True if the modality is high-risk.</returns>
    public static bool IsHighRisk(string? modality)
        => modality != null && All.Contains(modality);
}
