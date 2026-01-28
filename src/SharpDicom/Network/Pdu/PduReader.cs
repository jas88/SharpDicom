using System;
using System.Buffers.Binary;
using System.Text;
using SharpDicom.Network.Items;

namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Low-level PDU parser using Span&lt;T&gt; for zero-copy parsing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a ref struct and cannot escape the stack. For network I/O scenarios,
    /// use this with buffered data from socket reads.
    /// </para>
    /// <para>
    /// All PDU headers and variable item lengths use Big-Endian byte order per DICOM PS3.8.
    /// Methods return false when there is insufficient data to complete parsing,
    /// enabling TCP fragmentation handling.
    /// </para>
    /// </remarks>
    public ref struct PduReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _position;

        /// <summary>
        /// Initializes a new instance of the <see cref="PduReader"/> struct.
        /// </summary>
        /// <param name="buffer">The byte buffer to parse.</param>
        public PduReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        /// <summary>
        /// Gets the current position in the buffer.
        /// </summary>
        public int Position => _position;

        /// <summary>
        /// Gets the number of remaining bytes in the buffer.
        /// </summary>
        public int Remaining => _buffer.Length - _position;

        /// <summary>
        /// Attempts to read the PDU header (type and length).
        /// </summary>
        /// <param name="type">When successful, the PDU type.</param>
        /// <param name="length">When successful, the PDU length (excluding the 6-byte header).</param>
        /// <returns>true if the header was read; false if insufficient data.</returns>
        /// <remarks>
        /// The PDU header consists of:
        /// - 1 byte: PDU type
        /// - 1 byte: Reserved (0x00)
        /// - 4 bytes: PDU length (Big-Endian, excludes header)
        /// </remarks>
        public bool TryReadPduHeader(out PduType type, out uint length)
        {
            type = default;
            length = 0;

            if (Remaining < PduConstants.HeaderLength)
                return false;

            var span = _buffer.Slice(_position);
            type = (PduType)span[0];
            // span[1] is reserved byte
            length = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2));

            _position += PduConstants.HeaderLength;
            return true;
        }

        /// <summary>
        /// Attempts to read an A-ASSOCIATE-RQ PDU body after the header.
        /// </summary>
        /// <param name="protocolVersion">The protocol version (should be 0x0001).</param>
        /// <param name="calledAE">The Called AE Title (trailing spaces trimmed).</param>
        /// <param name="callingAE">The Calling AE Title (trailing spaces trimmed).</param>
        /// <param name="variableItems">The variable items portion for further parsing.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadAssociateRequest(
            out ushort protocolVersion,
            out string calledAE,
            out string callingAE,
            out ReadOnlySpan<byte> variableItems)
        {
            return TryReadAssociate(out protocolVersion, out calledAE, out callingAE, out variableItems);
        }

        /// <summary>
        /// Attempts to read an A-ASSOCIATE-AC PDU body after the header.
        /// </summary>
        /// <param name="protocolVersion">The protocol version (should be 0x0001).</param>
        /// <param name="calledAE">The Called AE Title (trailing spaces trimmed).</param>
        /// <param name="callingAE">The Calling AE Title (trailing spaces trimmed).</param>
        /// <param name="variableItems">The variable items portion for further parsing.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadAssociateAccept(
            out ushort protocolVersion,
            out string calledAE,
            out string callingAE,
            out ReadOnlySpan<byte> variableItems)
        {
            return TryReadAssociate(out protocolVersion, out calledAE, out callingAE, out variableItems);
        }

        /// <summary>
        /// Shared implementation for A-ASSOCIATE-RQ and A-ASSOCIATE-AC parsing.
        /// </summary>
        private bool TryReadAssociate(
            out ushort protocolVersion,
            out string calledAE,
            out string callingAE,
            out ReadOnlySpan<byte> variableItems)
        {
            protocolVersion = 0;
            calledAE = string.Empty;
            callingAE = string.Empty;
            variableItems = default;

            // Need at least 68 bytes for fixed fields
            if (Remaining < PduConstants.AssociateFixedFieldsLength)
                return false;

            var span = _buffer.Slice(_position);

            // Protocol version (2 bytes, Big-Endian)
            protocolVersion = BinaryPrimitives.ReadUInt16BigEndian(span);

            // Skip 2 reserved bytes
            // Called AE Title at offset 4, 16 bytes
            calledAE = ReadAeTitle(span.Slice(4, PduConstants.AETitleLength));

            // Calling AE Title at offset 20, 16 bytes
            callingAE = ReadAeTitle(span.Slice(20, PduConstants.AETitleLength));

            // Skip 32 reserved bytes (offset 36 to 68)
            // Variable items start at offset 68
            _position += PduConstants.AssociateFixedFieldsLength;
            variableItems = _buffer.Slice(_position);
            return true;
        }

        /// <summary>
        /// Attempts to read an A-ASSOCIATE-RJ PDU body after the header.
        /// </summary>
        /// <param name="result">The rejection result (permanent or transient).</param>
        /// <param name="source">The rejection source.</param>
        /// <param name="reason">The rejection reason.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadAssociateReject(
            out RejectResult result,
            out RejectSource source,
            out RejectReason reason)
        {
            result = default;
            source = default;
            reason = default;

            // A-ASSOCIATE-RJ has 4 bytes after header
            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            // span[0] is reserved
            result = (RejectResult)span[1];
            source = (RejectSource)span[2];
            reason = (RejectReason)span[3];

            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read a P-DATA-TF PDU body after the header.
        /// </summary>
        /// <param name="pdvData">The presentation data value items portion.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// The pdvData span contains all PDV items. Use <see cref="TryReadPresentationDataValue"/>
        /// to parse individual PDV items from this span.
        /// </remarks>
        public bool TryReadPData(out ReadOnlySpan<byte> pdvData)
        {
            // P-DATA has no fixed fields after header, just PDV items
            pdvData = _buffer.Slice(_position);
            return true;
        }

        /// <summary>
        /// Attempts to read an A-RELEASE-RQ PDU body after the header.
        /// </summary>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// A-RELEASE-RQ has 4 reserved bytes after the header, no meaningful content.
        /// </remarks>
        public bool TryReadReleaseRequest()
        {
            // A-RELEASE-RQ has 4 reserved bytes
            if (Remaining < 4)
                return false;

            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read an A-RELEASE-RP PDU body after the header.
        /// </summary>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// A-RELEASE-RP has 4 reserved bytes after the header, no meaningful content.
        /// </remarks>
        public bool TryReadReleaseResponse()
        {
            // A-RELEASE-RP has 4 reserved bytes
            if (Remaining < 4)
                return false;

            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read an A-ABORT PDU body after the header.
        /// </summary>
        /// <param name="source">The abort source.</param>
        /// <param name="reason">The abort reason (significant only when source is ServiceProvider).</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadAbort(out AbortSource source, out AbortReason reason)
        {
            source = default;
            reason = default;

            // A-ABORT has 4 bytes after header
            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            // span[0] is reserved
            // span[1] is reserved
            source = (AbortSource)span[2];
            reason = (AbortReason)span[3];

            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read a variable item header.
        /// </summary>
        /// <param name="type">The item type.</param>
        /// <param name="length">The item length (Big-Endian).</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// Variable items have a 4-byte header: Type (1), Reserved (1), Length (2).
        /// </remarks>
        public bool TryReadVariableItem(out ItemType type, out ushort length)
        {
            type = default;
            length = 0;

            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            type = (ItemType)span[0];
            // span[1] is reserved
            length = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));

            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read an Application Context item (UID).
        /// </summary>
        /// <param name="uid">The application context UID.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// Must be called after <see cref="TryReadVariableItem"/> confirms type and length.
        /// </remarks>
        public bool TryReadApplicationContext(out string uid)
        {
            return TryReadUidString(out uid);
        }

        /// <summary>
        /// Attempts to read a Presentation Context Request item.
        /// </summary>
        /// <param name="contextId">The presentation context ID.</param>
        /// <param name="itemData">The remaining item data for further parsing (abstract syntax, transfer syntaxes).</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadPresentationContextRequest(
            out byte contextId,
            out ReadOnlySpan<byte> itemData)
        {
            contextId = 0;
            itemData = default;

            // Need at least 4 bytes: Context ID (1), Reserved (3)
            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            contextId = span[0];
            // span[1..3] are reserved

            _position += 4;
            itemData = _buffer.Slice(_position);
            return true;
        }

        /// <summary>
        /// Attempts to read a Presentation Context Accept item.
        /// </summary>
        /// <param name="contextId">The presentation context ID.</param>
        /// <param name="result">The presentation context result.</param>
        /// <param name="itemData">The remaining item data for further parsing (transfer syntax).</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadPresentationContextAccept(
            out byte contextId,
            out PresentationContextResult result,
            out ReadOnlySpan<byte> itemData)
        {
            contextId = 0;
            result = default;
            itemData = default;

            // Need at least 4 bytes: Context ID (1), Reserved (1), Result (1), Reserved (1)
            if (Remaining < 4)
                return false;

            var span = _buffer.Slice(_position);
            contextId = span[0];
            // span[1] is reserved
            result = (PresentationContextResult)span[2];
            // span[3] is reserved

            _position += 4;
            itemData = _buffer.Slice(_position);
            return true;
        }

        /// <summary>
        /// Attempts to read an Abstract Syntax sub-item (UID).
        /// </summary>
        /// <param name="uid">The abstract syntax UID.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadAbstractSyntax(out string uid)
        {
            return TryReadUidString(out uid);
        }

        /// <summary>
        /// Attempts to read a Transfer Syntax sub-item (UID).
        /// </summary>
        /// <param name="uid">The transfer syntax UID.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadTransferSyntax(out string uid)
        {
            return TryReadUidString(out uid);
        }

        /// <summary>
        /// Attempts to read a User Information item header.
        /// </summary>
        /// <param name="itemData">The user information sub-items data.</param>
        /// <returns>true if read successfully; always returns true for empty items.</returns>
        public bool TryReadUserInformation(out ReadOnlySpan<byte> itemData)
        {
            itemData = _buffer.Slice(_position);
            return true;
        }

        /// <summary>
        /// Attempts to read a Maximum Length sub-item.
        /// </summary>
        /// <param name="maxLength">The maximum PDU length.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadMaxPduLength(out uint maxLength)
        {
            maxLength = 0;

            if (Remaining < 4)
                return false;

            maxLength = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_position));
            _position += 4;
            return true;
        }

        /// <summary>
        /// Attempts to read an Implementation Class UID sub-item.
        /// </summary>
        /// <param name="uid">The implementation class UID.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadImplementationClassUid(out string uid)
        {
            return TryReadUidString(out uid);
        }

        /// <summary>
        /// Attempts to read an Implementation Version Name sub-item.
        /// </summary>
        /// <param name="name">The implementation version name.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadImplementationVersionName(out string name)
        {
            return TryReadUidString(out name);
        }

        /// <summary>
        /// Attempts to read a Presentation Data Value from P-DATA.
        /// </summary>
        /// <param name="contextId">The presentation context ID.</param>
        /// <param name="isCommand">true if this is command data; false if dataset data.</param>
        /// <param name="isLastFragment">true if this is the last fragment.</param>
        /// <param name="data">The PDV payload data.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        /// <remarks>
        /// PDV format:
        /// - 4 bytes: PDV length (Big-Endian)
        /// - 1 byte: Presentation Context ID
        /// - 1 byte: Message Control Header (bit 0 = command, bit 1 = last)
        /// - N bytes: Data (length - 2)
        /// </remarks>
        public bool TryReadPresentationDataValue(
            out byte contextId,
            out bool isCommand,
            out bool isLastFragment,
            out ReadOnlySpan<byte> data)
        {
            contextId = 0;
            isCommand = false;
            isLastFragment = false;
            data = default;

            // Need at least 6 bytes for PDV item header
            if (Remaining < 6)
                return false;

            var span = _buffer.Slice(_position);

            // PDV item length (includes context ID and message control header)
            uint pdvLength = BinaryPrimitives.ReadUInt32BigEndian(span);

            // Check if we have the complete PDV
            if (Remaining < 4 + (int)pdvLength)
                return false;

            contextId = span[4];
            byte messageControlHeader = span[5];
            isCommand = (messageControlHeader & 0x01) != 0;
            isLastFragment = (messageControlHeader & 0x02) != 0;

            int dataLength = (int)pdvLength - 2; // Subtract context ID and message control header
            if (dataLength > 0)
            {
                data = span.Slice(6, dataLength);
            }

            _position += 4 + (int)pdvLength;
            return true;
        }

        /// <summary>
        /// Skips a specified number of bytes.
        /// </summary>
        /// <param name="count">The number of bytes to skip.</param>
        /// <returns>true if skipped successfully; false if insufficient data.</returns>
        public bool TrySkip(int count)
        {
            if (count > Remaining)
                return false;

            _position += count;
            return true;
        }

        /// <summary>
        /// Reads remaining bytes without advancing position (for length-delimited parsing).
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="data">The read data.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadBytes(int length, out ReadOnlySpan<byte> data)
        {
            data = default;

            if (length > Remaining)
                return false;

            data = _buffer.Slice(_position, length);
            _position += length;
            return true;
        }

        /// <summary>
        /// Reads an AE title (16 bytes, space-padded) and trims trailing spaces.
        /// </summary>
        private static string ReadAeTitle(ReadOnlySpan<byte> span)
        {
            // Find end of meaningful content (trim trailing spaces)
            int length = PduConstants.AETitleLength;
            while (length > 0 && span[length - 1] == ' ')
            {
                length--;
            }

            if (length == 0)
                return string.Empty;

#if NET8_0_OR_GREATER
            return Encoding.ASCII.GetString(span.Slice(0, length));
#else
            return Encoding.ASCII.GetString(span.Slice(0, length).ToArray());
#endif
        }

        /// <summary>
        /// Reads a UID string from the remaining buffer up to the specified length.
        /// </summary>
        private bool TryReadUidString(out string uid)
        {
            // The caller should have established length from variable item header
            // This reads the remaining bytes at current position as a string
            // Typically called after TryReadVariableItem
            uid = string.Empty;

            // UIDs are ASCII, no null terminator padding
            int length = Remaining;
            if (length == 0)
                return true;

            var span = _buffer.Slice(_position, length);
            _position += length;

#if NET8_0_OR_GREATER
            uid = Encoding.ASCII.GetString(span);
#else
            uid = Encoding.ASCII.GetString(span.ToArray());
#endif

            // Trim any null padding (DICOM UI VR padding)
            uid = uid.TrimEnd('\0');
            return true;
        }

        /// <summary>
        /// Reads a UID string of a specific length.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="uid">The resulting UID string.</param>
        /// <returns>true if read successfully; false if insufficient data.</returns>
        public bool TryReadUidString(int length, out string uid)
        {
            uid = string.Empty;

            if (length > Remaining)
                return false;

            if (length == 0)
                return true;

            var span = _buffer.Slice(_position, length);
            _position += length;

#if NET8_0_OR_GREATER
            uid = Encoding.ASCII.GetString(span);
#else
            uid = Encoding.ASCII.GetString(span.ToArray());
#endif

            // Trim any null padding
            uid = uid.TrimEnd('\0');
            return true;
        }
    }
}
