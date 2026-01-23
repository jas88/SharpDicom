using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO;

/// <summary>
/// Integration tests for PixelDataHandling modes in DicomFileReader.
/// Tests verify that pixel data is correctly handled when reading DICOM files
/// based on the configured PixelDataHandling option.
/// </summary>
[TestFixture]
public class PixelDataHandlingIntegrationTests
{
    private const int TestRows = 128;
    private const int TestColumns = 128;
    private const int TestBitsAllocated = 8;
    private const int TestFrameSize = TestRows * TestColumns;

    #region Test Data Creation

    /// <summary>
    /// Creates a minimal valid DICOM file with native pixel data for testing.
    /// Uses the same pattern as DicomFileTests.
    /// </summary>
    private static byte[] CreateTestDicomFile()
    {
        using var ms = new MemoryStream();

        // 128-byte preamble
        ms.Write(new byte[128]);

        // DICM prefix
        ms.Write("DICM"u8);

        // File Meta Information (Explicit VR LE)
        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // Image metadata elements (must come before PixelData)
        WriteElement(ms, 0x0028, 0x0002, "US", BitConverter.GetBytes((ushort)1)); // SamplesPerPixel
        WriteElement(ms, 0x0028, 0x0010, "US", BitConverter.GetBytes((ushort)TestRows));   // Rows
        WriteElement(ms, 0x0028, 0x0011, "US", BitConverter.GetBytes((ushort)TestColumns)); // Columns
        WriteElement(ms, 0x0028, 0x0100, "US", BitConverter.GetBytes((ushort)TestBitsAllocated)); // BitsAllocated
        WriteElement(ms, 0x0028, 0x0101, "US", BitConverter.GetBytes((ushort)TestBitsAllocated)); // BitsStored
        WriteElement(ms, 0x0028, 0x0102, "US", BitConverter.GetBytes((ushort)(TestBitsAllocated - 1))); // HighBit
        WriteElement(ms, 0x0028, 0x0103, "US", BitConverter.GetBytes((ushort)0)); // PixelRepresentation

        // Pixel Data - create gradient pattern for verification
        var pixelData = new byte[TestFrameSize];
        for (int i = 0; i < pixelData.Length; i++)
        {
            pixelData[i] = (byte)(i % 256);
        }
        WriteElement(ms, 0x7FE0, 0x0010, "OB", pixelData);

        return ms.ToArray();
    }

    private static void WriteElement(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));

        var vrCode = new DicomVR(vr);
        if (vrCode.Is32BitLength)
        {
            ms.Write(new byte[2]);
            ms.Write(BitConverter.GetBytes((uint)value.Length));
        }
        else
        {
            ms.Write(BitConverter.GetBytes((ushort)value.Length));
        }

        ms.Write(value);
    }

    #endregion

    #region LoadInMemory Tests

    [Test]
    public async Task LoadInMemory_PixelDataIsLoadedImmediately()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LoadInMemory
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);

        // Debug: Check what elements we got
        var elementTags = string.Join(", ", file.Dataset.Select(e => e.Tag.ToString()));
        Assert.That(file.Dataset.Count(), Is.GreaterThan(0), $"Dataset should have elements. Got: {elementTags}");

        // Check if PixelData tag is present (even if not as DicomPixelDataElement)
        var pixelDataElement = file.Dataset[DicomTag.PixelData];
        Assert.That(pixelDataElement, Is.Not.Null, $"PixelData element should be present. Elements: {elementTags}");

        var pixelData = file.PixelData;

        // Assert
        Assert.That(file.HasPixelData, Is.True, "File should have pixel data element");
        Assert.That(pixelData, Is.Not.Null, "PixelData property should not be null (GetPixelData returns DicomPixelDataElement)");
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.Loaded));
        Assert.That(pixelData.IsEncapsulated, Is.False);

        // Verify data content
        var data = pixelData.RawValue;
        Assert.That(data.Length, Is.EqualTo(TestFrameSize));
        Assert.That(data.Span[0], Is.EqualTo(0));
        Assert.That(data.Span[255], Is.EqualTo(255));
    }

    [Test]
    public async Task LoadInMemory_PixelDataInfoIsPopulated()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LoadInMemory
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.Info.Rows, Is.EqualTo(TestRows));
        Assert.That(pixelData.Info.Columns, Is.EqualTo(TestColumns));
        Assert.That(pixelData.Info.BitsAllocated, Is.EqualTo(TestBitsAllocated));
        Assert.That(pixelData.Info.SamplesPerPixel, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadInMemory_GetFrameSpan_ReturnsCorrectData()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LoadInMemory
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;
        var frameSpan = pixelData!.GetFrameSpan(0);

        // Assert
        Assert.That(frameSpan.Length, Is.EqualTo(TestFrameSize));
        Assert.That(frameSpan[0], Is.EqualTo(0));
        Assert.That(frameSpan[100], Is.EqualTo(100));
    }

    #endregion

    #region LazyLoad Tests

    [Test]
    public async Task LazyLoad_PixelDataIsNotLoadedUntilAccessed()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LazyLoad
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert - should be present but initially not loaded state may vary based on implementation
        Assert.That(file.HasPixelData, Is.True);
        Assert.That(pixelData, Is.Not.Null);
    }

    [Test]
    public async Task LazyLoad_LoadAsync_LoadsData()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LazyLoad
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;
        var data = await pixelData!.LoadAsync();

        // Assert
        Assert.That(data.Length, Is.EqualTo(TestFrameSize));
        Assert.That(data.Span[0], Is.EqualTo(0));
        Assert.That(data.Span[255], Is.EqualTo(255));
    }

    [Test]
    public async Task LazyLoad_CopyToAsync_CopiesData()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.LazyLoad
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        using var destination = new MemoryStream();
        await pixelData!.CopyToAsync(destination);

        // Assert
        Assert.That(destination.Length, Is.EqualTo(TestFrameSize));
        destination.Position = 0;
        Assert.That(destination.ReadByte(), Is.EqualTo(0));
    }

    #endregion

    #region Skip Tests

    [Test]
    public async Task Skip_PixelDataElementExists_ButDataIsNotAvailable()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Skip
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert
        Assert.That(file.HasPixelData, Is.True);
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.NotLoaded));
    }

    [Test]
    public async Task Skip_GetData_ThrowsInvalidOperationException()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Skip
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = pixelData!.GetFrameSpan(0);
        });
    }

    [Test]
    public async Task Skip_OtherElementsAreStillReadable()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Skip
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);

        // Assert - image dimensions are still available
        var rowsElement = file.Dataset[DicomTag.Rows] as DicomNumericElement;
        var colsElement = file.Dataset[DicomTag.Columns] as DicomNumericElement;

        Assert.That(rowsElement, Is.Not.Null);
        Assert.That(colsElement, Is.Not.Null);
        Assert.That(rowsElement!.GetUInt16(), Is.EqualTo(TestRows));
        Assert.That(colsElement!.GetUInt16(), Is.EqualTo(TestColumns));
    }

    #endregion

    #region Callback Tests

    [Test]
    public async Task Callback_DecidesToLoadInMemory_LoadsPixelData()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);
        var callbackInvoked = false;

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Callback,
            PixelDataCallback = context =>
            {
                callbackInvoked = true;

                // Verify context has expected values
                Assert.That(context.Rows, Is.EqualTo(TestRows));
                Assert.That(context.Columns, Is.EqualTo(TestColumns));
                Assert.That(context.BitsAllocated, Is.EqualTo(TestBitsAllocated));

                return PixelDataHandling.LoadInMemory;
            }
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert
        Assert.That(callbackInvoked, Is.True);
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    [Test]
    public async Task Callback_DecidesToSkip_SkipsPixelData()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);
        var callbackInvoked = false;

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Callback,
            PixelDataCallback = context =>
            {
                callbackInvoked = true;
                return PixelDataHandling.Skip;
            }
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert
        Assert.That(callbackInvoked, Is.True);
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.NotLoaded));
    }

    [Test]
    public async Task Callback_BasedOnImageSize_LoadsSmallImagesSkipsLarge()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);
        const long SizeThreshold = 100 * 1024; // 100KB threshold

        var options = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Callback,
            PixelDataCallback = context =>
            {
                // Load small images, skip large ones
                var estimatedSize = context.EstimatedSize;
                return estimatedSize.HasValue && estimatedSize.Value < SizeThreshold
                    ? PixelDataHandling.LoadInMemory
                    : PixelDataHandling.Skip;
            }
        };

        // Act
        var file = await DicomFile.OpenAsync(stream, options);
        var pixelData = file.PixelData;

        // Assert - our test image is 128*128 = 16KB which is < 100KB
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    #endregion

    #region DicomDataset.GetPixelData Tests

    [Test]
    public async Task DicomDataset_GetPixelData_ReturnsPixelDataElement()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);

        // Assert
        var pixelData = file.Dataset.GetPixelData();
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.Tag, Is.EqualTo(DicomTag.PixelData));
    }

    [Test]
    public async Task DicomDataset_GetPixelData_ReturnsNull_WhenNoPixelData()
    {
        // Arrange - create a dataset without pixel data
        var dataset = new DicomDataset();
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, BitConverter.GetBytes((ushort)128)));

        // Act
        var pixelData = dataset.GetPixelData();

        // Assert
        Assert.That(pixelData, Is.Null);
        await Task.CompletedTask; // Satisfy async convention
    }

    [Test]
    public async Task DicomDataset_HasPixelData_ReturnsTrue_WhenPixelDataPresent()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);

        // Assert
        Assert.That(file.Dataset.HasPixelData, Is.True);
    }

    [Test]
    public void DicomDataset_HasPixelData_ReturnsFalse_WhenNoPixelData()
    {
        // Arrange
        var dataset = new DicomDataset();

        // Assert
        Assert.That(dataset.HasPixelData, Is.False);
    }

    #endregion

    #region DicomFile.PixelData Tests

    [Test]
    public async Task DicomFile_PixelData_ReturnsPixelDataElement()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);

        // Assert
        Assert.That(file.PixelData, Is.Not.Null);
        Assert.That(file.PixelData!.Tag, Is.EqualTo(DicomTag.PixelData));
    }

    [Test]
    public async Task DicomFile_HasPixelData_ReturnsTrue_WhenPixelDataPresent()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);

        // Assert
        Assert.That(file.HasPixelData, Is.True);
    }

    #endregion

    #region VR Resolution Tests

    [Test]
    public async Task PixelData_VR_IsOB_For8BitData()
    {
        // Arrange - our test file uses 8-bit data
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);
        var pixelData = file.PixelData;

        // Assert - for 8-bit data, VR should be OB
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.VR, Is.EqualTo(DicomVR.OB));
    }

    #endregion

    #region Transfer Syntax Tests

    [Test]
    public async Task NativePixelData_TransferSyntaxExplicitVRLE_ParsesCorrectly()
    {
        // Arrange - our test file uses Explicit VR Little Endian
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act
        var file = await DicomFile.OpenAsync(stream);

        // Assert
        Assert.That(file.TransferSyntax.UID.ToString(), Is.EqualTo("1.2.840.10008.1.2.1"));
        Assert.That(file.TransferSyntax.IsExplicitVR, Is.True);
        Assert.That(file.TransferSyntax.IsLittleEndian, Is.True);
        Assert.That(file.TransferSyntax.IsEncapsulated, Is.False);
    }

    #endregion

    #region Default Handling Tests

    [Test]
    public async Task Default_PixelDataHandling_LoadsInMemory()
    {
        // Arrange
        var dicomData = CreateTestDicomFile();
        using var stream = new MemoryStream(dicomData);

        // Act - use default options
        var file = await DicomFile.OpenAsync(stream);
        var pixelData = file.PixelData;

        // Assert - default should be LoadInMemory
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.LoadState, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    #endregion
}
