using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Generic N-Service SCU for sending any normalized DIMSE-N operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NServiceScu provides methods for all 6 N-Service operations: N-CREATE, N-SET,
    /// N-GET, N-DELETE, N-ACTION, and N-EVENT-REPORT. Each method builds the appropriate
    /// DIMSE command, sends it with an optional dataset, receives the response, and
    /// returns an <see cref="NServiceResponse"/>.
    /// </para>
    /// <para>
    /// Example usage for MPPS N-CREATE:
    /// <code>
    /// var client = new DicomClient(options);
    /// await client.ConnectAsync(contexts, ct);
    ///
    /// var nScu = new NServiceScu(client);
    /// var response = await nScu.NCreateAsync(
    ///     DicomUID.ModalityPerformedProcedureStep,
    ///     attributeList,
    ///     mppsInstanceUid,
    ///     ct);
    ///
    /// if (response.Status.IsSuccess)
    ///     Console.WriteLine($"MPPS created: {response.AffectedSOPInstanceUID}");
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class NServiceScu
    {
        private readonly DicomClient _client;
        private int _messageIdCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="NServiceScu"/> class.
        /// </summary>
        /// <param name="client">Connected DicomClient with active association.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public NServiceScu(DicomClient client)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(client);
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
#endif
            _client = client;
        }

        /// <summary>
        /// Gets the next unique message ID.
        /// </summary>
        private ushort NextMessageId() => (ushort)Interlocked.Increment(ref _messageIdCounter);

        /// <summary>
        /// Sends an N-CREATE request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Affected SOP Class UID.</param>
        /// <param name="attributeList">Optional attribute list dataset with initial values.</param>
        /// <param name="sopInstanceUid">Optional SOP Instance UID. If null, the SCP assigns one.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status, optional dataset, and Affected SOP Instance UID.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NCreateAsync(
            DicomUID sopClassUid,
            DicomDataset? attributeList,
            DicomUID? sopInstanceUid = null,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            var command = DicomCommand.CreateNCreateRequest(messageId, sopClassUid, sopInstanceUid);
            await _client.SendDimseRequestAsync(context.Id, command, attributeList, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-CREATE");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Sends an N-SET request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="modificationList">Dataset containing the attribute values to set.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional modified attribute values.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NSetAsync(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            DicomDataset modificationList,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            var command = DicomCommand.CreateNSetRequest(messageId, sopClassUid, sopInstanceUid);
            await _client.SendDimseRequestAsync(context.Id, command, modificationList, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-SET");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Sends an N-GET request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="attributeIdentifierList">Optional list of tags to retrieve. If null, retrieves all attributes.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and attribute values dataset.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NGetAsync(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            DicomTag[]? attributeIdentifierList = null,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            // N-GET command has no dataset; attribute identifier list is encoded in the command
            var command = DicomCommand.CreateNGetRequest(messageId, sopClassUid, sopInstanceUid);

            // Per PS3.7 Section 10.3.1, add Attribute Identifier List (0000,1005) to command if specified
            if (attributeIdentifierList != null && attributeIdentifierList.Length > 0)
            {
                var bytes = new byte[attributeIdentifierList.Length * 4];
                for (int i = 0; i < attributeIdentifierList.Length; i++)
                {
                    var tag = attributeIdentifierList[i];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(i * 4), tag.Group);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(i * 4 + 2), tag.Element);
                }
                command.Dataset.AddOrUpdate(
                    new DicomBinaryElement(
                        DicomTag.AttributeIdentifierList,
                        DicomVR.AT,
                        bytes));
            }

            await _client.SendDimseRequestAsync(context.Id, command, null, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-GET");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Sends an N-DELETE request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NDeleteAsync(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            var command = DicomCommand.CreateNDeleteRequest(messageId, sopClassUid, sopInstanceUid);
            await _client.SendDimseRequestAsync(context.Id, command, null, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-DELETE");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Sends an N-ACTION request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Requested SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Requested SOP Instance UID.</param>
        /// <param name="actionTypeId">The Action Type ID identifying the action to perform.</param>
        /// <param name="actionInformation">Optional dataset containing action parameters.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional action reply dataset.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NActionAsync(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort actionTypeId,
            DicomDataset? actionInformation = null,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            var command = DicomCommand.CreateNActionRequest(messageId, sopClassUid, sopInstanceUid, actionTypeId);
            await _client.SendDimseRequestAsync(context.Id, command, actionInformation, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-ACTION");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Sends an N-EVENT-REPORT request to the remote AE.
        /// </summary>
        /// <param name="sopClassUid">The Affected SOP Class UID.</param>
        /// <param name="sopInstanceUid">The Affected SOP Instance UID.</param>
        /// <param name="eventTypeId">The Event Type ID identifying the type of event.</param>
        /// <param name="eventInformation">Optional dataset containing event details.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional event reply dataset.</returns>
        /// <exception cref="DicomNetworkException">Thrown when the SOP Class is not negotiated or unexpected response received.</exception>
        public async ValueTask<NServiceResponse> NEventReportAsync(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort eventTypeId,
            DicomDataset? eventInformation = null,
            CancellationToken ct = default)
        {
            var context = GetRequiredContext(sopClassUid);
            var messageId = NextMessageId();

            var command = DicomCommand.CreateNEventReportRequest(messageId, sopClassUid, sopInstanceUid, eventTypeId);
            await _client.SendDimseRequestAsync(context.Id, command, eventInformation, ct).ConfigureAwait(false);

            var (responseCmd, responseDataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

            ValidateResponse(responseCmd, messageId, "N-EVENT-REPORT");

            return new NServiceResponse(
                responseCmd.Status,
                responseDataset,
                responseCmd.AffectedSOPInstanceUID.ToString().Length > 0
                    ? responseCmd.AffectedSOPInstanceUID
                    : (DicomUID?)null);
        }

        /// <summary>
        /// Gets the accepted presentation context for the specified SOP Class, throwing if not found.
        /// </summary>
        private PresentationContext GetRequiredContext(DicomUID sopClassUid)
        {
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                throw new DicomNetworkException(
                    $"N-Service SOP Class {sopClassUid} not negotiated. " +
                    "Ensure the association includes the appropriate presentation context.");
            }
            return context;
        }

        /// <summary>
        /// Validates the DIMSE response command.
        /// </summary>
        private static void ValidateResponse(DicomCommand responseCmd, ushort messageId, string operationName)
        {
            if (responseCmd.MessageIDBeingRespondedTo != messageId)
            {
                throw new DicomNetworkException(
                    $"{operationName} message ID mismatch: expected {messageId}, got {responseCmd.MessageIDBeingRespondedTo}");
            }
        }
    }
}
