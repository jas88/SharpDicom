using System;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class PixelDataInfoTests
{
    [Test]
    public void FrameSize_512x512_8BitGrayscale_Returns256KB()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 8,
            SamplesPerPixel = 1
        };

        Assert.That(info.FrameSize, Is.EqualTo(512 * 512 * 1)); // 262,144 bytes = 256 KB
    }

    [Test]
    public void FrameSize_512x512_16BitGrayscale_Returns512KB()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 16,
            SamplesPerPixel = 1
        };

        Assert.That(info.FrameSize, Is.EqualTo(512 * 512 * 2)); // 524,288 bytes = 512 KB
    }

    [Test]
    public void FrameSize_512x512_RGB8Bit_Returns768KB()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 8,
            SamplesPerPixel = 3  // RGB
        };

        Assert.That(info.FrameSize, Is.EqualTo(512 * 512 * 3)); // 786,432 bytes = 768 KB
    }

    [Test]
    public void FrameSize_MissingRows_ReturnsNull()
    {
        var info = new PixelDataInfo
        {
            Columns = 512,
            BitsAllocated = 16,
            SamplesPerPixel = 1
        };

        Assert.That(info.FrameSize, Is.Null);
    }

    [Test]
    public void FrameSize_MissingColumns_ReturnsNull()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            BitsAllocated = 16,
            SamplesPerPixel = 1
        };

        Assert.That(info.FrameSize, Is.Null);
    }

    [Test]
    public void FrameSize_MissingSamplesPerPixel_ReturnsNull()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 16
        };

        Assert.That(info.FrameSize, Is.Null);
    }

    [Test]
    public void TotalSize_10Frames_CalculatesCorrectly()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 10
        };

        // 256 KB per frame * 10 frames = 2.5 MB
        Assert.That(info.TotalSize, Is.EqualTo(512L * 512 * 1 * 10));
    }

    [Test]
    public void TotalSize_NoNumberOfFrames_AssumesOneFrame()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 8,
            SamplesPerPixel = 1
        };

        // Should assume 1 frame when not specified
        Assert.That(info.TotalSize, Is.EqualTo(info.FrameSize));
    }

    [Test]
    public void TotalSize_MissingDimensions_ReturnsNull()
    {
        var info = new PixelDataInfo
        {
            BitsAllocated = 16,
            NumberOfFrames = 10
        };

        Assert.That(info.TotalSize, Is.Null);
    }

    [TestCase(8, 1)]
    [TestCase(12, 2)]  // 12 bits rounds up to 2 bytes
    [TestCase(16, 2)]
    [TestCase(24, 3)]
    [TestCase(32, 4)]
    public void BytesPerSample_CalculatesCorrectly(int bitsAllocated, int expectedBytes)
    {
        var info = new PixelDataInfo
        {
            BitsAllocated = (ushort)bitsAllocated
        };

        Assert.That(info.BytesPerSample, Is.EqualTo(expectedBytes));
    }

    [Test]
    public void BytesPerSample_MissingBitsAllocated_DefaultsTo16()
    {
        var info = new PixelDataInfo();

        // Default is 16 bits = 2 bytes
        Assert.That(info.BytesPerSample, Is.EqualTo(2));
    }

    [Test]
    public void HasImageDimensions_WithBothRowsAndColumns_ReturnsTrue()
    {
        var info = new PixelDataInfo
        {
            Rows = 512,
            Columns = 512
        };

        Assert.That(info.HasImageDimensions, Is.True);
    }

    [Test]
    public void HasImageDimensions_MissingRows_ReturnsFalse()
    {
        var info = new PixelDataInfo
        {
            Columns = 512
        };

        Assert.That(info.HasImageDimensions, Is.False);
    }

    [Test]
    public void HasImageDimensions_MissingColumns_ReturnsFalse()
    {
        var info = new PixelDataInfo
        {
            Rows = 512
        };

        Assert.That(info.HasImageDimensions, Is.False);
    }

    [Test]
    public void FromDataset_ExtractsAllValues()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, BitConverter.GetBytes((ushort)512)));
        dataset.Add(new DicomNumericElement(DicomTag.Columns, DicomVR.US, BitConverter.GetBytes((ushort)256)));
        dataset.Add(new DicomNumericElement(DicomTag.BitsAllocated, DicomVR.US, BitConverter.GetBytes((ushort)16)));
        dataset.Add(new DicomNumericElement(DicomTag.BitsStored, DicomVR.US, BitConverter.GetBytes((ushort)12)));
        dataset.Add(new DicomNumericElement(DicomTag.HighBit, DicomVR.US, BitConverter.GetBytes((ushort)11)));
        dataset.Add(new DicomNumericElement(DicomTag.SamplesPerPixel, DicomVR.US, BitConverter.GetBytes((ushort)1)));
        dataset.Add(new DicomNumericElement(DicomTag.PlanarConfiguration, DicomVR.US, BitConverter.GetBytes((ushort)0)));
        dataset.Add(new DicomNumericElement(DicomTag.PixelRepresentation, DicomVR.US, BitConverter.GetBytes((ushort)0)));
        dataset.Add(new DicomStringElement(DicomTag.NumberOfFrames, DicomVR.IS, System.Text.Encoding.ASCII.GetBytes("25")));
        dataset.Add(new DicomStringElement(DicomTag.PhotometricInterpretation, DicomVR.CS, System.Text.Encoding.ASCII.GetBytes("MONOCHROME2")));

        var info = PixelDataInfo.FromDataset(dataset);

        Assert.That(info.Rows, Is.EqualTo(512));
        Assert.That(info.Columns, Is.EqualTo(256));
        Assert.That(info.BitsAllocated, Is.EqualTo(16));
        Assert.That(info.BitsStored, Is.EqualTo(12));
        Assert.That(info.HighBit, Is.EqualTo(11));
        Assert.That(info.SamplesPerPixel, Is.EqualTo(1));
        Assert.That(info.PlanarConfiguration, Is.EqualTo(0));
        Assert.That(info.PixelRepresentation, Is.EqualTo(0));
        Assert.That(info.NumberOfFrames, Is.EqualTo(25));
        Assert.That(info.PhotometricInterpretation, Is.EqualTo("MONOCHROME2"));
    }

    [Test]
    public void FromDataset_MissingTags_ReturnsNullProperties()
    {
        var dataset = new DicomDataset();
        // Empty dataset

        var info = PixelDataInfo.FromDataset(dataset);

        Assert.That(info.Rows, Is.Null);
        Assert.That(info.Columns, Is.Null);
        Assert.That(info.BitsAllocated, Is.Null);
        Assert.That(info.BitsStored, Is.Null);
        Assert.That(info.HighBit, Is.Null);
        Assert.That(info.SamplesPerPixel, Is.Null);
        Assert.That(info.PlanarConfiguration, Is.Null);
        Assert.That(info.PixelRepresentation, Is.Null);
        Assert.That(info.NumberOfFrames, Is.Null);
        Assert.That(info.PhotometricInterpretation, Is.Null);
    }

    [Test]
    public void FromDataset_PartialTags_ReturnsPartialInfo()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, BitConverter.GetBytes((ushort)512)));
        dataset.Add(new DicomNumericElement(DicomTag.Columns, DicomVR.US, BitConverter.GetBytes((ushort)512)));
        // Other tags missing

        var info = PixelDataInfo.FromDataset(dataset);

        Assert.That(info.Rows, Is.EqualTo(512));
        Assert.That(info.Columns, Is.EqualTo(512));
        Assert.That(info.BitsAllocated, Is.Null);
        Assert.That(info.SamplesPerPixel, Is.Null);
        Assert.That(info.HasImageDimensions, Is.True);
        Assert.That(info.FrameSize, Is.Null); // Missing SamplesPerPixel
    }

    [Test]
    public void FromDataset_NumberOfFramesWithWhitespace_ParsesCorrectly()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, BitConverter.GetBytes((ushort)512)));
        dataset.Add(new DicomNumericElement(DicomTag.Columns, DicomVR.US, BitConverter.GetBytes((ushort)512)));
        dataset.Add(new DicomNumericElement(DicomTag.BitsAllocated, DicomVR.US, BitConverter.GetBytes((ushort)8)));
        dataset.Add(new DicomNumericElement(DicomTag.SamplesPerPixel, DicomVR.US, BitConverter.GetBytes((ushort)1)));
        // NumberOfFrames with padding (common in DICOM IS values)
        dataset.Add(new DicomStringElement(DicomTag.NumberOfFrames, DicomVR.IS, System.Text.Encoding.ASCII.GetBytes("  100  ")));

        var info = PixelDataInfo.FromDataset(dataset);

        Assert.That(info.NumberOfFrames, Is.EqualTo(100));
    }

    [Test]
    public void FromDataset_InvalidNumberOfFrames_ReturnsNull()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.NumberOfFrames, DicomVR.IS, System.Text.Encoding.ASCII.GetBytes("not-a-number")));

        var info = PixelDataInfo.FromDataset(dataset);

        Assert.That(info.NumberOfFrames, Is.Null);
    }

    [Test]
    public void FromDataset_NullDataset_ThrowsArgumentNullException()
    {
        Assert.Throws<System.ArgumentNullException>(() => PixelDataInfo.FromDataset(null!));
    }
}
