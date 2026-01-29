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

        /// <summary>
        /// Gets a value indicating whether this is a C-CANCEL request.
        /// </summary>
        public bool IsCCancelRequest => CommandFieldValue == Dimse.CommandField.CCancelRequest;

        #region Sub-operation Properties

        /// <summary>
        /// Gets the Move Destination AE Title (0000,0600).
        /// </summary>
        public string? MoveDestination
        {
            get
            {
                var val = _dataset.GetString(DicomTag.MoveDestination);
                return val?.TrimEnd('\0', ' ');
            }
        }

        /// <summary>
        /// Gets the Priority value (0000,0700).
        /// </summary>
        /// <remarks>
        /// Priority: 0=MEDIUM, 1=HIGH, 2=LOW per DICOM PS3.7.
        /// </remarks>
        public ushort Priority => GetUInt16(DicomTag.Priority);

        /// <summary>
        /// Gets the Number of Remaining Sub-operations (0000,1020).
        /// </summary>
        public ushort NumberOfRemainingSuboperations => GetUInt16(DicomTag.NumberOfRemainingSuboperations);

        /// <summary>
        /// Gets the Number of Completed Sub-operations (0000,1021).
        /// </summary>
        public ushort NumberOfCompletedSuboperations => GetUInt16(DicomTag.NumberOfCompletedSuboperations);

        /// <summary>
        /// Gets the Number of Failed Sub-operations (0000,1022).
        /// </summary>
        public ushort NumberOfFailedSuboperations => GetUInt16(DicomTag.NumberOfFailedSuboperations);

        /// <summary>
        /// Gets the Number of Warning Sub-operations (0000,1023).
        /// </summary>
        public ushort NumberOfWarningSuboperations => GetUInt16(DicomTag.NumberOfWarningSuboperations);

        /// <summary>
        /// Gets the sub-operation progress from this command response.
        /// </summary>
        /// <returns>A <see cref="SubOperationProgress"/> containing all sub-operation counts.</returns>
        /// <remarks>
        /// This is useful for C-MOVE and C-GET responses which report progress
        /// of sub-operations in Pending status messages.
        /// </remarks>
        public SubOperationProgress GetSubOperationProgress() => new(
            NumberOfRemainingSuboperations,
            NumberOfCompletedSuboperations,
            NumberOfFailedSuboperations,
            NumberOfWarningSuboperations);

        #endregion

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

        /// <summary>
        /// Creates a C-FIND request command.
        /// </summary>
        /// <param name="messageId">The unique message ID for this request.</param>
        /// <param name="sopClassUid">The SOP Class UID (e.g., PatientRootQueryRetrieveFind).</param>
        /// <param name="priority">The priority (0=MEDIUM, 1=HIGH, 2=LOW).</param>
        /// <returns>A new C-FIND request command.</returns>
        /// <remarks>
        /// The caller must also send an identifier dataset specifying the query keys.
        /// </remarks>
        public static DicomCommand CreateCFindRequest(
            ushort messageId,
            DicomUID sopClassUid,
            ushort priority = 0)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CFindRequest);
            AddUInt16Element(ds, DicomTag.MessageID, messageId);
            AddUInt16Element(ds, DicomTag.Priority, priority);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-FIND response command.
        /// </summary>
        /// <param name="messageIdBeingRespondedTo">The message ID of the request being responded to.</param>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="status">The response status.</param>
        /// <returns>A new C-FIND response command.</returns>
        /// <remarks>
        /// For Pending status, the response includes a matching identifier dataset.
        /// For Success/Failure status, no dataset is present.
        /// </remarks>
        public static DicomCommand CreateCFindResponse(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            DicomStatus status)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CFindResponse);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
            // Pending responses have dataset (identifier), final responses don't
            var hasDataset = status.IsPending;
            AddUInt16Element(ds, DicomTag.CommandDataSetType, hasDataset ? DataSetPresent : NoDataSetPresent);
            AddUInt16Element(ds, DicomTag.Status, status.Code);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-MOVE request command.
        /// </summary>
        /// <param name="messageId">The unique message ID for this request.</param>
        /// <param name="sopClassUid">The SOP Class UID (e.g., PatientRootQueryRetrieveMove).</param>
        /// <param name="moveDestination">The AE title of the destination for the C-STORE sub-operations.</param>
        /// <param name="priority">The priority (0=MEDIUM, 1=HIGH, 2=LOW).</param>
        /// <returns>A new C-MOVE request command.</returns>
        /// <remarks>
        /// The caller must also send an identifier dataset specifying what to retrieve.
        /// </remarks>
        public static DicomCommand CreateCMoveRequest(
            ushort messageId,
            DicomUID sopClassUid,
            string moveDestination,
            ushort priority = 0)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CMoveRequest);
            AddUInt16Element(ds, DicomTag.MessageID, messageId);
            AddUInt16Element(ds, DicomTag.Priority, priority);
            AddAEElement(ds, DicomTag.MoveDestination, moveDestination);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-MOVE response command.
        /// </summary>
        /// <param name="messageIdBeingRespondedTo">The message ID of the request being responded to.</param>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="status">The response status.</param>
        /// <param name="progress">The sub-operation progress counts.</param>
        /// <returns>A new C-MOVE response command.</returns>
        /// <remarks>
        /// Sub-operation counts are included in Pending and final responses.
        /// For failure status with failed sub-operations, a dataset may contain
        /// the list of failed SOP Instance UIDs.
        /// </remarks>
        public static DicomCommand CreateCMoveResponse(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            DicomStatus status,
            SubOperationProgress progress)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CMoveResponse);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
            AddUInt16Element(ds, DicomTag.Status, status.Code);
            AddUInt16Element(ds, DicomTag.NumberOfRemainingSuboperations, progress.Remaining);
            AddUInt16Element(ds, DicomTag.NumberOfCompletedSuboperations, progress.Completed);
            AddUInt16Element(ds, DicomTag.NumberOfFailedSuboperations, progress.Failed);
            AddUInt16Element(ds, DicomTag.NumberOfWarningSuboperations, progress.Warning);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-GET request command.
        /// </summary>
        /// <param name="messageId">The unique message ID for this request.</param>
        /// <param name="sopClassUid">The SOP Class UID (e.g., PatientRootQueryRetrieveGet).</param>
        /// <param name="priority">The priority (0=MEDIUM, 1=HIGH, 2=LOW).</param>
        /// <returns>A new C-GET request command.</returns>
        /// <remarks>
        /// The caller must also send an identifier dataset specifying what to retrieve.
        /// Unlike C-MOVE, C-GET retrieves data over the same association via C-STORE sub-operations.
        /// </remarks>
        public static DicomCommand CreateCGetRequest(
            ushort messageId,
            DicomUID sopClassUid,
            ushort priority = 0)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CGetRequest);
            AddUInt16Element(ds, DicomTag.MessageID, messageId);
            AddUInt16Element(ds, DicomTag.Priority, priority);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-GET response command.
        /// </summary>
        /// <param name="messageIdBeingRespondedTo">The message ID of the request being responded to.</param>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <param name="status">The response status.</param>
        /// <param name="progress">The sub-operation progress counts.</param>
        /// <returns>A new C-GET response command.</returns>
        /// <remarks>
        /// Sub-operation counts are included in Pending and final responses.
        /// For failure status with failed sub-operations, a dataset may contain
        /// the list of failed SOP Instance UIDs.
        /// </remarks>
        public static DicomCommand CreateCGetResponse(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            DicomStatus status,
            SubOperationProgress progress)
        {
            var ds = new DicomDataset();
            AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CGetResponse);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
            AddUInt16Element(ds, DicomTag.Status, status.Code);
            AddUInt16Element(ds, DicomTag.NumberOfRemainingSuboperations, progress.Remaining);
            AddUInt16Element(ds, DicomTag.NumberOfCompletedSuboperations, progress.Completed);
            AddUInt16Element(ds, DicomTag.NumberOfFailedSuboperations, progress.Failed);
            AddUInt16Element(ds, DicomTag.NumberOfWarningSuboperations, progress.Warning);
            return new DicomCommand(ds);
        }

        /// <summary>
        /// Creates a C-CANCEL request command.
        /// </summary>
        /// <param name="messageIdBeingCancelled">The message ID of the operation to cancel.</param>
        /// <returns>A new C-CANCEL request command.</returns>
        /// <remarks>
        /// C-CANCEL is used to request cancellation of an in-progress C-FIND, C-MOVE, or C-GET operation.
        /// The SCP may or may not honor the cancellation request.
        /// </remarks>
        public static DicomCommand CreateCCancelRequest(ushort messageIdBeingCancelled)
        {
            var ds = new DicomDataset();
            AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CCancelRequest);
            AddUInt16Element(ds, DicomTag.MessageIDBeingRespondedTo, messageIdBeingCancelled);
            AddUInt16Element(ds, DicomTag.CommandDataSetType, NoDataSetPresent);
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

        private static void AddAEElement(DicomDataset ds, DicomTag tag, string value)
        {
            // AE is 16 bytes max, space padded to even length
            var len = value.Length;
            if (len % 2 != 0)
                len++;
            var bytes = new byte[len];
            System.Text.Encoding.ASCII.GetBytes(value, 0, value.Length, bytes, 0);
            // Pad with space if needed
            if (len > value.Length)
                bytes[len - 1] = (byte)' ';
            ds.Add(new DicomStringElement(tag, DicomVR.AE, bytes));
        }

        #endregion
    }
}
