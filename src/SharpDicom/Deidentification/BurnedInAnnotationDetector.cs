using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Deidentification;

/// <summary>
/// Detects modalities with high risk of burned-in patient information.
/// </summary>
/// <remarks>
/// <para>
/// Many DICOM modalities embed patient identifying information directly into
/// pixel data (burned-in annotations). This is a significant privacy concern
/// that standard attribute-level de-identification cannot address.
/// </para>
/// <para>
/// This detector analyzes DICOM datasets to assess the risk level of
/// burned-in PHI (Protected Health Information) based on:
/// <list type="bullet">
/// <item><description>The BurnedInAnnotation (0028,0301) tag value</description></item>
/// <item><description>The modality type</description></item>
/// <item><description>The ImageType attribute</description></item>
/// </list>
/// </para>
/// </remarks>
public static class BurnedInAnnotationDetector
{
    // Modalities with HIGH burned-in PHI risk
    // These almost always contain patient information in pixel data
    private static readonly HashSet<string> HighRiskModalities = new(StringComparer.OrdinalIgnoreCase)
    {
        "US",   // Ultrasound - very common, text overlays with patient name/DOB
        "ES",   // Endoscopy - typically has on-screen display
        "SC",   // Secondary Capture (screenshots) - anything goes
        "XC",   // External-camera Photography
        "GM",   // General Microscopy
        "SM",   // Slide Microscopy
        "OP",   // Ophthalmic Photography
        "OPT",  // Ophthalmic Tomography (OCT)
        "ECG",  // Electrocardiography
        "HD"    // Hemodynamic waveform
    };

    // Modalities with MODERATE risk
    // May contain burned-in annotations depending on configuration
    private static readonly HashSet<string> ModerateRiskModalities = new(StringComparer.OrdinalIgnoreCase)
    {
        "XA",   // X-Ray Angiography - often has patient info overlay
        "RF",   // Radio Fluoroscopy
        "MG",   // Mammography - may have laterality markers
        "DX",   // Digital Radiography
        "CR",   // Computed Radiography
        "PX",   // Panoramic X-Ray
        "IO"    // Intra-oral Radiography
    };

    /// <summary>
    /// Detects the burned-in annotation risk level for a dataset.
    /// </summary>
    /// <param name="dataset">The DICOM dataset to analyze.</param>
    /// <returns>The assessed risk level for burned-in PHI.</returns>
    /// <exception cref="ArgumentNullException">Dataset is null.</exception>
    public static BurnedInAnnotationRisk DetectRisk(DicomDataset dataset)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(dataset);
#else
        if (dataset == null)
            throw new ArgumentNullException(nameof(dataset));
#endif

        // Check existing BurnedInAnnotation tag (0028,0301)
        var burnedInTag = dataset.GetString(DicomTag.BurnedInAnnotation)?.Trim();
        if (string.Equals(burnedInTag, "YES", StringComparison.OrdinalIgnoreCase))
        {
            return BurnedInAnnotationRisk.Confirmed;
        }

        // Honor explicit BurnedInAnnotation="NO" before modality checks
        // This allows manufacturers to explicitly declare images are clean
        if (string.Equals(burnedInTag, "NO", StringComparison.OrdinalIgnoreCase))
        {
            return BurnedInAnnotationRisk.Low;
        }

        // Check modality
        var modality = dataset.GetString(DicomTag.Modality)?.Trim();
        if (string.IsNullOrEmpty(modality))
        {
            return BurnedInAnnotationRisk.Unknown;
        }

        // modality is guaranteed non-null here due to IsNullOrEmpty check above
        if (HighRiskModalities.Contains(modality!))
        {
            return BurnedInAnnotationRisk.High;
        }

        if (ModerateRiskModalities.Contains(modality!))
        {
            return BurnedInAnnotationRisk.Moderate;
        }

        // Check for secondary capture indication in ImageType
        var imageType = dataset.GetString(DicomTag.ImageType)?.Trim();
        if (!string.IsNullOrEmpty(imageType))
        {
            // imageType is guaranteed non-null here due to IsNullOrEmpty check
            // ImageType values are backslash-separated (e.g., "DERIVED\SECONDARY")
            if (ContainsIgnoreCase(imageType!, "SECONDARY"))
            {
                return BurnedInAnnotationRisk.High;
            }

            // Screen captures and derived images are also high risk
            if (ContainsIgnoreCase(imageType!, "SCREEN") ||
                ContainsIgnoreCase(imageType!, "CAPTURE"))
            {
                return BurnedInAnnotationRisk.High;
            }
        }

        // Default for unlisted modalities
        return BurnedInAnnotationRisk.Low;
    }

    /// <summary>
    /// Gets a warning message appropriate for the given risk level.
    /// </summary>
    /// <param name="risk">The detected risk level.</param>
    /// <param name="dataset">The dataset (used to extract modality for the message).</param>
    /// <returns>A warning message, or empty string for Low risk.</returns>
    public static string GetWarningMessage(BurnedInAnnotationRisk risk, DicomDataset dataset)
    {
        var modality = dataset?.GetString(DicomTag.Modality)?.Trim() ?? "Unknown";

        return risk switch
        {
            BurnedInAnnotationRisk.Confirmed =>
                "BurnedInAnnotation tag indicates pixel data contains patient information. " +
                "Clean Pixel Data Option (pixel redaction) is recommended.",

            BurnedInAnnotationRisk.High =>
                $"Modality '{modality}' commonly contains burned-in patient information. " +
                "Manual review or Clean Pixel Data Option is strongly recommended.",

            BurnedInAnnotationRisk.Moderate =>
                $"Modality '{modality}' may contain burned-in annotations. " +
                "Review pixel data for patient identifying information.",

            BurnedInAnnotationRisk.Unknown =>
                "Unable to determine burned-in annotation risk (no Modality tag). " +
                "Manual review recommended.",

            _ => string.Empty
        };
    }

    /// <summary>
    /// Suggests default redaction regions based on modality.
    /// </summary>
    /// <param name="dataset">The dataset to analyze.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Suggested redaction options, or null if no suggestions.</returns>
    public static RedactionOptions? SuggestRedactionOptions(DicomDataset dataset, int width, int height)
    {
        var modality = dataset?.GetString(DicomTag.Modality)?.Trim()?.ToUpperInvariant();

        return modality switch
        {
            "US" => RedactionOptions.UltrasoundDefault(width, height),
            "ES" => RedactionOptions.Endoscopy(width, height),
            "SC" or "XC" => RedactionOptions.SecondaryCapture(width, height),
            _ => null
        };
    }

    /// <summary>
    /// Case-insensitive contains check that works on all .NET versions.
    /// </summary>
    private static bool ContainsIgnoreCase(string source, string value)
    {
#if NET6_0_OR_GREATER
        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
#else
        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
    }
}

/// <summary>
/// Risk level for burned-in patient information in pixel data.
/// </summary>
public enum BurnedInAnnotationRisk
{
    /// <summary>
    /// Low risk - modality typically does not contain burned-in annotations.
    /// </summary>
    /// <remarks>Examples: CT, MR, PT (PET).</remarks>
    Low,

    /// <summary>
    /// Moderate risk - modality may contain burned-in annotations.
    /// </summary>
    /// <remarks>Examples: XA, RF, MG, DX, CR.</remarks>
    Moderate,

    /// <summary>
    /// High risk - modality commonly contains burned-in annotations.
    /// </summary>
    /// <remarks>Examples: US, ES, SC, XC.</remarks>
    High,

    /// <summary>
    /// Confirmed - BurnedInAnnotation tag is set to "YES".
    /// </summary>
    Confirmed,

    /// <summary>
    /// Unknown - unable to determine risk (e.g., missing Modality tag).
    /// </summary>
    Unknown
}
