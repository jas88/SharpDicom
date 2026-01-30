using System;
using System.Collections.Generic;

namespace SharpDicom.Deidentification;

/// <summary>
/// Configuration options for pixel data redaction.
/// </summary>
public sealed class RedactionOptions
{
    /// <summary>
    /// Gets the regions to redact.
    /// </summary>
    public IReadOnlyList<RedactionRegion> Regions { get; init; } = Array.Empty<RedactionRegion>();

    /// <summary>
    /// Gets the fill value for redacted pixels.
    /// </summary>
    /// <remarks>
    /// <para>For grayscale images, this is the pixel value (0 = black for MONOCHROME2).</para>
    /// <para>For RGB images, this is interpreted as (R &lt;&lt; 16 | G &lt;&lt; 8 | B).</para>
    /// <para>For signed pixel representations, cast to the appropriate signed type.</para>
    /// </remarks>
    public uint FillValue { get; init; }

    /// <summary>
    /// Gets whether to update the BurnedInAnnotation (0028,0301) tag after redaction.
    /// </summary>
    /// <remarks>
    /// When true (default), sets BurnedInAnnotation to "NO" after successful redaction,
    /// indicating that burned-in annotations have been removed.
    /// </remarks>
    public bool UpdateBurnedInAnnotationTag { get; init; } = true;

    /// <summary>
    /// Gets whether to skip redaction if pixel data is compressed.
    /// </summary>
    /// <remarks>
    /// When true (default), redaction is skipped for encapsulated (compressed) pixel data
    /// and a warning is added to the result. When false, an exception is thrown.
    /// </remarks>
    public bool SkipCompressed { get; init; } = true;

    /// <summary>
    /// Creates default redaction options with no regions.
    /// </summary>
    public static RedactionOptions Empty => new();

    /// <summary>
    /// Creates default redaction options for ultrasound images.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Options configured for typical ultrasound annotation locations.</returns>
    /// <remarks>
    /// Ultrasound images commonly have patient information in:
    /// <list type="bullet">
    /// <item><description>Top bar (50 pixels) - Institution, patient name, date</description></item>
    /// <item><description>Bottom bar (30 pixels) - Technical parameters, measurement markers</description></item>
    /// </list>
    /// </remarks>
    public static RedactionOptions UltrasoundDefault(int width, int height) => new()
    {
        Regions = new[]
        {
            RedactionRegion.TopBar(50, width),
            RedactionRegion.BottomBar(30, width, height)
        }
    };

    /// <summary>
    /// Creates default redaction options for secondary capture images.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Options configured for typical secondary capture annotation locations.</returns>
    /// <remarks>
    /// Secondary capture images (screenshots, photographs) commonly have
    /// patient information in the corners.
    /// </remarks>
    public static RedactionOptions SecondaryCapture(int width, int height) => new()
    {
        Regions = new[]
        {
            // Top-left corner
            new RedactionRegion(0, 0, 200, 50),
            // Top-right corner
            new RedactionRegion(width - 200, 0, 200, 50),
            // Bottom-left corner
            new RedactionRegion(0, height - 50, 200, 50),
            // Bottom-right corner
            new RedactionRegion(width - 200, height - 50, 200, 50)
        }
    };

    /// <summary>
    /// Creates default redaction options for endoscopy images.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Options configured for typical endoscopy annotation locations.</returns>
    /// <remarks>
    /// Endoscopy images typically have text overlays at the top and bottom.
    /// </remarks>
    public static RedactionOptions Endoscopy(int width, int height) => new()
    {
        Regions = new[]
        {
            RedactionRegion.TopBar(40, width),
            RedactionRegion.BottomBar(40, width, height)
        }
    };

    /// <summary>
    /// Creates options that redact the entire image.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Options that will blank the entire image.</returns>
    /// <remarks>
    /// Use with caution - this removes all image content.
    /// </remarks>
    public static RedactionOptions FullImage(int width, int height) => new()
    {
        Regions = new[] { new RedactionRegion(0, 0, width, height) }
    };
}
