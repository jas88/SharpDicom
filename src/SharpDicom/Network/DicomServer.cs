using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;
using SharpDicom.Network.Association;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;
using SharpDicom.Network.Tls;

#if NETSTANDARD2_0
using BufferWriter = SharpDicom.Internal.ArrayBufferWriterPolyfill<byte>;
#else
using BufferWriter = System.Buffers.ArrayBufferWriter<byte>;
#endif

namespace SharpDicom.Network
{
    /// <summary>
    /// DICOM SCP (Service Class Provider) server that listens for incoming associations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DicomServer listens on a TCP port for incoming DICOM associations from SCUs.
    /// Each connection is handled in a separate Task, allowing multiple concurrent associations.
    /// </para>
    /// <para>
    /// Use <see cref="DicomServerOptions"/> to configure the server behavior, including
    /// handlers for association requests and C-ECHO operations.
    /// </para>
    /// <example>
    /// <code>
    /// var options = new DicomServerOptions
    /// {
    ///     AETitle = "MY_SCP",
    ///     Port = 11112,
    ///     OnCEcho = ctx => ValueTask.FromResult(DicomStatus.Success)
    /// };
    ///
    /// await using var server = new DicomServer(options);
    /// server.Start();
    ///
    /// // ... server is running ...
    ///
    /// await server.StopAsync();
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class DicomServer : IAsyncDisposable
    {
        private readonly DicomServerOptions _options;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _activeTasks = new();
        private readonly SemaphoreSlim _semaphore;
        private Task? _acceptTask;
        private bool _disposed;
#if NET6_0_OR_GREATER
        private SslStreamCertificateContext? _certificateContext;
#endif

        /// <summary>
        /// Initializes a new instance of <see cref="DicomServer"/>.
        /// </summary>
        /// <param name="options">The server configuration options.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when options validation fails.
        /// </exception>
        public DicomServer(DicomServerOptions options)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options == null)
                throw new ArgumentNullException(nameof(options));
#endif
            options.Validate();
            _options = options;
            _listener = new TcpListener(_options.BindAddress, _options.Port);
            _semaphore = new SemaphoreSlim(_options.MaxAssociations, _options.MaxAssociations);
        }

        /// <summary>
        /// Gets a value indicating whether the server is listening for connections.
        /// </summary>
        public bool IsListening => _acceptTask != null && !_acceptTask.IsCompleted;

        /// <summary>
        /// Gets the number of currently active associations.
        /// </summary>
        public int ActiveAssociations
        {
            get
            {
                lock (_activeTasks)
                    return _activeTasks.Count;
            }
        }

        /// <summary>
        /// Gets the server options.
        /// </summary>
        public DicomServerOptions Options => _options;

        /// <summary>
        /// Starts listening for incoming associations.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when the server has been disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the server is already listening.
        /// </exception>
        public void Start()
        {
            ThrowIfDisposed();

            if (IsListening)
                throw new InvalidOperationException("Server is already listening.");

            // Pre-build certificate context if TLS is configured
            if (_options.Tls != null)
            {
#if NET6_0_OR_GREATER
                _certificateContext = _options.Tls.ServerCertificateContext
                    ?? SslStreamCertificateContext.Create(
                        _options.Tls.ServerCertificate!,
                        additionalCertificates: null,
                        offline: false);
#endif
            }

            _listener.Start();
            _acceptTask = AcceptLoopAsync(_cts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Wait for connection slot (respects MaxAssociations)
                    await _semaphore.WaitAsync(ct).ConfigureAwait(false);

                    TcpClient client;
                    try
                    {
#if NET6_0_OR_GREATER
                        client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
#else
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
#endif
                    }
                    catch
                    {
                        _semaphore.Release();
                        throw;
                    }

                    var task = HandleAssociationAsync(client, ct);
                    lock (_activeTasks)
                        _activeTasks.Add(task);

                    // Fire-and-forget with cleanup
                    _ = task.ContinueWith(t =>
                    {
                        lock (_activeTasks)
                            _activeTasks.Remove(task);
                        _semaphore.Release();
                    }, TaskScheduler.Default);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                    // Listener was stopped
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed
                    break;
                }
            }
        }

        private async Task HandleAssociationAsync(TcpClient client, CancellationToken ct)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint
                ?? new IPEndPoint(IPAddress.Any, 0);

            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    Stream activeStream = stream;
                    SslStream? sslStream = null;

                    // Perform TLS handshake if configured
                    if (_options.Tls != null)
                    {
                        sslStream = new SslStream(
                            stream,
                            leaveInnerStreamOpen: false,
                            _options.Tls.ClientCertificateValidationCallback);

                        try
                        {
                            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            handshakeCts.CancelAfter(_options.Tls.HandshakeTimeout);

#if NET6_0_OR_GREATER
                            var serverAuthOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificateContext = _certificateContext,
                                ServerCertificate = _options.Tls.ServerCertificate,
                                ClientCertificateRequired = _options.Tls.RequireClientCertificate,
                                EnabledSslProtocols = _options.Tls.EnabledProtocols ?? SslProtocols.None,
                                CertificateRevocationCheckMode = _options.Tls.RevocationMode,
                            };

                            await sslStream.AuthenticateAsServerAsync(serverAuthOptions, handshakeCts.Token)
                                .ConfigureAwait(false);
#else
                            await sslStream.AuthenticateAsServerAsync(
                                _options.Tls.ServerCertificate!,
                                clientCertificateRequired: _options.Tls.RequireClientCertificate,
                                enabledSslProtocols: _options.Tls.EnabledProtocols ?? SslProtocols.None,
                                checkCertificateRevocation: _options.Tls.RevocationMode != X509RevocationMode.NoCheck)
                                .ConfigureAwait(false);
#endif
                        }
                        catch (Exception ex) when (ex is AuthenticationException or OperationCanceledException)
                        {
                            sslStream.Dispose();
                            return; // Connection failed TLS, close silently
                        }

                        // Validate DICOM TLS profile compliance
                        if (_options.Tls.EnforceDicomTlsProfile)
                        {
                            if (!DicomTlsProfile.IsCompliantProtocol(sslStream.SslProtocol))
                            {
                                sslStream.Dispose();
                                return;
                            }

#if NET6_0_OR_GREATER
                            if (!DicomTlsProfile.IsCompliant(sslStream.NegotiatedCipherSuite.ToString()))
                            {
                                sslStream.Dispose();
                                return;
                            }
#endif
                        }

                        activeStream = sslStream;
                    }

                    try
                    {
                        // Start ARTIM timer
                    using var artimCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    artimCts.CancelAfter(_options.ArtimTimeout);

                    try
                    {
                        // Read A-ASSOCIATE-RQ first to get AE titles
                        var (callingAE, calledAE, requestedContexts) =
                            await ReadAssociateRequestAsync(activeStream, artimCts.Token).ConfigureAwait(false);

                        // Now create association for SCP path with actual AE titles
                        var assocOptions = new AssociationOptions(
                            calledAE,  // Called AE from request (should match _options.AETitle)
                            callingAE, // Calling AE from request
                            requestedContexts);

                        var association = new DicomAssociation(assocOptions);
                        // For SCP: Transport open -> Association request received
                        association.ProcessEvent(AssociationEvent.TransportConnectionIndication);
                        association.ProcessEvent(AssociationEvent.AssociateRqPduReceived);

                        // Stop ARTIM timer (got valid PDU)
#if NET6_0_OR_GREATER
                        artimCts.CancelAfter(Timeout.InfiniteTimeSpan);
#else
                        // netstandard2.0 doesn't support CancelAfter with InfiniteTimeSpan after already set
                        // The timer is effectively stopped when we proceed
#endif

                        // Validate and decide
                        var requestContext = new AssociationRequestContext(
                            callingAE,
                            calledAE,
                            remoteEndPoint,
                            requestedContexts);

                        var result = _options.OnAssociationRequest != null
                            ? await _options.OnAssociationRequest(requestContext).ConfigureAwait(false)
                            : CreateDefaultAcceptResult(requestedContexts);

                        if (result.Accept && result.AcceptedContexts != null)
                        {
                            await SendAssociateAcceptAsync(activeStream, callingAE, calledAE, result.AcceptedContexts, ct)
                                .ConfigureAwait(false);
                            association.ProcessEvent(AssociationEvent.AAssociateResponse);
                            association.SetAcceptedContexts(result.AcceptedContexts);

                            // Run DIMSE loop
                            await RunDimseLoopAsync(activeStream, association, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await SendAssociateRejectAsync(activeStream, result, ct).ConfigureAwait(false);
                            // Association rejected - connection closes
                        }
                    }
                        catch (OperationCanceledException) when (artimCts.IsCancellationRequested && !ct.IsCancellationRequested)
                        {
                            // ARTIM timeout - no A-ASSOCIATE-RQ received in time
                            // Connection will be closed
                        }
                    }
                    finally
                    {
                        // Dispose SslStream if it was created (sends TLS close_notify)
                        // The using(client) block will dispose the NetworkStream
                        sslStream?.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Error handling association - connection will be closed
            }
        }

        private static AssociationRequestResult CreateDefaultAcceptResult(List<PresentationContext> requested)
        {
            // Accept all contexts with their first proposed transfer syntax
            var accepted = new List<PresentationContext>(requested.Count);
            foreach (var ctx in requested.Where(c => c.TransferSyntaxes.Count > 0))
            {
                accepted.Add(PresentationContext.CreateAccepted(
                    ctx.Id,
                    ctx.AbstractSyntax,
                    ctx.TransferSyntaxes[0]));
            }
            return AssociationRequestResult.Accepted(accepted);
        }

        private async Task RunDimseLoopAsync(
            Stream stream,
            DicomAssociation association,
            CancellationToken ct)
        {
            // Process PDUs until release or abort
            while (association.IsEstablished && !ct.IsCancellationRequested)
            {
                var (pduType, pduBody) = await ReadPduAsync(stream, ct).ConfigureAwait(false);

                switch (pduType)
                {
                    case PduType.PDataTransfer:
                        await HandlePDataAsync(stream, association, pduBody, ct).ConfigureAwait(false);
                        break;

                    case PduType.ReleaseRequest:
                        association.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);
                        await SendReleaseResponseAsync(stream, ct).ConfigureAwait(false);
                        association.ProcessEvent(AssociationEvent.AReleaseResponse);
                        return;

                    case PduType.Abort:
                        association.ProcessEvent(AssociationEvent.AbortPduReceived);
                        return;

                    default:
                        // Unexpected PDU type in established state
                        break;
                }
            }
        }

        private async Task HandlePDataAsync(
            Stream stream,
            DicomAssociation association,
            byte[] pduBody,
            CancellationToken ct)
        {
            // Parse P-DATA-TF to extract PDVs (must complete before any await)
            // Each PDV contains: 4-byte length, 1-byte context ID, 1-byte message control header, data
            var (pendingEchoRequests, pendingStoreRequests, pendingQRRequests) = ExtractDimseRequests(pduBody);

            // Now process extracted requests (can await)
            foreach (var (contextId, messageId) in pendingEchoRequests)
            {
                await HandleCEchoAsync(stream, association, contextId, messageId, ct)
                    .ConfigureAwait(false);
            }

            // Handle C-STORE requests (command was in this PDU, dataset follows)
            foreach (var storeCmd in pendingStoreRequests)
            {
                await HandleCStoreAsync(stream, association, storeCmd, ct)
                    .ConfigureAwait(false);
            }

            // Handle Query/Retrieve requests
            foreach (var qrCmd in pendingQRRequests)
            {
                switch (qrCmd.CommandFieldValue)
                {
                    case CommandFields.CFindRequest:
                        await HandleCFindAsync(stream, association, qrCmd, ct)
                            .ConfigureAwait(false);
                        break;

                    case CommandFields.CMoveRequest:
                        await HandleCMoveAsync(stream, association, qrCmd, ct)
                            .ConfigureAwait(false);
                        break;

                    case CommandFields.CGetRequest:
                        await HandleCGetAsync(stream, association, qrCmd, ct)
                            .ConfigureAwait(false);
                        break;

                    case CommandFields.CCancelRequest:
                        // C-CANCEL is handled inline during C-FIND/C-MOVE/C-GET processing.
                        // If we receive it outside of an active operation, ignore it.
                        break;
                }
            }
        }

        private static (List<(byte ContextId, ushort MessageId)> EchoRequests,
                 List<CStoreCommandInfo> StoreRequests,
                 List<QRCommandInfo> QRRequests)
            ExtractDimseRequests(byte[] pduBody)
        {
            var echoRequests = new List<(byte, ushort)>();
            var storeRequests = new List<CStoreCommandInfo>();
            var qrRequests = new List<QRCommandInfo>();
            var reader = new PduReader(pduBody);

            while (reader.TryReadPresentationDataValue(
                out byte contextId,
                out bool isCommand,
                out bool isLastFragment,
                out var data))
            {
                if (isCommand && isLastFragment)
                {
                    // Parse command dataset to check what operation this is
                    var commandField = ParseCommandField(data);

                    if (commandField == CommandFields.CEchoRequest)
                    {
                        var messageId = ParseMessageId(data);
                        echoRequests.Add((contextId, messageId));
                    }
                    else if (commandField == CommandFields.CStoreRequest)
                    {
                        var commandInfo = ParseCStoreCommand(data, contextId);
                        storeRequests.Add(commandInfo);
                    }
                    else if (commandField == CommandFields.CFindRequest
                          || commandField == CommandFields.CMoveRequest
                          || commandField == CommandFields.CGetRequest
                          || commandField == CommandFields.CCancelRequest)
                    {
                        var qrInfo = ParseQRCommand(data, contextId, commandField);
                        qrRequests.Add(qrInfo);
                    }
                }
            }

            return (echoRequests, storeRequests, qrRequests);
        }

        private static QRCommandInfo ParseQRCommand(ReadOnlySpan<byte> commandData, byte contextId, ushort commandField)
        {
            ushort messageId = 0;
            string? sopClassUid = null;
            ushort dataSetType = 0x0101; // Default: no dataset
            string? moveDestination = null;

            int offset = 0;
            while (offset + 8 <= commandData.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(commandData.Slice(offset + 4));

                if (offset + 8 + length > commandData.Length)
                    break;

                var valueSpan = commandData.Slice(offset + 8, (int)length);

                if (group == 0x0000)
                {
                    switch (element)
                    {
                        case 0x0002: // AffectedSOPClassUID
                            sopClassUid = Encoding.ASCII.GetString(valueSpan.ToArray()).TrimEnd('\0', ' ');
                            break;
                        case 0x0110: // MessageID
                            if (length >= 2)
                                messageId = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);
                            break;
                        case 0x0120: // MessageIDBeingRespondedTo (used by C-CANCEL)
                            if (length >= 2 && commandField == CommandFields.CCancelRequest)
                                messageId = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);
                            break;
                        case 0x0600: // MoveDestination
                            moveDestination = Encoding.ASCII.GetString(valueSpan.ToArray()).TrimEnd('\0', ' ');
                            break;
                        case 0x0800: // CommandDataSetType
                            if (length >= 2)
                                dataSetType = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);
                            break;
                    }
                }

                offset += 8 + (int)length;
            }

            return new QRCommandInfo(
                contextId,
                messageId,
                new DicomUID(sopClassUid ?? string.Empty),
                hasDataset: dataSetType != 0x0101,
                commandField,
                moveDestination);
        }

        private async Task HandleCEchoAsync(
            Stream stream,
            DicomAssociation association,
            byte presentationContextId,
            ushort messageId,
            CancellationToken ct)
        {
            var context = new CEchoRequestContext(association, messageId);

            var status = _options.OnCEcho != null
                ? await _options.OnCEcho(context).ConfigureAwait(false)
                : DicomStatus.Success;

            // Build C-ECHO-RSP command dataset and send as P-DATA-TF
            await SendCEchoResponseAsync(stream, presentationContextId, messageId, status, ct)
                .ConfigureAwait(false);
        }

        private async Task HandleCStoreAsync(
            Stream stream,
            DicomAssociation association,
            CStoreCommandInfo command,
            CancellationToken ct)
        {
            var callingAE = association.Options.CallingAETitle;
            var calledAE = association.Options.CalledAETitle;

            var requestContext = new CStoreRequestContext(
                callingAE,
                calledAE,
                command.SOPClassUID,
                command.SOPInstanceUID,
                command.MessageID,
                command.PresentationContextId);

            DicomStatus status;

            // Check if we have a handler configured
            if (!_options.HasCStoreHandler)
            {
                // No handler - reject with SOP Class Not Supported
                status = DicomStatus.NoSuchSOPClass;

                // Still need to read and discard the dataset if present
                if (command.HasDataset)
                {
                    await ReadAndDiscardDatasetAsync(stream, ct).ConfigureAwait(false);
                }
            }
            else if (command.HasDataset)
            {
                // Read the dataset from subsequent P-DATA PDUs
                try
                {
                    // For buffered mode, check size incrementally during reading to prevent memory exhaustion
                    var maxSize = _options.StoreHandlerMode == CStoreHandlerMode.Buffered
                        ? _options.MaxBufferedDatasetSize
                        : long.MaxValue;

                    var datasetBytes = await ReadDatasetAsync(stream, maxSize, ct).ConfigureAwait(false);

                    // Parse the dataset
                    var dataset = ParseDataset(datasetBytes, association, command.PresentationContextId);

                    // Call the appropriate handler
                    status = await InvokeCStoreHandlerAsync(requestContext, dataset, ct)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds maximum"))
                {
                    // Size limit exceeded - need to discard remaining data and return error
                    await ReadAndDiscardDatasetAsync(stream, ct).ConfigureAwait(false);
                    status = DicomStatus.OutOfResources;
                }
                catch (Exception)
                {
                    status = DicomStatus.ProcessingFailure;
                }
            }
            else
            {
                // No dataset - unusual for C-STORE but handle gracefully
                status = DicomStatus.ProcessingFailure;
            }

            // Send C-STORE-RSP
            await SendCStoreResponseAsync(
                stream,
                command.PresentationContextId,
                command.MessageID,
                command.SOPClassUID,
                command.SOPInstanceUID,
                status,
                ct).ConfigureAwait(false);
        }

        private async ValueTask<DicomStatus> InvokeCStoreHandlerAsync(
            CStoreRequestContext context,
            DicomDataset dataset,
            CancellationToken ct)
        {
            try
            {
                // Delegate takes precedence over interface
                if (_options.OnCStoreRequest != null)
                {
                    return await _options.OnCStoreRequest(context, dataset, ct).ConfigureAwait(false);
                }

                if (_options.CStoreHandler != null)
                {
                    return await _options.CStoreHandler.OnCStoreAsync(context, dataset, ct)
                        .ConfigureAwait(false);
                }

                // No handler - should not reach here if HasCStoreHandler was true
                return DicomStatus.NoSuchSOPClass;
            }
            catch (Exception)
            {
                return DicomStatus.ProcessingFailure;
            }
        }

        private async Task HandleCFindAsync(
            Stream stream,
            DicomAssociation association,
            QRCommandInfo command,
            CancellationToken ct)
        {
            // CRITICAL: Always read the identifier dataset even if no handler is registered
            // (Pitfall 7 from RESEARCH.md)
            DicomDataset? identifierDataset = null;

            if (command.HasDataset)
            {
                try
                {
                    var datasetBytes = await ReadDatasetAsync(stream, ct).ConfigureAwait(false);
                    identifierDataset = ParseDataset(datasetBytes, association, command.PresentationContextId);
                }
                catch (Exception)
                {
                    // Failed to read/parse identifier - send failure
                    await SendQRFailureResponseAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, command.CommandFieldValue, ct)
                        .ConfigureAwait(false);
                    return;
                }
            }

            if (_options.OnCFind == null)
            {
                // No handler registered - return 0xA900 Unable to Process (Pitfall 6)
                await SendQRFailureResponseAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, command.CommandFieldValue, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Full C-FIND handling with streaming responses is implemented in HandleCFindStreamingAsync
            await HandleCFindStreamingAsync(
                stream, association, command, identifierDataset!, ct)
                .ConfigureAwait(false);
        }

        private async Task HandleCFindStreamingAsync(
            Stream stream,
            DicomAssociation association,
            QRCommandInfo command,
            DicomDataset identifierDataset,
            CancellationToken ct)
        {
            var responseCommandField = (ushort)(command.CommandFieldValue | 0x8000);

            try
            {
                await foreach (var match in _options.OnCFind!(identifierDataset, ct).ConfigureAwait(false))
                {
                    // Filter return keys per DICOM PS3.4 C.2.2 (Pitfall 1)
                    var filtered = DicomQueryMatcher.FilterReturnKeys(match, identifierDataset);

                    // Send Pending C-FIND-RSP with identifier dataset
                    await SendDimseResponseWithDatasetAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, responseCommandField,
                        DicomStatus.Pending.Code, filtered, association, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Send Cancel status (0xFE00)
                await SendQRResponseAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.Cancel.Code, ct)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception)
            {
                // Send failure status 0xC000 (Unable to Process)
                await SendQRResponseAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    0xC000, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Send final C-FIND-RSP with Success status (no dataset)
            await SendQRResponseAsync(
                stream, command.PresentationContextId, command.MessageID,
                command.SOPClassUID, responseCommandField,
                DicomStatus.Success.Code, ct)
                .ConfigureAwait(false);
        }

        private async Task HandleCMoveAsync(
            Stream stream,
            DicomAssociation association,
            QRCommandInfo command,
            CancellationToken ct)
        {
            // Always read the identifier dataset even if no handler is registered (Pitfall 7)
            DicomDataset? identifierDataset = null;

            if (command.HasDataset)
            {
                try
                {
                    var datasetBytes = await ReadDatasetAsync(stream, ct).ConfigureAwait(false);
                    identifierDataset = ParseDataset(datasetBytes, association, command.PresentationContextId);
                }
                catch (Exception)
                {
                    await SendQRFailureResponseAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, command.CommandFieldValue, ct)
                        .ConfigureAwait(false);
                    return;
                }
            }

            var responseCommandField = (ushort)(command.CommandFieldValue | 0x8000);

            // Check if all required handlers are configured
            if (!_options.HasCMoveHandler)
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    0xA900, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Resolve the move destination
            var moveDestination = command.MoveDestination;
            if (string.IsNullOrEmpty(moveDestination))
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.MoveDestinationUnknown.Code, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            var resolved = _options.OnResolveMoveDestination!(moveDestination!);
            if (resolved == null)
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.MoveDestinationUnknown.Code, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            var (destHost, destPort) = resolved.Value;

            // Collect all matches (need total count for progress tracking)
            var matches = new List<DicomDataset>();
            try
            {
                const int maxMatches = 10000;
                await foreach (var match in _options.OnCFind!(identifierDataset!, ct).ConfigureAwait(false))
                {
                    matches.Add(match);
                    if (matches.Count >= maxMatches)
                        break;
                }
            }
            catch (Exception)
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    0xC000, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            if (matches.Count == 0)
            {
                // No matches - send Success with zero sub-ops
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.Success.Code, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Collect unique SOP Class UIDs from matches for presentation context negotiation
            var sopClassUids = new HashSet<string>();
            foreach (var match in matches)
            {
                var sopClassStr = match.GetString(DicomTag.SOPClassUID);
                if (!string.IsNullOrEmpty(sopClassStr))
                    sopClassUids.Add(sopClassStr!.TrimEnd('\0', ' '));
            }

            // Build presentation contexts for forwarding association
            var forwardingContexts = new List<PresentationContext>();
            byte pcId = 1;
            foreach (var sopClass in sopClassUids)
            {
                if (pcId > 255) break;
                forwardingContexts.Add(new PresentationContext(
                    pcId,
                    new DicomUID(sopClass),
                    TransferSyntax.ExplicitVRLittleEndian,
                    TransferSyntax.ImplicitVRLittleEndian));
                pcId += 2;
            }

            // If we couldn't derive SOP Classes, add a generic one
            if (forwardingContexts.Count == 0)
            {
                forwardingContexts.Add(new PresentationContext(
                    1,
                    new DicomUID("1.2.840.10008.5.1.4.1.1.2"), // CT Image Storage
                    TransferSyntax.ExplicitVRLittleEndian,
                    TransferSyntax.ImplicitVRLittleEndian));
            }

            // Open forwarding association (SEPARATE from the C-MOVE association per PS3.4 C.4.2)
            ushort completed = 0;
            ushort failed = 0;
            ushort warning = 0;
            ushort remaining = (ushort)matches.Count;
            bool cancelled = false;

            DicomClient? forwardingClient = null;
            try
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = destHost,
                    Port = destPort,
                    CalledAE = moveDestination!,
                    CallingAE = _options.AETitle,
                    ConnectionTimeout = TimeSpan.FromSeconds(30),
                    DimseTimeout = TimeSpan.FromSeconds(60)
                };

                forwardingClient = new DicomClient(clientOptions);

                try
                {
                    await forwardingClient.ConnectAsync(forwardingContexts, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Failed to connect to destination
                    await SendQRResponseWithProgressAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, responseCommandField,
                        0xA801, new SubOperationProgress(remaining, 0, remaining, 0), ct)
                        .ConfigureAwait(false);
                    return;
                }

                // Forward files via C-STORE sub-operations
                var storeScu = new Dimse.Services.CStoreScu(forwardingClient);

                for (int i = 0; i < matches.Count; i++)
                {
                    if (cancelled) break;

                    var match = matches[i];
                    DicomFile? file = null;

                    try
                    {
                        file = await _options.OnCMoveRetrieve!(match, ct).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Retrieve failed
                        file = null;
                    }

                    if (file == null)
                    {
                        failed++;
                    }
                    else
                    {
                        try
                        {
                            var storeResponse = await storeScu.SendAsync(file, ct: ct).ConfigureAwait(false);

                            if (storeResponse.Status.IsSuccess)
                                completed++;
                            else if (storeResponse.Status.IsWarning)
                                warning++;
                            else
                                failed++;
                        }
                        catch (Exception)
                        {
                            failed++;
                        }
                    }

                    remaining--;

                    // Send intermediate Pending C-MOVE-RSP on the ORIGINAL association
                    var progress = new SubOperationProgress(remaining, completed, failed, warning);
                    await SendQRResponseWithProgressAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, responseCommandField,
                        DicomStatus.Pending.Code, progress, ct)
                        .ConfigureAwait(false);

                    // Check for C-CANCEL: peek for incoming PDU on the original stream
                    // Note: In a production implementation we'd use async polling, but for
                    // simplicity we check the CancellationToken which may be triggered by
                    // the RunDimseLoopAsync if a C-CANCEL is received
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                    }
                }
            }
            finally
            {
                // Close forwarding association
                if (forwardingClient != null)
                {
                    try
                    {
                        await forwardingClient.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Best effort cleanup
                    }
                }
            }

            // Send final C-MOVE-RSP
            var finalProgress = new SubOperationProgress(0, completed, failed, warning);
            ushort finalStatus;

            if (cancelled)
            {
                finalStatus = DicomStatus.Cancel.Code;
            }
            else if (failed == 0 && warning == 0)
            {
                finalStatus = DicomStatus.Success.Code;
            }
            else if (completed == 0 && warning == 0)
            {
                // All failed
                finalStatus = 0xA702; // Unable to perform sub-operations
            }
            else
            {
                // Some succeeded, some failed
                finalStatus = DicomStatus.SubOperationsCompleteWithFailures.Code;
            }

            await SendQRResponseWithProgressAsync(
                stream, command.PresentationContextId, command.MessageID,
                command.SOPClassUID, responseCommandField,
                finalStatus, finalProgress, ct)
                .ConfigureAwait(false);
        }

        private async Task HandleCGetAsync(
            Stream stream,
            DicomAssociation association,
            QRCommandInfo command,
            CancellationToken ct)
        {
            // Always read the identifier dataset even if no handler is registered (Pitfall 7)
            DicomDataset? identifierDataset = null;

            if (command.HasDataset)
            {
                try
                {
                    var datasetBytes = await ReadDatasetAsync(stream, ct).ConfigureAwait(false);
                    identifierDataset = ParseDataset(datasetBytes, association, command.PresentationContextId);
                }
                catch (Exception)
                {
                    await SendQRFailureResponseAsync(
                        stream, command.PresentationContextId, command.MessageID,
                        command.SOPClassUID, command.CommandFieldValue, ct)
                        .ConfigureAwait(false);
                    return;
                }
            }

            var responseCommandField = (ushort)(command.CommandFieldValue | 0x8000);

            // Check if all required handlers are configured
            if (!_options.HasCGetHandler)
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    0xA900, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Collect all matches (need total count for progress tracking)
            var matches = new List<DicomDataset>();
            try
            {
                const int maxMatches = 10000;
                await foreach (var match in _options.OnCFind!(identifierDataset!, ct).ConfigureAwait(false))
                {
                    matches.Add(match);
                    if (matches.Count >= maxMatches)
                        break;
                }
            }
            catch (Exception)
            {
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    0xC000, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            if (matches.Count == 0)
            {
                // No matches - send Success with zero sub-ops
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.Success.Code, SubOperationProgress.Empty, ct)
                    .ConfigureAwait(false);
                return;
            }

            // Send C-STORE sub-operations on the SAME association (per PS3.4 C.4.3)
            ushort completed = 0;
            ushort failed = 0;
            ushort warning = 0;
            ushort remaining = (ushort)matches.Count;
            bool cancelled = false;
            ushort subOpMessageId = (ushort)(command.MessageID + 1);

            for (int i = 0; i < matches.Count; i++)
            {
                if (cancelled) break;

                var match = matches[i];
                DicomFile? file = null;

                try
                {
                    file = await _options.OnCGetRetrieve!(match, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    file = null;
                }

                if (file == null)
                {
                    failed++;
                }
                else
                {
                    // Get the SOP Class UID and SOP Instance UID from the file
                    var sopClassStr = file.Dataset.GetString(DicomTag.SOPClassUID);
                    var sopInstanceStr = file.Dataset.GetString(DicomTag.SOPInstanceUID);
                    var sopClassUid = new DicomUID(sopClassStr?.TrimEnd('\0', ' ') ?? string.Empty);
                    var sopInstanceUid = new DicomUID(sopInstanceStr?.TrimEnd('\0', ' ') ?? string.Empty);

                    // Find an accepted presentation context for this SOP Class
                    var storePcId = FindAcceptedContextForSopClass(association, sopClassUid);

                    if (storePcId == 0)
                    {
                        // No matching presentation context accepted (SCU didn't propose Storage SOP Class)
                        failed++;
                    }
                    else
                    {
                        try
                        {
                            // Send C-STORE-RQ on the SAME association
                            await SendCStoreSubOpRequestAsync(
                                stream, storePcId, subOpMessageId,
                                sopClassUid, sopInstanceUid,
                                file.Dataset, association, ct)
                                .ConfigureAwait(false);

                            // Read C-STORE-RSP from the SCU (SCU acts as SCP for these sub-ops)
                            var storeStatus = await ReadCStoreSubOpResponseAsync(stream, ct)
                                .ConfigureAwait(false);

                            if (storeStatus.IsSuccess)
                                completed++;
                            else if (storeStatus.IsWarning)
                                warning++;
                            else
                                failed++;
                        }
                        catch (Exception)
                        {
                            failed++;
                        }

                        subOpMessageId++;
                    }
                }

                remaining--;

                // Send intermediate Pending C-GET-RSP on the SAME association
                var progress = new SubOperationProgress(remaining, completed, failed, warning);
                await SendQRResponseWithProgressAsync(
                    stream, command.PresentationContextId, command.MessageID,
                    command.SOPClassUID, responseCommandField,
                    DicomStatus.Pending.Code, progress, ct)
                    .ConfigureAwait(false);

                // Check for C-CANCEL
                if (ct.IsCancellationRequested)
                {
                    cancelled = true;
                }
            }

            // Send final C-GET-RSP
            var finalProgress = new SubOperationProgress(0, completed, failed, warning);
            ushort finalStatus;

            if (cancelled)
            {
                finalStatus = DicomStatus.Cancel.Code;
            }
            else if (failed == 0 && warning == 0)
            {
                finalStatus = DicomStatus.Success.Code;
            }
            else if (completed == 0 && warning == 0)
            {
                // All failed
                finalStatus = 0xA702; // Unable to perform sub-operations
            }
            else
            {
                // Some succeeded, some failed
                finalStatus = DicomStatus.SubOperationsCompleteWithFailures.Code;
            }

            await SendQRResponseWithProgressAsync(
                stream, command.PresentationContextId, command.MessageID,
                command.SOPClassUID, responseCommandField,
                finalStatus, finalProgress, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Finds an accepted presentation context ID for the given SOP Class UID.
        /// Returns 0 if no matching context is accepted.
        /// </summary>
        private static byte FindAcceptedContextForSopClass(DicomAssociation association, DicomUID sopClassUid)
        {
            var contexts = association.AcceptedContexts;
            if (contexts == null) return 0;

            foreach (var ctx in contexts)
            {
                if (ctx.AbstractSyntax == sopClassUid)
                    return ctx.Id;
            }

            return 0;
        }

        /// <summary>
        /// Sends a C-STORE-RQ sub-operation on the same association (used by C-GET SCP).
        /// </summary>
        private static async Task SendCStoreSubOpRequestAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageId,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            DicomDataset dataset,
            DicomAssociation association,
            CancellationToken ct)
        {
            // Build C-STORE-RQ command
            var commandData = BuildCStoreRequestCommand(messageId, sopClassUid, sopInstanceUid);

            // Serialize the dataset using the transfer syntax from the presentation context
            var datasetBytes = SerializeDatasetBytes(dataset, presentationContextId, association);

            // Create PDVs: command PDV and dataset PDV
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var commandPdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            var datasetPdv = new PresentationDataValue(
                presentationContextId,
                isCommand: false,
                isLastFragment: true,
                datasetBytes);

            writer.WritePData(new[] { commandPdv, datasetPdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Reads a C-STORE-RSP from the SCU after sending a C-STORE sub-operation (used by C-GET SCP).
        /// </summary>
        private static async Task<DicomStatus> ReadCStoreSubOpResponseAsync(
            Stream stream,
            CancellationToken ct)
        {
            // Read the next P-DATA PDU which should contain the C-STORE-RSP command
            var (pduType, pduBody) = await ReadPduAsync(stream, ct).ConfigureAwait(false);

            if (pduType != PduType.PDataTransfer)
            {
                return DicomStatus.ProcessingFailure;
            }

            var reader = new PduReader(pduBody);
            while (reader.TryReadPresentationDataValue(
                out _,
                out bool isCommand,
                out bool isLastFragment,
                out var data))
            {
                if (isCommand && isLastFragment)
                {
                    // Parse status from the C-STORE-RSP command
                    var status = ParseStatusFromCommand(data);
                    return new DicomStatus(status);
                }
            }

            return DicomStatus.ProcessingFailure;
        }

        /// <summary>
        /// Parses the Status field (0000,0900) from a command dataset.
        /// </summary>
        private static ushort ParseStatusFromCommand(ReadOnlySpan<byte> commandData)
        {
            int offset = 0;
            while (offset + 8 <= commandData.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(commandData.Slice(offset + 4));

                if (group == 0x0000 && element == 0x0900 && // Status tag
                    length >= 2 && offset + 8 + length <= commandData.Length)
                {
                    return BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 8));
                }

                offset += 8 + (int)length;
            }

            return 0x0110; // Processing Failure as default
        }

        /// <summary>
        /// Builds a C-STORE-RQ command dataset for sub-operations.
        /// </summary>
        private static byte[] BuildCStoreRequestCommand(
            ushort messageId,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid)
        {
            var buffer = new BufferWriter();

            // SOP Class UID
            var sopClassUidBytes = Encoding.ASCII.GetBytes(sopClassUid.ToString());
            var sopClassUidLength = sopClassUidBytes.Length;
            if (sopClassUidLength % 2 != 0) sopClassUidLength++;

            // SOP Instance UID
            var sopInstanceUidBytes = Encoding.ASCII.GetBytes(sopInstanceUid.ToString());
            var sopInstanceUidLength = sopInstanceUidBytes.Length;
            if (sopInstanceUidLength % 2 != 0) sopInstanceUidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, sopClassUidBytes, sopClassUidLength);

            // (0000,0100) CommandField = 0x0001 (C-STORE-RQ)
            WriteElementUS(buffer, 0x0000, 0x0100, CommandFields.CStoreRequest);

            // (0000,0110) MessageID
            WriteElementUS(buffer, 0x0000, 0x0110, messageId);

            // (0000,0700) Priority = MEDIUM
            WriteElementUS(buffer, 0x0000, 0x0700, 0);

            // (0000,0800) CommandDataSetType = 0x0102 (dataset present)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0102);

            // (0000,1000) AffectedSOPInstanceUID
            WriteElement(buffer, 0x0000, 0x1000, sopInstanceUidBytes, sopInstanceUidLength);

            return buffer.WrittenSpan.ToArray();
        }

        private static async Task SendQRResponseWithProgressAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode,
            SubOperationProgress progress,
            CancellationToken ct)
        {
            var commandData = BuildQRResponseCommandWithProgress(
                messageIdBeingRespondedTo, sopClassUid, responseCommandField, statusCode, progress);

            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            writer.WritePData(new[] { pdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static byte[] BuildQRResponseCommandWithProgress(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode,
            SubOperationProgress progress)
        {
            var buffer = new BufferWriter();

            // SOP Class UID
            var sopClassUidBytes = Encoding.ASCII.GetBytes(sopClassUid.ToString());
            var sopClassUidLength = sopClassUidBytes.Length;
            if (sopClassUidLength % 2 != 0) sopClassUidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, sopClassUidBytes, sopClassUidLength);

            // (0000,0100) CommandField
            WriteElementUS(buffer, 0x0000, 0x0100, responseCommandField);

            // (0000,0120) MessageIDBeingRespondedTo
            WriteElementUS(buffer, 0x0000, 0x0120, messageIdBeingRespondedTo);

            // (0000,0800) CommandDataSetType = 0x0101 (no dataset)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0101);

            // (0000,0900) Status
            WriteElementUS(buffer, 0x0000, 0x0900, statusCode);

            // (0000,1020) NumberOfRemainingSuboperations
            WriteElementUS(buffer, 0x0000, 0x1020, progress.Remaining);

            // (0000,1021) NumberOfCompletedSuboperations
            WriteElementUS(buffer, 0x0000, 0x1021, progress.Completed);

            // (0000,1022) NumberOfFailedSuboperations
            WriteElementUS(buffer, 0x0000, 0x1022, progress.Failed);

            // (0000,1023) NumberOfWarningSuboperations
            WriteElementUS(buffer, 0x0000, 0x1023, progress.Warning);

            return buffer.WrittenSpan.ToArray();
        }

        private static async Task SendQRResponseAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode,
            CancellationToken ct)
        {
            var commandData = BuildQRResponseCommand(
                messageIdBeingRespondedTo, sopClassUid, responseCommandField, statusCode);

            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            writer.WritePData(new[] { pdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendDimseResponseWithDatasetAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode,
            DicomDataset identifierDataset,
            DicomAssociation association,
            CancellationToken ct)
        {
            // Build command with DataSetPresent since we have an identifier
            var commandData = BuildQRResponseCommandWithDataset(
                messageIdBeingRespondedTo, sopClassUid, responseCommandField, statusCode);

            // Serialize the identifier dataset using the transfer syntax from the presentation context
            var datasetBytes = SerializeDatasetBytes(identifierDataset, presentationContextId, association);

            // Create PDVs: command PDV and dataset PDV
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var commandPdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            var datasetPdv = new PresentationDataValue(
                presentationContextId,
                isCommand: false,
                isLastFragment: true,
                datasetBytes);

            writer.WritePData(new[] { commandPdv, datasetPdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static byte[] BuildQRResponseCommandWithDataset(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode)
        {
            var buffer = new BufferWriter();

            // SOP Class UID
            var sopClassUidBytes = Encoding.ASCII.GetBytes(sopClassUid.ToString());
            var sopClassUidLength = sopClassUidBytes.Length;
            if (sopClassUidLength % 2 != 0) sopClassUidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, sopClassUidBytes, sopClassUidLength);

            // (0000,0100) CommandField
            WriteElementUS(buffer, 0x0000, 0x0100, responseCommandField);

            // (0000,0120) MessageIDBeingRespondedTo
            WriteElementUS(buffer, 0x0000, 0x0120, messageIdBeingRespondedTo);

            // (0000,0800) CommandDataSetType = 0x0102 (dataset present)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0102);

            // (0000,0900) Status
            WriteElementUS(buffer, 0x0000, 0x0900, statusCode);

            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] SerializeDatasetBytes(
            DicomDataset dataset,
            byte contextId,
            DicomAssociation association)
        {
            // Get the accepted transfer syntax for this presentation context
            var context = association.GetPresentationContext(contextId);
            var transferSyntax = context?.AcceptedTransferSyntax ?? TransferSyntax.ImplicitVRLittleEndian;

            // Serialize using DicomStreamWriter
            var buffer = new BufferWriter();
            var writer = new DicomStreamWriter(
                buffer,
                transferSyntax.IsExplicitVR,
                transferSyntax.IsLittleEndian);

            writer.WriteDataset(dataset);

            return buffer.WrittenSpan.ToArray();
        }

        private static Task<byte[]> ReadDatasetAsync(Stream stream, CancellationToken ct)
        {
            return ReadDatasetAsync(stream, long.MaxValue, ct);
        }

        private static async Task<byte[]> ReadDatasetAsync(Stream stream, long maxSize, CancellationToken ct)
        {
            // Read P-DATA PDUs until we get the last fragment
            using var ms = new MemoryStream();
            bool lastFragment = false;

            while (!lastFragment)
            {
                var (pduType, pduBody) = await ReadPduAsync(stream, ct).ConfigureAwait(false);

                if (pduType != PduType.PDataTransfer)
                {
                    throw new InvalidOperationException($"Expected P-DATA-TF, got {pduType}");
                }

                var reader = new PduReader(pduBody);

                while (reader.TryReadPresentationDataValue(
                    out _,  // contextId
                    out bool isCommand,
                    out bool isLast,
                    out var data))
                {
                    if (!isCommand)
                    {
                        // Check size incrementally to prevent memory exhaustion
                        if (ms.Length + data.Length > maxSize)
                        {
                            throw new InvalidOperationException(
                                $"Dataset size exceeds maximum allowed ({maxSize} bytes). " +
                                "Consider using streaming mode for large datasets.");
                        }

                        // This is dataset data
                        ms.Write(data.ToArray(), 0, data.Length);
                        lastFragment = isLast;
                    }
                }
            }

            return ms.ToArray();
        }

        private static async Task ReadAndDiscardDatasetAsync(Stream stream, CancellationToken ct)
        {
            // Read and discard P-DATA PDUs until we get the last fragment
            bool lastFragment = false;

            while (!lastFragment)
            {
                var (pduType, pduBody) = await ReadPduAsync(stream, ct).ConfigureAwait(false);

                if (pduType != PduType.PDataTransfer)
                {
                    break; // Unexpected PDU, stop reading
                }

                var reader = new PduReader(pduBody);

                while (reader.TryReadPresentationDataValue(
                    out _,  // contextId
                    out bool isCommand,
                    out bool isLast,
                    out _)) // data - discarded
                {
                    if (!isCommand)
                    {
                        lastFragment = isLast;
                    }
                }
            }
        }

        private static DicomDataset ParseDataset(byte[] data, DicomAssociation association, byte contextId)
        {
            // Get the accepted transfer syntax for this presentation context
            var context = association.GetPresentationContext(contextId);
            var transferSyntax = context?.AcceptedTransferSyntax ?? TransferSyntax.ImplicitVRLittleEndian;

            // Parse the dataset using DicomStreamReader
            var dataset = new DicomDataset();
            var span = data.AsSpan();
            var reader = new DicomStreamReader(span, transferSyntax.IsExplicitVR, transferSyntax.IsLittleEndian);

            // Create SequenceParser for handling sequences
            var sequenceParser = new SequenceParser(
                transferSyntax.IsExplicitVR,
                transferSyntax.IsLittleEndian,
                null); // default options

            while (reader.TryReadElementHeader(out var tag, out var vr, out var valueLength))
            {
                if (valueLength == 0xFFFFFFFF)
                {
                    // Undefined length element
                    if (tag == DicomTag.PixelData && reader.Remaining > 0)
                    {
                        // Encapsulated pixel data - store all remaining bytes
                        var remainingData = reader.ReadBytes(reader.Remaining);
                        var pixelElement = new DicomBinaryElement(tag, vr, remainingData.ToArray());
                        dataset.Add(pixelElement);
                    }
                    else if (vr == DicomVR.SQ)
                    {
                        // Sequence with undefined length - parse using SequenceParser
                        var remainingBuffer = span.Slice(reader.Position);
                        var sequence = sequenceParser.ParseSequence(remainingBuffer, tag, valueLength, dataset);
                        dataset.Add(sequence);

                        // Advance reader position past the sequence content and delimiter
                        var bytesConsumed = FindSequenceEndPosition(remainingBuffer, transferSyntax.IsExplicitVR, transferSyntax.IsLittleEndian);
                        reader.Skip(bytesConsumed);
                    }
                    else
                    {
                        // Undefined length for non-SQ/non-PixelData is not supported
                        throw new DicomDataException($"Undefined length for non-sequence element {tag} not supported");
                    }
                    continue;
                }

                // Handle defined-length sequences
                if (vr == DicomVR.SQ)
                {
                    // Sequence with defined length
                    var seqBuffer = span.Slice(reader.Position, (int)valueLength);
                    var sequence = sequenceParser.ParseSequence(seqBuffer, tag, valueLength, dataset);
                    dataset.Add(sequence);
                    reader.Skip((int)valueLength);
                    continue;
                }

                if (!reader.TryReadValue(valueLength, out var value))
                    break;

                // Create element based on VR type (string, numeric, or binary)
                var valueData = value.ToArray();
                var vrInfo = DicomVRInfo.GetInfo(vr);
                IDicomElement element = vrInfo.IsStringVR
                    ? new DicomStringElement(tag, vr, valueData)
                    : new DicomNumericElement(tag, vr, valueData);
                dataset.Add(element);
            }

            return dataset;
        }

        private static int FindSequenceEndPosition(ReadOnlySpan<byte> buffer, bool explicitVR, bool littleEndian)
        {
            // Scan for SequenceDelimitationItem (FFFE,E0DD) accounting for nesting
            // Returns bytes consumed including the delimiter (8 bytes: tag + zero length)
            int contentLength = FindSequenceContentLengthStatic(buffer, explicitVR, littleEndian);
            return contentLength + 8; // Add 8 bytes for SequenceDelimitationItem
        }

        private static int FindSequenceContentLengthStatic(ReadOnlySpan<byte> buffer, bool explicitVR, bool littleEndian)
        {
            // Scan for SequenceDelimitationItem (FFFE,E0DD)
            int position = 0;
            int depth = 0;

            while (position + 8 <= buffer.Length)
            {
                ushort group = littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position))
                    : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position));
                ushort element = littleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position + 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position + 2));

                var tag = new DicomTag(group, element);

                if (tag == DicomTag.Item)
                {
                    // Read item length
                    uint itemLength = littleEndian
                        ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 4))
                        : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 4));
                    position += 8;

                    if (itemLength == 0xFFFFFFFF)
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
                    // Regular element - skip it properly
                    if (explicitVR)
                    {
                        // Explicit VR: tag(4) + VR(2)
                        if (position + 8 > buffer.Length)
                            throw new DicomDataException("Unexpected end of data in sequence");

                        var vr = DicomVR.FromBytes(buffer.Slice(position + 4, 2));
                        if (vr.Is32BitLength)
                        {
                            // Long VR: tag(4) + VR(2) + reserved(2) + length(4) = 12 bytes header
                            if (position + 12 > buffer.Length)
                                throw new DicomDataException("Unexpected end of data in sequence");

                            uint elemLen = littleEndian
                                ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 8))
                                : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 8));
                            position += 12;

                            if (elemLen != 0xFFFFFFFF)
                            {
                                position += (int)elemLen;
                            }
                            else if (vr == DicomVR.SQ)
                            {
                                // Nested sequence with undefined length - increment depth
                                depth++;
                            }
                        }
                        else
                        {
                            // Short VR: tag(4) + VR(2) + length(2) = 8 bytes header
                            ushort elemLen = littleEndian
                                ? BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position + 6))
                                : BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(position + 6));
                            position += 8 + elemLen;
                        }
                    }
                    else
                    {
                        // Implicit VR: tag(4) + length(4) = 8 bytes header
                        uint elemLen = littleEndian
                            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position + 4))
                            : BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(position + 4));
                        position += 8;

                        if (elemLen != 0xFFFFFFFF)
                        {
                            position += (int)elemLen;
                        }
                        else
                        {
                            // Implicit VR undefined length - must be SQ, increment depth
                            var entry = DicomDictionary.Default.GetEntry(tag);
                            var vr = entry?.DefaultVR ?? DicomVR.UN;
                            if (vr == DicomVR.SQ)
                            {
                                depth++;
                            }
                        }
                    }
                }
            }

            throw new DicomDataException("Could not find SequenceDelimitationItem");
        }

        /// <summary>
        /// Stops accepting new connections and waits for active associations to complete.
        /// </summary>
        /// <returns>A task representing the asynchronous stop operation.</returns>
        public async Task StopAsync()
        {
            if (_disposed)
                return;

#if NET6_0_OR_GREATER
            await _cts.CancelAsync().ConfigureAwait(false);
#else
            _cts.Cancel();
#endif
            _listener.Stop();

            // Wait for accept loop to exit
            if (_acceptTask != null)
            {
                try
                {
                    await _acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            // Wait for active associations with timeout
            Task[] tasks;
            lock (_activeTasks)
                tasks = _activeTasks.ToArray();

            if (tasks.Length > 0)
            {
                var allCompleted = Task.WhenAll(tasks);
                await Task.WhenAny(
                    allCompleted,
                    Task.Delay(_options.ShutdownTimeout)).ConfigureAwait(false);

                // If timeout elapsed and tasks still running, they'll be abandoned
                // (the CancellationToken is already cancelled, so they should exit soon)
            }
        }

        /// <summary>
        /// Disposes the server and releases all resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            await StopAsync().ConfigureAwait(false);
            _cts.Dispose();
            _semaphore.Dispose();
        }

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
                throw new ObjectDisposedException(nameof(DicomServer));
#endif
        }

        #region PDU I/O Helpers

        private static async Task<(string CallingAE, string CalledAE, List<PresentationContext> Contexts)>
            ReadAssociateRequestAsync(Stream stream, CancellationToken ct)
        {
            var (pduType, body) = await ReadPduAsync(stream, ct).ConfigureAwait(false);

            if (pduType != PduType.AssociateRequest)
            {
                throw new InvalidOperationException($"Expected A-ASSOCIATE-RQ, got {pduType}");
            }

            var reader = new PduReader(body);

            // Read fixed fields
            if (!reader.TryReadAssociateRequest(
                out _,          // protocolVersion
                out var calledAE,
                out var callingAE,
                out var variableItems))
            {
                throw new InvalidOperationException("Failed to parse A-ASSOCIATE-RQ fixed fields");
            }

            // Parse variable items to extract presentation contexts
            var contexts = ParsePresentationContextsFromVariableItems(variableItems);

            return (callingAE, calledAE, contexts);
        }

        private static List<PresentationContext> ParsePresentationContextsFromVariableItems(ReadOnlySpan<byte> variableItems)
        {
            var contexts = new List<PresentationContext>();
            var reader = new PduReader(variableItems);

            while (reader.TryReadVariableItem(out var itemType, out var itemLength))
            {
                if (itemType == ItemType.PresentationContextRequest)
                {
                    // Parse presentation context
                    if (reader.TryReadPresentationContextRequest(out var contextId, out var itemData))
                    {
                        // Parse abstract syntax and transfer syntaxes from itemData
                        var (abstractSyntax, transferSyntaxes) = ParsePresentationContextItems(itemData, itemLength - 4);

                        if (!abstractSyntax.IsEmpty && transferSyntaxes.Count > 0)
                        {
                            var tsArray = transferSyntaxes.Select(ts => TransferSyntax.FromUID(ts)).ToArray();
                            contexts.Add(new PresentationContext(contextId, abstractSyntax, tsArray));
                        }
                    }
                }
                else
                {
                    // Skip other items (ApplicationContext, UserInformation, etc.)
                    reader.TrySkip(itemLength);
                }
            }

            return contexts;
        }

        private static (DicomUID AbstractSyntax, List<DicomUID> TransferSyntaxes) ParsePresentationContextItems(
            ReadOnlySpan<byte> data, int maxLength)
        {
            var abstractSyntax = new DicomUID(string.Empty);
            var transferSyntaxes = new List<DicomUID>();

            var reader = new PduReader(data.Slice(0, Math.Min(data.Length, maxLength)));

            while (reader.TryReadVariableItem(out var subItemType, out var subItemLength))
            {
                if (subItemType == ItemType.AbstractSyntax)
                {
                    if (reader.TryReadUidString(subItemLength, out var uid))
                    {
                        abstractSyntax = new DicomUID(uid);
                    }
                }
                else if (subItemType == ItemType.TransferSyntax)
                {
                    if (reader.TryReadUidString(subItemLength, out var uid))
                    {
                        transferSyntaxes.Add(new DicomUID(uid));
                    }
                }
                else
                {
                    reader.TrySkip(subItemLength);
                }
            }

            return (abstractSyntax, transferSyntaxes);
        }

        private static async Task<(PduType Type, byte[] Body)> ReadPduAsync(Stream stream, CancellationToken ct)
        {
            return await ReadPduAsync(stream, PduConstants.AbsoluteMaxPduLength, ct).ConfigureAwait(false);
        }

        private static async Task<(PduType Type, byte[] Body)> ReadPduAsync(Stream stream, uint maxLength, CancellationToken ct)
        {
            // Read 6-byte PDU header
            var header = new byte[Pdu.PduConstants.HeaderLength];
            await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);

            var pduType = (PduType)header[0];
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));

            // Validate PDU length to prevent denial-of-service attacks
            // Association PDUs (types 1-3) have stricter limits than data transfer PDUs
            uint effectiveMaxLength = pduType switch
            {
                PduType.AssociateRequest or PduType.AssociateAccept or PduType.AssociateReject
                    => Math.Min(maxLength, PduConstants.MaxAssociationPduLength),
                _ => Math.Min(maxLength, PduConstants.AbsoluteMaxPduLength)
            };

            if (length > effectiveMaxLength)
            {
                throw new DicomNetworkException(
                    $"PDU length {length} exceeds maximum allowed length {effectiveMaxLength}. " +
                    "This may indicate a malformed PDU or denial-of-service attack.");
            }

            // Read PDU body
            var body = new byte[length];
            await ReadExactlyAsync(stream, body, ct).ConfigureAwait(false);

            return (pduType, body);
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
#if NET8_0_OR_GREATER
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct)
                    .ConfigureAwait(false);
#endif
                if (read == 0)
                    throw new EndOfStreamException("Connection closed before PDU was fully received.");

                totalRead += read;
            }
        }

        private static async Task SendAssociateAcceptAsync(
            Stream stream,
            string callingAE,
            string calledAE,
            IReadOnlyList<PresentationContext> acceptedContexts,
            CancellationToken ct)
        {
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateAccept(
                calledAE,
                callingAE,
                acceptedContexts.ToList(),
                UserInformation.Default);

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendAssociateRejectAsync(
            Stream stream,
            AssociationRequestResult result,
            CancellationToken ct)
        {
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WriteAssociateReject(result.RejectResult, result.RejectSource, result.RejectReason);

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendReleaseResponseAsync(Stream stream, CancellationToken ct)
        {
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);
            writer.WriteReleaseResponse();

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendCEchoResponseAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageId,
            DicomStatus status,
            CancellationToken ct)
        {
            // Build C-ECHO-RSP command dataset
            var commandData = BuildCEchoResponseCommand(messageId, status);

            // Wrap in P-DATA-TF
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            writer.WritePData(new[] { pdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendCStoreResponseAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageIdBeingRespondedTo,
            DicomUID affectedSopClassUid,
            DicomUID affectedSopInstanceUid,
            DicomStatus status,
            CancellationToken ct)
        {
            // Build C-STORE-RSP command dataset
            var commandData = BuildCStoreResponseCommand(
                messageIdBeingRespondedTo,
                affectedSopClassUid,
                affectedSopInstanceUid,
                status);

            // Wrap in P-DATA-TF
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            writer.WritePData(new[] { pdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static async Task SendQRFailureResponseAsync(
            Stream stream,
            byte presentationContextId,
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort requestCommandField,
            CancellationToken ct)
        {
            // Determine the response command field from the request command field
            ushort responseCommandField = (ushort)(requestCommandField | 0x8000);

            // Build failure response command dataset (0xA900 = Unable to Process / Identifier does not match SOP Class)
            var commandData = BuildQRResponseCommand(
                messageIdBeingRespondedTo,
                sopClassUid,
                responseCommandField,
                0xA900); // Unable to Process

            // Wrap in P-DATA-TF
            var buffer = new BufferWriter();
            var writer = new PduWriter(buffer);

            var pdv = new PresentationDataValue(
                presentationContextId,
                isCommand: true,
                isLastFragment: true,
                commandData);

            writer.WritePData(new[] { pdv });

#if NET8_0_OR_GREATER
            await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
#else
            var array = buffer.WrittenSpan.ToArray();
            await stream.WriteAsync(array, 0, array.Length, ct).ConfigureAwait(false);
#endif
        }

        private static byte[] BuildQRResponseCommand(
            ushort messageIdBeingRespondedTo,
            DicomUID sopClassUid,
            ushort responseCommandField,
            ushort statusCode)
        {
            var buffer = new BufferWriter();

            // SOP Class UID
            var sopClassUidBytes = Encoding.ASCII.GetBytes(sopClassUid.ToString());
            var sopClassUidLength = sopClassUidBytes.Length;
            if (sopClassUidLength % 2 != 0) sopClassUidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, sopClassUidBytes, sopClassUidLength);

            // (0000,0100) CommandField
            WriteElementUS(buffer, 0x0000, 0x0100, responseCommandField);

            // (0000,0120) MessageIDBeingRespondedTo
            WriteElementUS(buffer, 0x0000, 0x0120, messageIdBeingRespondedTo);

            // (0000,0800) CommandDataSetType = 0x0101 (no dataset)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0101);

            // (0000,0900) Status
            WriteElementUS(buffer, 0x0000, 0x0900, statusCode);

            return buffer.WrittenSpan.ToArray();
        }

        #endregion

        #region DIMSE Command Parsing/Building

        // These are minimal implementations for C-ECHO only.
        // Full DIMSE support will come from DicomCommand in plan 10-05.

        private static ushort ParseCommandField(ReadOnlySpan<byte> commandData)
        {
            // Command dataset is encoded in Implicit VR Little Endian
            // Look for tag (0000,0100) = CommandField
            int offset = 0;
            while (offset + 8 <= commandData.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(commandData.Slice(offset + 4));

                if (group == 0x0000 && element == 0x0100 && // CommandField tag
                    length >= 2 && offset + 8 + length <= commandData.Length)
                {
                    return BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 8));
                }

                offset += 8 + (int)length;
            }

            return 0; // Not found
        }

        private static ushort ParseMessageId(ReadOnlySpan<byte> commandData)
        {
            // Look for tag (0000,0110) = MessageID
            int offset = 0;
            while (offset + 8 <= commandData.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(commandData.Slice(offset + 4));

                if (group == 0x0000 && element == 0x0110 && // MessageID tag
                    length >= 2 && offset + 8 + length <= commandData.Length)
                {
                    return BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 8));
                }

                offset += 8 + (int)length;
            }

            return 0; // Not found
        }

        private static byte[] BuildCStoreResponseCommand(
            ushort messageIdBeingRespondedTo,
            DicomUID affectedSopClassUid,
            DicomUID affectedSopInstanceUid,
            DicomStatus status)
        {
            // Build command dataset in Implicit VR Little Endian
            // Required elements for C-STORE-RSP:
            // - AffectedSOPClassUID (0000,0002)
            // - CommandField (0000,0100) = 0x8001
            // - MessageIDBeingRespondedTo (0000,0120)
            // - CommandDataSetType (0000,0800) = 0x0101 (no dataset)
            // - Status (0000,0900)
            // - AffectedSOPInstanceUID (0000,1000)

            var buffer = new BufferWriter();

            // SOP Class UID
            var sopClassUidBytes = Encoding.ASCII.GetBytes(affectedSopClassUid.ToString());
            var sopClassUidLength = sopClassUidBytes.Length;
            if (sopClassUidLength % 2 != 0) sopClassUidLength++;

            // SOP Instance UID
            var sopInstanceUidBytes = Encoding.ASCII.GetBytes(affectedSopInstanceUid.ToString());
            var sopInstanceUidLength = sopInstanceUidBytes.Length;
            if (sopInstanceUidLength % 2 != 0) sopInstanceUidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, sopClassUidBytes, sopClassUidLength);

            // (0000,0100) CommandField = 0x8001 (C-STORE-RSP)
            WriteElementUS(buffer, 0x0000, 0x0100, CommandFields.CStoreResponse);

            // (0000,0120) MessageIDBeingRespondedTo
            WriteElementUS(buffer, 0x0000, 0x0120, messageIdBeingRespondedTo);

            // (0000,0800) CommandDataSetType = 0x0101 (no dataset)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0101);

            // (0000,0900) Status
            WriteElementUS(buffer, 0x0000, 0x0900, status.Code);

            // (0000,1000) AffectedSOPInstanceUID
            WriteElement(buffer, 0x0000, 0x1000, sopInstanceUidBytes, sopInstanceUidLength);

            return buffer.WrittenSpan.ToArray();
        }

        private static CStoreCommandInfo ParseCStoreCommand(ReadOnlySpan<byte> commandData, byte contextId)
        {
            ushort messageId = 0;
            string? sopClassUid = null;
            string? sopInstanceUid = null;
            ushort dataSetType = 0x0101; // Default: no dataset

            int offset = 0;
            while (offset + 8 <= commandData.Length)
            {
                ushort group = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset));
                ushort element = BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 2));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(commandData.Slice(offset + 4));

                if (offset + 8 + length > commandData.Length)
                    break;

                var valueSpan = commandData.Slice(offset + 8, (int)length);

                if (group == 0x0000)
                {
                    switch (element)
                    {
                        case 0x0002: // AffectedSOPClassUID
                            sopClassUid = Encoding.ASCII.GetString(valueSpan.ToArray()).TrimEnd('\0', ' ');
                            break;
                        case 0x0110: // MessageID
                            if (length >= 2)
                                messageId = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);
                            break;
                        case 0x0800: // CommandDataSetType
                            if (length >= 2)
                                dataSetType = BinaryPrimitives.ReadUInt16LittleEndian(valueSpan);
                            break;
                        case 0x1000: // AffectedSOPInstanceUID
                            sopInstanceUid = Encoding.ASCII.GetString(valueSpan.ToArray()).TrimEnd('\0', ' ');
                            break;
                    }
                }

                offset += 8 + (int)length;
            }

            return new CStoreCommandInfo(
                contextId,
                messageId,
                new DicomUID(sopClassUid ?? string.Empty),
                new DicomUID(sopInstanceUid ?? string.Empty),
                hasDataset: dataSetType != 0x0101);
        }

        private static byte[] BuildCEchoResponseCommand(ushort messageIdBeingRespondedTo, DicomStatus status)
        {
            // Build command dataset in Implicit VR Little Endian
            // Required elements for C-ECHO-RSP:
            // - AffectedSOPClassUID (0000,0002)
            // - CommandField (0000,0100) = 0x8030
            // - MessageIDBeingRespondedTo (0000,0120)
            // - CommandDataSetType (0000,0800) = 0x0101 (no dataset)
            // - Status (0000,0900)

            var buffer = new BufferWriter();

            // Verification SOP Class UID: 1.2.840.10008.1.1
            var verificationUid = Encoding.ASCII.GetBytes("1.2.840.10008.1.1");
            // Pad to even length if needed
            var uidLength = verificationUid.Length;
            if (uidLength % 2 != 0) uidLength++;

            // (0000,0002) AffectedSOPClassUID
            WriteElement(buffer, 0x0000, 0x0002, verificationUid, uidLength);

            // (0000,0100) CommandField = 0x8030 (C-ECHO-RSP)
            WriteElementUS(buffer, 0x0000, 0x0100, CommandFields.CEchoResponse);

            // (0000,0120) MessageIDBeingRespondedTo
            WriteElementUS(buffer, 0x0000, 0x0120, messageIdBeingRespondedTo);

            // (0000,0800) CommandDataSetType = 0x0101 (no dataset)
            WriteElementUS(buffer, 0x0000, 0x0800, 0x0101);

            // (0000,0900) Status
            WriteElementUS(buffer, 0x0000, 0x0900, status.Code);

            return buffer.WrittenSpan.ToArray();
        }

        private static void WriteElement(BufferWriter buffer, ushort group, ushort element, byte[] value, int length)
        {
            var span = buffer.GetSpan(8 + length);
            BinaryPrimitives.WriteUInt16LittleEndian(span, group);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), element);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)length);
            value.AsSpan().CopyTo(span.Slice(8));
            // Pad with null if needed
            if (length > value.Length)
            {
                span.Slice(8 + value.Length, length - value.Length).Clear();
            }
            buffer.Advance(8 + length);
        }

        private static void WriteElementUS(BufferWriter buffer, ushort group, ushort element, ushort value)
        {
            var span = buffer.GetSpan(10);
            BinaryPrimitives.WriteUInt16LittleEndian(span, group);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), element);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), 2); // Length = 2 bytes for US
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), value);
            buffer.Advance(10);
        }

        #endregion

        /// <summary>
        /// Constants for DIMSE command field values.
        /// </summary>
        /// <remarks>
        /// Per DICOM PS3.7. Request commands have high bit 0, responses have high bit 1.
        /// </remarks>
        private static class CommandFields
        {
            public const ushort CStoreRequest = 0x0001;
            public const ushort CStoreResponse = 0x8001;
            public const ushort CGetRequest = 0x0010;
            public const ushort CGetResponse = 0x8010;
            public const ushort CFindRequest = 0x0020;
            public const ushort CFindResponse = 0x8020;
            public const ushort CMoveRequest = 0x0021;
            public const ushort CMoveResponse = 0x8021;
            public const ushort CEchoRequest = 0x0030;
            public const ushort CEchoResponse = 0x8030;
            public const ushort CCancelRequest = 0x0FFF;
        }

        /// <summary>
        /// Parsed C-STORE command information.
        /// </summary>
        private readonly struct CStoreCommandInfo
        {
            public CStoreCommandInfo(
                byte presentationContextId,
                ushort messageId,
                DicomUID sopClassUid,
                DicomUID sopInstanceUid,
                bool hasDataset)
            {
                PresentationContextId = presentationContextId;
                MessageID = messageId;
                SOPClassUID = sopClassUid;
                SOPInstanceUID = sopInstanceUid;
                HasDataset = hasDataset;
            }

            public byte PresentationContextId { get; }
            public ushort MessageID { get; }
            public DicomUID SOPClassUID { get; }
            public DicomUID SOPInstanceUID { get; }
            public bool HasDataset { get; }
        }

        /// <summary>
        /// Parsed Query/Retrieve command information (C-FIND, C-MOVE, C-GET, C-CANCEL).
        /// </summary>
        private readonly struct QRCommandInfo
        {
            public QRCommandInfo(
                byte presentationContextId,
                ushort messageId,
                DicomUID sopClassUid,
                bool hasDataset,
                ushort commandField,
                string? moveDestination)
            {
                PresentationContextId = presentationContextId;
                MessageID = messageId;
                SOPClassUID = sopClassUid;
                HasDataset = hasDataset;
                CommandFieldValue = commandField;
                MoveDestination = moveDestination;
            }

            public byte PresentationContextId { get; }
            public ushort MessageID { get; }
            public DicomUID SOPClassUID { get; }
            public bool HasDataset { get; }
            public ushort CommandFieldValue { get; }
            public string? MoveDestination { get; }
        }
    }
}
