using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.IO;

/// <summary>
/// A pixel data source that represents skipped pixel data.
/// </summary>
/// <remarks>
/// This source is used when pixel data was skipped during parsing (metadata-only mode).
/// All data access methods throw exceptions since the data is not available.
/// </remarks>
public sealed class SkippedPixelDataSource : IPixelDataSource
{
    private const string SkippedMessage = "Pixel data was skipped during parsing. Use ToOwned() on the source DicomFile before disposing the stream.";

    private readonly long _offset;
    private readonly long _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedPixelDataSource"/> class.
    /// </summary>
    /// <param name="offset">The offset where pixel data would have been in the stream.</param>
    /// <param name="length">The length of the pixel data in bytes.</param>
    public SkippedPixelDataSource(long offset, long length)
    {
        _offset = offset;
        _length = length;
    }

    /// <summary>
    /// Gets the offset where pixel data would have been in the stream.
    /// </summary>
    /// <remarks>
    /// This is metadata-only and does not allow data retrieval.
    /// </remarks>
    public long Offset => _offset;

    /// <inheritdoc />
    public bool IsLoaded => false;

    /// <inheritdoc />
    public long Length => _length;

    /// <inheritdoc />
    public PixelDataLoadState State => PixelDataLoadState.NotLoaded;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always thrown because pixel data was skipped.</exception>
    public ReadOnlyMemory<byte> GetData()
    {
        throw new InvalidOperationException(SkippedMessage);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always thrown because pixel data was skipped.</exception>
    public ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default)
    {
        throw new InvalidOperationException(SkippedMessage);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always thrown because pixel data was skipped.</exception>
    public ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        throw new InvalidOperationException(SkippedMessage);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always thrown because skipped data cannot be recovered.</exception>
    public IPixelDataSource ToOwned()
    {
        throw new InvalidOperationException(SkippedMessage);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No-op
    }
}
