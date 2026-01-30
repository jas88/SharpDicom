using System;
using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;
using SharpDicom.IO;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class PixelDataRedactorTests
{
    [Test]
    public void RedactRegions_SingleRegion_FillsWithBlack()
    {
        // Create a simple 10x10 8-bit grayscale image, all white (255)
        var pixelData = new byte[100];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(2, 2, 3, 3) },
            FillValue = 0
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        // Verify the region is blacked out
        var modified = GetPixelData(dataset);
        Assert.That(modified[2 * 10 + 2], Is.EqualTo(0), "(2,2) should be black");
        Assert.That(modified[4 * 10 + 4], Is.EqualTo(0), "(4,4) should be black");
        Assert.That(modified[0], Is.EqualTo(255), "(0,0) should be unchanged");
        Assert.That(modified[5 * 10 + 5], Is.EqualTo(255), "(5,5) should be unchanged");

        Assert.That(result.RegionsRedacted, Is.EqualTo(1));
        Assert.That(result.FramesModified, Is.EqualTo(1));
    }

    [Test]
    public void RedactRegions_FillWithNonZeroValue_FillsCorrectly()
    {
        var pixelData = new byte[100];
        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 5, 5) },
            FillValue = 128
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        Assert.That(modified[0], Is.EqualTo(128));
        Assert.That(modified[4 * 10 + 4], Is.EqualTo(128));
        Assert.That(modified[5 * 10 + 5], Is.EqualTo(0), "Outside region should be unchanged");
    }

    [Test]
    public void RedactRegions_16Bit_FillsCorrectly()
    {
        // 4x4 16-bit image
        var pixelData = new byte[32];  // 4*4*2 bytes
        Array.Fill(pixelData, (byte)0xFF);  // All max value

        var dataset = CreateTestDataset(4, 4, 16, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 2, 2) },
            FillValue = 0
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // First pixel (row 0, col 0) should be zeroed
        Assert.That(modified[0], Is.EqualTo(0), "Low byte of first pixel");
        Assert.That(modified[1], Is.EqualTo(0), "High byte of first pixel");
        // Pixel at (3,3) should be unchanged
        var lastPixelOffset = (3 * 4 + 3) * 2;
        Assert.That(modified[lastPixelOffset], Is.EqualTo(0xFF));
        Assert.That(modified[lastPixelOffset + 1], Is.EqualTo(0xFF));
    }

    [Test]
    public void RedactRegions_16Bit_WritesLittleEndian()
    {
        // 4x4 16-bit image, all zeros
        var pixelData = new byte[32];
        var dataset = CreateTestDataset(4, 4, 16, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 1, 1) },
            FillValue = 0x1234
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // Little-endian: low byte first
        Assert.That(modified[0], Is.EqualTo(0x34), "Low byte");
        Assert.That(modified[1], Is.EqualTo(0x12), "High byte");
    }

    [Test]
    public void RedactRegions_32Bit_FillsCorrectly()
    {
        // 2x2 32-bit image
        var pixelData = new byte[16];  // 2*2*4 bytes
        Array.Fill(pixelData, (byte)0xFF);

        var dataset = CreateTestDataset(2, 2, 32, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 1, 1) },
            FillValue = 0x12345678
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // Little-endian: 78 56 34 12
        Assert.That(modified[0], Is.EqualTo(0x78));
        Assert.That(modified[1], Is.EqualTo(0x56));
        Assert.That(modified[2], Is.EqualTo(0x34));
        Assert.That(modified[3], Is.EqualTo(0x12));
    }

    [Test]
    public void RedactRegions_MultipleRegions_RedactsAll()
    {
        var pixelData = new byte[100];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[]
            {
                new RedactionRegion(0, 0, 2, 2),  // Top-left
                new RedactionRegion(8, 8, 2, 2)   // Bottom-right
            },
            FillValue = 0
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        Assert.That(modified[0], Is.EqualTo(0), "Top-left corner");
        Assert.That(modified[9 * 10 + 9], Is.EqualTo(0), "Bottom-right corner");
        Assert.That(modified[5 * 10 + 5], Is.EqualTo(255), "Center unchanged");

        Assert.That(result.RegionsRedacted, Is.EqualTo(2));
    }

    [Test]
    public void RedactRegions_RegionExceedsImage_ClampsToBounds()
    {
        var pixelData = new byte[100];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(8, 8, 10, 10) },  // Exceeds bounds
            FillValue = 0
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // Only the valid portion (8-9, 8-9) should be redacted
        Assert.That(modified[8 * 10 + 8], Is.EqualTo(0));
        Assert.That(modified[9 * 10 + 9], Is.EqualTo(0));

        Assert.That(result.RegionsRedacted, Is.EqualTo(1));
        Assert.That(result.Warnings.Count, Is.GreaterThan(0), "Should warn about exceeding bounds");
    }

    [Test]
    public void RedactRegions_NegativeCoordinates_ClampsToZero()
    {
        var pixelData = new byte[100];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(-5, -5, 10, 10) },
            FillValue = 0
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // Only (0-4, 0-4) should be redacted
        Assert.That(modified[0], Is.EqualTo(0), "(0,0)");
        Assert.That(modified[4 * 10 + 4], Is.EqualTo(0), "(4,4)");
        Assert.That(modified[5 * 10 + 5], Is.EqualTo(255), "(5,5) outside region");

        Assert.That(result.Warnings.Count, Is.GreaterThan(0), "Should warn about negative coordinates");
    }

    [Test]
    public void RedactRegions_NoPixelData_ReturnsWarning()
    {
        var dataset = new DicomDataset();
        // Add pixel data metadata but no actual pixel data
        AddUShort(dataset, DicomTag.Rows, 10);
        AddUShort(dataset, DicomTag.Columns, 10);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 5, 5) }
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        Assert.That(result.RegionsRedacted, Is.EqualTo(0));
        Assert.That(result.Warnings, Has.Some.Contains("No pixel data"));
    }

    [Test]
    public void RedactRegions_NoRegions_ReturnsEmpty()
    {
        var pixelData = new byte[100];
        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = Array.Empty<RedactionRegion>()
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        Assert.That(result.RegionsRedacted, Is.EqualTo(0));
        Assert.That(result.FramesModified, Is.EqualTo(0));
    }

    [Test]
    public void RedactRegions_UpdatesBurnedInAnnotationTag()
    {
        var pixelData = new byte[100];
        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 5, 5) },
            UpdateBurnedInAnnotationTag = true
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var burnedIn = dataset.GetString(DicomTag.BurnedInAnnotation);
        Assert.That(burnedIn?.Trim(), Is.EqualTo("NO"));
    }

    [Test]
    public void RedactRegions_DoesNotUpdateBurnedInAnnotation_WhenDisabled()
    {
        var pixelData = new byte[100];
        var dataset = CreateTestDataset(10, 10, 8, 1, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 5, 5) },
            UpdateBurnedInAnnotationTag = false
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var burnedIn = dataset.GetString(DicomTag.BurnedInAnnotation);
        Assert.That(burnedIn, Is.Null);
    }

    [Test]
    public void RedactRegions_RgbImage_FillsAllSamples()
    {
        // 2x2 RGB image (3 samples per pixel)
        var pixelData = new byte[12];  // 2*2*3 bytes
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateTestDataset(2, 2, 8, 3, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 1, 1) },
            FillValue = 0  // Black
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        // First pixel RGB should all be 0
        Assert.That(modified[0], Is.EqualTo(0), "R");
        Assert.That(modified[1], Is.EqualTo(0), "G");
        Assert.That(modified[2], Is.EqualTo(0), "B");
        // Second pixel should be unchanged
        Assert.That(modified[3], Is.EqualTo(255));
    }

    [Test]
    public void RedactRegions_RgbImage_FillsWithColor()
    {
        // 2x2 RGB image
        var pixelData = new byte[12];
        var dataset = CreateTestDataset(2, 2, 8, 3, pixelData);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 1, 1) },
            FillValue = 0xFF0000  // Red (R << 16 | G << 8 | B)
        };

        PixelDataRedactor.RedactRegions(dataset, options);

        var modified = GetPixelData(dataset);
        Assert.That(modified[0], Is.EqualTo(0xFF), "R");
        Assert.That(modified[1], Is.EqualTo(0x00), "G");
        Assert.That(modified[2], Is.EqualTo(0x00), "B");
    }

    [Test]
    public void RedactionRegion_TopBar_CreatesCorrectRegion()
    {
        var region = RedactionRegion.TopBar(50, 640);

        Assert.That(region.X, Is.EqualTo(0));
        Assert.That(region.Y, Is.EqualTo(0));
        Assert.That(region.Width, Is.EqualTo(640));
        Assert.That(region.Height, Is.EqualTo(50));
    }

    [Test]
    public void RedactionRegion_BottomBar_CreatesCorrectRegion()
    {
        var region = RedactionRegion.BottomBar(30, 640, 480);

        Assert.That(region.X, Is.EqualTo(0));
        Assert.That(region.Y, Is.EqualTo(450));  // 480 - 30
        Assert.That(region.Width, Is.EqualTo(640));
        Assert.That(region.Height, Is.EqualTo(30));
    }

    [Test]
    public void RedactionRegion_FromCorners_NormalizesCoordinates()
    {
        // Specify bottom-right first, then top-left
        var region = RedactionRegion.FromCorners(100, 100, 0, 0);

        Assert.That(region.X, Is.EqualTo(0), "X should be normalized to min");
        Assert.That(region.Y, Is.EqualTo(0), "Y should be normalized to min");
        Assert.That(region.Width, Is.EqualTo(100));
        Assert.That(region.Height, Is.EqualTo(100));
    }

    [Test]
    public void RedactionRegion_Equality_WorksCorrectly()
    {
        var r1 = new RedactionRegion(10, 20, 30, 40);
        var r2 = new RedactionRegion(10, 20, 30, 40);
        var r3 = new RedactionRegion(10, 20, 30, 41);

        Assert.That(r1, Is.EqualTo(r2));
        Assert.That(r1, Is.Not.EqualTo(r3));
        Assert.That(r1 == r2, Is.True);
        Assert.That(r1 != r3, Is.True);
    }

    [Test]
    public void RedactionOptions_UltrasoundDefault_HasTopAndBottomBars()
    {
        var options = RedactionOptions.UltrasoundDefault(640, 480);

        Assert.That(options.Regions.Count, Is.EqualTo(2));
        Assert.That(options.Regions[0].Y, Is.EqualTo(0), "Top bar");
        Assert.That(options.Regions[1].Y, Is.EqualTo(450), "Bottom bar at 480-30");
    }

    // Helper methods

    private static DicomDataset CreateTestDataset(int columns, int rows, int bitsAllocated, int samplesPerPixel, byte[] pixelData)
    {
        var dataset = new DicomDataset();

        AddUShort(dataset, DicomTag.Rows, (ushort)rows);
        AddUShort(dataset, DicomTag.Columns, (ushort)columns);
        AddUShort(dataset, DicomTag.BitsAllocated, (ushort)bitsAllocated);
        AddUShort(dataset, DicomTag.BitsStored, (ushort)bitsAllocated);
        AddUShort(dataset, DicomTag.HighBit, (ushort)(bitsAllocated - 1));
        AddUShort(dataset, DicomTag.SamplesPerPixel, (ushort)samplesPerPixel);
        AddUShort(dataset, DicomTag.PixelRepresentation, 0);  // Unsigned

        var source = new ImmediatePixelDataSource(pixelData);
        var info = PixelDataInfo.FromDataset(dataset);
        var vr = bitsAllocated > 8 ? DicomVR.OW : DicomVR.OB;
        var element = new DicomPixelDataElement(source, vr, info, isEncapsulated: false);
        dataset.Add(element);

        return dataset;
    }

    private static void AddUShort(DicomDataset dataset, DicomTag tag, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        dataset.Add(new DicomNumericElement(tag, DicomVR.US, bytes));
    }

    private static byte[] GetPixelData(DicomDataset dataset)
    {
        var element = dataset.GetPixelData();
        return element!.RawValue.ToArray();
    }
}

[TestFixture]
public class BurnedInAnnotationDetectorTests
{
    [Test]
    public void DetectRisk_UltrasoundModality_ReturnsHigh()
    {
        var dataset = CreateDatasetWithModality("US");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void DetectRisk_EndoscopyModality_ReturnsHigh()
    {
        var dataset = CreateDatasetWithModality("ES");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void DetectRisk_SecondaryCaptureModality_ReturnsHigh()
    {
        var dataset = CreateDatasetWithModality("SC");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void DetectRisk_XRayAngiography_ReturnsModerate()
    {
        var dataset = CreateDatasetWithModality("XA");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Moderate));
    }

    [Test]
    public void DetectRisk_Mammography_ReturnsModerate()
    {
        var dataset = CreateDatasetWithModality("MG");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Moderate));
    }

    [Test]
    public void DetectRisk_CTModality_ReturnsLow()
    {
        var dataset = CreateDatasetWithModality("CT");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Low));
    }

    [Test]
    public void DetectRisk_MRModality_ReturnsLow()
    {
        var dataset = CreateDatasetWithModality("MR");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Low));
    }

    [Test]
    public void DetectRisk_BurnedInAnnotationYes_ReturnsConfirmed()
    {
        var dataset = new DicomDataset();
        AddString(dataset, DicomTag.BurnedInAnnotation, "YES");
        AddString(dataset, DicomTag.Modality, "CT");  // Even low-risk modality

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Confirmed));
    }

    [Test]
    public void DetectRisk_BurnedInAnnotationNo_ReturnsLow()
    {
        var dataset = new DicomDataset();
        AddString(dataset, DicomTag.BurnedInAnnotation, "NO");
        AddString(dataset, DicomTag.Modality, "OT");  // Unknown modality

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Low));
    }

    [Test]
    public void DetectRisk_NoModality_ReturnsUnknown()
    {
        var dataset = new DicomDataset();

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.Unknown));
    }

    [Test]
    public void DetectRisk_SecondaryInImageType_ReturnsHigh()
    {
        var dataset = new DicomDataset();
        AddString(dataset, DicomTag.Modality, "OT");
        AddString(dataset, DicomTag.ImageType, "DERIVED\\SECONDARY");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void DetectRisk_CaptureInImageType_ReturnsHigh()
    {
        var dataset = new DicomDataset();
        AddString(dataset, DicomTag.Modality, "OT");
        AddString(dataset, DicomTag.ImageType, "ORIGINAL\\PRIMARY\\SCREEN_CAPTURE");

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void DetectRisk_CaseInsensitive_MatchesModality()
    {
        var dataset = CreateDatasetWithModality("us");  // lowercase

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void GetWarningMessage_Confirmed_ReturnsMessage()
    {
        var dataset = CreateDatasetWithModality("US");

        var message = BurnedInAnnotationDetector.GetWarningMessage(BurnedInAnnotationRisk.Confirmed, dataset);

        Assert.That(message, Does.Contain("BurnedInAnnotation"));
        Assert.That(message, Does.Contain("Clean Pixel Data"));
    }

    [Test]
    public void GetWarningMessage_High_IncludesModality()
    {
        var dataset = CreateDatasetWithModality("US");

        var message = BurnedInAnnotationDetector.GetWarningMessage(BurnedInAnnotationRisk.High, dataset);

        Assert.That(message, Does.Contain("US"));
        Assert.That(message, Does.Contain("burned-in"));
    }

    [Test]
    public void GetWarningMessage_Low_ReturnsEmpty()
    {
        var dataset = CreateDatasetWithModality("CT");

        var message = BurnedInAnnotationDetector.GetWarningMessage(BurnedInAnnotationRisk.Low, dataset);

        Assert.That(message, Is.Empty);
    }

    [Test]
    public void SuggestRedactionOptions_Ultrasound_ReturnsPreset()
    {
        var dataset = CreateDatasetWithModality("US");

        var options = BurnedInAnnotationDetector.SuggestRedactionOptions(dataset, 640, 480);

        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Regions.Count, Is.GreaterThan(0));
    }

    [Test]
    public void SuggestRedactionOptions_SecondaryCaptrue_ReturnsPreset()
    {
        var dataset = CreateDatasetWithModality("SC");

        var options = BurnedInAnnotationDetector.SuggestRedactionOptions(dataset, 1920, 1080);

        Assert.That(options, Is.Not.Null);
    }

    [Test]
    public void SuggestRedactionOptions_CT_ReturnsNull()
    {
        var dataset = CreateDatasetWithModality("CT");

        var options = BurnedInAnnotationDetector.SuggestRedactionOptions(dataset, 512, 512);

        Assert.That(options, Is.Null);
    }

    [Test]
    public void DetectRisk_NullDataset_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BurnedInAnnotationDetector.DetectRisk(null!));
    }

    // Helper methods

    private static DicomDataset CreateDatasetWithModality(string modality)
    {
        var dataset = new DicomDataset();
        AddString(dataset, DicomTag.Modality, modality);
        return dataset;
    }

    private static void AddString(DicomDataset dataset, DicomTag tag, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        // Pad to even length if needed
        if (bytes.Length % 2 != 0)
        {
            var padded = new byte[bytes.Length + 1];
            bytes.CopyTo(padded, 0);
            padded[padded.Length - 1] = (byte)' ';
            bytes = padded;
        }
        dataset.Add(new DicomStringElement(tag, DicomVR.CS, bytes));
    }
}

[TestFixture]
public class MultiFrameRedactionTests
{
    [Test]
    public void RedactRegions_MultiFrame_RedactsAllFrames()
    {
        // 4x4 image with 2 frames
        var pixelData = new byte[32];  // 4*4*2 frames
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateMultiFrameDataset(4, 4, 8, 1, pixelData, numberOfFrames: 2);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 2, 2) }
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        Assert.That(result.FramesModified, Is.EqualTo(2));
        Assert.That(result.RegionsRedacted, Is.EqualTo(2));  // 1 region x 2 frames

        var modified = GetPixelData(dataset);
        // First frame: (0,0) should be 0
        Assert.That(modified[0], Is.EqualTo(0));
        // Second frame: (0,0) should also be 0
        Assert.That(modified[16], Is.EqualTo(0));  // Frame 2 starts at offset 16
    }

    [Test]
    public void RedactRegions_SpecificFrame_OnlyRedactsThatFrame()
    {
        var pixelData = new byte[32];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateMultiFrameDataset(4, 4, 8, 1, pixelData, numberOfFrames: 2);

        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 2, 2, frame: 0) }
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        Assert.That(result.FramesModified, Is.EqualTo(1));
        Assert.That(result.RegionsRedacted, Is.EqualTo(1));

        var modified = GetPixelData(dataset);
        // First frame: (0,0) should be 0
        Assert.That(modified[0], Is.EqualTo(0));
        // Second frame: (0,0) should be unchanged
        Assert.That(modified[16], Is.EqualTo(255));
    }

    [Test]
    public void RedactRegions_DifferentRegionsPerFrame()
    {
        var pixelData = new byte[32];
        Array.Fill(pixelData, (byte)255);

        var dataset = CreateMultiFrameDataset(4, 4, 8, 1, pixelData, numberOfFrames: 2);

        var options = new RedactionOptions
        {
            Regions = new[]
            {
                new RedactionRegion(0, 0, 2, 2, frame: 0),  // Frame 0: top-left
                new RedactionRegion(2, 2, 2, 2, frame: 1)   // Frame 1: bottom-right
            }
        };

        var result = PixelDataRedactor.RedactRegions(dataset, options);

        Assert.That(result.FramesModified, Is.EqualTo(2));

        var modified = GetPixelData(dataset);
        // Frame 0: (0,0) redacted, (3,3) unchanged
        Assert.That(modified[0], Is.EqualTo(0));
        Assert.That(modified[15], Is.EqualTo(255));  // 3*4+3 = 15
        // Frame 1: (0,0) unchanged, (3,3) redacted
        Assert.That(modified[16], Is.EqualTo(255));
        Assert.That(modified[31], Is.EqualTo(0));  // 16 + 15 = 31
    }

    private static DicomDataset CreateMultiFrameDataset(int columns, int rows, int bitsAllocated, int samplesPerPixel, byte[] pixelData, int numberOfFrames)
    {
        var dataset = new DicomDataset();

        AddUShort(dataset, DicomTag.Rows, (ushort)rows);
        AddUShort(dataset, DicomTag.Columns, (ushort)columns);
        AddUShort(dataset, DicomTag.BitsAllocated, (ushort)bitsAllocated);
        AddUShort(dataset, DicomTag.BitsStored, (ushort)bitsAllocated);
        AddUShort(dataset, DicomTag.HighBit, (ushort)(bitsAllocated - 1));
        AddUShort(dataset, DicomTag.SamplesPerPixel, (ushort)samplesPerPixel);
        AddUShort(dataset, DicomTag.PixelRepresentation, 0);

        // NumberOfFrames is IS (Integer String) VR
        var nfBytes = Encoding.ASCII.GetBytes(numberOfFrames.ToString(System.Globalization.CultureInfo.InvariantCulture));
        dataset.Add(new DicomStringElement(DicomTag.NumberOfFrames, DicomVR.IS, nfBytes));

        var source = new ImmediatePixelDataSource(pixelData);
        var info = PixelDataInfo.FromDataset(dataset);
        var vr = bitsAllocated > 8 ? DicomVR.OW : DicomVR.OB;
        var element = new DicomPixelDataElement(source, vr, info, isEncapsulated: false);
        dataset.Add(element);

        return dataset;
    }

    private static void AddUShort(DicomDataset dataset, DicomTag tag, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        dataset.Add(new DicomNumericElement(tag, DicomVR.US, bytes));
    }

    private static byte[] GetPixelData(DicomDataset dataset)
    {
        var element = dataset.GetPixelData();
        return element!.RawValue.ToArray();
    }
}
