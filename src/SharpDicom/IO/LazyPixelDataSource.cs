using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.IO;

/// <summary>
/// A pixel data source that loads data from a stream on first access.
/// </summary>
/// <remarks>
/// This source is used when pixel data is lazily loaded from a seekable stream.
/// The stream must remain open until pixel data is accessed.
/// Thread-safe for concurrent access.
/// </remarks>
public sealed class LazyPixelDataSource : IPixelDataSource
{
    private readonly Stream _stream;
    private readonly long _offset;
    private readonly long _length;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private ReadOnlyMemory<byte>? _cached;
    private volatile bool _disposed;
    private volatile PixelDataLoadState _state = PixelDataLoadState.NotLoaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="LazyPixelDataSource"/> class.
    /// </summary>
    /// <param name="stream">The stream containing the pixel data.</param>
    /// <param name="offset">The offset in the stream where pixel data begins.</param>
    /// <param name="length">The length of the pixel data in bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="ArgumentException">Thrown when stream is not seekable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when offset or length is negative.</exception>
    public LazyPixelDataSource(Stream stream, long offset, long length)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        if (!stream.CanSeek)
        {
            throw new ArgumentException("Stream must be seekable for lazy loading.", nameof(stream));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
        }

        _offset = offset;
        _length = length;
    }

    /// <inheritdoc />
    public bool IsLoaded => _cached.HasValue;

    /// <inheritdoc />
    public long Length => _length;

    /// <inheritdoc />
    public PixelDataLoadState State => _state;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetData()
    {
        if (_cached.HasValue)
        {
            return _cached.Value;
        }

        // Sync over async is acceptable for this fallback case
        return GetDataAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default)
    {
        if (_cached.HasValue)
        {
            return _cached.Value;
        }

        ThrowIfDisposed();

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_cached.HasValue)
            {
                return _cached.Value;
            }

            ThrowIfDisposed();

            _state = PixelDataLoadState.Loading;

            try
            {
                _stream.Position = _offset;

                var buffer = new byte[_length];
                int totalRead = 0;

                while (totalRead < _length)
                {
                    ct.ThrowIfCancellationRequested();

#if NETSTANDARD2_0
                    int read = await _stream.ReadAsync(buffer, totalRead, (int)Math.Min(_length - totalRead, int.MaxValue), ct).ConfigureAwait(false);
#else
                    int read = await _stream.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
#endif

                    if (read == 0)
                    {
                        throw new EndOfStreamException($"Unexpected end of stream while reading pixel data. Expected {_length} bytes, read {totalRead}.");
                    }

                    totalRead += read;
                }

                _cached = buffer;
                _state = PixelDataLoadState.Loaded;

                return _cached.Value;
            }
            catch (Exception) when (_state != PixelDataLoadState.Loaded)
            {
                _state = PixelDataLoadState.Failed;
                throw;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        ThrowHelpers.ThrowIfNull(destination, nameof(destination));

        ThrowIfDisposed();

        if (_cached.HasValue)
        {
            // Data is already loaded, write from cache
#if NETSTANDARD2_0
            var array = _cached.Value.ToArray();
            await destination.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#else
            await destination.WriteAsync(_cached.Value, ct).ConfigureAwait(false);
#endif
            return;
        }

        // Stream directly without caching (for streaming scenarios where caller doesn't need data in memory)
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            _stream.Position = _offset;

            var buffer = new byte[Math.Min(_length, 81920)]; // 80KB buffer
            long remaining = _length;

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(remaining, buffer.Length);

#if NETSTANDARD2_0
                int read = await _stream.ReadAsync(buffer, 0, toRead, ct).ConfigureAwait(false);
#else
                int read = await _stream.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
#endif

                if (read == 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream while copying pixel data.");
                }

#if NETSTANDARD2_0
                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
#else
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
#endif

                remaining -= read;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public IPixelDataSource ToOwned()
    {
        var data = GetData();
        return new ImmediatePixelDataSource(data.ToArray());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadLock.Dispose();
        // Do NOT dispose the stream - it's managed externally
    }

    private void ThrowIfDisposed()
    {
        ThrowHelpers.ThrowIfDisposed(_disposed, this);
    }
}
