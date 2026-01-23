using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.IO;

/// <summary>
/// A pixel data source that provides immediate access to data already loaded in memory.
/// </summary>
/// <remarks>
/// This source is used when pixel data is loaded during file parsing (LoadInMemory mode)
/// or when creating owned copies from other sources.
/// </remarks>
public sealed class ImmediatePixelDataSource : IPixelDataSource
{
    private readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImmediatePixelDataSource"/> class.
    /// </summary>
    /// <param name="data">The pixel data.</param>
    public ImmediatePixelDataSource(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    /// <inheritdoc />
    public bool IsLoaded => true;

    /// <inheritdoc />
    public long Length => _data.Length;

    /// <inheritdoc />
    public PixelDataLoadState State => PixelDataLoadState.Loaded;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> GetData() => _data;

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return new ValueTask<ReadOnlyMemory<byte>>(_data);
    }

    /// <inheritdoc />
    public async ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        ct.ThrowIfCancellationRequested();

#if NETSTANDARD2_0
        // netstandard2.0 does not have WriteAsync overload for ReadOnlyMemory<byte>
        var array = MemoryMarshal.TryGetArray(_data, out var segment) && segment.Array is not null
            ? segment.Array
            : _data.ToArray();
        var offset = MemoryMarshal.TryGetArray(_data, out var seg) ? seg.Offset : 0;
        await destination.WriteAsync(array, offset, _data.Length, ct).ConfigureAwait(false);
#else
        await destination.WriteAsync(_data, ct).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc />
    public IPixelDataSource ToOwned()
    {
        // If the data is backed by an array, we can return self (already owned)
        // Otherwise, create a copy
        if (MemoryMarshal.TryGetArray(_data, out var segment) && segment.Array is not null)
        {
            return this;
        }

        return new ImmediatePixelDataSource(_data.ToArray());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No-op: data is owned by caller
    }
}
