using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace SharpDicom.Data;

/// <summary>
/// DICOM fragment sequence for encapsulated pixel data.
/// </summary>
/// <remarks>
/// Encapsulated pixel data (compressed images) is stored as a sequence of fragments.
/// The first item is always the Basic Offset Table (may be empty with length 0).
/// Subsequent items contain the compressed pixel data fragments.
/// </remarks>
public sealed class DicomFragmentSequence : IDicomElement
{
    private IReadOnlyList<uint>? _parsedBasicOffsets;
    private IReadOnlyList<ulong>? _parsedExtendedOffsets;
    private IReadOnlyList<ulong>? _parsedExtendedLengths;

    /// <inheritdoc />
    public DicomTag Tag { get; }

    /// <inheritdoc />
    public DicomVR VR { get; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawValue => ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc />
    public int Length => -1;  // Undefined length

    /// <inheritdoc />
    public bool IsEmpty => Fragments.Count == 0;

    /// <summary>
    /// Gets the Basic Offset Table (BOT) as raw bytes.
    /// </summary>
    /// <remarks>
    /// The BOT contains 32-bit offsets from the start of the first fragment to each frame.
    /// An empty BOT (length 0) is valid and common.
    /// </remarks>
    public ReadOnlyMemory<byte> OffsetTable { get; }

    /// <summary>
    /// Gets the compressed pixel data fragments.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fragments { get; }

    /// <summary>
    /// Gets the Extended Offset Table as raw bytes.
    /// </summary>
    /// <remarks>
    /// The Extended Offset Table (7FE0,0001) contains 64-bit offsets for large multi-frame images.
    /// This is only present when the total pixel data size exceeds 4GB.
    /// </remarks>
    public ReadOnlyMemory<byte> ExtendedOffsetTable { get; }

    /// <summary>
    /// Gets the Extended Offset Table Lengths as raw bytes.
    /// </summary>
    /// <remarks>
    /// The Extended Offset Table Lengths (7FE0,0002) contains 64-bit frame lengths.
    /// This is only present when the Extended Offset Table is present.
    /// </remarks>
    public ReadOnlyMemory<byte> ExtendedOffsetTableLengths { get; }

    /// <summary>
    /// Gets the parsed 32-bit offsets from the Basic Offset Table.
    /// </summary>
    /// <remarks>
    /// Offsets are relative to the start of the first fragment (not the BOT).
    /// Returns an empty list if the BOT is empty.
    /// </remarks>
    public IReadOnlyList<uint> ParsedBasicOffsets
    {
        get
        {
            if (_parsedBasicOffsets is null)
            {
                _parsedBasicOffsets = ParseBasicOffsetTable(OffsetTable.Span);
            }
            return _parsedBasicOffsets;
        }
    }

    /// <summary>
    /// Gets the parsed 64-bit offsets from the Extended Offset Table.
    /// </summary>
    /// <remarks>
    /// Returns null if no Extended Offset Table is present.
    /// </remarks>
    public IReadOnlyList<ulong>? ParsedExtendedOffsets
    {
        get
        {
            if (_parsedExtendedOffsets is null && !ExtendedOffsetTable.IsEmpty)
            {
                _parsedExtendedOffsets = ParseExtendedOffsetTable(ExtendedOffsetTable.Span);
            }
            return _parsedExtendedOffsets;
        }
    }

    /// <summary>
    /// Gets the parsed 64-bit lengths from the Extended Offset Table Lengths.
    /// </summary>
    /// <remarks>
    /// Returns null if no Extended Offset Table Lengths is present.
    /// </remarks>
    public IReadOnlyList<ulong>? ParsedExtendedLengths
    {
        get
        {
            if (_parsedExtendedLengths is null && !ExtendedOffsetTableLengths.IsEmpty)
            {
                _parsedExtendedLengths = ParseExtendedOffsetTable(ExtendedOffsetTableLengths.Span);
            }
            return _parsedExtendedLengths;
        }
    }

    /// <summary>
    /// Gets the number of fragments.
    /// </summary>
    public int FragmentCount => Fragments.Count;

    /// <summary>
    /// Gets the total size of all fragment data in bytes.
    /// </summary>
    public long TotalSize => Fragments.Sum(f => (long)f.Length);

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomFragmentSequence"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag (typically Pixel Data).</param>
    /// <param name="vr">The Value Representation (OB or OW).</param>
    /// <param name="offsetTable">The Basic Offset Table.</param>
    /// <param name="fragments">The compressed pixel data fragments.</param>
    public DicomFragmentSequence(
        DicomTag tag,
        DicomVR vr,
        ReadOnlyMemory<byte> offsetTable,
        IEnumerable<ReadOnlyMemory<byte>> fragments)
        : this(tag, vr, offsetTable, fragments,
               ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomFragmentSequence"/> class with extended offset table support.
    /// </summary>
    /// <param name="tag">The DICOM tag (typically Pixel Data).</param>
    /// <param name="vr">The Value Representation (OB or OW).</param>
    /// <param name="offsetTable">The Basic Offset Table.</param>
    /// <param name="fragments">The compressed pixel data fragments.</param>
    /// <param name="extendedOffsetTable">The Extended Offset Table (64-bit offsets).</param>
    /// <param name="extendedOffsetTableLengths">The Extended Offset Table Lengths (64-bit lengths).</param>
    public DicomFragmentSequence(
        DicomTag tag,
        DicomVR vr,
        ReadOnlyMemory<byte> offsetTable,
        IEnumerable<ReadOnlyMemory<byte>> fragments,
        ReadOnlyMemory<byte> extendedOffsetTable,
        ReadOnlyMemory<byte> extendedOffsetTableLengths)
    {
        Tag = tag;
        VR = vr;
        OffsetTable = offsetTable;
        Fragments = fragments.ToList();
        ExtendedOffsetTable = extendedOffsetTable;
        ExtendedOffsetTableLengths = extendedOffsetTableLengths;
    }

    /// <inheritdoc />
    public IDicomElement ToOwned() =>
        new DicomFragmentSequence(
            Tag,
            VR,
            OffsetTable.ToArray(),
            Fragments.Select(f => (ReadOnlyMemory<byte>)f.ToArray()),
            ExtendedOffsetTable.ToArray(),
            ExtendedOffsetTableLengths.ToArray());

    /// <summary>
    /// Parses a Basic Offset Table into an array of 32-bit offsets.
    /// </summary>
    /// <param name="data">The raw BOT bytes (little-endian uint32 values).</param>
    /// <returns>An array of parsed offsets.</returns>
    public static uint[] ParseBasicOffsetTable(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return Array.Empty<uint>();
        }

        int count = data.Length / 4;
        var result = new uint[count];

        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4));
        }

        return result;
    }

    /// <summary>
    /// Parses an Extended Offset Table into an array of 64-bit values.
    /// </summary>
    /// <param name="data">The raw extended offset table bytes (little-endian uint64 values).</param>
    /// <returns>An array of parsed 64-bit values.</returns>
    public static ulong[] ParseExtendedOffsetTable(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return Array.Empty<ulong>();
        }

        int count = data.Length / 8;
        var result = new ulong[count];

        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i * 8));
        }

        return result;
    }
}
