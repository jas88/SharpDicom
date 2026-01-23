using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.IO
{
    /// <summary>
    /// Parses DICOM sequences (SQ VR) with nested datasets.
    /// </summary>
    /// <remarks>
    /// Uses an explicit stack instead of recursion to handle deeply nested
    /// sequences without risk of stack overflow on malformed files.
    /// </remarks>
    public sealed class SequenceParser
    {
        /// <summary>
        /// Undefined length constant (0xFFFFFFFF).
        /// </summary>
        public const uint UndefinedLength = 0xFFFFFFFF;

        private readonly DicomReaderOptions _options;
        private readonly bool _explicitVR;
        private readonly bool _littleEndian;

        /// <summary>
        /// Gets the maximum sequence nesting depth.
        /// </summary>
        public int MaxSequenceDepth => _options.MaxSequenceDepth;

        /// <summary>
        /// Gets the maximum total items across all sequences.
        /// </summary>
        public int MaxTotalItems => _options.MaxTotalItems;

        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceParser"/> class.
        /// </summary>
        /// <param name="explicitVR">True for explicit VR mode; false for implicit VR.</param>
        /// <param name="littleEndian">True for little-endian; false for big-endian.</param>
        /// <param name="options">Reader options, or null for defaults.</param>
        public SequenceParser(
            bool explicitVR = true,
            bool littleEndian = true,
            DicomReaderOptions? options = null)
        {
            _options = options ?? DicomReaderOptions.Default;
            _explicitVR = explicitVR;
            _littleEndian = littleEndian;
        }

        /// <summary>
        /// Parses a DICOM sequence from the buffer.
        /// </summary>
        /// <param name="buffer">The byte buffer containing sequence data.</param>
        /// <param name="tag">The sequence tag.</param>
        /// <param name="length">The sequence length (may be UndefinedLength).</param>
        /// <param name="parent">The parent dataset for context inheritance.</param>
        /// <returns>The parsed sequence.</returns>
        /// <exception cref="DicomDataException">
        /// Thrown when sequence parsing fails due to malformed data or exceeded limits.
        /// </exception>
        public DicomSequence ParseSequence(
            ReadOnlySpan<byte> buffer,
            DicomTag tag,
            uint length,
            DicomDataset? parent = null)
        {
            int totalItems = 0;
            return ParseSequenceInternal(buffer, tag, length, parent, 1, ref totalItems);
        }

        /// <summary>
        /// Parses a DICOM sequence item from the buffer.
        /// </summary>
        /// <param name="buffer">The byte buffer containing item data.</param>
        /// <param name="length">The item length (may be UndefinedLength).</param>
        /// <param name="parent">The parent dataset for context inheritance.</param>
        /// <param name="depth">The current nesting depth.</param>
        /// <returns>The parsed item dataset.</returns>
        /// <exception cref="DicomDataException">
        /// Thrown when item parsing fails due to malformed data or exceeded limits.
        /// </exception>
        public DicomDataset ParseItem(
            ReadOnlySpan<byte> buffer,
            uint length,
            DicomDataset? parent = null,
            int depth = 1)
        {
            int totalItems = 0;
            return ParseItemInternal(buffer, length, parent, depth, ref totalItems);
        }

        private DicomSequence ParseSequenceInternal(
            ReadOnlySpan<byte> buffer,
            DicomTag tag,
            uint length,
            DicomDataset? parent,
            int depth,
            ref int totalItems)
        {
            if (depth > _options.MaxSequenceDepth)
            {
                throw new DicomDataException(
                    $"Sequence nesting depth {depth} exceeds maximum {_options.MaxSequenceDepth}");
            }

            var items = new List<DicomDataset>();
            int position = 0;
            int endPosition = length == UndefinedLength ? int.MaxValue : (int)length;

            while (position < endPosition && position < buffer.Length)
            {
                // Read item tag
                if (!TryReadTag(buffer.Slice(position), out var itemTag, out int tagBytes))
                {
                    throw new DicomDataException("Unexpected end of data reading item tag in sequence");
                }
                position += tagBytes;

                // Check for sequence delimitation
                if (itemTag == DicomTag.SequenceDelimitationItem)
                {
                    // Skip the 4-byte zero length
                    position += 4;
                    break;
                }

                // Must be an Item tag
                if (itemTag != DicomTag.Item)
                {
                    throw new DicomDataException(
                        $"Expected Item tag (FFFE,E000) or SequenceDelimitationItem, got {itemTag}");
                }

                // Read item length
                if (position + 4 > buffer.Length)
                {
                    throw new DicomDataException("Unexpected end of data reading item length");
                }
                uint itemLength = ReadUInt32(buffer.Slice(position));
                position += 4;

                // Check total items limit
                totalItems++;
                if (totalItems > _options.MaxTotalItems)
                {
                    throw new DicomDataException(
                        $"Total item count exceeds maximum {_options.MaxTotalItems}");
                }

                // Calculate item end position
                int itemEndPosition;
                if (itemLength == UndefinedLength)
                {
                    itemEndPosition = int.MaxValue;
                }
                else
                {
                    itemEndPosition = position + (int)itemLength;
                    if (itemEndPosition > buffer.Length)
                    {
                        throw new DicomDataException(
                            $"Item length {itemLength} exceeds buffer size");
                    }
                }

                // Parse item content
                var itemBuffer = itemLength == UndefinedLength
                    ? buffer.Slice(position)
                    : buffer.Slice(position, (int)itemLength);

                var itemDataset = ParseItemInternal(
                    itemBuffer,
                    itemLength,
                    parent,
                    depth,
                    ref totalItems);

                itemDataset.Parent = parent;
                items.Add(itemDataset);

                // Advance position
                if (itemLength == UndefinedLength)
                {
                    // Position advanced by item parsing, need to find ItemDelimitationItem
                    // The item parser handles this and returns the consumed bytes count
                    // We need to scan for the delimiter from the current position
                    position = FindPositionAfterItemDelimiter(buffer, position, itemDataset);
                }
                else
                {
                    position = itemEndPosition;
                }
            }

            return new DicomSequence(tag, items);
        }

        private DicomDataset ParseItemInternal(
            ReadOnlySpan<byte> buffer,
            uint length,
            DicomDataset? parent,
            int depth,
            ref int totalItems)
        {
            var dataset = new DicomDataset();
            dataset.Parent = parent;

            int position = 0;
            int endPosition = length == UndefinedLength ? int.MaxValue : (int)length;

            while (position < endPosition && position < buffer.Length)
            {
                // Need at least 8 bytes for element header
                if (buffer.Length - position < 8)
                {
                    if (length == UndefinedLength)
                    {
                        // Looking for delimiter but not enough bytes
                        throw new DicomDataException("Unexpected end of data in undefined length item");
                    }
                    break;
                }

                // Peek at tag to check for delimiters
                var tagSpan = buffer.Slice(position);
                if (!TryReadTag(tagSpan, out var tag, out int tagBytes))
                {
                    break;
                }

                // Check for item delimitation
                if (tag == DicomTag.ItemDelimitationItem)
                {
                    // Skip tag + 4-byte zero length
                    position += tagBytes + 4;
                    break;
                }

                // Check for sequence delimitation (shouldn't happen in item, but handle gracefully)
                if (tag == DicomTag.SequenceDelimitationItem)
                {
                    break;
                }

                // Parse element header
                var elementSpan = buffer.Slice(position);
                if (!TryReadElementHeader(elementSpan, out var elemTag, out var elemVR, out var elemLength, out int headerBytes))
                {
                    throw new DicomDataException($"Failed to read element header at position {position}");
                }
                position += headerBytes;

                // Handle sequence elements recursively
                if (elemVR == DicomVR.SQ)
                {
                    int seqContentLength;
                    if (elemLength == UndefinedLength)
                    {
                        // Find sequence end by scanning
                        seqContentLength = FindSequenceContentLength(buffer.Slice(position));
                    }
                    else
                    {
                        seqContentLength = (int)elemLength;
                    }

                    if (position + seqContentLength > buffer.Length)
                    {
                        throw new DicomDataException($"Sequence length exceeds buffer");
                    }

                    var seqBuffer = buffer.Slice(position, seqContentLength);
                    var nestedSequence = ParseSequenceInternal(
                        seqBuffer,
                        elemTag,
                        elemLength,
                        dataset,
                        depth + 1,
                        ref totalItems);

                    dataset.Add(nestedSequence);
                    position += seqContentLength;

                    // Skip sequence delimitation item if undefined length
                    if (elemLength == UndefinedLength)
                    {
                        // Skip the SequenceDelimitationItem (8 bytes: tag + zero length)
                        position += 8;
                    }
                }
                else
                {
                    // Regular element - read value
                    if (elemLength == UndefinedLength)
                    {
                        // Undefined length for non-SQ (e.g., encapsulated pixel data)
                        // Skip for now - this will be handled in Phase 5
                        throw new DicomDataException(
                            $"Undefined length for non-sequence element {elemTag} not yet supported");
                    }

                    if (position + (int)elemLength > buffer.Length)
                    {
                        throw new DicomDataException(
                            $"Element {elemTag} value length {elemLength} exceeds buffer");
                    }

                    var value = buffer.Slice(position, (int)elemLength).ToArray();
                    position += (int)elemLength;

                    var element = CreateElement(elemTag, elemVR, value);
                    dataset.Add(element);
                }
            }

            return dataset;
        }

        private bool TryReadTag(ReadOnlySpan<byte> buffer, out DicomTag tag, out int bytesRead)
        {
            tag = default;
            bytesRead = 0;

            if (buffer.Length < 4)
                return false;

            ushort group = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
                : BinaryPrimitives.ReadUInt16BigEndian(buffer);
            ushort element = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2))
                : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));

            tag = new DicomTag(group, element);
            bytesRead = 4;
            return true;
        }

        private bool TryReadElementHeader(
            ReadOnlySpan<byte> buffer,
            out DicomTag tag,
            out DicomVR vr,
            out uint length,
            out int bytesRead)
        {
            tag = default;
            vr = default;
            length = 0;
            bytesRead = 0;

            if (buffer.Length < 8)
                return false;

            // Read tag
            ushort group = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
                : BinaryPrimitives.ReadUInt16BigEndian(buffer);
            ushort element = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2))
                : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));

            tag = new DicomTag(group, element);

            if (_explicitVR)
            {
                // Explicit VR: read VR from stream
                vr = DicomVR.FromBytes(buffer.Slice(4, 2));

                if (vr.Is32BitLength)
                {
                    // Long VRs: 2 reserved bytes + 4-byte length (12 bytes total)
                    if (buffer.Length < 12)
                        return false;

                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8))
                        : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8));

                    bytesRead = 12;
                }
                else
                {
                    // Short VRs: 2-byte length (8 bytes total)
                    length = _littleEndian
                        ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6))
                        : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(6));

                    bytesRead = 8;
                }
            }
            else
            {
                // Implicit VR: dictionary lookup, 4-byte length
                var entry = DicomDictionary.Default.GetEntry(tag);
                vr = entry?.DefaultVR ?? DicomVR.UN;

                length = _littleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4))
                    : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4));

                bytesRead = 8;
            }

            return true;
        }

        private uint ReadUInt32(ReadOnlySpan<byte> buffer)
        {
            return _littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer)
                : BinaryPrimitives.ReadUInt32BigEndian(buffer);
        }

        private int FindSequenceContentLength(ReadOnlySpan<byte> buffer)
        {
            // Scan for SequenceDelimitationItem (FFFE,E0DD)
            int position = 0;
            int depth = 0;

            while (position + 8 <= buffer.Length)
            {
                if (!TryReadTag(buffer.Slice(position), out var tag, out _))
                    break;

                if (tag == DicomTag.Item)
                {
                    // Read item length
                    uint itemLength = ReadUInt32(buffer.Slice(position + 4));
                    position += 8;

                    if (itemLength == UndefinedLength)
                    {
                        depth++;
                    }
                    else
                    {
                        position += (int)itemLength;
                    }
                }
                else if (tag == DicomTag.ItemDelimitationItem)
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                    position += 8; // tag + zero length
                }
                else if (tag == DicomTag.SequenceDelimitationItem)
                {
                    if (depth == 0)
                    {
                        // Found the end of our sequence
                        return position;
                    }
                    // Nested sequence ended
                    depth--;
                    position += 8;
                }
                else
                {
                    // Regular element - skip it
                    if (!TryReadElementHeader(buffer.Slice(position), out _, out var vr, out var elemLen, out int headerLen))
                        break;

                    position += headerLen;
                    if (elemLen != UndefinedLength)
                    {
                        position += (int)elemLen;
                    }
                    else if (vr == DicomVR.SQ)
                    {
                        // Nested sequence with undefined length - increment depth
                        // The SequenceDelimitationItem handling above will decrement it
                        depth++;
                    }
                }
            }

            throw new DicomDataException("Could not find SequenceDelimitationItem");
        }

        private int FindPositionAfterItemDelimiter(
            ReadOnlySpan<byte> buffer,
            int startPosition,
            DicomDataset parsedItem)
        {
            // Calculate the actual bytes consumed by scanning for ItemDelimitationItem
            int position = startPosition;
            int depth = 0;

            while (position + 8 <= buffer.Length)
            {
                if (!TryReadTag(buffer.Slice(position), out var tag, out _))
                    break;

                if (tag == DicomTag.ItemDelimitationItem && depth == 0)
                {
                    // Found our delimiter
                    return position + 8; // tag + zero length
                }
                else if (tag == DicomTag.Item)
                {
                    uint len = ReadUInt32(buffer.Slice(position + 4));
                    position += 8;
                    if (len == UndefinedLength)
                    {
                        depth++;
                    }
                    else
                    {
                        position += (int)len;
                    }
                }
                else if (tag == DicomTag.ItemDelimitationItem)
                {
                    depth--;
                    position += 8;
                }
                else if (tag == DicomTag.SequenceDelimitationItem)
                {
                    // Sequence ended before item delimiter - malformed but handle it
                    return position;
                }
                else
                {
                    // Regular element
                    if (!TryReadElementHeader(buffer.Slice(position), out _, out var vr, out var len, out int headerLen))
                        break;

                    position += headerLen;
                    if (len != UndefinedLength)
                    {
                        position += (int)len;
                    }
                    else if (vr == DicomVR.SQ)
                    {
                        // Nested sequence with undefined length
                        int seqLen = FindSequenceContentLength(buffer.Slice(position));
                        position += seqLen + 8; // content + delimiter
                    }
                }
            }

            // If we get here, no delimiter was found
            throw new DicomDataException("Could not find ItemDelimitationItem");
        }

        private static IDicomElement CreateElement(DicomTag tag, DicomVR vr, byte[] value)
        {
            // Use appropriate element type based on VR
            if (vr.IsStringVR)
            {
                return new DicomStringElement(tag, vr, value);
            }
            else if (IsNumericVR(vr))
            {
                return new DicomNumericElement(tag, vr, value);
            }
            else
            {
                return new DicomBinaryElement(tag, vr, value);
            }
        }

        private static bool IsNumericVR(DicomVR vr)
        {
            // Numeric VRs: SS, US, SL, UL, FL, FD, AT (tag is also numeric)
            return vr == DicomVR.SS || vr == DicomVR.US ||
                   vr == DicomVR.SL || vr == DicomVR.UL ||
                   vr == DicomVR.FL || vr == DicomVR.FD ||
                   vr == DicomVR.AT;
        }
    }
}
