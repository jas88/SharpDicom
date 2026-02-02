#if TESSERACT_AVAILABLE
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Deidentification.PixelCleaner;

/// <summary>
/// OCR-based PHI detector using Tesseract for text detection in pixel data.
/// Falls back to heuristic detection when Tesseract is not available.
/// </summary>
/// <remarks>
/// <para>
/// Requires on net6.0+:
/// </para>
/// <list type="bullet">
/// <item><description>TesseractOCR NuGet package (included automatically)</description></item>
/// <item><description>tessdata folder with eng.traineddata from https://github.com/tesseract-ocr/tessdata</description></item>
/// <item><description>TESSDATA_PREFIX environment variable or explicit path</description></item>
/// </list>
/// <para>
/// On netstandard2.0, this class always falls back to heuristic detection
/// since TesseractOCR requires native dependencies not available on that target.
/// </para>
/// </remarks>
public sealed class TesseractPhiDetector : IBurnedInPhiDetector, IDisposable
{
#if TESSERACT_AVAILABLE
    private readonly Engine? _engine;
    private readonly HeuristicPhiDetector _fallback;
    private readonly float _confidenceThreshold;
    private bool _disposed;

    /// <summary>
    /// Creates a new Tesseract PHI detector.
    /// </summary>
    /// <param name="tessdataPath">Path to tessdata folder, or null to use TESSDATA_PREFIX env var.</param>
    /// <param name="confidenceThreshold">Minimum confidence (0-1) to include detected text. Default 0.6.</param>
    public TesseractPhiDetector(string? tessdataPath = null, float confidenceThreshold = 0.6f)
    {
        _confidenceThreshold = confidenceThreshold;
        _fallback = new HeuristicPhiDetector();

        try
        {
            var dataPath = tessdataPath
                ?? Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
                ?? Path.Combine(AppContext.BaseDirectory, "tessdata");

            if (Directory.Exists(dataPath))
            {
                // Check if eng.traineddata exists
                var engPath = Path.Combine(dataPath, "eng.traineddata");
                if (File.Exists(engPath))
                {
                    _engine = new Engine(dataPath, Language.English, EngineMode.Default);
                }
            }
        }
        catch
        {
            // Tesseract initialization failed - will use fallback
            _engine = null;
        }
    }

    /// <summary>
    /// Gets whether Tesseract OCR is available and initialized.
    /// </summary>
    public bool IsOcrAvailable => _engine != null;

    /// <inheritdoc />
    public ValueTask<PhiDetectionResult> DetectAsync(
        ReadOnlyMemory<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel,
        string? modality,
        CancellationToken ct = default)
    {
        if (_engine == null)
        {
            // Fall back to heuristic detection
            return _fallback.DetectAsync(
                pixelData, width, height, bitsAllocated, samplesPerPixel, modality, ct);
        }

        // Convert to 8-bit grayscale for Tesseract
        var grayscale = ConvertToGrayscale(pixelData.Span, width, height, bitsAllocated, samplesPerPixel);

        // Run OCR
        var regions = new List<PhiRegion>();

        try
        {
            using var pix = Image.LoadFromMemory(grayscale);
            using var page = _engine.Process(pix);

            // Iterate through layout to get word-level bounding boxes
            foreach (var block in page.Layout)
            {
                foreach (var paragraph in block.Paragraphs)
                {
                    foreach (var textLine in paragraph.TextLines)
                    {
                        foreach (var word in textLine.Words)
                        {
                            var confidence = word.Confidence / 100f;

                            if (confidence >= _confidenceThreshold && word.BoundingBox.HasValue)
                            {
                                var bounds = word.BoundingBox.Value;
                                regions.Add(new PhiRegion(
                                    bounds.X1,
                                    bounds.Y1,
                                    bounds.Width,
                                    bounds.Height,
                                    confidence,
                                    "OCR"));
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // OCR failed - fall back to heuristic
            return _fallback.DetectAsync(
                pixelData, width, height, bitsAllocated, samplesPerPixel, modality, ct);
        }

        // Merge overlapping regions
        regions = MergeOverlappingRegions(regions);

        // Combine with heuristic regions for modality-specific areas
        var heuristicRegions = BurnedInPhiRegions.GetRegions(modality, width, height);

        // Union the regions - add heuristic regions that don't overlap with OCR detections
        foreach (var hr in heuristicRegions)
        {
            var overlaps = false;
            foreach (var r in regions)
            {
                if (RegionsOverlap(r, hr))
                {
                    overlaps = true;
                    break;
                }
            }
            if (!overlaps)
            {
                regions.Add(hr);
            }
        }

        var result = new PhiDetectionResult
        {
            Regions = regions,
            HasHighRiskModality = HighRiskModalities.IsHighRisk(modality),
            Modality = modality,
            BurnedInAnnotationPresent = false,
            BurnedInAnnotationValue = null
        };

        return ValueTask.FromResult(result);
    }

    private static byte[] ConvertToGrayscale(
        ReadOnlySpan<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel)
    {
        var result = new byte[width * height];

        if (bitsAllocated == 8 && samplesPerPixel == 1)
        {
            // Already 8-bit grayscale
            pixelData.Slice(0, Math.Min(pixelData.Length, result.Length)).CopyTo(result);
        }
        else if (bitsAllocated == 16 && samplesPerPixel == 1)
        {
            // 16-bit grayscale - scale to 8-bit
            for (int i = 0; i < result.Length && i * 2 + 1 < pixelData.Length; i++)
            {
                var value16 = (ushort)(pixelData[i * 2] | (pixelData[i * 2 + 1] << 8));
                result[i] = (byte)(value16 >> 8); // Take high byte
            }
        }
        else if (bitsAllocated == 8 && samplesPerPixel == 3)
        {
            // RGB - convert to grayscale using standard luminance weights
            for (int i = 0; i < result.Length && i * 3 + 2 < pixelData.Length; i++)
            {
                var r = pixelData[i * 3];
                var g = pixelData[i * 3 + 1];
                var b = pixelData[i * 3 + 2];
                // ITU-R BT.601 luma: 0.299*R + 0.587*G + 0.114*B
                result[i] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
            }
        }

        return result;
    }

    private static List<PhiRegion> MergeOverlappingRegions(List<PhiRegion> regions)
    {
        if (regions.Count <= 1)
            return regions;

        var merged = new List<PhiRegion>();
        var used = new bool[regions.Count];

        for (int i = 0; i < regions.Count; i++)
        {
            if (used[i])
                continue;

            var current = regions[i];

            for (int j = i + 1; j < regions.Count; j++)
            {
                if (used[j])
                    continue;

                if (RegionsOverlap(current, regions[j]))
                {
                    // Merge into bounding box
                    var minX = Math.Min(current.X, regions[j].X);
                    var minY = Math.Min(current.Y, regions[j].Y);
                    var maxX = Math.Max(current.X + current.Width, regions[j].X + regions[j].Width);
                    var maxY = Math.Max(current.Y + current.Height, regions[j].Y + regions[j].Height);

                    current = new PhiRegion(
                        minX, minY, maxX - minX, maxY - minY,
                        Math.Max(current.Confidence, regions[j].Confidence),
                        "OCR");

                    used[j] = true;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    private static bool RegionsOverlap(PhiRegion a, PhiRegion b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }

    /// <summary>
    /// Disposes the Tesseract engine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _engine?.Dispose();
    }
#else
    private readonly HeuristicPhiDetector _fallback = new HeuristicPhiDetector();

    /// <summary>
    /// Creates a new Tesseract PHI detector.
    /// On netstandard2.0, this always falls back to heuristic detection.
    /// </summary>
    /// <param name="tessdataPath">Ignored on netstandard2.0.</param>
    /// <param name="confidenceThreshold">Ignored on netstandard2.0.</param>
    public TesseractPhiDetector(string? tessdataPath = null, float confidenceThreshold = 0.6f)
    {
        // Parameters unused on netstandard2.0 - suppress warnings
        _ = tessdataPath;
        _ = confidenceThreshold;
    }

    /// <summary>
    /// Tesseract is not available on this target framework (netstandard2.0).
    /// </summary>
    /// <remarks>
    /// Property must be instance for API consistency with TESSERACT_AVAILABLE build.
    /// References <see cref="_fallback"/> to satisfy CA1822 analyzer.
    /// </remarks>
    public bool IsOcrAvailable => _fallback == null;

    /// <inheritdoc />
    public ValueTask<PhiDetectionResult> DetectAsync(
        ReadOnlyMemory<byte> pixelData,
        int width,
        int height,
        int bitsAllocated,
        int samplesPerPixel,
        string? modality,
        CancellationToken ct = default)
    {
        // Fall back to heuristic detection
        return _fallback.DetectAsync(
            pixelData, width, height, bitsAllocated, samplesPerPixel, modality, ct);
    }

    /// <summary>
    /// No-op on netstandard2.0.
    /// </summary>
    public void Dispose() { }
#endif
}
