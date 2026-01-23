using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class DicomPixelDataElementTests
{
    #region Basic Properties Tests

    [Test]
    public void DicomPixelDataElement_Tag_ReturnsPixelDataTag()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Assert
        Assert.That(element.Tag, Is.EqualTo(DicomTag.PixelData));
    }

    [Test]
    public void DicomPixelDataElement_VR_ReturnsProvidedVR()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OW, info, isEncapsulated: false);

        // Assert
        Assert.That(element.VR, Is.EqualTo(DicomVR.OW));
    }

    [Test]
    public void DicomPixelDataElement_IsEncapsulated_ReturnsFalseForNative()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Assert
        Assert.That(element.IsEncapsulated, Is.False);
    }

    [Test]
    public void DicomPixelDataElement_IsEncapsulated_ReturnsTrueForEncapsulated()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(Array.Empty<byte>());
        var info = new PixelDataInfo();
        var fragments = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            ReadOnlyMemory<byte>.Empty,
            Array.Empty<ReadOnlyMemory<byte>>());

        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: true, fragments);

        // Assert
        Assert.That(element.IsEncapsulated, Is.True);
        Assert.That(element.Fragments, Is.Not.Null);
    }

    [Test]
    public void DicomPixelDataElement_Fragments_ReturnsNullForNative()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Assert
        Assert.That(element.Fragments, Is.Null);
    }

    [Test]
    public void DicomPixelDataElement_NumberOfFrames_DefaultsToOne()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1
            // NumberOfFrames not specified
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Assert
        Assert.That(element.NumberOfFrames, Is.EqualTo(1));
    }

    [Test]
    public void DicomPixelDataElement_Length_ReturnsSourceLength()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[256]);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Assert
        Assert.That(element.Length, Is.EqualTo(256));
    }

    [Test]
    public void DicomPixelDataElement_Length_ReturnsMinusOneForEncapsulated()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(Array.Empty<byte>());
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: true);

        // Assert
        Assert.That(element.Length, Is.EqualTo(-1));
    }

    #endregion

    #region Native Pixel Data Frame Access Tests

    [Test]
    public void GetFrameSpan_SingleFrame_ReturnsAllBytes()
    {
        // Arrange: 4x4 8-bit grayscale image (16 bytes)
        var data = new byte[16];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var frameSpan = element.GetFrameSpan(0);

        // Assert
        Assert.That(frameSpan.Length, Is.EqualTo(16));
        Assert.That(frameSpan.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void GetFrameSpan_MultiFrame_ReturnsCorrectFrame()
    {
        // Arrange: 2 frames of 4x4 8-bit grayscale (32 bytes total)
        var data = new byte[32];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 2
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var frame0 = element.GetFrameSpan(0);
        var frame1 = element.GetFrameSpan(1);

        // Assert
        Assert.That(frame0.Length, Is.EqualTo(16));
        Assert.That(frame1.Length, Is.EqualTo(16));

        // First frame is bytes 0-15
        Assert.That(frame0[0], Is.EqualTo(0));
        Assert.That(frame0[15], Is.EqualTo(15));

        // Second frame is bytes 16-31
        Assert.That(frame1[0], Is.EqualTo(16));
        Assert.That(frame1[15], Is.EqualTo(31));
    }

    [Test]
    public void GetFrameSpan_InvalidFrameIndex_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[32]);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 2
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => element.GetFrameSpan(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => element.GetFrameSpan(-1));
    }

    [Test]
    public void GetFrameSpan_Encapsulated_ThrowsNotSupportedException()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(Array.Empty<byte>());
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: true);

        // Act & Assert
        var ex = Assert.Throws<NotSupportedException>(() => element.GetFrameSpan(0));
        Assert.That(ex!.Message, Does.Contain("Fragments"));
    }

    #endregion

    #region GetFrame<T> Tests

    [Test]
    public void GetFrame_ByteType_ReturnsCorrectArray()
    {
        // Arrange: 4x4 8-bit grayscale image
        var data = new byte[16];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 10);
        }

        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var frame = element.GetFrame<byte>(0);

        // Assert
        Assert.That(frame.Length, Is.EqualTo(16));
        Assert.That(frame[0], Is.EqualTo(0));
        Assert.That(frame[1], Is.EqualTo(10));
        Assert.That(frame[15], Is.EqualTo(150));
    }

    [Test]
    public void GetFrame_UShortType_16BitData_ReturnsCorrectArray()
    {
        // Arrange: 4x4 16-bit grayscale image (32 bytes)
        var data = new byte[32];
        // Write ushort values in little-endian
        for (int i = 0; i < 16; i++)
        {
            ushort value = (ushort)(i * 100);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)(value >> 8);
        }

        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 16,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OW, info, isEncapsulated: false);

        // Act
        var frame = element.GetFrame<ushort>(0);

        // Assert
        Assert.That(frame.Length, Is.EqualTo(16));
        Assert.That(frame[0], Is.EqualTo(0));
        Assert.That(frame[1], Is.EqualTo(100));
        Assert.That(frame[15], Is.EqualTo(1500));
    }

    [Test]
    public void GetFrame_MultiFrame_ReturnsCorrectFrame()
    {
        // Arrange: 2 frames of 2x2 16-bit (16 bytes total)
        var data = new byte[16];
        // Frame 0: values 1, 2, 3, 4
        data[0] = 1; data[1] = 0;  // 1
        data[2] = 2; data[3] = 0;  // 2
        data[4] = 3; data[5] = 0;  // 3
        data[6] = 4; data[7] = 0;  // 4
        // Frame 1: values 10, 20, 30, 40
        data[8] = 10; data[9] = 0;   // 10
        data[10] = 20; data[11] = 0;  // 20
        data[12] = 30; data[13] = 0;  // 30
        data[14] = 40; data[15] = 0;  // 40

        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo
        {
            Rows = 2,
            Columns = 2,
            BitsAllocated = 16,
            SamplesPerPixel = 1,
            NumberOfFrames = 2
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OW, info, isEncapsulated: false);

        // Act
        var frame0 = element.GetFrame<ushort>(0);
        var frame1 = element.GetFrame<ushort>(1);

        // Assert
        Assert.That(frame0, Is.EqualTo(new ushort[] { 1, 2, 3, 4 }));
        Assert.That(frame1, Is.EqualTo(new ushort[] { 10, 20, 30, 40 }));
    }

    #endregion

    #region Async Methods Tests

    [Test]
    public async Task LoadAsync_ReturnsPixelData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var result = await element.LoadAsync();

        // Assert
        Assert.That(result.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public async Task CopyToAsync_CopiesToStream()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);
        using var destination = new MemoryStream();

        // Act
        await element.CopyToAsync(destination);

        // Assert
        Assert.That(destination.ToArray(), Is.EqualTo(data));
    }

    #endregion

    #region ToOwned Tests

    [Test]
    public void ToOwned_CreatesIndependentCopy()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4 };
        using var stream = new MemoryStream(data);
        var lazySource = new LazyPixelDataSource(stream, 0, 4);
        var info = new PixelDataInfo
        {
            Rows = 2,
            Columns = 2,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(lazySource, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var owned = element.ToOwned() as DicomPixelDataElement;

        // Dispose original stream and source
        stream.Dispose();
        lazySource.Dispose();

        // Assert - owned copy should still work
        Assert.That(owned, Is.Not.Null);
        var frameData = owned!.GetFrameSpan(0);
        Assert.That(frameData.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void ToOwned_PreservesMetadata()
    {
        // Arrange
        var source = new ImmediatePixelDataSource(new byte[16]);
        var info = new PixelDataInfo
        {
            Rows = 4,
            Columns = 4,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(source, DicomVR.OW, info, isEncapsulated: false);

        // Act
        var owned = element.ToOwned() as DicomPixelDataElement;

        // Assert
        Assert.That(owned, Is.Not.Null);
        Assert.That(owned!.VR, Is.EqualTo(DicomVR.OW));
        Assert.That(owned.Info.Rows, Is.EqualTo(4));
        Assert.That(owned.Info.Columns, Is.EqualTo(4));
        Assert.That(owned.IsEncapsulated, Is.False);
    }

    #endregion

    #region RawValue Tests

    [Test]
    public void RawValue_WhenLoaded_ReturnsData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var source = new ImmediatePixelDataSource(data);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(source, DicomVR.OB, info, isEncapsulated: false);

        // Act
        var rawValue = element.RawValue;

        // Assert
        Assert.That(rawValue.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void RawValue_WhenNotLoaded_ReturnsEmpty()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);
        var lazySource = new LazyPixelDataSource(stream, 0, 5);
        var info = new PixelDataInfo();
        using var element = new DicomPixelDataElement(lazySource, DicomVR.OB, info, isEncapsulated: false);

        // Act - access RawValue without loading first
        var rawValue = element.RawValue;

        // Assert
        Assert.That(rawValue.IsEmpty, Is.True);
    }

    #endregion

    #region LoadState Tests

    [Test]
    public void LoadState_ReflectsSourceState()
    {
        // Arrange: 2x5 8-bit image (10 bytes)
        var data = new byte[10];
        using var stream = new MemoryStream(data);
        var lazySource = new LazyPixelDataSource(stream, 0, 10);
        var info = new PixelDataInfo
        {
            Rows = 2,
            Columns = 5,
            BitsAllocated = 8,
            SamplesPerPixel = 1,
            NumberOfFrames = 1
        };
        using var element = new DicomPixelDataElement(lazySource, DicomVR.OB, info, isEncapsulated: false);

        // Assert - before loading
        Assert.That(element.LoadState, Is.EqualTo(PixelDataLoadState.NotLoaded));

        // Act - trigger load
        _ = element.GetFrameSpan(0);

        // Assert - after loading
        Assert.That(element.LoadState, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    #endregion

    #region PixelDataContext Tests

    [Test]
    public void PixelDataContext_EstimatedSize_CalculatesCorrectly()
    {
        // Arrange: 512x512 16-bit grayscale, 10 frames, 1 sample per pixel
        var context = new PixelDataContext
        {
            Rows = 512,
            Columns = 512,
            BitsAllocated = 16,
            SamplesPerPixel = 1,
            NumberOfFrames = 10
        };

        // Act
        var estimatedSize = context.EstimatedSize;

        // Assert: 512 * 512 * 2 bytes * 1 sample * 10 frames = 5,242,880 bytes
        Assert.That(estimatedSize, Is.EqualTo(5_242_880));
    }

    [Test]
    public void PixelDataContext_EstimatedSize_ReturnsNullWhenIncomplete()
    {
        // Arrange - missing BitsAllocated
        var context = new PixelDataContext
        {
            Rows = 512,
            Columns = 512
        };

        // Act
        var estimatedSize = context.EstimatedSize;

        // Assert
        Assert.That(estimatedSize, Is.Null);
    }

    [Test]
    public void PixelDataContext_HasImageDimensions_TrueWhenComplete()
    {
        // Arrange
        var context = new PixelDataContext
        {
            Rows = 256,
            Columns = 256
        };

        // Assert
        Assert.That(context.HasImageDimensions, Is.True);
    }

    [Test]
    public void PixelDataContext_HasImageDimensions_FalseWhenIncomplete()
    {
        // Arrange
        var context = new PixelDataContext
        {
            Rows = 256
        };

        // Assert
        Assert.That(context.HasImageDimensions, Is.False);
    }

    #endregion
}
