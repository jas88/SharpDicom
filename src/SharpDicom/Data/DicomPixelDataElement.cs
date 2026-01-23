using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.IO;

namespace SharpDicom.Data;

/// <summary>
/// A DICOM element that wraps pixel data with support for lazy loading and frame-level access.
/// </summary>
/// <remarks>
/// This element provides unified access to pixel data whether it was loaded immediately,
/// lazily loaded from a stream, or is in encapsulated (compressed) format.
/// </remarks>
public sealed class DicomPixelDataElement : IDicomElement, IDisposable
{
    private bool _disposed;
    private readonly IPixelDataSource _source;
    private readonly PixelDataInfo _info;
    private readonly bool _isEncapsulated;
    private readonly DicomFragmentSequence? _fragments;
    private readonly DicomVR _vr;

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomPixelDataElement"/> class.
    /// </summary>
    /// <param name="source">The pixel data source.</param>
    /// <param name="vr">The Value Representation (OB or OW).</param>
    /// <param name="info">Metadata about the pixel data.</param>
    /// <param name="isEncapsulated">Whether the pixel data is encapsulated (compressed).</param>
    /// <param name="fragments">The fragment sequence for encapsulated data, or null for native.</param>
    /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
    public DicomPixelDataElement(
        IPixelDataSource source,
        DicomVR vr,
        PixelDataInfo info,
        bool isEncapsulated,
        DicomFragmentSequence? fragments = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _vr = vr;
        _info = info;
        _isEncapsulated = isEncapsulated;
        _fragments = fragments;
    }

    /// <inheritdoc />
    public DicomTag Tag => DicomTag.PixelData;

    /// <inheritdoc />
    public DicomVR VR => _vr;

    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawValue
    {
        get
        {
            if (!_source.IsLoaded)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            return _source.GetData();
        }
    }

    /// <inheritdoc />
    public int Length
    {
        get
        {
            long length = _source.Length;
            if (length > int.MaxValue || _isEncapsulated)
            {
                return -1; // Undefined length
            }
            return (int)length;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => _source.Length == 0;

    /// <summary>
    /// Gets metadata about the pixel data.
    /// </summary>
    public PixelDataInfo Info => _info;

    /// <summary>
    /// Gets the current load state of the pixel data.
    /// </summary>
    public PixelDataLoadState LoadState => _source.State;

    /// <summary>
    /// Gets a value indicating whether the pixel data is encapsulated (compressed).
    /// </summary>
    public bool IsEncapsulated => _isEncapsulated;

    /// <summary>
    /// Gets the fragment sequence for encapsulated data.
    /// </summary>
    /// <remarks>
    /// Returns null for native (uncompressed) pixel data.
    /// </remarks>
    public DicomFragmentSequence? Fragments => _fragments;

    /// <summary>
    /// Gets the number of frames in the pixel data.
    /// </summary>
    /// <remarks>
    /// Returns 1 if NumberOfFrames is not specified in the dataset.
    /// </remarks>
    public int NumberOfFrames => _info.NumberOfFrames.GetValueOrDefault(1);

    /// <summary>
    /// Gets a span to the raw bytes of a specific frame.
    /// </summary>
    /// <param name="frameIndex">The zero-based frame index.</param>
    /// <returns>A span to the frame's raw bytes.</returns>
    /// <exception cref="NotSupportedException">Thrown for encapsulated pixel data.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when frameIndex is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when frame size cannot be determined.</exception>
    public ReadOnlySpan<byte> GetFrameSpan(int frameIndex)
    {
        if (_isEncapsulated)
        {
            throw new NotSupportedException("Use Fragments property for encapsulated data.");
        }

        int numFrames = NumberOfFrames;
        if (frameIndex < 0 || frameIndex >= numFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex),
                $"Frame index must be between 0 and {numFrames - 1}.");
        }

        var frameSize = _info.FrameSize;
        if (!frameSize.HasValue)
        {
            throw new InvalidOperationException(
                "Cannot determine frame size. Missing Rows, Columns, or SamplesPerPixel in pixel data info.");
        }

        var data = _source.GetData();
        long offset = frameIndex * frameSize.Value;

        return data.Span.Slice((int)offset, (int)frameSize.Value);
    }

    /// <summary>
    /// Gets a typed array of pixel values for a specific frame.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to interpret pixels as (e.g., byte, ushort, short).</typeparam>
    /// <param name="frameIndex">The zero-based frame index.</param>
    /// <returns>An array of pixel values for the frame.</returns>
    /// <exception cref="NotSupportedException">Thrown for encapsulated pixel data.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when frameIndex is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when frame size cannot be determined.</exception>
    public T[] GetFrame<T>(int frameIndex) where T : unmanaged
    {
        var frameSpan = GetFrameSpan(frameIndex);
        var typedSpan = MemoryMarshal.Cast<byte, T>(frameSpan);
        return typedSpan.ToArray();
    }

    /// <summary>
    /// Loads the pixel data asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pixel data as a read-only memory.</returns>
    public ValueTask<ReadOnlyMemory<byte>> LoadAsync(CancellationToken ct = default)
    {
        return _source.GetDataAsync(ct);
    }

    /// <summary>
    /// Copies the pixel data to a destination stream.
    /// </summary>
    /// <param name="destination">The stream to copy to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the copy operation.</returns>
    public ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        return _source.CopyToAsync(destination, ct);
    }

    /// <inheritdoc />
    public IDicomElement ToOwned()
    {
        var ownedSource = _source.ToOwned();
        var ownedFragments = _fragments?.ToOwned() as DicomFragmentSequence;

        return new DicomPixelDataElement(
            ownedSource,
            _vr,
            _info,
            _isEncapsulated,
            ownedFragments);
    }

    /// <summary>
    /// Disposes the pixel data source.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Dispose();
    }
}
