using System;
using System.Buffers.Binary;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.IO
{
    /// <summary>
    /// Low-level DICOM element parser using Span&lt;T&gt; for zero-copy parsing.
    /// </summary>
    /// <remarks>
    /// This is a ref struct and cannot escape the stack. For async scenarios,
    /// use DicomFileReader which manages buffers and provides async streaming.
    /// </remarks>
    public ref struct DicomStreamReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;
        private readonly DicomReaderOptions _options;
        private readonly bool _explicitVR;
        private readonly bool _littleEndian;

        /// <summary>
        /// Gets the current position in the buffer.
        /// </summary>
        public int Position => _position;

        /// <summary>
        /// Gets the number of remaining bytes in the buffer.
        /// </summary>
        public int Remaining => _buffer.Length - _position;

        /// <summary>
        /// Gets a value indicating whether the end of buffer has been reached.
        /// </summary>
        public bool IsAtEnd => _position >= _buffer.Length;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomStreamReader"/> struct.
        /// </summary>
        /// <param name="buffer">The byte buffer to parse.</param>
        /// <param name="explicitVR">True if parsing Explicit VR; false for Implicit VR.</param>
        /// <param name="littleEndian">True for little-endian; false for big-endian.</param>
        /// <param name="options">Reader options, or null for defaults.</param>
        public DicomStreamReader(
            ReadOnlySpan<byte> buffer,
            bool explicitVR = true,
            bool littleEndian = true,
            DicomReaderOptions? options = null)
        {
            _buffer = buffer;
            _position = 0;
            _options = options ?? DicomReaderOptions.Default;
            _explicitVR = explicitVR;
            _littleEndian = littleEndian;
        }

        /// <summary>
        /// Attempts to read the next element header from the buffer.
        /// </summary>
        /// <param name="tag">When successful, contains the parsed tag.</param>
        /// <param name="vr">When successful, contains the parsed or inferred VR.</param>
        /// <param name="length">When successful, contains the value length.</param>
        /// <returns>True if the header was successfully read; false if insufficient data.</returns>
        /// <exception cref="DicomDataException">Thrown when element length exceeds maximum.</exception>
        public bool TryReadElementHeader(
            out DicomTag tag,
            out DicomVR vr,
            out uint length)
        {
            tag = default;
            vr = default;
            length = 0;

            // Need at least 8 bytes for tag + VR/length (short form)
            if (Remaining < 8)
                return false;

            var span = _buffer.Slice(_position);

            // Read tag
            ushort group = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);
            ushort element = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2))
                : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));

            tag = new DicomTag(group, element);

            if (_explicitVR)
            {
                // Explicit VR: read VR from stream
                vr = DicomVR.FromBytes(span.Slice(4, 2));

                if (vr.Is32BitLength)
                {
                    // Long VRs: 2 reserved bytes + 4-byte length (12 bytes total)
                    if (Remaining < 12)
                        return false;

                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8))
                        : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8));

                    _position += 12;
                }
                else
                {
                    // Short VRs: 2-byte length (8 bytes total)
                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6))
                        : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6));

                    _position += 8;
                }
            }
            else
            {
                // Implicit VR: look up VR from dictionary, 4-byte length
                var entry = DicomDictionary.Default.GetEntry(tag);
                vr = entry?.DefaultVR ?? DicomVR.UN;

                length = _littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4))
                    : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4));

                _position += 8;
            }

            return true;
        }

        /// <summary>
        /// Attempts to read the element value bytes after reading the header.
        /// </summary>
        /// <param name="length">The length of the value to read.</param>
        /// <param name="value">When successful, contains the value bytes.</param>
        /// <returns>True if the value was successfully read; false if undefined length or insufficient data.</returns>
        /// <exception cref="DicomDataException">Thrown when element length exceeds maximum.</exception>
        public bool TryReadValue(uint length, out ReadOnlySpan<byte> value)
        {
            value = default;

            // Undefined length - cannot read with this method
            if (length == 0xFFFFFFFF)
                return false;

            if (length > _options.MaxElementLength)
                throw new DicomDataException($"Element length {length} exceeds maximum {_options.MaxElementLength}");

            if ((int)length > Remaining)
                return false;

            value = _buffer.Slice(_position, (int)length);
            _position += (int)length;
            return true;
        }

        /// <summary>
        /// Skips a specified number of bytes in the buffer.
        /// </summary>
        /// <param name="count">The number of bytes to skip.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when count exceeds remaining bytes.</exception>
        public void Skip(int count)
        {
            if (count > Remaining)
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot skip {count} bytes, only {Remaining} remaining");
            _position += count;
        }

        /// <summary>
        /// Checks if the buffer contains the DICM prefix at the current position.
        /// </summary>
        /// <returns>True if the DICM prefix is present; otherwise, false.</returns>
        public bool CheckDicmPrefix()
        {
            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position, 4);
            return span[0] == (byte)'D' &&
                   span[1] == (byte)'I' &&
                   span[2] == (byte)'C' &&
                   span[3] == (byte)'M';
        }

        /// <summary>
        /// Reads a 16-bit unsigned integer from the buffer.
        /// </summary>
        /// <returns>The 16-bit unsigned integer value.</returns>
        /// <exception cref="DicomDataException">Thrown when insufficient data remains.</exception>
        public ushort ReadUInt16()
        {
            if (Remaining < 2)
                throw new DicomDataException("Unexpected end of data reading UInt16");

            var value = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_position))
                : BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_position));

            _position += 2;
            return value;
        }

        /// <summary>
        /// Reads a 32-bit unsigned integer from the buffer.
        /// </summary>
        /// <returns>The 32-bit unsigned integer value.</returns>
        /// <exception cref="DicomDataException">Thrown when insufficient data remains.</exception>
        public uint ReadUInt32()
        {
            if (Remaining < 4)
                throw new DicomDataException("Unexpected end of data reading UInt32");

            var value = _littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position))
                : BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_position));

            _position += 4;
            return value;
        }

        /// <summary>
        /// Peeks at bytes without advancing position.
        /// </summary>
        /// <param name="count">The number of bytes to peek.</param>
        /// <returns>A span containing the peeked bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when count exceeds remaining bytes.</exception>
        public ReadOnlySpan<byte> Peek(int count)
        {
            if (count > Remaining)
                throw new ArgumentOutOfRangeException(nameof(count), $"Cannot peek {count} bytes, only {Remaining} remaining");

            return _buffer.Slice(_position, count);
        }

        /// <summary>
        /// Reads bytes from the buffer.
        /// </summary>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A span containing the read bytes.</returns>
        /// <exception cref="DicomDataException">Thrown when insufficient data remains.</exception>
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count > Remaining)
                throw new DicomDataException($"Cannot read {count} bytes, only {Remaining} remaining");

            var result = _buffer.Slice(_position, count);
            _position += count;
            return result;
        }

        /// <summary>
        /// Peeks at the next tag without advancing the position.
        /// </summary>
        /// <param name="tag">When successful, contains the peeked tag.</param>
        /// <returns>True if a tag could be read; false if insufficient data.</returns>
        public bool TryPeekTag(out DicomTag tag)
        {
            tag = default;

            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            ushort group = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);
            ushort element = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2))
                : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));

            tag = new DicomTag(group, element);
            return true;
        }

        /// <summary>
        /// Checks if a tag is a delimiter tag (group FFFE).
        /// </summary>
        /// <param name="tag">The tag to check.</param>
        /// <returns>True if the tag is a delimiter tag; otherwise, false.</returns>
        public static bool IsDelimiterTag(DicomTag tag)
        {
            return tag.Group == 0xFFFE;
        }

        /// <summary>
        /// Finds the position of the SequenceDelimitationItem (FFFE,E0DD) in the remaining buffer.
        /// </summary>
        /// <returns>The offset from the current position to the delimiter, or -1 if not found.</returns>
        /// <remarks>
        /// This method scans through the buffer tracking nesting depth to find the
        /// sequence delimiter that matches the current nesting level.
        /// </remarks>
        public int FindSequenceDelimiter()
        {
            int searchPos = 0;
            int depth = 0;
            var searchBuffer = _buffer.Slice(_position);

            while (searchPos + 8 <= searchBuffer.Length)
            {
                ushort group = _littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(searchBuffer.Slice(searchPos))
                    : BinaryPrimitives.ReadUInt16BigEndian(searchBuffer.Slice(searchPos));
                ushort element = _littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(searchBuffer.Slice(searchPos + 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(searchBuffer.Slice(searchPos + 2));

                var tag = new DicomTag(group, element);

                if (tag == DicomTag.SequenceDelimitationItem && depth == 0)
                {
                    return searchPos;
                }
                else if (tag == DicomTag.Item)
                {
                    uint itemLength = _littleEndian
                        ? BinaryPrimitives.ReadUInt32LittleEndian(searchBuffer.Slice(searchPos + 4))
                        : BinaryPrimitives.ReadUInt32BigEndian(searchBuffer.Slice(searchPos + 4));
                    searchPos += 8;

                    if (itemLength == 0xFFFFFFFF)
                    {
                        depth++;
                    }
                    else
                    {
                        searchPos += (int)itemLength;
                    }
                }
                else if (tag == DicomTag.ItemDelimitationItem)
                {
                    if (depth > 0)
                        depth--;
                    searchPos += 8;
                }
                else if (tag == DicomTag.SequenceDelimitationItem)
                {
                    // Nested sequence delimiter
                    depth--;
                    searchPos += 8;
                }
                else
                {
                    // Skip past the tag - attempt to read element header
                    if (!TryReadElementHeaderAt(searchBuffer, searchPos, out _, out _, out var len, out int headerLen))
                    {
                        searchPos += 4; // Minimum advance
                    }
                    else
                    {
                        searchPos += headerLen;
                        if (len != 0xFFFFFFFF)
                        {
                            searchPos += (int)len;
                        }
                    }
                }
            }

            return -1; // Not found
        }

        /// <summary>
        /// Skips a value of the specified length without reading it.
        /// </summary>
        /// <param name="length">The number of bytes to skip.</param>
        /// <returns>True if the skip was successful; false if insufficient data.</returns>
        public bool TrySkipValue(uint length)
        {
            if (length == 0xFFFFFFFF)
                return false; // Cannot skip undefined length

            if ((int)length > Remaining)
                return false;

            _position += (int)length;
            return true;
        }

        /// <summary>
        /// Reads an element header at a specific position without advancing the main position.
        /// </summary>
        private bool TryReadElementHeaderAt(
            ReadOnlySpan<byte> buffer,
            int position,
            out DicomTag tag,
            out DicomVR vr,
            out uint length,
            out int bytesRead)
        {
            tag = default;
            vr = default;
            length = 0;
            bytesRead = 0;

            if (buffer.Length - position < 8)
                return false;

            var span = buffer.Slice(position);

            ushort group = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span);
            ushort element = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2))
                : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));

            tag = new DicomTag(group, element);

            if (_explicitVR)
            {
                vr = DicomVR.FromBytes(span.Slice(4, 2));

                if (vr.Is32BitLength)
                {
                    if (buffer.Length - position < 12)
                        return false;

                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8))
                        : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8));

                    bytesRead = 12;
                }
                else
                {
                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6))
                        : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6));

                    bytesRead = 8;
                }
            }
            else
            {
                var entry = DicomDictionary.Default.GetEntry(tag);
                vr = entry?.DefaultVR ?? DicomVR.UN;

                length = _littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4))
                    : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4));

                bytesRead = 8;
            }

            return true;
        }
    }
}
