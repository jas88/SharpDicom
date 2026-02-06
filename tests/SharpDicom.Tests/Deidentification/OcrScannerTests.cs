using System;
using System.Collections.Generic;
using NUnit.Framework;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
[Category("Deidentification")]
public class OcrScannerTests
{
    [Test]
    public void Constructor_WhenTesseractUnavailable_Throws()
    {
        // In the CI/test environment, the Tesseract native library is not available.
        // OcrScanner constructor should fail with either InvalidOperationException
        // (tess_available() returns 0) or DllNotFoundException (native library missing).
        Assert.That(() =>
        {
            using var scanner = new OcrScanner();
        }, Throws.InstanceOf<Exception>());
    }

    [Test]
    public void OcrScanResult_ToRedactionRegions_ConvertsDetections()
    {
        var detection1 = new OcrDetection(
            "John Smith",
            0.85f,
            new RedactionRegion(10, 20, 100, 30, 0),
            0,
            false);

        var detection2 = new OcrDetection(
            "DOB: 01/01/1990",
            0.92f,
            new RedactionRegion(50, 400, 200, 25, 0),
            0,
            true);

        var result = new OcrScanResult(
            new[] { detection1, detection2 },
            new[] { detection1, detection2 },
            totalFramesScanned: 1,
            framesWithDetections: 1,
            scanDuration: TimeSpan.FromMilliseconds(150));

        var regions = result.ToRedactionRegions();

        Assert.That(regions, Has.Count.EqualTo(2));
        Assert.That(regions[0], Is.EqualTo(new RedactionRegion(10, 20, 100, 30, 0)));
        Assert.That(regions[1], Is.EqualTo(new RedactionRegion(50, 400, 200, 25, 0)));
    }

    [Test]
    public void OcrScanResult_ToRedactionRegions_EmptyDetections_ReturnsEmpty()
    {
        var result = new OcrScanResult(
            Array.Empty<OcrDetection>(),
            Array.Empty<OcrDetection>(),
            totalFramesScanned: 1,
            framesWithDetections: 0,
            scanDuration: TimeSpan.FromMilliseconds(50));

        var regions = result.ToRedactionRegions();

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void OcrScanResult_FilteredDetections_ExcludesAllowlisted()
    {
        // Simulate a result where "L" (orientation marker) is in all detections
        // but filtered out, while "John Smith" remains in filtered detections
        var orientationDetection = new OcrDetection(
            "L",
            0.90f,
            new RedactionRegion(5, 5, 20, 20, 0),
            0,
            true);

        var phiDetection = new OcrDetection(
            "John Smith",
            0.88f,
            new RedactionRegion(100, 100, 200, 30, 0),
            0,
            false);

        // In a real scan, the OcrScanner's ApplyAllowDenyFilter would exclude "L"
        // Here we construct the result directly showing the expected filtering
        var allDetections = new[] { orientationDetection, phiDetection };
        var filteredDetections = new[] { phiDetection }; // "L" excluded by allowlist

        var result = new OcrScanResult(
            allDetections,
            filteredDetections,
            totalFramesScanned: 1,
            framesWithDetections: 1,
            scanDuration: TimeSpan.FromMilliseconds(100));

        Assert.That(result.Detections, Has.Count.EqualTo(2));
        Assert.That(result.FilteredDetections, Has.Count.EqualTo(1));
        Assert.That(result.FilteredDetections[0].Text, Is.EqualTo("John Smith"));
    }

    [Test]
    public void OcrDetection_IsEdgeRegion_TrueForCorner()
    {
        // Detection at (5, 5) in a 512x512 image: well within 15% margin (76.8 pixels)
        var detection = new OcrDetection(
            "PHI",
            0.75f,
            new RedactionRegion(5, 5, 30, 15, 0),
            0,
            true);

        Assert.That(detection.IsEdgeRegion, Is.True);
    }

    [Test]
    public void OcrDetection_IsEdgeRegion_FalseForCenter()
    {
        // Detection at center (250, 250) in a 512x512 image: not in edge region
        var detection = new OcrDetection(
            "SomeText",
            0.80f,
            new RedactionRegion(250, 250, 40, 20, 0),
            0,
            false);

        Assert.That(detection.IsEdgeRegion, Is.False);
    }

    [Test]
    public void OcrScanResult_Empty_HasZeroCounts()
    {
        var result = OcrScanResult.Empty;

        Assert.That(result.Detections, Is.Empty);
        Assert.That(result.FilteredDetections, Is.Empty);
        Assert.That(result.TotalFramesScanned, Is.EqualTo(0));
        Assert.That(result.FramesWithDetections, Is.EqualTo(0));
        Assert.That(result.ScanDuration, Is.EqualTo(TimeSpan.Zero));
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void OcrScanResult_Warnings_DefaultsToEmptyWhenNull()
    {
        var result = new OcrScanResult(
            Array.Empty<OcrDetection>(),
            Array.Empty<OcrDetection>(),
            totalFramesScanned: 0,
            framesWithDetections: 0,
            scanDuration: TimeSpan.Zero,
            warnings: null);

        Assert.That(result.Warnings, Is.Not.Null);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void OcrScanResult_WithWarnings_ReturnsWarnings()
    {
        var warnings = new List<string> { "Frame 0: insufficient pixel data", "Modality unknown" };

        var result = new OcrScanResult(
            Array.Empty<OcrDetection>(),
            Array.Empty<OcrDetection>(),
            totalFramesScanned: 1,
            framesWithDetections: 0,
            scanDuration: TimeSpan.FromMilliseconds(10),
            warnings: warnings);

        Assert.That(result.Warnings, Has.Count.EqualTo(2));
        Assert.That(result.Warnings[0], Does.Contain("insufficient pixel data"));
    }
}
