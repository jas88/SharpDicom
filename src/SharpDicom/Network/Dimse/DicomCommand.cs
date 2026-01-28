using System;
using System.Buffers.Binary;
using SharpDicom.Data;
using SharpDicom.Network;

namespace SharpDicom.Network.Dimse
{
    /// <summary>
    /// Represents a DICOM DIMSE command dataset (Group 0000 elements).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DICOM command is a set of Group 0000 elements that precede data in P-DATA PDUs.
    /// Commands control the DIMSE operations (C-STORE, C-FIND, C-MOVE, C-GET, C-ECHO, etc.).
    /// </para>
    /// <para>
    /// This class wraps a <see cref="DicomDataset"/> containing command elements and provides
    /// typed access to the common command fields.
    /// </para>
    /// </remarks>
    public sealed class DicomCommand
    {
        /// <summary>
        /// Value indicating no dataset is present in the message (0x0101).
        /// </summary>
        public const ushort NoDataSetPresent = 0x0101;

        /// <summary>
        /// Value indicating a dataset is present in the message (any value other than 0x0101).
        /// </summary>
        public const ushort DataSetPresent = 0x0102;

        private readonly DicomDataset _dataset;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomCommand"/> class.
        /// </summary>
        /// <param name="dataset">The command dataset containing Group 0000 elements.</param>
        /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
        public DicomCommand(DicomDataset dataset)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
#endif
            _dataset = dataset;
        }

        /// <summary>
        /// Gets the underlying command dataset.
        /// </summary>
        public DicomDataset Dataset => _dataset;

        /// <summary>
        /// Gets the Affected SOP Class UID (0000,0002).
        /// </summary>
        public DicomUID AffectedSOPClassUID
        {
            get
            {
                var uid = _dataset.GetString(DicomTag.AffectedSOPClassUID);
                return uid != null ? new DicomUID(uid.TrimEnd('\0', ' ')) : default;
            }
        }

        /// <summary>
        /// Gets the Requested SOP Class UID (0000,0003).
        /// </summary>
        public DicomUID RequestedSOPClassUID
        {
            get
            {
                var uid = _dataset.GetString(DicomTag.RequestedSOPClassUID);
                return uid != null ? new DicomUID(uid.TrimEnd('\0', ' ')) : default;
            }
        }

        /// <summary>
        /// Gets the Command Field (0000,0100).
        /// </summary>
        /// <remarks>
        /// Identifies the DIMSE command type. See <see cref="Dimse.CommandField"/> for constants.
        /// </remarks>
        public ushort CommandFieldValue => GetUInt16(DicomTag.CommandField);

        /// <summary>
        /// Gets the Message ID (0000,0110).
        /// </summary>
        public ushort MessageID => GetUInt16(DicomTag.MessageID);

        /// <summary>
        /// Gets the Message ID Being Responded To (0000,0120).
        /// </summary>
        public ushort MessageIDBeingRespondedTo => GetUInt16(DicomTag.MessageIDBeingRespondedTo);

        /// <summary>
        /// Gets the Command Data Set Type (0000,0800).
        /// </summary>
        /// <remarks>
        /// 0x0101 indicates no dataset present. Any other value indicates a dataset follows.
        /// </remarks>
        public ushort CommandDataSetType => GetUInt16(DicomTag.CommandDataSetType);

        /// <summary>
        /// Gets the DIMSE Status (0000,0900).
        /// </summary>
        public DicomStatus Status => new DicomStatus(GetUInt16(DicomTag.Status));

        /// <summary>
        /// Gets the Affected SOP Instance UID (0000,1000).
        /// </summary>
        public DicomUID AffectedSOPInstanceUID
        {
            get
            {
                var uid = _dataset.GetString(DicomTag.AffectedSOPInstanceUID);
                return uid != null ? new DicomUID(uid.TrimEnd('\0', ' ')) : default;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a dataset is present after this command.
        /// </summary>
        /// <remarks>
        /// Returns true if CommandDataSetType is anything other than 0x0101.
        /// </remarks>
        public bool HasDataset => CommandDataSetType != NoDataSetPresent;

        /// <summary>
        /// Gets a value indicating whether this command is a request.
        /// </summary>
        public bool IsRequest => CommandField.IsRequest(CommandFieldValue);

        /// <summary>
        /// Gets a value indicating whether this command is a response.
        /// </summary>
        public bool IsResponse => CommandField.IsResponse(CommandFieldValue);

        /// <summary>
        /// Gets a value indicating whether this is a C-ECHO request.
        /// </summary>
        public bool IsCEchoRequest => CommandFieldValue == Dimse.CommandField.CEchoRequest;

        /// <summary>
        /// Gets a value indicating whether this is a C-ECHO response.
        /// </summary>
        public bool IsCEchoResponse => CommandFieldValue == Dimse.CommandField.CEchoResponse;

        /// <summary>
        /// Gets a value indicating whether this is a C-STORE request.
        /// </summary>
        public bool IsCStoreRequest => CommandFieldValue == Dimse.CommandField.CStoreRequest;

        /// <summary>
        /// Gets a value indicating whether this is a C-STORE response.
        /// </summary>
        public bool IsCStoreResponse => CommandFieldValue == Dimse.CommandField.CStoreResponse;

        /// <summary>
        /// Gets a value indicating whether this is a C-FIND request.
        /// </summary>
        public bool IsCFindRequest => CommandFieldValue == Dimse.CommandField.CFindRequest;

        /// <summary>
        /// Gets a value indicating whether this is a C-FIND response.
        /// </summary>
        public bool IsCFindResponse => CommandFieldValue == Dimse.CommandField.CFindResponse;

        /// <summary>
        /// Gets a value indicating whether this is a C-MOVE request.
        /// </summary>
        public bool IsCMoveRequest => CommandFieldValue == Dimse.CommandField.CMoveRequest;

        /// <summary>
        /// Gets a value indicating whether this is a C-MOVE response.
        /// </summary>
        public bool IsCMoveResponse => CommandFieldValue == Dimse.CommandField.CMoveResponse;

        /// <summary>
        /// Gets a value indicating whether this is a C-GET request.
        /// </summary>
        public bool IsCGetRequest => CommandFieldValue == Dimse.CommandField.CGetRequest;

        /// <summary>
        /// Gets a value indicating whether this is a C-GET response.
        /// </summary>
        public bool IsCGetResponse => CommandFieldValue == Dimse.CommandField.CGetResponse;

        #region Factory Methods

        /// <summary>
        /// Creates a C-ECHO request command.
        /// </summary>
        /// <param name="messageId">The unique message ID for this request.</param>
        /// <returns>A new C-ECHO request command.</returns>
        public static DicomCommand CreateCEchoRequest(ushort messageId)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, DicomUID.Verification);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CEchoRequest);
            AddUInt16Element(ds, DicomTag.MessageID, messageId);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-ECHO response command.
        /// </summary>
        /// <param name="messageIdBeingRespondedTo">The message ID of the request being responded to.</param>
        /// <param name="status">The response status.</param>
        /// <returns>A new C-ECHO response command.</returns>
        public static DicomCommand CreateCEchoResponse(ushort messageIdBeingRespondedTo, DicomStatus status)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, DicomUID.Verification);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CEchoResponse);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
            AddUInt16Element(ds, DicomTag.Status, status.Code);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-STORE request command.
        /// </summary>
        /// <param name="messageId">The unique message ID for this request.</param>
        /// <param name="sopClassUid">The SOP Class UID of the object being stored.</param>
        /// <param name="sopInstanceUid">The SOP Instance UID of the object being stored.</param>
        /// <param name="priority">The priority (0=MEDIUM, 1=HIGH, 2=LOW).</param>
        /// <returns>A new C-STORE request command.</returns>
        public static DicomCommand CreateCStoreRequest(
            ushort messageId,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort priority = 0)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CStoreRequest);
            AddUInt16Element(ds, DicomTag.MessageID, messageId);
            AddUInt16Element(ds, DicomTag.Priority, priority);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
            AddUidElement(ds, DicomTag.AffectedSOPInstanceUID, sopInstanceUid);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-STORE response command.
        /// </summary>
        /// <param name="messageIdBeingRespondedTo">The message ID of the request being responded to.</param>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="sopInstanceUid">The SOP Instance UID.</param>
        /// <param name="status">The response status.</param>
        /// <returns>A new C-STORE response command.</returns>
        public static DicomCommand CreateCStoreResponse(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            DicomStatus status)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CStoreResponse);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
            AddUInt16Element(ds, DicomTag.Status, status.Code);
            AddUidElement(ds, DicomTag.AffectedSOPInstanceUID, sopInstanceUid);
            return new DicomCommand(ds);
        }

        #endregion

        #region Helper Methods

        private ushort GetUInt16(DicomTag tag)
        {
            var element = _dataset[tag];
            if (element is DicomNumericElement ne)
            {
                return ne.GetUInt16() ?? 0;
            }
            return 0;
        }

        private static void AddUInt16Element(DicomDataset ds, DicomTag tag, ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            ds.Add(new DicomNumericElement(tag, DicomVR.US, bytes));
        }

        private static void AddUidElement(DicomDataset ds, DicomTag tag, DicomUID uid)
        {
            var uidStr = uid.ToString();
            // Pad to even length with null byte if needed
            byte[] bytes;
            if (uidStr.Length % 2 != 0)
            {
                bytes = new byte[uidStr.Length + 1];
                System.Text.Encoding.ASCII.GetBytes(uidStr, 0, uidStr.Length, bytes, 0);
                bytes[bytes.Length - 1] = 0; // Null padding
            }
            else
            {
                bytes = System.Text.Encoding.ASCII.GetBytes(uidStr);
            }
            ds.Add(new DicomStringElement(tag, DicomVR.UI, bytes));
        }

        #endregion
    }
}
