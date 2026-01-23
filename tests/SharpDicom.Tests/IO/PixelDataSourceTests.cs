using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO;

[TestFixture]
public class PixelDataSourceTests
{
    #region ImmediatePixelDataSource Tests

    [Test]
    public void ImmediatePixelDataSource_GetData_ReturnsProvidedData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new ImmediatePixelDataSource(data);

        // Act
        var result = source.GetData();

        // Assert
        Assert.That(result.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void ImmediatePixelDataSource_IsLoaded_AlwaysTrue()
    {
        // Arrange
        using var source = new ImmediatePixelDataSource(Array.Empty<byte>());

        // Assert
        Assert.That(source.IsLoaded, Is.True);
    }

    [Test]
    public void ImmediatePixelDataSource_State_AlwaysLoaded()
    {
        // Arrange
        using var source = new ImmediatePixelDataSource(Array.Empty<byte>());

        // Assert
        Assert.That(source.State, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    [Test]
    public void ImmediatePixelDataSource_Length_ReturnsDataLength()
    {
        // Arrange
        var data = new byte[100];
        using var source = new ImmediatePixelDataSource(data);

        // Assert
        Assert.That(source.Length, Is.EqualTo(100));
    }

    [Test]
    public async Task ImmediatePixelDataSource_GetDataAsync_ReturnsProvidedData()
    {
        // Arrange
        var data = new byte[] { 10, 20, 30 };
        using var source = new ImmediatePixelDataSource(data);

        // Act
        var result = await source.GetDataAsync();

        // Assert
        Assert.That(result.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public async Task ImmediatePixelDataSource_CopyToAsync_WritesToStream()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new ImmediatePixelDataSource(data);
        using var destination = new MemoryStream();

        // Act
        await source.CopyToAsync(destination);

        // Assert
        Assert.That(destination.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void ImmediatePixelDataSource_ToOwned_ReturnsEquivalentSource()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new ImmediatePixelDataSource(data);

        // Act
        using var owned = source.ToOwned();

        // Assert
        Assert.That(owned.GetData().ToArray(), Is.EqualTo(data));
        Assert.That(owned.IsLoaded, Is.True);
    }

    [Test]
    public void ImmediatePixelDataSource_ToOwned_WithArrayBackedMemory_ReturnsSelf()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var source = new ImmediatePixelDataSource(data);

        // Act
        using var owned = source.ToOwned();

        // Assert - should be same instance for array-backed memory
        Assert.That(owned, Is.SameAs(source));
    }

    #endregion

    #region LazyPixelDataSource Tests

    [Test]
    public void LazyPixelDataSource_GetData_ReturnsCorrectDataAfterSeek()
    {
        // Arrange
        var data = new byte[] { 0, 0, 0, 0, 1, 2, 3, 4, 5, 0, 0 }; // Data at offset 4, length 5
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, offset: 4, length: 5);

        // Act
        var result = source.GetData();

        // Assert
        Assert.That(result.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void LazyPixelDataSource_IsLoaded_FalseBeforeLoad()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        using var source = new LazyPixelDataSource(stream, 0, 10);

        // Assert
        Assert.That(source.IsLoaded, Is.False);
    }

    [Test]
    public void LazyPixelDataSource_IsLoaded_TrueAfterLoad()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        using var source = new LazyPixelDataSource(stream, 0, 10);

        // Act
        _ = source.GetData();

        // Assert
        Assert.That(source.IsLoaded, Is.True);
    }

    [Test]
    public void LazyPixelDataSource_State_NotLoadedBeforeAccess()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        using var source = new LazyPixelDataSource(stream, 0, 10);

        // Assert
        Assert.That(source.State, Is.EqualTo(PixelDataLoadState.NotLoaded));
    }

    [Test]
    public void LazyPixelDataSource_State_LoadedAfterAccess()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        using var source = new LazyPixelDataSource(stream, 0, 10);

        // Act
        _ = source.GetData();

        // Assert
        Assert.That(source.State, Is.EqualTo(PixelDataLoadState.Loaded));
    }

    [Test]
    public void LazyPixelDataSource_MultipleGetData_ReturnsSameCachedData()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, 0, 3);

        // Act
        var result1 = source.GetData();
        var result2 = source.GetData();

        // Assert - should be same memory instance (cached)
        Assert.That(result1.Equals(result2), Is.True);
    }

    [Test]
    public async Task LazyPixelDataSource_GetDataAsync_ReturnsCorrectData()
    {
        // Arrange
        var data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, offset: 2, length: 4);

        // Act
        var result = await source.GetDataAsync();

        // Assert
        Assert.That(result.ToArray(), Is.EqualTo(new byte[] { 2, 3, 4, 5 }));
    }

    [Test]
    public async Task LazyPixelDataSource_CopyToAsync_WorksWhenLoaded()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, 0, 5);
        using var destination = new MemoryStream();

        // Pre-load the data
        _ = source.GetData();

        // Act
        await source.CopyToAsync(destination);

        // Assert
        Assert.That(destination.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public async Task LazyPixelDataSource_CopyToAsync_WorksWhenNotLoaded()
    {
        // Arrange
        var data = new byte[] { 0, 0, 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, offset: 2, length: 5);
        using var destination = new MemoryStream();

        // Act
        await source.CopyToAsync(destination);

        // Assert
        Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task LazyPixelDataSource_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var data = new byte[1000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, 0, 1000);

        // Act - multiple concurrent access
        var tasks = new Task<ReadOnlyMemory<byte>>[10];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = source.GetDataAsync().AsTask();
        }
        var results = await Task.WhenAll(tasks);

        // Assert - all should return the same data
        foreach (var result in results)
        {
            Assert.That(result.ToArray(), Is.EqualTo(data));
        }
    }

    [Test]
    public void LazyPixelDataSource_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);
        var source = new LazyPixelDataSource(stream, 0, 10);

        // Act
        source.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => source.GetData());
    }

    [Test]
    public void LazyPixelDataSource_ToOwned_ReturnsImmediateSource()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(data);
        using var source = new LazyPixelDataSource(stream, 0, 5);

        // Act
        using var owned = source.ToOwned();

        // Assert
        Assert.That(owned, Is.TypeOf<ImmediatePixelDataSource>());
        Assert.That(owned.GetData().ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void LazyPixelDataSource_NonSeekableStream_ThrowsArgumentException()
    {
        // Arrange
        using var stream = new NonSeekableStream();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LazyPixelDataSource(stream, 0, 10));
    }

    [Test]
    public void LazyPixelDataSource_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new LazyPixelDataSource(stream, -1, 10));
    }

    [Test]
    public void LazyPixelDataSource_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[10]);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new LazyPixelDataSource(stream, 0, -1));
    }

    #endregion

    #region SkippedPixelDataSource Tests

    [Test]
    public void SkippedPixelDataSource_GetData_ThrowsInvalidOperationException()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => source.GetData());
        Assert.That(ex!.Message, Does.Contain("skipped during parsing"));
    }

    [Test]
    public void SkippedPixelDataSource_GetDataAsync_ThrowsInvalidOperationException()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await source.GetDataAsync());
    }

    [Test]
    public void SkippedPixelDataSource_CopyToAsync_ThrowsInvalidOperationException()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);
        using var destination = new MemoryStream();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await source.CopyToAsync(destination));
    }

    [Test]
    public void SkippedPixelDataSource_ToOwned_ThrowsInvalidOperationException()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => source.ToOwned());
        Assert.That(ex!.Message, Does.Contain("skipped during parsing"));
    }

    [Test]
    public void SkippedPixelDataSource_Length_ReturnsStoredValue()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(50, 12345);

        // Assert
        Assert.That(source.Length, Is.EqualTo(12345));
    }

    [Test]
    public void SkippedPixelDataSource_Offset_ReturnsStoredValue()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(12345, 100);

        // Assert
        Assert.That(source.Offset, Is.EqualTo(12345));
    }

    [Test]
    public void SkippedPixelDataSource_IsLoaded_AlwaysFalse()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);

        // Assert
        Assert.That(source.IsLoaded, Is.False);
    }

    [Test]
    public void SkippedPixelDataSource_State_AlwaysNotLoaded()
    {
        // Arrange
        using var source = new SkippedPixelDataSource(0, 100);

        // Assert
        Assert.That(source.State, Is.EqualTo(PixelDataLoadState.NotLoaded));
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// A stream that does not support seeking.
    /// </summary>
    private class NonSeekableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
