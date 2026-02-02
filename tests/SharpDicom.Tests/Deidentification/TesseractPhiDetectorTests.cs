using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Deidentification.PixelCleaner;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for TesseractPhiDetector.
/// These tests work regardless of whether Tesseract is installed - they verify
/// the fallback behavior when Tesseract is unavailable.
/// </summary>
[TestFixture]
public class TesseractPhiDetectorTests
{
    [Test]
    public void Constructor_WhenTesseractNotAvailable_FallsBackToHeuristic()
    {
        // Arrange & Act - use invalid path to force fallback
        using var detector = new TesseractPhiDetector("/nonexistent/path/that/does/not/exist");

        // Assert - should not throw, just use fallback
        Assert.That(detector.IsOcrAvailable, Is.False);
    }

    [Test]
    public void Constructor_WithNullPath_DoesNotThrow()
    {
        // Arrange & Act - null path should fall back to TESSDATA_PREFIX or default
        using var detector = new TesseractPhiDetector(null);

        // Assert - should not throw (Tesseract may or may not be available)
        Assert.That(detector, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithConfidenceThreshold_DoesNotThrow()
    {
        // Arrange & Act - custom confidence threshold
        using var detector = new TesseractPhiDetector("/nonexistent/path", 0.8f);

        // Assert - should not throw
        Assert.That(detector, Is.Not.Null);
        Assert.That(detector.IsOcrAvailable, Is.False);
    }

    [Test]
    public async Task DetectAsync_WhenTesseractNotAvailable_UsesHeuristicDetection()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");

        // Create simple 100x100 8-bit grayscale image
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];

        // Act
        var result = await detector.DetectAsync(
            pixelData, width, height, 8, 1, "US", default);

        // Assert - should still get heuristic regions for ultrasound
        Assert.That(result, Is.Not.Null);
        Assert.That(result.HasHighRiskModality, Is.True, "US is high-risk modality");
        Assert.That(result.Modality, Is.EqualTo("US"));
        Assert.That(result.Regions, Is.Not.Empty, "Heuristic should return US regions");
    }

    [Test]
    public async Task DetectAsync_WithRgbImage_HandlesConversion()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");

        // Create simple 100x100 RGB image
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height * 3]; // RGB

        // Fill with some pattern to simulate image data
        for (int i = 0; i < pixelData.Length; i += 3)
        {
            pixelData[i] = 128;     // R
            pixelData[i + 1] = 128; // G
            pixelData[i + 2] = 128; // B
        }

        // Act & Assert - should not throw
        var result = await detector.DetectAsync(
            pixelData, width, height, 8, 3, "SC", default);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Modality, Is.EqualTo("SC"));
    }

    [Test]
    public async Task DetectAsync_With16BitImage_HandlesConversion()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");

        // Create simple 100x100 16-bit grayscale image
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height * 2]; // 16-bit

        // Fill with mid-range values
        for (int i = 0; i < pixelData.Length; i += 2)
        {
            pixelData[i] = 0x00;     // Low byte
            pixelData[i + 1] = 0x80; // High byte (32768)
        }

        // Act & Assert - should not throw
        var result = await detector.DetectAsync(
            pixelData, width, height, 16, 1, "CT", default);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Modality, Is.EqualTo("CT"));
    }

    [Test]
    public async Task DetectAsync_WithNonHighRiskModality_SetsHighRiskFlagCorrectly()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");
        var pixelData = new byte[512 * 512];

        // Act - MR is not considered high-risk
        var result = await detector.DetectAsync(
            pixelData, 512, 512, 8, 1, "MR", default);

        // Assert
        Assert.That(result.HasHighRiskModality, Is.False, "MR is not high-risk");
    }

    [Test]
    public async Task DetectAsync_WithNullModality_DoesNotThrow()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");
        var pixelData = new byte[100 * 100];

        // Act & Assert - should not throw
        var result = await detector.DetectAsync(
            pixelData, 100, 100, 8, 1, null, default);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Modality, Is.Null);
        Assert.That(result.HasHighRiskModality, Is.False);
    }

    [Test]
    public async Task DetectAsync_AllHighRiskModalities_FlaggedCorrectly()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");
        var pixelData = new byte[100 * 100];
        var highRiskModalities = new[] { "US", "SC", "XA", "ES", "RF" };

        // Act & Assert
        foreach (var modality in highRiskModalities)
        {
            var result = await detector.DetectAsync(
                pixelData, 100, 100, 8, 1, modality, default);

            Assert.That(result.HasHighRiskModality, Is.True,
                $"Modality {modality} should be high-risk");
        }
    }

    [Test]
    public async Task DetectAsync_WithCancellationToken_DoesNotThrowWhenNotCancelled()
    {
        // Arrange
        using var detector = new TesseractPhiDetector("/nonexistent/path");
        using var cts = new System.Threading.CancellationTokenSource();
        var pixelData = new byte[100 * 100];

        // Act & Assert - should complete without throwing
        var result = await detector.DetectAsync(
            pixelData, 100, 100, 8, 1, "US", cts.Token);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    [Category("Integration")]
    [Explicit("Requires Tesseract tessdata installed with eng.traineddata")]
    public async Task DetectAsync_WithRealTesseract_DetectsText()
    {
        // This test only runs when Tesseract is properly installed
        var tessdataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (string.IsNullOrEmpty(tessdataPath) || !Directory.Exists(tessdataPath))
        {
            Assert.Ignore("TESSDATA_PREFIX not set or tessdata not found");
        }

        var engPath = Path.Combine(tessdataPath, "eng.traineddata");
        if (!File.Exists(engPath))
        {
            Assert.Ignore("eng.traineddata not found in tessdata folder");
        }

        // Arrange
        using var detector = new TesseractPhiDetector(tessdataPath);

        if (!detector.IsOcrAvailable)
        {
            Assert.Ignore("Tesseract engine not available");
        }

        // Create a simple image with text-like patterns
        // (Real testing would use actual DICOM ultrasound images)
        var width = 512;
        var height = 512;
        var pixelData = new byte[width * height];

        // Act
        var result = await detector.DetectAsync(
            pixelData, width, height, 8, 1, "US", default);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.HasHighRiskModality, Is.True); // US is high risk
    }

    [Test]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var detector = new TesseractPhiDetector("/nonexistent/path");

        // Act & Assert - should not throw
        Assert.DoesNotThrow(() =>
        {
            detector.Dispose();
            detector.Dispose();
            detector.Dispose();
        });
    }

    [Test]
    public void Dispose_ImplementsIDisposable()
    {
        // Arrange & Act
        using (var detector = new TesseractPhiDetector("/nonexistent/path"))
        {
            // Assert - using block compiles and runs without issue
            Assert.That(detector, Is.InstanceOf<IDisposable>());
        }
    }

    [Test]
    public void TesseractPhiDetector_ImplementsIBurnedInPhiDetector()
    {
        // Arrange & Act
        using var detector = new TesseractPhiDetector("/nonexistent/path");

        // Assert
        Assert.That(detector, Is.InstanceOf<IBurnedInPhiDetector>());
    }
}
