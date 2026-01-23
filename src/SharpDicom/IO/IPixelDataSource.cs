using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.IO;

/// <summary>
/// Interface for pixel data source abstraction.
/// </summary>
/// <remarks>
/// Provides a unified API for accessing pixel data regardless of how it was loaded:
/// immediately into memory, lazily from a stream, or skipped entirely.
/// </remarks>
public interface IPixelDataSource : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the pixel data is currently loaded in memory.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Gets the length of the pixel data in bytes.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Gets the current load state of the pixel data.
    /// </summary>
    PixelDataLoadState State { get; }

    /// <summary>
    /// Gets the pixel data synchronously.
    /// </summary>
    /// <returns>The pixel data as a read-only memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when pixel data was skipped during parsing.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the source has been disposed.</exception>
    ReadOnlyMemory<byte> GetData();

    /// <summary>
    /// Gets the pixel data asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that resolves to the pixel data as a read-only memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when pixel data was skipped during parsing.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the source has been disposed.</exception>
    ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default);

    /// <summary>
    /// Copies the pixel data to a destination stream.
    /// </summary>
    /// <param name="destination">The stream to copy to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the copy operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when destination is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when pixel data was skipped during parsing.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the source has been disposed.</exception>
    ValueTask CopyToAsync(Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Creates a copy of this source that owns its data, detached from any underlying stream.
    /// </summary>
    /// <returns>A new pixel data source that owns its data.</returns>
    /// <exception cref="InvalidOperationException">Thrown when pixel data was skipped during parsing.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the source has been disposed.</exception>
    IPixelDataSource ToOwned();
}
