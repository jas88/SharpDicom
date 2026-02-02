using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// Predefined PHI-prone regions by modality based on common equipment patterns.
/// </summary>
public static class BurnedInPhiRegions
{
    /// <summary>
    /// Region template with relative coordinates.
    /// Negative values are from opposite edge; -1 means full dimension.
    /// </summary>
    public readonly record struct RegionTemplate(int X, int Y, int Width, int Height);

    // Ultrasound: corners and header/footer regions
    private static readonly RegionTemplate[] UltrasoundRegions =
    {
        new(0, 0, -1, 80),        // Top banner (full width)
        new(0, -60, -1, 60),      // Bottom banner
        new(0, 0, 150, 150),      // Top-left corner
        new(-150, 0, 150, 150),   // Top-right corner
        new(0, -100, 150, 100),   // Bottom-left corner
        new(-150, -100, 150, 100) // Bottom-right corner
    };

    // CT/MR: typically minimal burned-in except corners
    private static readonly RegionTemplate[] CtMrRegions =
    {
        new(0, 0, 120, 100),    // Top-left corner
        new(-120, 0, 120, 100), // Top-right corner
        new(0, -80, 120, 80)    // Bottom-left corner
    };

    // Secondary Capture: assume worst case (same as ultrasound)
    private static readonly RegionTemplate[] SecondaryCapture = UltrasoundRegions;

    // XA: header regions with patient/study info
    private static readonly RegionTemplate[] XaRegions =
    {
        new(0, 0, -1, 100),    // Top banner
        new(0, -80, -1, 80),   // Bottom banner
        new(0, 0, 200, 200)    // Top-left corner
    };

    /// <summary>
    /// Gets PHI-prone regions for the given modality and image dimensions.
    /// </summary>
    /// <param name="modality">DICOM modality code (US, CT, SC, etc).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>List of PHI regions for this modality.</returns>
    public static IReadOnlyList<PhiRegion> GetRegions(string? modality, int width, int height)
    {
        var templates = GetTemplatesForModality(modality);

        if (templates.Length == 0)
            return Array.Empty<PhiRegion>();

        var regions = new List<PhiRegion>(templates.Length);
        foreach (var t in templates)
        {
            var region = NormalizeRegion(t, width, height);
            if (region.Width > 0 && region.Height > 0)
            {
                regions.Add(region);
            }
        }
        return regions;
    }

    private static RegionTemplate[] GetTemplatesForModality(string? modality)
    {
        if (string.IsNullOrEmpty(modality))
            return Array.Empty<RegionTemplate>();

        return modality!.ToUpperInvariant() switch
        {
            "US" => UltrasoundRegions,
            "CT" or "MR" or "MG" or "DX" or "CR" => CtMrRegions,
            "SC" => SecondaryCapture,
            "XA" or "RF" => XaRegions,
            "ES" => UltrasoundRegions, // Endoscopy similar to US
            _ => Array.Empty<RegionTemplate>()
        };
    }

    private static PhiRegion NormalizeRegion(RegionTemplate t, int width, int height)
    {
        // Convert relative coordinates to absolute
        var x = t.X >= 0 ? t.X : width + t.X;
        var y = t.Y >= 0 ? t.Y : height + t.Y;
        var w = t.Width == -1 ? width : (t.Width > 0 ? t.Width : width + t.Width);
        var h = t.Height == -1 ? height : (t.Height > 0 ? t.Height : height + t.Height);

        // Clamp to image bounds
        x = Math.Max(0, Math.Min(x, width - 1));
        y = Math.Max(0, Math.Min(y, height - 1));
        w = Math.Min(w, width - x);
        h = Math.Min(h, height - y);

        return new PhiRegion(x, y, w, h, 0.7f, "Heuristic");
    }
}
