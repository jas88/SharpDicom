using System;
using System.Buffers;
using System.Buffers.Binary;
using SharpDicom.Data;
using SharpDicom.Internal;

namespace SharpDicom.IO
{
    /// <summary>
    /// Low-level DICOM element writer using IBufferWriter&lt;byte&gt; for zero-copy writing.
    /// </summary>
    /// <remarks>
    /// This class writes DICOM elements to any IBufferWriter target (Stream wrappers,
    /// PipeWriter, ArrayBufferWriter, etc.) following DICOM Part 5 encoding rules.
    /// Supports both defined-length and undefined-length sequence encoding.
    /// </remarks>
    public sealed class DicomStreamWriter
    {
        private readonly IBufferWriter<byte> _writer;
        private readonly DicomWriterOptions _options;
        private readonly bool _explicitVR;
        private readonly bool _littleEndian;
        private readonly SequenceLengthEncoding _sequenceLengthMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomStreamWriter"/> class.
        /// </summary>
        /// <param name="writer">The buffer writer to write to.</param>
        /// <param name="options">Writer options, or null for defaults.</param>
        public DicomStreamWriter(IBufferWriter<byte> writer, DicomWriterOptions? options = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _options = options ?? DicomWriterOptions.Default;
            _explicitVR = _options.TransferSyntax.IsExplicitVR;
            _littleEndian = _options.TransferSyntax.IsLittleEndian;
            _sequenceLengthMode = _options.SequenceLength;
        }

        /// <summary>
        /// Creates a DicomStreamWriter with specific encoding options.
        /// </summary>
        /// <param name="writer">The buffer writer to write to.</param>
        /// <param name="explicitVR">True for explicit VR encoding; false for implicit VR.</param>
        /// <param name="littleEndian">True for little-endian; false for big-endian.</param>
        /// <param name="sequenceLengthMode">Sequence length encoding mode (defaults to Undefined).</param>
        public DicomStreamWriter(
            IBufferWriter<byte> writer,
            bool explicitVR,
            bool littleEndian,
            SequenceLengthEncoding sequenceLengthMode = SequenceLengthEncoding.Undefined)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _options = DicomWriterOptions.Default;
            _explicitVR = explicitVR;
            _littleEndian = littleEndian;
            _sequenceLengthMode = sequenceLengthMode;
        }

        /// <summary>
        /// Writes a DICOM element to the buffer.
        /// </summary>
        /// <param name="element">The element to write.</param>
        /// <remarks>
        /// Value padding is applied automatically if needed:
        /// - String VRs (except UI) are padded with space (0x20)
        /// - Binary VRs and UI are padded with null (0x00)
        /// </remarks>
        public void WriteElement(IDicomElement element)
        {
            ThrowHelpers.ThrowIfNull(element, nameof(element));

            var vrInfo = DicomVRInfo.GetInfo(element.VR);
            var value = element.RawValue.Span;
            int valueLength = value.Length;

            // Determine if padding is needed
            bool needsPadding = (valueLength & 1) == 1;
            int paddedLength = needsPadding ? valueLength + 1 : valueLength;
            byte paddingByte = vrInfo.PaddingByte;

            if (_explicitVR)
            {
                WriteExplicitVRElement(element.Tag, element.VR, vrInfo, value, paddedLength, needsPadding, paddingByte);
            }
            else
            {
                WriteImplicitVRElement(element.Tag, value, paddedLength, needsPadding, paddingByte);
            }
        }

        /// <summary>
        /// Writes an element with explicit tag, VR, and value.
        /// </summary>
        /// <param name="tag">The DICOM tag.</param>
        /// <param name="vr">The Value Representation.</param>
        /// <param name="value">The raw value bytes.</param>
        public void WriteElement(DicomTag tag, DicomVR vr, ReadOnlySpan<byte> value)
        {
            var vrInfo = DicomVRInfo.GetInfo(vr);
            int valueLength = value.Length;

            // Determine if padding is needed
            bool needsPadding = (valueLength & 1) == 1;
            int paddedLength = needsPadding ? valueLength + 1 : valueLength;
            byte paddingByte = vrInfo.PaddingByte;

            if (_explicitVR)
            {
                WriteExplicitVRElement(tag, vr, vrInfo, value, paddedLength, needsPadding, paddingByte);
            }
            else
            {
                WriteImplicitVRElement(tag, value, paddedLength, needsPadding, paddingByte);
            }
        }

        /// <summary>
        /// Writes only a DICOM tag (4 bytes) - useful for delimiter tags.
        /// </summary>
        /// <param name="tag">The tag to write.</param>
        public void WriteTag(DicomTag tag)
        {
            var span = _writer.GetSpan(4);

            if (_littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
            }

            _writer.Advance(4);
        }

        /// <summary>
        /// Writes a tag with a 32-bit length value - useful for delimiter tags and item markers.
        /// </summary>
        /// <param name="tag">The tag to write.</param>
        /// <param name="length">The length value to write.</param>
        public void WriteTagWithLength(DicomTag tag, uint length)
        {
            var span = _writer.GetSpan(8);

            if (_littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
                BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), length);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4), length);
            }

            _writer.Advance(8);
        }

        /// <summary>
        /// Writes raw bytes to the buffer.
        /// </summary>
        /// <param name="bytes">The bytes to write.</param>
        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return;

            var span = _writer.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            _writer.Advance(bytes.Length);
        }

        /// <summary>
        /// Writes a 16-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt16(ushort value)
        {
            var span = _writer.GetSpan(2);

            if (_littleEndian)
                BinaryPrimitives.WriteUInt16LittleEndian(span, value);
            else
                BinaryPrimitives.WriteUInt16BigEndian(span, value);

            _writer.Advance(2);
        }

        /// <summary>
        /// Writes a 32-bit unsigned integer.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteUInt32(uint value)
        {
            var span = _writer.GetSpan(4);

            if (_littleEndian)
                BinaryPrimitives.WriteUInt32LittleEndian(span, value);
            else
                BinaryPrimitives.WriteUInt32BigEndian(span, value);

            _writer.Advance(4);
        }

        /// <summary>
        /// Gets a span from the underlying buffer writer for direct writing.
        /// </summary>
        /// <param name="sizeHint">The minimum size needed.</param>
        /// <returns>A writable span.</returns>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return _writer.GetSpan(sizeHint);
        }

        /// <summary>
        /// Advances the underlying buffer writer by the specified number of bytes.
        /// </summary>
        /// <param name="count">The number of bytes to advance.</param>
        public void Advance(int count)
        {
            _writer.Advance(count);
        }

        /// <summary>
        /// Writes a complete dataset (all elements including sequences).
        /// </summary>
        /// <param name="dataset">The dataset to write.</param>
        /// <remarks>
        /// Elements are written in sorted tag order. Sequences are handled according
        /// to the configured <see cref="SequenceLengthEncoding"/> mode.
        /// </remarks>
        public void WriteDataset(DicomDataset dataset)
        {
            ThrowHelpers.ThrowIfNull(dataset, nameof(dataset));

            foreach (var element in dataset)
            {
                if (element is DicomSequence sequence)
                {
                    WriteSequence(sequence);
                }
                else
                {
                    WriteElement(element);
                }
            }
        }

        /// <summary>
        /// Writes a sequence element using the current length encoding mode.
        /// </summary>
        /// <param name="sequence">The sequence to write.</param>
        /// <remarks>
        /// The sequence is written using either undefined length (with delimiters)
        /// or defined length (calculated) based on the configured
        /// <see cref="SequenceLengthEncoding"/> mode.
        /// </remarks>
        public void WriteSequence(DicomSequence sequence)
        {
            ThrowHelpers.ThrowIfNull(sequence, nameof(sequence));

            if (_sequenceLengthMode == SequenceLengthEncoding.Defined)
            {
                // Try defined length encoding
                uint seqLength = SequenceLengthCalculator.CalculateSequenceLength(sequence, _explicitVR);
                if (seqLength != SequenceLengthCalculator.UndefinedLength)
                {
                    WriteSequenceDefined(sequence, seqLength);
                    return;
                }
                // Fall through to undefined length if calculation overflowed
            }

            WriteSequenceUndefined(sequence);
        }

        /// <summary>
        /// Writes a sequence element with undefined length using delimiters.
        /// </summary>
        /// <param name="sequence">The sequence to write.</param>
        private void WriteSequenceUndefined(DicomSequence sequence)
        {
            // Write sequence header
            WriteSequenceHeader(sequence.Tag, SequenceLengthCalculator.UndefinedLength);

            // Write each item with undefined length
            foreach (var item in sequence.Items)
            {
                // Item tag (FFFE,E000) with undefined length
                WriteTagWithLength(DicomTag.Item, SequenceLengthCalculator.UndefinedLength);

                // Write item dataset elements
                WriteDataset(item);

                // Item delimitation item (FFFE,E00D) with zero length
                WriteTagWithLength(DicomTag.ItemDelimitationItem, 0);
            }

            // Sequence delimitation item (FFFE,E0DD) with zero length
            WriteTagWithLength(DicomTag.SequenceDelimitationItem, 0);
        }

        /// <summary>
        /// Writes a sequence element with defined (calculated) length.
        /// </summary>
        /// <param name="sequence">The sequence to write.</param>
        /// <param name="sequenceLength">The pre-calculated sequence length.</param>
        private void WriteSequenceDefined(DicomSequence sequence, uint sequenceLength)
        {
            // Write sequence header with defined length
            WriteSequenceHeader(sequence.Tag, sequenceLength);

            // Write each item with defined length
            foreach (var item in sequence.Items)
            {
                // Calculate item length
                uint itemLength = SequenceLengthCalculator.CalculateDatasetLength(item, _explicitVR);

                // Item tag (FFFE,E000) with calculated length
                WriteTagWithLength(DicomTag.Item, itemLength);

                // Write item dataset elements
                WriteDataset(item);

                // No Item Delimitation Item for defined length
            }

            // No Sequence Delimitation Item for defined length
        }

        /// <summary>
        /// Writes the sequence header (tag + VR + length).
        /// </summary>
        /// <param name="tag">The sequence tag.</param>
        /// <param name="length">The sequence length (or 0xFFFFFFFF for undefined).</param>
        private void WriteSequenceHeader(DicomTag tag, uint length)
        {
            if (_explicitVR)
            {
                // Explicit VR SQ: Tag(4) + VR(2) + Reserved(2) + Length(4) = 12 bytes
                var span = _writer.GetSpan(12);

                // Write tag
                if (_littleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
                }

                // Write VR "SQ"
                span[4] = (byte)'S';
                span[5] = (byte)'Q';

                // Write reserved bytes
                span[6] = 0x00;
                span[7] = 0x00;

                // Write length
                if (_littleEndian)
                    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), length);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8), length);

                _writer.Advance(12);
            }
            else
            {
                // Implicit VR: Tag(4) + Length(4) = 8 bytes
                WriteTagWithLength(tag, length);
            }
        }

        private void WriteExplicitVRElement(
            DicomTag tag,
            DicomVR vr,
            DicomVRInfo vrInfo,
            ReadOnlySpan<byte> value,
            int paddedLength,
            bool needsPadding,
            byte paddingByte)
        {
            if (vrInfo.Is16BitLength)
            {
                // 16-bit length VRs: Tag(4) + VR(2) + Length(2) = 8 bytes header
                int totalSize = 8 + paddedLength;
                var span = _writer.GetSpan(totalSize);

                // Write tag
                if (_littleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
                }

                // Write VR
                span[4] = vr.Char1;
                span[5] = vr.Char2;

                // Write length (16-bit)
                if (_littleEndian)
                    BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(6), (ushort)paddedLength);
                else
                    BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6), (ushort)paddedLength);

                // Write value
                value.CopyTo(span.Slice(8));

                // Write padding if needed
                if (needsPadding)
                    span[8 + value.Length] = paddingByte;

                _writer.Advance(totalSize);
            }
            else
            {
                // 32-bit length VRs: Tag(4) + VR(2) + Reserved(2) + Length(4) = 12 bytes header
                int totalSize = 12 + paddedLength;
                var span = _writer.GetSpan(totalSize);

                // Write tag
                if (_littleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                    BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
                }

                // Write VR
                span[4] = vr.Char1;
                span[5] = vr.Char2;

                // Write reserved bytes
                span[6] = 0x00;
                span[7] = 0x00;

                // Write length (32-bit)
                if (_littleEndian)
                    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), (uint)paddedLength);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8), (uint)paddedLength);

                // Write value
                value.CopyTo(span.Slice(12));

                // Write padding if needed
                if (needsPadding)
                    span[12 + value.Length] = paddingByte;

                _writer.Advance(totalSize);
            }
        }

        private void WriteImplicitVRElement(
            DicomTag tag,
            ReadOnlySpan<byte> value,
            int paddedLength,
            bool needsPadding,
            byte paddingByte)
        {
            // Implicit VR: Tag(4) + Length(4) = 8 bytes header
            int totalSize = 8 + paddedLength;
            var span = _writer.GetSpan(totalSize);

            // Write tag
            if (_littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), tag.Element);
            }

            // Write length (32-bit)
            if (_littleEndian)
                BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)paddedLength);
            else
                BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4), (uint)paddedLength);

            // Write value
            value.CopyTo(span.Slice(8));

            // Write padding if needed
            if (needsPadding)
                span[8 + value.Length] = paddingByte;

            _writer.Advance(totalSize);
        }
    }
}
