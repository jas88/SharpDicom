using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Deidentification;
using SharpDicom.Deidentification.PixelCleaner;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for pixel data cleaning and burned-in PHI detection.
/// </summary>
[TestFixture]
public class PixelCleanerTests
{
    [Test]
    public void Clean_8BitImage_BlacksOutRegion()
    {
        // Create 100x100 white image (8-bit)
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        Array.Fill(pixelData, (byte)255); // White

        // Clean 10x10 region at (20, 20)
        var regions = new List<PhiRegion>
        {
            new(20, 20, 10, 10, 0.9f, "Test")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 8, samplesPerPixel: 1,
            regions, PixelReplacementValue.Black);

        // Verify region is black
        for (int y = 20; y < 30; y++)
        {
            for (int x = 20; x < 30; x++)
            {
                Assert.That(pixelData[y * width + x], Is.EqualTo(0),
                    $"Pixel at ({x},{y}) should be black");
            }
        }

        // Verify outside region is still white
        Assert.That(pixelData[0], Is.EqualTo(255));
        Assert.That(pixelData[19 * width + 19], Is.EqualTo(255));
        Assert.That(pixelData[30 * width + 30], Is.EqualTo(255));
    }

    [Test]
    public void Clean_8BitImage_WhitesOutRegion()
    {
        // Create 100x100 black image (8-bit)
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        // Already all zeros (black)

        var regions = new List<PhiRegion>
        {
            new(10, 10, 5, 5, 0.9f, "Test")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 8, samplesPerPixel: 1,
            regions, PixelReplacementValue.White);

        // Verify region is white
        for (int y = 10; y < 15; y++)
        {
            for (int x = 10; x < 15; x++)
            {
                Assert.That(pixelData[y * width + x], Is.EqualTo(255),
                    $"Pixel at ({x},{y}) should be white");
            }
        }

        // Verify outside region is still black
        Assert.That(pixelData[0], Is.EqualTo(0));
    }

    [Test]
    public void Clean_16BitImage_BlacksOutRegion()
    {
        // Create 50x50 white image (16-bit)
        var width = 50;
        var height = 50;
        var pixelData = new byte[width * height * 2];

        // Fill with white (65535 little-endian)
        for (int i = 0; i < pixelData.Length; i += 2)
        {
            pixelData[i] = 0xFF;
            pixelData[i + 1] = 0xFF;
        }

        var regions = new List<PhiRegion>
        {
            new(10, 10, 5, 5, 0.9f, "Test")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 16, samplesPerPixel: 1,
            regions, PixelReplacementValue.Black);

        // Verify region is black (0 little-endian)
        var rowStride = width * 2;
        for (int y = 10; y < 15; y++)
        {
            for (int x = 10; x < 15; x++)
            {
                var offset = y * rowStride + x * 2;
                Assert.That(pixelData[offset], Is.EqualTo(0),
                    $"Low byte at ({x},{y}) should be 0");
                Assert.That(pixelData[offset + 1], Is.EqualTo(0),
                    $"High byte at ({x},{y}) should be 0");
            }
        }
    }

    [Test]
    public void Clean_16BitImage_WhitesOutRegion()
    {
        // Create 50x50 black image (16-bit)
        var width = 50;
        var height = 50;
        var pixelData = new byte[width * height * 2];
        // Already all zeros (black)

        var regions = new List<PhiRegion>
        {
            new(10, 10, 5, 5, 0.9f, "Test")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 16, samplesPerPixel: 1,
            regions, PixelReplacementValue.White);

        // Verify region is white (65535 little-endian)
        var rowStride = width * 2;
        for (int y = 10; y < 15; y++)
        {
            for (int x = 10; x < 15; x++)
            {
                var offset = y * rowStride + x * 2;
                Assert.That(pixelData[offset], Is.EqualTo(0xFF),
                    $"Low byte at ({x},{y}) should be 0xFF");
                Assert.That(pixelData[offset + 1], Is.EqualTo(0xFF),
                    $"High byte at ({x},{y}) should be 0xFF");
            }
        }
    }

    [Test]
    public void Clean_EmptyRegions_NoChanges()
    {
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        Array.Fill(pixelData, (byte)128);
        var original = (byte[])pixelData.Clone();

        var regions = new List<PhiRegion>();

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 8, samplesPerPixel: 1,
            regions, PixelReplacementValue.Black);

        Assert.That(pixelData, Is.EqualTo(original));
    }

    [Test]
    public void Clean_RegionOutOfBounds_ClampsCorrectly()
    {
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        Array.Fill(pixelData, (byte)255);

        // Region extends beyond image bounds
        var regions = new List<PhiRegion>
        {
            new(90, 90, 50, 50, 0.9f, "Test") // Extends to 140, 140
        };

        // Should not throw
        Assert.DoesNotThrow(() =>
            PixelDataCleaner.Clean(
                pixelData, width, height,
                bitsAllocated: 8, samplesPerPixel: 1,
                regions, PixelReplacementValue.Black));

        // Should clean only the valid portion (90-99, 90-99)
        Assert.That(pixelData[90 * width + 90], Is.EqualTo(0));
        Assert.That(pixelData[99 * width + 99], Is.EqualTo(0));
        Assert.That(pixelData[89 * width + 89], Is.EqualTo(255)); // Outside region
    }

    [Test]
    public void Clean_NegativeRegion_ClampsCorrectly()
    {
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        Array.Fill(pixelData, (byte)255);

        // Region starts at negative coordinates
        var regions = new List<PhiRegion>
        {
            new(-10, -10, 30, 30, 0.9f, "Test")
        };

        Assert.DoesNotThrow(() =>
            PixelDataCleaner.Clean(
                pixelData, width, height,
                bitsAllocated: 8, samplesPerPixel: 1,
                regions, PixelReplacementValue.Black));

        // Should clean only the valid portion (0-19, 0-19)
        Assert.That(pixelData[0], Is.EqualTo(0));
        Assert.That(pixelData[19], Is.EqualTo(0));
        Assert.That(pixelData[20], Is.EqualTo(255)); // Outside region
    }

    [Test]
    public void Clean_MultipleRegions_CleansAll()
    {
        var width = 100;
        var height = 100;
        var pixelData = new byte[width * height];
        Array.Fill(pixelData, (byte)255);

        var regions = new List<PhiRegion>
        {
            new(0, 0, 10, 10, 0.9f, "Region1"),
            new(50, 50, 10, 10, 0.9f, "Region2"),
            new(90, 90, 10, 10, 0.9f, "Region3")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 8, samplesPerPixel: 1,
            regions, PixelReplacementValue.Black);

        // Verify all regions are cleaned
        Assert.That(pixelData[0], Is.EqualTo(0));
        Assert.That(pixelData[55 * width + 55], Is.EqualTo(0));
        Assert.That(pixelData[95 * width + 95], Is.EqualTo(0));

        // Verify outside regions is still white
        Assert.That(pixelData[25 * width + 25], Is.EqualTo(255));
    }

    [Test]
    public async Task HeuristicDetector_Ultrasound_ReturnsHighRisk()
    {
        var detector = new HeuristicPhiDetector();
        var result = await detector.DetectAsync(
            Memory<byte>.Empty, 640, 480, 8, 1, "US");

        Assert.That(result.HasHighRiskModality, Is.True);
        Assert.That(result.Modality, Is.EqualTo("US"));
        Assert.That(result.Regions.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task HeuristicDetector_CT_ReturnsLowRisk()
    {
        var detector = new HeuristicPhiDetector();
        var result = await detector.DetectAsync(
            Memory<byte>.Empty, 512, 512, 16, 1, "CT");

        Assert.That(result.HasHighRiskModality, Is.False);
        Assert.That(result.Modality, Is.EqualTo("CT"));
    }

    [Test]
    public async Task HeuristicDetector_MR_ReturnsLowRisk()
    {
        var detector = new HeuristicPhiDetector();
        var result = await detector.DetectAsync(
            Memory<byte>.Empty, 256, 256, 16, 1, "MR");

        Assert.That(result.HasHighRiskModality, Is.False);
        Assert.That(result.Modality, Is.EqualTo("MR"));
    }

    [Test]
    public async Task HeuristicDetector_NullModality_ReturnsNoHighRisk()
    {
        var detector = new HeuristicPhiDetector();
        var result = await detector.DetectAsync(
            Memory<byte>.Empty, 512, 512, 8, 1, null);

        Assert.That(result.HasHighRiskModality, Is.False);
        Assert.That(result.Modality, Is.Null);
    }

    [Test]
    public void BurnedInPhiRegions_Ultrasound_ReturnsMultipleRegions()
    {
        var regions = BurnedInPhiRegions.GetRegions("US", 640, 480);

        Assert.That(regions.Count, Is.GreaterThan(2));
        // Should have top banner at minimum
        Assert.That(regions.Any(r => r.Y == 0 && r.Width == 640), Is.True);
    }

    [Test]
    public void BurnedInPhiRegions_CT_ReturnsCornerRegions()
    {
        var regions = BurnedInPhiRegions.GetRegions("CT", 512, 512);

        Assert.That(regions.Count, Is.GreaterThan(0));
        // Should have top-left corner
        Assert.That(regions.Any(r => r.X == 0 && r.Y == 0), Is.True);
    }

    [Test]
    public void BurnedInPhiRegions_UnknownModality_ReturnsEmpty()
    {
        var regions = BurnedInPhiRegions.GetRegions("UNKNOWN", 512, 512);

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void BurnedInPhiRegions_NullModality_ReturnsEmpty()
    {
        var regions = BurnedInPhiRegions.GetRegions(null, 512, 512);

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void BurnedInPhiRegions_SecondaryCapture_ReturnsSameAsUltrasound()
    {
        var usRegions = BurnedInPhiRegions.GetRegions("US", 640, 480);
        var scRegions = BurnedInPhiRegions.GetRegions("SC", 640, 480);

        // SC is high risk like US - should have similar regions
        Assert.That(scRegions.Count, Is.EqualTo(usRegions.Count));
    }

    [Test]
    public void HighRiskModalities_ContainsUS()
    {
        Assert.That(HighRiskModalities.IsHighRisk("US"), Is.True);
    }

    [Test]
    public void HighRiskModalities_CaseInsensitive()
    {
        Assert.That(HighRiskModalities.IsHighRisk("us"), Is.True);
        Assert.That(HighRiskModalities.IsHighRisk("Us"), Is.True);
        Assert.That(HighRiskModalities.IsHighRisk("uS"), Is.True);
    }

    [Test]
    public void HighRiskModalities_DoesNotContainMR()
    {
        Assert.That(HighRiskModalities.IsHighRisk("MR"), Is.False);
    }

    [Test]
    public void HighRiskModalities_DoesNotContainCT()
    {
        Assert.That(HighRiskModalities.IsHighRisk("CT"), Is.False);
    }

    [Test]
    public void HighRiskModalities_ContainsSC()
    {
        Assert.That(HighRiskModalities.IsHighRisk("SC"), Is.True);
    }

    [Test]
    public void HighRiskModalities_ContainsXA()
    {
        Assert.That(HighRiskModalities.IsHighRisk("XA"), Is.True);
    }

    [Test]
    public void HighRiskModalities_ContainsES()
    {
        Assert.That(HighRiskModalities.IsHighRisk("ES"), Is.True);
    }

    [Test]
    public void HighRiskModalities_ContainsRF()
    {
        Assert.That(HighRiskModalities.IsHighRisk("RF"), Is.True);
    }

    [Test]
    public void HighRiskModalities_NullReturnsfalse()
    {
        Assert.That(HighRiskModalities.IsHighRisk(null), Is.False);
    }

    [Test]
    public void PhiRegion_RecordStruct_HasCorrectProperties()
    {
        var region = new PhiRegion(10, 20, 30, 40, 0.95f, "OCR");

        Assert.That(region.X, Is.EqualTo(10));
        Assert.That(region.Y, Is.EqualTo(20));
        Assert.That(region.Width, Is.EqualTo(30));
        Assert.That(region.Height, Is.EqualTo(40));
        Assert.That(region.Confidence, Is.EqualTo(0.95f).Within(0.001f));
        Assert.That(region.Source, Is.EqualTo("OCR"));
    }

    [Test]
    public void PhiRegion_Equality()
    {
        var region1 = new PhiRegion(10, 20, 30, 40, 0.95f, "OCR");
        var region2 = new PhiRegion(10, 20, 30, 40, 0.95f, "OCR");
        var region3 = new PhiRegion(10, 20, 30, 40, 0.95f, "Heuristic");

        Assert.That(region1, Is.EqualTo(region2));
        Assert.That(region1, Is.Not.EqualTo(region3));
    }

    [Test]
    public void PhiDetectionResult_DefaultValues()
    {
        var result = new PhiDetectionResult();

        Assert.That(result.Regions, Is.Not.Null);
        Assert.That(result.Regions, Is.Empty);
        Assert.That(result.HasHighRiskModality, Is.False);
        Assert.That(result.Modality, Is.Null);
        Assert.That(result.BurnedInAnnotationPresent, Is.False);
        Assert.That(result.BurnedInAnnotationValue, Is.Null);
    }

    [Test]
    public void PixelReplacementValue_Enum_HasExpectedValues()
    {
        Assert.That(Enum.IsDefined(PixelReplacementValue.Black), Is.True);
        Assert.That(Enum.IsDefined(PixelReplacementValue.White), Is.True);
        Assert.That(Enum.IsDefined(PixelReplacementValue.AverageOfRegion), Is.True);
    }

    [Test]
    public void Clean_AverageOfRegion_CalculatesCorrectValue()
    {
        var width = 10;
        var height = 10;
        var pixelData = new byte[width * height];

        // Fill region with known values (100)
        for (int y = 2; y < 5; y++)
        {
            for (int x = 2; x < 5; x++)
            {
                pixelData[y * width + x] = 100;
            }
        }

        var regions = new List<PhiRegion>
        {
            new(2, 2, 3, 3, 0.9f, "Test")
        };

        PixelDataCleaner.Clean(
            pixelData, width, height,
            bitsAllocated: 8, samplesPerPixel: 1,
            regions, PixelReplacementValue.AverageOfRegion);

        // All pixels in region should now be the average (100)
        for (int y = 2; y < 5; y++)
        {
            for (int x = 2; x < 5; x++)
            {
                Assert.That(pixelData[y * width + x], Is.EqualTo(100),
                    $"Pixel at ({x},{y}) should be average value 100");
            }
        }
    }
}
