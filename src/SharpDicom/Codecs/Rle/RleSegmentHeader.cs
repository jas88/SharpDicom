using System;
using System.Buffers.Binary;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Codecs.Rle
{
    /// <summary>
    /// Represents the 64-byte header that precedes each RLE-compressed frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The RLE header contains the number of segments and offsets to each segment
    /// from the start of the compressed frame data (including the header).
    /// </para>
    /// <para>
    /// Header structure (64 bytes):
    /// - Bytes 0-3: Number of segments (uint32 LE, 1-15)
    /// - Bytes 4-7: Offset to segment 1 (always 64)
    /// - Bytes 8-11: Offset to segment 2 (or 0 if unused)
    /// - ... up to 15 segments total
    /// </para>
    /// </remarks>
    public readonly struct RleSegmentHeader
    {
        /// <summary>
        /// The fixed size of the RLE header in bytes.
        /// </summary>
        public const int HeaderSize = 64;

        /// <summary>
        /// Maximum number of segments allowed in an RLE frame.
        /// </summary>
        public const int MaxSegments = 15;

        private readonly int _numberOfSegments;
        private readonly uint _offset0, _offset1, _offset2, _offset3, _offset4;
        private readonly uint _offset5, _offset6, _offset7, _offset8, _offset9;
        private readonly uint _offset10, _offset11, _offset12, _offset13, _offset14;

        /// <summary>
        /// Gets the number of segments in this RLE frame.
        /// </summary>
        public int NumberOfSegments => _numberOfSegments;

        /// <summary>
        /// Initializes a new instance of the <see cref="RleSegmentHeader"/> struct.
        /// </summary>
        /// <param name="numberOfSegments">The number of segments.</param>
        /// <param name="offsets">An array of 15 segment offsets.</param>
        private RleSegmentHeader(int numberOfSegments, ReadOnlySpan<uint> offsets)
        {
            _numberOfSegments = numberOfSegments;
            _offset0 = offsets[0];
            _offset1 = offsets[1];
            _offset2 = offsets[2];
            _offset3 = offsets[3];
            _offset4 = offsets[4];
            _offset5 = offsets[5];
            _offset6 = offsets[6];
            _offset7 = offsets[7];
            _offset8 = offsets[8];
            _offset9 = offsets[9];
            _offset10 = offsets[10];
            _offset11 = offsets[11];
            _offset12 = offsets[12];
            _offset13 = offsets[13];
            _offset14 = offsets[14];
        }

        /// <summary>
        /// Gets the offset to a specific segment.
        /// </summary>
        /// <param name="index">The zero-based segment index (0-14).</param>
        /// <returns>The byte offset from the start of the frame data to the segment.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="index"/> is negative, greater than or equal to <see cref="NumberOfSegments"/>,
        /// or exceeds the maximum segment count.
        /// </exception>
        public uint GetSegmentOffset(int index)
        {
            if (index < 0 || index >= _numberOfSegments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"Segment index must be between 0 and {_numberOfSegments - 1}.");
            }

            return index switch
            {
                0 => _offset0,
                1 => _offset1,
                2 => _offset2,
                3 => _offset3,
                4 => _offset4,
                5 => _offset5,
                6 => _offset6,
                7 => _offset7,
                8 => _offset8,
                9 => _offset9,
                10 => _offset10,
                11 => _offset11,
                12 => _offset12,
                13 => _offset13,
                14 => _offset14,
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Segment index out of range.")
            };
        }

        /// <summary>
        /// Parses an RLE segment header from the given data.
        /// </summary>
        /// <param name="data">The raw header bytes (must be at least 64 bytes).</param>
        /// <returns>A parsed <see cref="RleSegmentHeader"/>.</returns>
        /// <exception cref="DicomCodecException">
        /// The header is too short, segment count is invalid, or first segment offset is not 64.
        /// </exception>
        public static RleSegmentHeader Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderSize)
            {
                throw new DicomCodecException($"RLE header too short: expected {HeaderSize} bytes, got {data.Length}")
                {
                    BytePosition = 0
                };
            }

            var numSegments = BinaryPrimitives.ReadUInt32LittleEndian(data);

            if (numSegments == 0)
            {
                throw new DicomCodecException("RLE segment count is zero")
                {
                    BytePosition = 0
                };
            }

            if (numSegments > MaxSegments)
            {
                throw new DicomCodecException($"RLE segment count {numSegments} exceeds maximum of {MaxSegments}")
                {
                    BytePosition = 0
                };
            }

            Span<uint> offsets = stackalloc uint[MaxSegments];
            for (int i = 0; i < MaxSegments; i++)
            {
                offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4 + i * 4));
            }

            // Validate first segment offset is the header size
            if (offsets[0] != HeaderSize)
            {
                throw new DicomCodecException($"First RLE segment offset must be {HeaderSize}, got {offsets[0]}")
                {
                    BytePosition = 4
                };
            }

            return new RleSegmentHeader((int)numSegments, offsets);
        }

        /// <summary>
        /// Attempts to parse an RLE segment header from the given data.
        /// </summary>
        /// <param name="data">The raw header bytes.</param>
        /// <param name="header">When successful, the parsed header; otherwise, default.</param>
        /// <param name="error">When failed, a description of the error; otherwise, null.</param>
        /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
        public static bool TryParse(ReadOnlySpan<byte> data, out RleSegmentHeader header, out string? error)
        {
            header = default;
            error = null;

            if (data.Length < HeaderSize)
            {
                error = $"RLE header too short: expected {HeaderSize} bytes, got {data.Length}";
                return false;
            }

            var numSegments = BinaryPrimitives.ReadUInt32LittleEndian(data);

            if (numSegments == 0)
            {
                error = "RLE segment count is zero";
                return false;
            }

            if (numSegments > MaxSegments)
            {
                error = $"RLE segment count {numSegments} exceeds maximum of {MaxSegments}";
                return false;
            }

            Span<uint> offsets = stackalloc uint[MaxSegments];
            for (int i = 0; i < MaxSegments; i++)
            {
                offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4 + i * 4));
            }

            // Validate first segment offset
            if (offsets[0] != HeaderSize)
            {
                error = $"First RLE segment offset must be {HeaderSize}, got {offsets[0]}";
                return false;
            }

            header = new RleSegmentHeader((int)numSegments, offsets);
            return true;
        }

        /// <summary>
        /// Creates an RLE segment header for encoding.
        /// </summary>
        /// <param name="segmentLengths">The lengths of each encoded segment.</param>
        /// <returns>A new <see cref="RleSegmentHeader"/> with calculated offsets.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="segmentLengths"/> is empty or has more than <see cref="MaxSegments"/> elements.
        /// </exception>
        public static RleSegmentHeader Create(ReadOnlySpan<int> segmentLengths)
        {
            if (segmentLengths.Length == 0)
            {
                throw new ArgumentException("At least one segment is required.", nameof(segmentLengths));
            }

            if (segmentLengths.Length > MaxSegments)
            {
                throw new ArgumentException(
                    $"Segment count {segmentLengths.Length} exceeds maximum of {MaxSegments}.",
                    nameof(segmentLengths));
            }

            Span<uint> offsets = stackalloc uint[MaxSegments];
            uint offset = HeaderSize;

            for (int i = 0; i < segmentLengths.Length; i++)
            {
                offsets[i] = offset;
                offset += (uint)segmentLengths[i];
            }

            // Zero out unused slots
            for (int i = segmentLengths.Length; i < MaxSegments; i++)
            {
                offsets[i] = 0;
            }

            return new RleSegmentHeader(segmentLengths.Length, offsets);
        }

        /// <summary>
        /// Writes this header to a destination span.
        /// </summary>
        /// <param name="destination">The destination span (must be at least 64 bytes).</param>
        /// <exception cref="ArgumentException">Destination is too small.</exception>
        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < HeaderSize)
            {
                throw new ArgumentException(
                    $"Destination too small: need {HeaderSize} bytes, got {destination.Length}.",
                    nameof(destination));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)_numberOfSegments);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), _offset0);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), _offset1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12), _offset2);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16), _offset3);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20), _offset4);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24), _offset5);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28), _offset6);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(32), _offset7);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(36), _offset8);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(40), _offset9);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(44), _offset10);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(48), _offset11);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(52), _offset12);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(56), _offset13);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(60), _offset14);
        }
    }
}
