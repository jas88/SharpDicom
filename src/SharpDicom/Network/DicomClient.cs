using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Network.Association;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;
using SharpDicom.IO;

#if NETSTANDARD2_0
using BufferWriter = SharpDicom.Internal.ArrayBufferWriterPolyfill<byte>;
#else
using BufferWriter = System.Buffers.ArrayBufferWriter<byte>;
#endif

namespace SharpDicom.Network
{
    /// <summary>
    /// DICOM SCU (Service Class User) client for connecting to remote DICOM AEs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DicomClient manages TCP connection, association negotiation, and DIMSE
    /// message exchange with a remote DICOM application entity.
    /// </para>
    /// <para>
    /// This class implements <see cref="IAsyncDisposable"/> and should be disposed
    /// after use to properly release the association and close the connection.
    /// </para>
    /// </remarks>
    public sealed class DicomClient : IAsyncDisposable
    {
        private readonly DicomClientOptions _options;
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private DicomAssociation? _association;
        private ushort _messageId;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of <see cref="DicomClient"/>.
        /// </summary>
        /// <param name="options">The client configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        /// <exception cref="ArgumentException">Thrown when options are invalid.</exception>
        public DicomClient(DicomClientOptions options)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options == null)
                throw new ArgumentNullException(nameof(options));
#endif
            options.Validate();
            _options = options;
        }

        /// <summary>
        /// Gets a value indicating whether the association is established.
        /// </summary>
        public bool IsConnected => _association?.IsEstablished ?? false;

        /// <summary>
        /// Gets the current association, or null if not connected.
        /// </summary>
        public DicomAssociation? Association => _association;

        /// <summary>
        /// Connect to the remote AE and establish association.
        /// </summary>
        /// <param name="presentationContexts">The presentation contexts to propose.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The established association.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the client has been disposed.</exception>
        /// <exception cref="TimeoutException">Thrown if connection times out.</exception>
        /// <exception cref="DicomAssociationException">Thrown if association negotiation fails.</exception>
        public async ValueTask<DicomAssociation> ConnectAsync(
            IReadOnlyList<PresentationContext> presentationContexts,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            _tcp = new TcpClient();

            // Connect with timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_options.ConnectionTimeout);

            try
            {
#if NET6_0_OR_GREATER
                await _tcp.ConnectAsync(_options.Host, _options.Port, connectCts.Token).ConfigureAwait(false);
#else
                var connectTask = _tcp.ConnectAsync(_options.Host, _options.Port);
                var timeoutTask = Task.Delay(_options.ConnectionTimeout, connectCts.Token);
                var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completed == timeoutTask)
                {
                    _tcp.Close();
                    throw new TimeoutException($"Connection to {_options.Host}:{_options.Port} timed out.");
                }
                await connectTask.ConfigureAwait(false);
#endif
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Connection to {_options.Host}:{_options.Port} timed out after {_options.ConnectionTimeout}.");
            }

            _stream = _tcp.GetStream();

            // Create association options
            var assocOptions = new AssociationOptions(
                _options.CalledAE,
                _options.CallingAE,
                presentationContexts,
                UserInformation.Default.WithMaxPduLength(_options.MaxPduLength),
                _options.AssociationTimeout,
                _options.DimseTimeout);

            _association = new DicomAssociation(assocOptions);
            // SCU state machine: Idle -> AwaitingTransportConnectionOpen -> AwaitingAssociateResponse
            _association.ProcessEvent(AssociationEvent.AAssociateRequest);
            _association.ProcessEvent(AssociationEvent.TransportConnectionConfirm);

            // Send A-ASSOCIATE-RQ
            await SendAssociateRequestAsync(presentationContexts, ct).ConfigureAwait(false);

            // Receive A-ASSOCIATE-AC/RJ
            await ReceiveAssociateResponseAsync(ct).ConfigureAwait(false);

            if (!_association.IsEstablished)
            {
                throw new DicomAssociationException("Association was not established.");
            }

            return _association;
        }

        /// <summary>
        /// Performs a C-ECHO operation to verify connectivity.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The DIMSE status from the response.</returns>
        /// <exception cref="DicomAssociationException">Thrown if not connected.</exception>
        public async ValueTask<DicomStatus> CEchoAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (_association == null || !_association.IsEstablished)
                throw new DicomAssociationException("Not connected. Call ConnectAsync first.");

            // Find Verification SOP Class presentation context
            var context = _association.AcceptedContexts?.FirstOrDefault(
                c => c.AbstractSyntax == DicomUID.Verification);

            if (context == null)
                throw new DicomAssociationException("Verification SOP Class not negotiated.");

            // Create C-ECHO request
            var messageId = NextMessageId();
            var request = DicomCommand.CreateCEchoRequest(messageId);

            // Send command
            await SendDimseCommandAsync(context.Id, request, ct).ConfigureAwait(false);

            // Receive response
            var response = await ReceiveDimseCommandAsync(ct).ConfigureAwait(false);

            if (!response.IsCEchoResponse)
                throw new DicomNetworkException($"Expected C-ECHO-RSP, got command field 0x{response.CommandFieldValue:X4}.");

            if (response.MessageIDBeingRespondedTo != messageId)
                throw new DicomNetworkException($"Message ID mismatch: expected {messageId}, got {response.MessageIDBeingRespondedTo}.");

            return response.Status;
        }

        /// <summary>
        /// Send A-RELEASE-RQ and wait for A-RELEASE-RP.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask ReleaseAsync(CancellationToken ct = default)
        {
            if (_association == null || !_association.IsEstablished)
                return;

            _association.ProcessEvent(AssociationEvent.AReleaseRequest);

            // Send A-RELEASE-RQ
            await SendReleaseRequestAsync(ct).ConfigureAwait(false);

            // Receive A-RELEASE-RP
            await ReceiveReleaseResponseAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Send A-ABORT and close connection immediately.
        /// </summary>
        /// <param name="source">The abort source.</param>
        /// <param name="reason">The abort reason.</param>
        public void Abort(AbortSource source = AbortSource.ServiceUser, AbortReason reason = AbortReason.NotSpecified)
        {
            if (_stream == null) return;

            _association?.ProcessEvent(AssociationEvent.AAbortRequest);

            // Send A-ABORT (best effort, don't wait for response)
            try
            {
                var buffer = new BufferWriter();
                var writer = new PduWriter(buffer);
                writer.WriteAbort(source, reason);
#if NET6_0_OR_GREATER
                _stream.Write(buffer.WrittenSpan);
#else
                _stream.Write(buffer.WrittenMemory.ToArray(), 0, buffer.WrittenCount);
#endif
            }
            catch
            {
                // Best effort - ignore errors
            }
        }

        /// <summary>
        /// Disposes the client, releasing the association and closing the connection.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
            catch
            {
                Abort();
            }
            finally
            {
                _association?.Dispose();
                _stream?.Dispose();
                _tcp?.Dispose();
            }
        }

        /// <summary>
        /// Gets the next unique message ID.
        /// </summary>
        /// <returns>The next message ID.</returns>
        internal ushort NextMessageId() => ++_messageId;

        #region Internal DIMSE Primitives

        /// <summary>
        /// Sends a DIMSE request with optional dataset.
        /// </summary>
        /// <param name="presentationContextId">Presentation context ID to use.</param>
        /// <param name="command">The DIMSE command to send.</param>
        /// <param name="dataset">Optional dataset to send (identifier, data, etc.).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// <para>
        /// Command is always encoded as Implicit VR Little Endian per PS3.7 Section 9.3.1.
        /// Dataset uses the negotiated transfer syntax for the presentation context.
        /// </para>
        /// <para>
        /// This method is used by SCU service classes to send DIMSE requests.
        /// </para>
        /// </remarks>
        internal async ValueTask SendDimseRequestAsync(
            byte presentationContextId,
            DicomCommand command,
            DicomDataset? dataset,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            if (_association == null || !_association.IsEstablished)
                throw new DicomAssociationException("Not connected.");

            // Send command PDV (Implicit VR LE, isCommand=true)
            await SendDimseCommandAsync(presentationContextId, command, ct).ConfigureAwait(false);

            // If dataset present, serialize with negotiated TS and send as data PDVs
            if (dataset != null)
            {
                await SendDatasetAsync(presentationContextId, dataset, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Receives a DIMSE response with optional dataset.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Tuple of (command, optional dataset).</returns>
        /// <remarks>
        /// <para>
        /// This method receives a single DIMSE response. For multi-response operations
        /// (C-FIND, C-MOVE, C-GET), call this repeatedly until a final status is received.
        /// </para>
        /// <para>
        /// The command is parsed from Implicit VR Little Endian.
        /// The dataset (if present) is parsed using the presentation context transfer syntax.
        /// </para>
        /// </remarks>
        internal async ValueTask<(DicomCommand command, DicomDataset? dataset)> ReceiveDimseResponseAsync(
            CancellationToken ct)
        {
            ThrowIfDisposed();

            // Receive command PDV
            var command = await ReceiveDimseCommandAsync(ct).ConfigureAwait(false);

            // Check if dataset follows (CommandDataSetType != 0x0101)
            DicomDataset? dataset = null;
            if (command.HasDataset)
            {
                dataset = await ReceiveDatasetAsync(ct).ConfigureAwait(false);
            }

            return (command, dataset);
        }

        /// <summary>
        /// Sends a C-CANCEL request to cancel an in-progress operation.
        /// </summary>
        /// <param name="presentationContextId">Presentation context ID for the operation to cancel.</param>
        /// <param name="messageIdBeingCancelled">Message ID of the operation to cancel.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// C-CANCEL is used to request cancellation of an in-progress C-FIND, C-MOVE, or C-GET operation.
        /// The SCP may or may not honor the cancellation request.
        /// </remarks>
        internal async ValueTask SendCCancelAsync(
            byte presentationContextId,
            ushort messageIdBeingCancelled,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            var cancel = DicomCommand.CreateCCancelRequest(messageIdBeingCancelled);
            await SendDimseCommandAsync(presentationContextId, cancel, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the first accepted presentation context.
        /// </summary>
        /// <returns>The first accepted presentation context.</returns>
        /// <exception cref="DicomAssociationException">Thrown if no contexts are accepted.</exception>
        internal PresentationContext GetFirstAcceptedContext()
        {
            var contexts = _association?.AcceptedContexts;
            if (contexts == null || contexts.Count == 0)
                throw new DicomAssociationException("No presentation contexts accepted.");

            return contexts[0];
        }

        /// <summary>
        /// Gets an accepted presentation context for the specified SOP Class.
        /// </summary>
        /// <param name="sopClassUid">The SOP Class UID.</param>
        /// <returns>The accepted presentation context, or null if not found.</returns>
        internal PresentationContext? GetAcceptedContext(DicomUID sopClassUid)
        {
            return _association?.AcceptedContexts?.FirstOrDefault(c => c.AbstractSyntax == sopClassUid);
        }

        /// <summary>
        /// Sends a dataset as data PDVs using the negotiated transfer syntax.
        /// </summary>
        private async ValueTask SendDatasetAsync(byte pcid, DicomDataset dataset, CancellationToken ct)
        {
            // Get negotiated transfer syntax for this presentation context
            var context = _association!.AcceptedContexts?.FirstOrDefault(c => c.Id == pcid);
            if (context == null)
                throw new DicomAssociationException($"Presentation context {pcid} not found.");

            var ts = context.AcceptedTransferSyntax ?? TransferSyntax.ImplicitVRLittleEndian;

            // Serialize dataset using negotiated transfer syntax
            var dataBytes = SerializeDataset(dataset, ts);

            // Send as data PDV
            var pdv = new PresentationDataValue(
                pcid,
                isCommand: false,
                isLastFragment: true,
                dataBytes);

            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WritePData(new[] { pdv });

#if NET6_0_OR_GREATER
            await _stream!.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenMemory.ToArray();
            await _stream!.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Receives a dataset from data PDVs.
        /// </summary>
        private async ValueTask<DicomDataset> ReceiveDatasetAsync(CancellationToken ct)
        {
            // Receive PDVs and accumulate data until last fragment flag set
            var dataBuffer = new List<byte>();

            while (true)
            {
                var pdu = await ReceivePduAsync(ct).ConfigureAwait(false);
                var pduType = (PduType)pdu[0];

                if (pduType != PduType.PDataTransfer)
                    throw new DicomNetworkException($"Expected P-DATA-TF, got PDU type: {pduType}");

                var reader = new PduReader(pdu.AsSpan(6));
                if (!reader.TryReadPresentationDataValue(
                    out var pcid,
                    out var isCommand,
                    out var isLastFragment,
                    out var data))
                {
                    throw new DicomNetworkException("Failed to parse PDV.");
                }

                if (isCommand)
                    throw new DicomNetworkException("Expected data PDV, got command PDV.");

                // Accumulate data
                dataBuffer.AddRange(data.ToArray());

                if (isLastFragment)
                    break;
            }

            // Parse dataset - need to determine transfer syntax from presentation context
            // For now, use Implicit VR Little Endian as fallback
            return ParseDataset(dataBuffer.ToArray());
        }

        /// <summary>
        /// Serializes a dataset to bytes using the specified transfer syntax.
        /// </summary>
        private static byte[] SerializeDataset(DicomDataset dataset, TransferSyntax ts)
        {
            var buffer = new BufferWriter();
            var options = new DicomWriterOptions { TransferSyntax = ts };
            var writer = new DicomStreamWriter(buffer, options);

            foreach (var element in dataset)
            {
                writer.WriteElement(element);
            }

            return buffer.WrittenMemory.ToArray();
        }

        /// <summary>
        /// Parses a dataset from bytes.
        /// </summary>
        private static DicomDataset ParseDataset(byte[] data)
        {
            // Simple parser - assumes Implicit VR Little Endian for now
            // Full implementation would detect/use the transfer syntax
            var dataset = new DicomDataset();
            int offset = 0;

            while (offset + 8 <= data.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2));
                uint vl = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
                offset += 8;

                // Skip sequence delimiters
                if (group == 0xFFFE)
                {
                    offset += (int)vl;
                    continue;
                }

                if (vl == 0xFFFFFFFF || offset + vl > data.Length)
                    break;

                var tag = new DicomTag(group, element);
                var entry = DicomDictionary.Default.GetEntry(tag);
                var vr = entry.HasValue && entry.Value.ValueRepresentations?.Length > 0
                    ? entry.Value.ValueRepresentations[0]
                    : DicomVR.UN;
                var valueData = data.AsSpan(offset, (int)vl).ToArray();

                IDicomElement dicomElement;
                var vrInfo = DicomVRInfo.GetInfo(vr);
                if (vrInfo.IsStringVR)
                {
                    dicomElement = new DicomStringElement(tag, vr, valueData);
                }
                else
                {
                    dicomElement = new DicomNumericElement(tag, vr, valueData);
                }

                dataset.Add(dicomElement);
                offset += (int)vl;
            }

            return dataset;
        }

        #endregion

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
                throw new ObjectDisposedException(nameof(DicomClient));
#endif
        }

        private async ValueTask SendAssociateRequestAsync(
            IReadOnlyList<PresentationContext> contexts,
            CancellationToken ct)
        {
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            writer.WriteAssociateRequest(
                _options.CalledAE,
                _options.CallingAE,
                contexts,
                UserInformation.Default.WithMaxPduLength(_options.MaxPduLength));

#if NET6_0_OR_GREATER
            await _stream!.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenMemory.ToArray();
            await _stream!.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private async ValueTask ReceiveAssociateResponseAsync(CancellationToken ct)
        {
            var pdu = await ReceivePduAsync(ct).ConfigureAwait(false);
            var pduType = (PduType)pdu[0];

            switch (pduType)
            {
                case PduType.AssociateAccept:
                    ParseAssociateAccept(pdu);
                    _association!.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
                    break;

                case PduType.AssociateReject:
                    var reader = new PduReader(pdu.AsSpan(6));
                    if (reader.TryReadAssociateReject(out var result, out var source, out var reason))
                    {
                        var rejectArgs = new AssociateRejectEventArgs
                        {
                            Result = result,
                            Source = source,
                            Reason = reason
                        };
                        _association!.ProcessEvent(AssociationEvent.AssociateRjPduReceived, rejectArgs);
                        throw new DicomAssociationException(result, source, reason);
                    }
                    throw new DicomNetworkException("Failed to parse A-ASSOCIATE-RJ.");

                default:
                    _association!.ProcessEvent(AssociationEvent.InvalidPduReceived);
                    throw new DicomNetworkException($"Unexpected PDU type during association: {pduType}");
            }
        }

        private void ParseAssociateAccept(byte[] pdu)
        {
            var reader = new PduReader(pdu.AsSpan(6));

            if (!reader.TryReadAssociateAccept(
                out _,
                out _,
                out _,
                out var variableItems))
            {
                throw new DicomNetworkException("Failed to parse A-ASSOCIATE-AC fixed fields.");
            }

            // Parse variable items to get accepted contexts and user info
            var acceptedContexts = new List<PresentationContext>();
            uint remoteMaxPdu = _options.MaxPduLength;

            var itemReader = new PduReader(variableItems);
            while (itemReader.Remaining > 0)
            {
                if (!itemReader.TryReadVariableItem(out var itemType, out var itemLength))
                    break;

                switch (itemType)
                {
                    case ItemType.PresentationContextAccept:
                        ParsePresentationContextAccept(ref itemReader, itemLength, acceptedContexts);
                        break;

                    case ItemType.UserInformation:
                        remoteMaxPdu = ParseUserInformation(ref itemReader, itemLength);
                        break;

                    default:
                        // Skip unknown items
                        itemReader.TrySkip(itemLength);
                        break;
                }
            }

            // Update association with accepted contexts
            _association!.SetAcceptedContexts(acceptedContexts);
            _association.SetMaxPduLength(Math.Min(_options.MaxPduLength, remoteMaxPdu));
        }

        private void ParsePresentationContextAccept(
            ref PduReader reader,
            int itemLength,
            List<PresentationContext> acceptedContexts)
        {
            if (!reader.TryReadPresentationContextAccept(out var contextId, out var result, out _))
                return;

            // Read the remainder of the item (transfer syntax)
            int bytesRead = 4; // Context ID, reserved, result, reserved
            TransferSyntax? acceptedTs = null;

            while (bytesRead < itemLength)
            {
                if (!reader.TryReadVariableItem(out var subItemType, out var subItemLength))
                    break;

                bytesRead += 4 + subItemLength;

                if (subItemType == ItemType.TransferSyntax)
                {
                    if (reader.TryReadUidString(subItemLength, out var tsUid))
                    {
                        acceptedTs = TransferSyntax.FromUID(new DicomUID(tsUid));
                    }
                }
                else
                {
                    reader.TrySkip(subItemLength);
                }
            }

            // Find the original proposed context to get the abstract syntax
            var proposedContext = _association!.Options.PresentationContexts
                .FirstOrDefault(c => c.Id == contextId);

            if (proposedContext != null && result == PresentationContextResult.Acceptance && acceptedTs.HasValue)
            {
                acceptedContexts.Add(PresentationContext.CreateAccepted(
                    contextId,
                    proposedContext.AbstractSyntax,
                    acceptedTs.Value));
            }
        }

        private uint ParseUserInformation(ref PduReader reader, int itemLength)
        {
            uint maxPdu = _options.MaxPduLength;
            int bytesRead = 0;

            while (bytesRead < itemLength)
            {
                if (!reader.TryReadVariableItem(out var subItemType, out var subItemLength))
                    break;

                bytesRead += 4 + subItemLength;

                if (subItemType == ItemType.MaximumLength)
                {
                    if (reader.TryReadMaxPduLength(out var pduLen))
                    {
                        maxPdu = pduLen;
                    }
                }
                else
                {
                    reader.TrySkip(subItemLength);
                }
            }

            return maxPdu;
        }

        private async ValueTask SendReleaseRequestAsync(CancellationToken ct)
        {
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WriteReleaseRequest();

#if NET6_0_OR_GREATER
            await _stream!.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenMemory.ToArray();
            await _stream!.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private async ValueTask ReceiveReleaseResponseAsync(CancellationToken ct)
        {
            var pdu = await ReceivePduAsync(ct).ConfigureAwait(false);
            var pduType = (PduType)pdu[0];

            if (pduType == PduType.ReleaseResponse)
            {
                _association!.ProcessEvent(AssociationEvent.ReleaseRpPduReceived);
            }
            else
            {
                throw new DicomNetworkException($"Expected A-RELEASE-RP, got PDU type: {pduType}");
            }
        }

        private async ValueTask SendDimseCommandAsync(
            byte presentationContextId,
            DicomCommand command,
            CancellationToken ct)
        {
            // Serialize command to bytes (Implicit VR Little Endian per PS3.7)
            var cmdBytes = SerializeCommand(command);

            // Wrap in PDV
            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                cmdBytes);

            // Build P-DATA PDU
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WritePData(new[] { pdv });

#if NET6_0_OR_GREATER
            await _stream!.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenMemory.ToArray();
            await _stream!.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private async ValueTask<DicomCommand> ReceiveDimseCommandAsync(CancellationToken ct)
        {
            var pdu = await ReceivePduAsync(ct).ConfigureAwait(false);
            var pduType = (PduType)pdu[0];

            if (pduType != PduType.PDataTransfer)
                throw new DicomNetworkException($"Expected P-DATA-TF, got PDU type: {pduType}");

            // Parse PDV from P-DATA
            var reader = new PduReader(pdu.AsSpan(6));
            if (!reader.TryReadPresentationDataValue(
                out _,
                out var isCommand,
                out _,
                out var data))
            {
                throw new DicomNetworkException("Failed to parse PDV.");
            }

            if (!isCommand)
                throw new DicomNetworkException("Expected command PDV, got data PDV.");

            // Parse command dataset (Implicit VR Little Endian)
            var cmdDataset = ParseCommandDataset(data);
            return new DicomCommand(cmdDataset);
        }

        private static byte[] SerializeCommand(DicomCommand command)
        {
            // Serialize command to Implicit VR Little Endian
            // Group 0000 elements only, with group length
            var elements = new List<(DicomTag tag, DicomVR vr, byte[] data)>();
            uint dataLength = 0;

            foreach (var element in command.Dataset.Where(e => e.Tag.Group == 0x0000))
            {
                var vr = GetCommandVR(element.Tag.Element);
                var valueBytes = element.RawValue.ToArray();

                elements.Add((element.Tag, vr, valueBytes));
                // Tag(4) + VL(4) + value
                dataLength += 4 + 4 + (uint)valueBytes.Length;
            }

            // Calculate total length: Group Length element + other elements
            // Group Length: Tag(4) + VL(4) + value(4) = 12 bytes
            uint groupLengthValue = dataLength;
            uint totalLength = 4 + 4 + 4 + dataLength;

            var buffer = new byte[totalLength];
            int offset = 0;

            // Write CommandGroupLength (0000,0000)
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), 0x0000);
            offset += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), 0x0000);
            offset += 2;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), 4);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), groupLengthValue);
            offset += 4;

            // Write other elements in tag order
            foreach (var (tag, _, valueBytes) in elements.OrderBy(e => e.tag))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), tag.Group);
                offset += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), tag.Element);
                offset += 2;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), (uint)valueBytes.Length);
                offset += 4;
                if (valueBytes.Length > 0)
                {
                    valueBytes.CopyTo(buffer.AsSpan(offset));
                    offset += valueBytes.Length;
                }
            }

            return buffer;
        }

        private static DicomDataset ParseCommandDataset(ReadOnlySpan<byte> data)
        {
            var dataset = new DicomDataset();
            int offset = 0;

            while (offset + 8 <= data.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2));
                uint vl = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4));
                offset += 8;

                if (group != 0x0000)
                    break;

                if (offset + vl > data.Length)
                    break;

                var tag = new DicomTag(group, element);
                var vr = GetCommandVR(element);
                var valueData = data.Slice(offset, (int)vl).ToArray();

                IDicomElement dicomElement;
                dicomElement = (vr == DicomVR.US || vr == DicomVR.UL)
                    ? new DicomNumericElement(tag, vr, valueData)
                    : new DicomStringElement(tag, vr, valueData);

                dataset.Add(dicomElement);
                offset += (int)vl;
            }

            return dataset;
        }

        private static DicomVR GetCommandVR(ushort element)
        {
            // Command elements have fixed VRs per PS3.7
            return element switch
            {
                0x0000 => DicomVR.UL, // CommandGroupLength
                0x0002 => DicomVR.UI, // AffectedSOPClassUID
                0x0003 => DicomVR.UI, // RequestedSOPClassUID
                0x0100 => DicomVR.US, // CommandField
                0x0110 => DicomVR.US, // MessageID
                0x0120 => DicomVR.US, // MessageIDBeingRespondedTo
                0x0600 => DicomVR.AE, // MoveDestination
                0x0700 => DicomVR.US, // Priority
                0x0800 => DicomVR.US, // CommandDataSetType
                0x0900 => DicomVR.US, // Status
                0x0901 => DicomVR.AT, // OffendingElement
                0x0902 => DicomVR.LO, // ErrorComment
                0x0903 => DicomVR.US, // ErrorID
                0x1000 => DicomVR.UI, // AffectedSOPInstanceUID
                0x1001 => DicomVR.UI, // RequestedSOPInstanceUID
                0x1002 => DicomVR.US, // EventTypeID
                0x1005 => DicomVR.AT, // AttributeIdentifierList
                0x1008 => DicomVR.US, // ActionTypeID
                0x1020 => DicomVR.US, // NumberOfRemainingSuboperations
                0x1021 => DicomVR.US, // NumberOfCompletedSuboperations
                0x1022 => DicomVR.US, // NumberOfFailedSuboperations
                0x1023 => DicomVR.US, // NumberOfWarningSuboperations
                0x1030 => DicomVR.AE, // MoveOriginatorApplicationEntityTitle
                0x1031 => DicomVR.US, // MoveOriginatorMessageID
                _ => DicomVR.UN
            };
        }

        private async ValueTask<byte[]> ReceivePduAsync(CancellationToken ct)
        {
            // Read PDU header (6 bytes)
            var header = new byte[6];
#if NET6_0_OR_GREATER
            await _stream!.ReadExactlyAsync(header, ct).ConfigureAwait(false);
#else
            int totalRead = 0;
            while (totalRead < 6)
            {
                int read = await _stream!.ReadAsync(header, totalRead, 6 - totalRead, ct).ConfigureAwait(false);
                if (read == 0)
                    throw new DicomNetworkException("Connection closed while reading PDU header.");
                totalRead += read;
            }
#endif

            // Parse PDU type and length
            var pduType = (Pdu.PduType)header[0];
            uint pduLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));

            // Validate PDU length to prevent denial-of-service attacks
            // Association PDUs (types 1-3) have stricter limits than data transfer PDUs
            uint maxLength = pduType switch
            {
                Pdu.PduType.AssociateRequest or Pdu.PduType.AssociateAccept or Pdu.PduType.AssociateReject
                    => PduConstants.MaxAssociationPduLength,
                _ => Math.Min(_options.MaxPduLength, PduConstants.AbsoluteMaxPduLength)
            };

            if (pduLength > maxLength)
            {
                throw new DicomNetworkException(
                    $"PDU length {pduLength} exceeds maximum allowed length {maxLength}. " +
                    "This may indicate a malformed PDU or denial-of-service attack.");
            }

            // Read PDU body
            var pdu = new byte[6 + pduLength];
            header.CopyTo(pdu, 0);

#if NET6_0_OR_GREATER
            await _stream!.ReadExactlyAsync(pdu.AsMemory(6, (int)pduLength), ct).ConfigureAwait(false);
#else
            totalRead = 0;
            while (totalRead < (int)pduLength)
            {
                int read = await _stream!.ReadAsync(pdu, 6 + totalRead, (int)pduLength - totalRead, ct).ConfigureAwait(false);
                if (read == 0)
                    throw new DicomNetworkException("Connection closed while reading PDU body.");
                totalRead += read;
            }
#endif

            return pdu;
        }
    }
}
