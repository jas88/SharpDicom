using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Network.Items;

namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Low-level PDU writer using IBufferWriter&lt;byte&gt; for efficient PDU construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a ref struct for efficient stack-based operation. It writes PDUs directly
    /// to any IBufferWriter&lt;byte&gt; target (ArrayBufferWriter, PipeWriter, etc.).
    /// </para>
    /// <para>
    /// All PDU headers and variable item lengths use Big-Endian byte order per DICOM PS3.8.
    /// </para>
    /// </remarks>
    public ref struct PduWriter
    {
        private readonly IBufferWriter<byte> _writer;

        /// <summary>
        /// The DICOM Application Context UID (always "1.2.840.10008.3.1.1.1").
        /// </summary>
        private const string ApplicationContextUidString = "1.2.840.10008.3.1.1.1";
        private static ReadOnlySpan<byte> ApplicationContextUid => new byte[]
        {
            (byte)'1', (byte)'.', (byte)'2', (byte)'.', (byte)'8', (byte)'4', (byte)'0', (byte)'.',
            (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)'8', (byte)'.', (byte)'3', (byte)'.',
            (byte)'1', (byte)'.', (byte)'1', (byte)'.', (byte)'1'
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="PduWriter"/> struct.
        /// </summary>
        /// <param name="writer">The buffer writer to write to.</param>
        public PduWriter(IBufferWriter<byte> writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        /// Writes an A-ASSOCIATE-RQ PDU.
        /// </summary>
        /// <param name="calledAE">The Called AE Title (max 16 characters).</param>
        /// <param name="callingAE">The Calling AE Title (max 16 characters).</param>
        /// <param name="contexts">The presentation contexts to propose.</param>
        /// <param name="userInfo">The user information sub-items.</param>
        public void WriteAssociateRequest(
            string calledAE,
            string callingAE,
            IReadOnlyList<PresentationContext> contexts,
            UserInformation userInfo)
        {
            WriteAssociate(PduType.AssociateRequest, calledAE, callingAE, contexts, userInfo, isRequest: true);
        }

        /// <summary>
        /// Writes an A-ASSOCIATE-AC PDU.
        /// </summary>
        /// <param name="calledAE">The Called AE Title (max 16 characters).</param>
        /// <param name="callingAE">The Calling AE Title (max 16 characters).</param>
        /// <param name="contexts">The presentation contexts (with results).</param>
        /// <param name="userInfo">The user information sub-items.</param>
        public void WriteAssociateAccept(
            string calledAE,
            string callingAE,
            IReadOnlyList<PresentationContext> contexts,
            UserInformation userInfo)
        {
            WriteAssociate(PduType.AssociateAccept, calledAE, callingAE, contexts, userInfo, isRequest: false);
        }

        /// <summary>
        /// Shared implementation for A-ASSOCIATE-RQ and A-ASSOCIATE-AC writing.
        /// </summary>
        private void WriteAssociate(
            PduType type,
            string calledAE,
            string callingAE,
            IReadOnlyList<PresentationContext> contexts,
            UserInformation userInfo,
            bool isRequest)
        {
            // Calculate variable items length first
            int variableItemsLength = CalculateVariableItemsLength(contexts, userInfo, isRequest);

            // Total PDU length = fixed fields (68) + variable items
            uint pduLength = (uint)(PduConstants.AssociateFixedFieldsLength + variableItemsLength);

            // Write PDU header (6 bytes)
            WritePduHeader(type, pduLength);

            // Write fixed fields (68 bytes)
            var fixedSpan = _writer.GetSpan(PduConstants.AssociateFixedFieldsLength);

            // Protocol version (2 bytes, Big-Endian)
            BinaryPrimitives.WriteUInt16BigEndian(fixedSpan, PduConstants.ProtocolVersion);

            // Reserved (2 bytes)
            fixedSpan[2] = 0x00;
            fixedSpan[3] = 0x00;

            // Called AE Title (16 bytes, space-padded)
            WriteAeTitle(fixedSpan.Slice(4, PduConstants.AETitleLength), calledAE);

            // Calling AE Title (16 bytes, space-padded)
            WriteAeTitle(fixedSpan.Slice(20, PduConstants.AETitleLength), callingAE);

            // Reserved (32 bytes)
            fixedSpan.Slice(36, 32).Clear();

            _writer.Advance(PduConstants.AssociateFixedFieldsLength);

            // Write variable items
            WriteApplicationContext();

            foreach (var context in contexts)
            {
                if (isRequest)
                    WritePresentationContextRequest(context);
                else
                    WritePresentationContextAccept(context);
            }

            WriteUserInformation(userInfo);
        }

        /// <summary>
        /// Writes an A-ASSOCIATE-RJ PDU.
        /// </summary>
        /// <param name="result">The rejection result (permanent or transient).</param>
        /// <param name="source">The rejection source.</param>
        /// <param name="reason">The rejection reason.</param>
        public void WriteAssociateReject(
            RejectResult result,
            RejectSource source,
            RejectReason reason)
        {
            // A-ASSOCIATE-RJ: Header (6) + Body (4) = 10 bytes
            WritePduHeader(PduType.AssociateReject, 4);

            var span = _writer.GetSpan(4);
            span[0] = 0x00; // Reserved
            span[1] = (byte)result;
            span[2] = (byte)source;
            span[3] = (byte)reason;

            _writer.Advance(4);
        }

        /// <summary>
        /// Writes a P-DATA-TF PDU.
        /// </summary>
        /// <param name="pdvs">The presentation data values to include.</param>
        public void WritePData(IReadOnlyList<PresentationDataValue> pdvs)
        {
            // Calculate total PDV data length
            uint totalLength = 0;
            foreach (var pdv in pdvs)
            {
                // Each PDV: Length (4) + Context ID (1) + Message Control Header (1) + Data
                totalLength += (uint)(4 + 2 + pdv.Data.Length);
            }

            // Write PDU header
            WritePduHeader(PduType.PDataTransfer, totalLength);

            // Write each PDV
            foreach (var pdv in pdvs)
            {
                WritePresentationDataValue(pdv);
            }
        }

        /// <summary>
        /// Writes an A-RELEASE-RQ PDU.
        /// </summary>
        public void WriteReleaseRequest()
        {
            // A-RELEASE-RQ: Header (6) + Reserved (4) = 10 bytes
            WritePduHeader(PduType.ReleaseRequest, 4);

            var span = _writer.GetSpan(4);
            span.Clear(); // Reserved bytes

            _writer.Advance(4);
        }

        /// <summary>
        /// Writes an A-RELEASE-RP PDU.
        /// </summary>
        public void WriteReleaseResponse()
        {
            // A-RELEASE-RP: Header (6) + Reserved (4) = 10 bytes
            WritePduHeader(PduType.ReleaseResponse, 4);

            var span = _writer.GetSpan(4);
            span.Clear(); // Reserved bytes

            _writer.Advance(4);
        }

        /// <summary>
        /// Writes an A-ABORT PDU.
        /// </summary>
        /// <param name="source">The abort source.</param>
        /// <param name="reason">The abort reason (significant only when source is ServiceProvider).</param>
        public void WriteAbort(AbortSource source, AbortReason reason)
        {
            // A-ABORT: Header (6) + Body (4) = 10 bytes
            WritePduHeader(PduType.Abort, 4);

            var span = _writer.GetSpan(4);
            span[0] = 0x00; // Reserved
            span[1] = 0x00; // Reserved
            span[2] = (byte)source;
            span[3] = (byte)reason;

            _writer.Advance(4);
        }

        /// <summary>
        /// Writes a PDU header (type, reserved, length).
        /// </summary>
        private void WritePduHeader(PduType type, uint length)
        {
            var span = _writer.GetSpan(PduConstants.HeaderLength);
            span[0] = (byte)type;
            span[1] = 0x00; // Reserved
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(2), length);
            _writer.Advance(PduConstants.HeaderLength);
        }

        /// <summary>
        /// Writes an AE title to a span, padding with spaces to 16 bytes.
        /// </summary>
        private static void WriteAeTitle(Span<byte> span, string aeTitle)
        {
            // Fill with spaces
            span.Fill((byte)' ');

            if (string.IsNullOrEmpty(aeTitle))
                return;

            // Copy AE title bytes (ASCII)
            int copyLength = Math.Min(aeTitle.Length, PduConstants.AETitleLength);
            for (int i = 0; i < copyLength; i++)
            {
                span[i] = (byte)aeTitle[i];
            }
        }

        /// <summary>
        /// Writes a variable item header (type, reserved, length).
        /// </summary>
        private void WriteVariableItemHeader(ItemType type, ushort length)
        {
            var span = _writer.GetSpan(4);
            span[0] = (byte)type;
            span[1] = 0x00; // Reserved
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), length);
            _writer.Advance(4);
        }

        /// <summary>
        /// Writes the Application Context item.
        /// </summary>
        private void WriteApplicationContext()
        {
            // Item type (1) + Reserved (1) + Length (2) + UID bytes
            WriteVariableItemHeader(ItemType.ApplicationContext, (ushort)ApplicationContextUid.Length);

            var span = _writer.GetSpan(ApplicationContextUid.Length);
            ApplicationContextUid.CopyTo(span);
            _writer.Advance(ApplicationContextUid.Length);
        }

        /// <summary>
        /// Writes a Presentation Context item for A-ASSOCIATE-RQ.
        /// </summary>
        private void WritePresentationContextRequest(PresentationContext context)
        {
            // Calculate item length:
            // Context ID (1) + Reserved (3) + Abstract Syntax item + Transfer Syntax items
            int abstractSyntaxLength = context.AbstractSyntax.ToString().Length;
            int itemLength = 4; // Context ID + Reserved
            itemLength += 4 + abstractSyntaxLength; // Abstract Syntax item

            foreach (var ts in context.TransferSyntaxes)
            {
                itemLength += 4 + ts.UID.ToString().Length; // Transfer Syntax item
            }

            WriteVariableItemHeader(ItemType.PresentationContextRequest, (ushort)itemLength);

            // Write context ID and reserved bytes
            var span = _writer.GetSpan(4);
            span[0] = context.Id;
            span[1] = 0x00; // Reserved
            span[2] = 0x00; // Reserved
            span[3] = 0x00; // Reserved
            _writer.Advance(4);

            // Write Abstract Syntax sub-item
            WriteAbstractSyntax(context.AbstractSyntax);

            // Write Transfer Syntax sub-items
            foreach (var ts in context.TransferSyntaxes)
            {
                WriteTransferSyntax(ts.UID);
            }
        }

        /// <summary>
        /// Writes a Presentation Context item for A-ASSOCIATE-AC.
        /// </summary>
        private void WritePresentationContextAccept(PresentationContext context)
        {
            // Calculate item length:
            // Context ID (1) + Reserved (1) + Result (1) + Reserved (1) + Transfer Syntax item
            int itemLength = 4; // Fixed fields

            if (context.Result == PresentationContextResult.Acceptance && context.AcceptedTransferSyntax.HasValue)
            {
                itemLength += 4 + context.AcceptedTransferSyntax.Value.UID.ToString().Length;
            }

            WriteVariableItemHeader(ItemType.PresentationContextAccept, (ushort)itemLength);

            // Write context ID, result, and reserved bytes
            var span = _writer.GetSpan(4);
            span[0] = context.Id;
            span[1] = 0x00; // Reserved
            span[2] = (byte)(context.Result ?? PresentationContextResult.NoReason);
            span[3] = 0x00; // Reserved
            _writer.Advance(4);

            // Write Transfer Syntax sub-item if accepted
            if (context.Result == PresentationContextResult.Acceptance && context.AcceptedTransferSyntax.HasValue)
            {
                WriteTransferSyntax(context.AcceptedTransferSyntax.Value.UID);
            }
        }

        /// <summary>
        /// Writes an Abstract Syntax sub-item.
        /// </summary>
        private void WriteAbstractSyntax(DicomUID uid)
        {
            var uidString = uid.ToString();
            WriteVariableItemHeader(ItemType.AbstractSyntax, (ushort)uidString.Length);
            WriteUidString(uidString);
        }

        /// <summary>
        /// Writes a Transfer Syntax sub-item.
        /// </summary>
        private void WriteTransferSyntax(DicomUID uid)
        {
            var uidString = uid.ToString();
            WriteVariableItemHeader(ItemType.TransferSyntax, (ushort)uidString.Length);
            WriteUidString(uidString);
        }

        /// <summary>
        /// Writes the User Information item.
        /// </summary>
        private void WriteUserInformation(UserInformation info)
        {
            // Calculate sub-items length
            int subItemsLength = 0;

            // Max PDU Length: Header (4) + Value (4) = 8
            subItemsLength += 8;

            // Implementation Class UID: Header (4) + UID length
            subItemsLength += 4 + info.ImplementationClassUid.Length;

            // Implementation Version Name (optional): Header (4) + name length
            if (!string.IsNullOrEmpty(info.ImplementationVersionName))
            {
                subItemsLength += 4 + info.ImplementationVersionName!.Length;
            }

            WriteVariableItemHeader(ItemType.UserInformation, (ushort)subItemsLength);

            // Write Max PDU Length sub-item
            WriteMaxPduLength(info.MaxPduLength);

            // Write Implementation Class UID sub-item
            WriteImplementationClassUid(info.ImplementationClassUid);

            // Write Implementation Version Name sub-item (optional)
            if (!string.IsNullOrEmpty(info.ImplementationVersionName))
            {
                WriteImplementationVersionName(info.ImplementationVersionName!);
            }
        }

        /// <summary>
        /// Writes a Maximum Length sub-item.
        /// </summary>
        private void WriteMaxPduLength(uint maxLength)
        {
            WriteVariableItemHeader(ItemType.MaximumLength, 4);

            var span = _writer.GetSpan(4);
            BinaryPrimitives.WriteUInt32BigEndian(span, maxLength);
            _writer.Advance(4);
        }

        /// <summary>
        /// Writes an Implementation Class UID sub-item.
        /// </summary>
        private void WriteImplementationClassUid(string uid)
        {
            WriteVariableItemHeader(ItemType.ImplementationClassUid, (ushort)uid.Length);
            WriteUidString(uid);
        }

        /// <summary>
        /// Writes an Implementation Version Name sub-item.
        /// </summary>
        private void WriteImplementationVersionName(string name)
        {
            WriteVariableItemHeader(ItemType.ImplementationVersionName, (ushort)name.Length);

            var span = _writer.GetSpan(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                span[i] = (byte)name[i];
            }
            _writer.Advance(name.Length);
        }

        /// <summary>
        /// Writes a UID string (ASCII bytes).
        /// </summary>
        private void WriteUidString(string uid)
        {
            var span = _writer.GetSpan(uid.Length);
            for (int i = 0; i < uid.Length; i++)
            {
                span[i] = (byte)uid[i];
            }
            _writer.Advance(uid.Length);
        }

        /// <summary>
        /// Writes a single Presentation Data Value.
        /// </summary>
        private void WritePresentationDataValue(PresentationDataValue pdv)
        {
            // PDV length = Context ID (1) + Message Control Header (1) + Data length
            uint pdvLength = (uint)(2 + pdv.Data.Length);

            var headerSpan = _writer.GetSpan(6);
            BinaryPrimitives.WriteUInt32BigEndian(headerSpan, pdvLength);
            headerSpan[4] = pdv.PresentationContextId;
            headerSpan[5] = pdv.ToMessageControlHeader();
            _writer.Advance(6);

            // Write data
            if (!pdv.Data.IsEmpty)
            {
                var dataSpan = _writer.GetSpan(pdv.Data.Length);
                pdv.Data.Span.CopyTo(dataSpan);
                _writer.Advance(pdv.Data.Length);
            }
        }

        /// <summary>
        /// Calculates the total length of variable items in an associate PDU.
        /// </summary>
        private static int CalculateVariableItemsLength(
            IReadOnlyList<PresentationContext> contexts,
            UserInformation userInfo,
            bool isRequest)
        {
            int length = 0;

            // Application Context: Header (4) + UID (21)
            length += 4 + ApplicationContextUid.Length;

            // Presentation Contexts
            foreach (var context in contexts)
            {
                if (isRequest)
                {
                    // Header (4) + Context ID/Reserved (4) + Abstract Syntax + Transfer Syntaxes
                    length += 4 + 4;
                    length += 4 + context.AbstractSyntax.ToString().Length;
                    foreach (var ts in context.TransferSyntaxes)
                    {
                        length += 4 + ts.UID.ToString().Length;
                    }
                }
                else
                {
                    // Header (4) + Context ID/Result/Reserved (4) + Transfer Syntax (if accepted)
                    length += 4 + 4;
                    if (context.Result == PresentationContextResult.Acceptance && context.AcceptedTransferSyntax.HasValue)
                    {
                        length += 4 + context.AcceptedTransferSyntax.Value.UID.ToString().Length;
                    }
                }
            }

            // User Information
            // Header (4) + Max PDU Length (8) + Implementation Class UID (4 + len) + Implementation Version Name (optional)
            int userInfoLength = 8 + 4 + userInfo.ImplementationClassUid.Length;
            if (!string.IsNullOrEmpty(userInfo.ImplementationVersionName))
            {
                userInfoLength += 4 + userInfo.ImplementationVersionName!.Length;
            }
            length += 4 + userInfoLength;

            return length;
        }
    }
}
