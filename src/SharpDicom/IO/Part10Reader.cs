using System;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.IO
{
    /// <summary>
    /// Parses DICOM Part 10 file structure (preamble, prefix, File Meta Information).
    /// </summary>
    /// <remarks>
    /// DICOM Part 10 file structure:
    /// - 128-byte preamble (optional, typically zeros)
    /// - "DICM" prefix (4 bytes)
    /// - File Meta Information (Group 0002, always Explicit VR Little Endian)
    /// - Dataset (uses Transfer Syntax from (0002,0010))
    /// </remarks>
    public sealed class Part10Reader
    {
        private readonly DicomReaderOptions _options;

        /// <summary>
        /// Gets the preamble bytes (128 bytes or empty if not present).
        /// </summary>
        public ReadOnlyMemory<byte> Preamble { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the DICM prefix was found.
        /// </summary>
        public bool HasDicmPrefix { get; private set; }

        /// <summary>
        /// Gets the File Meta Information dataset (Group 0002).
        /// </summary>
        public DicomDataset? FileMetaInfo { get; private set; }

        /// <summary>
        /// Gets the Transfer Syntax from File Meta Information.
        /// Defaults to Implicit VR Little Endian if not specified.
        /// </summary>
        public TransferSyntax TransferSyntax { get; private set; }

        /// <summary>
        /// Gets the position where the dataset starts (after FMI).
        /// </summary>
        public int DatasetStartPosition { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Part10Reader"/> class.
        /// </summary>
        /// <param name="options">Reader options, or null for defaults.</param>
        public Part10Reader(DicomReaderOptions? options = null)
        {
            _options = options ?? DicomReaderOptions.Default;
            TransferSyntax = TransferSyntax.ImplicitVRLittleEndian;
        }

        /// <summary>
        /// Parse Part 10 header from buffer.
        /// </summary>
        /// <param name="buffer">File data buffer</param>
        /// <returns>True if header parsed successfully</returns>
        /// <exception cref="DicomFileException">Thrown when required structure is missing and options are strict.</exception>
        public bool TryParseHeader(ReadOnlySpan<byte> buffer)
        {
            int position = 0;
            Preamble = ReadOnlyMemory<byte>.Empty;
            HasDicmPrefix = false;

            // Try to detect preamble + DICM
            if (buffer.Length >= 132)
            {
                // Check for DICM at position 128 (after preamble)
                if (IsDicmPrefix(buffer.Slice(128, 4)))
                {
                    Preamble = buffer.Slice(0, 128).ToArray();
                    HasDicmPrefix = true;
                    position = 132;
                }
                // Check for DICM at position 0 (no preamble)
                else if (IsDicmPrefix(buffer.Slice(0, 4)))
                {
                    Preamble = ReadOnlyMemory<byte>.Empty;
                    HasDicmPrefix = true;
                    position = 4;
                }
            }
            else if (buffer.Length >= 4)
            {
                // Short file, check for DICM at start
                if (IsDicmPrefix(buffer.Slice(0, 4)))
                {
                    Preamble = ReadOnlyMemory<byte>.Empty;
                    HasDicmPrefix = true;
                    position = 4;
                }
            }

            // Handle based on options
            if (!HasDicmPrefix)
            {
                switch (_options.Preamble)
                {
                    case FilePreambleHandling.Require:
                        throw new DicomPreambleException("Missing DICM prefix");

                    case FilePreambleHandling.Optional:
                        // Try to detect if this looks like a DICOM dataset
                        if (!LooksLikeDicomDataset(buffer))
                            throw new DicomFileException("File does not appear to be DICOM");
                        position = 0;
                        break;

                    case FilePreambleHandling.Ignore:
                        position = 0;
                        break;
                }
            }

            // Parse File Meta Information if present
            if (HasDicmPrefix || _options.FileMetaInfo != FileMetaInfoHandling.Ignore)
            {
                if (!TryParseFileMetaInfo(buffer.Slice(position), out var fmiLength))
                {
                    if (_options.FileMetaInfo == FileMetaInfoHandling.Require)
                        throw new DicomMetaInfoException("Invalid or missing File Meta Information");

                    // No FMI, use defaults
                    TransferSyntax = TransferSyntax.ImplicitVRLittleEndian;
                    DatasetStartPosition = position;
                    return true;
                }

                DatasetStartPosition = position + fmiLength;
            }
            else
            {
                DatasetStartPosition = position;
            }

            return true;
        }

        /// <summary>
        /// Checks if the span contains the DICM prefix.
        /// </summary>
        private static bool IsDicmPrefix(ReadOnlySpan<byte> span)
        {
            return span.Length >= 4 &&
                   span[0] == (byte)'D' &&
                   span[1] == (byte)'I' &&
                   span[2] == (byte)'C' &&
                   span[3] == (byte)'M';
        }

        /// <summary>
        /// Attempts to parse File Meta Information from the buffer.
        /// </summary>
        /// <param name="buffer">Buffer starting at FMI position.</param>
        /// <param name="bytesConsumed">Number of bytes consumed by FMI.</param>
        /// <returns>True if FMI was parsed successfully.</returns>
        private bool TryParseFileMetaInfo(ReadOnlySpan<byte> buffer, out int bytesConsumed)
        {
            bytesConsumed = 0;
            var fmi = new DicomDataset();

            // FMI is always Explicit VR Little Endian
            var reader = new DicomStreamReader(buffer, explicitVR: true, littleEndian: true, _options);

            // First element should be in Group 0002
            if (!reader.TryReadElementHeader(out var firstTag, out var firstVr, out var firstLength))
                return false;

            // If not group 0002, no FMI present
            if (firstTag.Group != 0x0002)
                return false;

            // Read group length if present to know FMI boundaries
            uint fmiEndPosition = 0;
            bool hasGroupLength = false;

            if (firstTag == DicomTag.FileMetaInformationGroupLength && firstLength == 4)
            {
                if (!reader.TryReadValue(firstLength, out var lengthValue))
                    return false;

                uint groupLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(lengthValue);
                fmiEndPosition = (uint)reader.Position + groupLength;
                hasGroupLength = true;

                // Add the group length element
                var groupLengthElement = new DicomNumericElement(firstTag, firstVr, lengthValue.ToArray());
                fmi.Add(groupLengthElement);
            }
            else
            {
                // No group length, will read until group changes
                fmiEndPosition = uint.MaxValue;

                // Read the first element's value
                if (!reader.TryReadValue(firstLength, out var firstValue))
                    return false;

                var firstElement = CreateElement(firstTag, firstVr, firstValue.ToArray());
                fmi.Add(firstElement);
            }

            // Continue reading FMI elements
            while (!reader.IsAtEnd)
            {
                // Check if we've reached the end of FMI
                if (hasGroupLength && (uint)reader.Position >= fmiEndPosition)
                {
                    break;
                }

                // Try to peek at the next tag without advancing
                if (reader.Remaining < 4)
                    break;

                var peekSpan = reader.Peek(4);
                ushort nextGroup = (ushort)(peekSpan[0] | (peekSpan[1] << 8));

                // Stop if we've left group 0002
                if (nextGroup != 0x0002)
                {
                    break;
                }

                // Read the next element
                if (!reader.TryReadElementHeader(out var tag, out var vr, out var length))
                    break;

                if (!reader.TryReadValue(length, out var value))
                    break;

                var element = CreateElement(tag, vr, value.ToArray());
                fmi.Add(element);
            }

            bytesConsumed = reader.Position;

            if (fmi.Count == 0)
                return false;

            FileMetaInfo = fmi;

            // Extract Transfer Syntax
            var tsElement = fmi[DicomTag.TransferSyntaxUID];
            if (tsElement != null)
            {
                var tsString = (tsElement as DicomStringElement)?.GetString();
                if (!string.IsNullOrEmpty(tsString))
                {
                    // Trim trailing null/space padding
                    tsString = tsString!.TrimEnd('\0', ' ');
                    TransferSyntax = TransferSyntax.FromUID(new DicomUID(tsString));
                }
            }

            return true;
        }

        /// <summary>
        /// Heuristically determines if the buffer looks like a DICOM dataset.
        /// </summary>
        private static bool LooksLikeDicomDataset(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 8)
                return false;

            // Check if first bytes look like a tag + VR
            // Common first tags: (0008,0005), (0008,0008), (0008,0016), etc.
            ushort group = (ushort)(buffer[0] | (buffer[1] << 8));

            // Group 0008 is very common for datasets
            if (group == 0x0008)
                return true;

            // Group 0002 means FMI without DICM (unusual but possible)
            if (group == 0x0002)
                return true;

            // Check if bytes 4-5 look like a VR (two uppercase letters)
            if (buffer[4] >= 'A' && buffer[4] <= 'Z' &&
                buffer[5] >= 'A' && buffer[5] <= 'Z')
                return true;

            return false;
        }

        /// <summary>
        /// Creates an appropriate element type based on VR.
        /// </summary>
        private static IDicomElement CreateElement(DicomTag tag, DicomVR vr, byte[] value)
        {
            if (vr == DicomVR.SQ)
                return new DicomSequence(tag, Array.Empty<DicomDataset>());

            if (vr.IsStringVR)
                return new DicomStringElement(tag, vr, value);

            if (vr.IsNumericVR)
                return new DicomNumericElement(tag, vr, value);

            return new DicomBinaryElement(tag, vr, value);
        }
    }
}
