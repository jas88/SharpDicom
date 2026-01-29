using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.IO;
using SharpDicom.Network.Association;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

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

                    // Start ARTIM timer
                    using var artimCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    artimCts.CancelAfter(_options.ArtimTimeout);

                    try
                    {
                        // Read A-ASSOCIATE-RQ first to get AE titles
                        var (callingAE, calledAE, requestedContexts) =
                            await ReadAssociateRequestAsync(stream, artimCts.Token).ConfigureAwait(false);

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
                            await SendAssociateAcceptAsync(stream, callingAE, calledAE, result.AcceptedContexts, ct)
                                .ConfigureAwait(false);
                            association.ProcessEvent(AssociationEvent.AAssociateResponse);
                            association.SetAcceptedContexts(result.AcceptedContexts);

                            // Run DIMSE loop
                            await RunDimseLoopAsync(stream, association, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await SendAssociateRejectAsync(stream, result, ct).ConfigureAwait(false);
                            // Association rejected - connection closes
                        }
                    }
                    catch (OperationCanceledException) when (artimCts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        // ARTIM timeout - no A-ASSOCIATE-RQ received in time
                        // Connection will be closed
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
            foreach (var ctx in requested)
            {
                if (ctx.TransferSyntaxes.Count > 0)
                {
                    accepted.Add(PresentationContext.CreateAccepted(
                        ctx.Id,
                        ctx.AbstractSyntax,
                        ctx.TransferSyntaxes[0]));
                }
            }
            return AssociationRequestResult.Accepted(accepted);
        }

        private async Task RunDimseLoopAsync(
            NetworkStream stream,
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
            NetworkStream stream,
            DicomAssociation association,
            byte[] pduBody,
            CancellationToken ct)
        {
            // Parse P-DATA-TF to extract PDVs (must complete before any await)
            // Each PDV contains: 4-byte length, 1-byte context ID, 1-byte message control header, data
            var (pendingEchoRequests, pendingStoreRequests) = ExtractDimseRequests(pduBody);

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
        }

        private static (List<(byte ContextId, ushort MessageId)> EchoRequests,
                 List<CStoreCommandInfo> StoreRequests)
            ExtractDimseRequests(byte[] pduBody)
        {
            var echoRequests = new List<(byte, ushort)>();
            var storeRequests = new List<CStoreCommandInfo>();
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
                }
            }

            return (echoRequests, storeRequests);
        }

        private async Task HandleCEchoAsync(
            NetworkStream stream,
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
            NetworkStream stream,
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
                    var datasetBytes = await ReadDatasetAsync(stream, ct).ConfigureAwait(false);

                    // Check size for buffered mode
                    if (_options.StoreHandlerMode == CStoreHandlerMode.Buffered &&
                        datasetBytes.Length > _options.MaxBufferedDatasetSize)
                    {
                        status = DicomStatus.OutOfResources;
                    }
                    else
                    {
                        // Parse the dataset
                        var dataset = ParseDataset(datasetBytes, association, command.PresentationContextId);

                        // Call the appropriate handler
                        status = await InvokeCStoreHandlerAsync(requestContext, dataset, ct)
                            .ConfigureAwait(false);
                    }
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

        private static async Task<byte[]> ReadDatasetAsync(NetworkStream stream, CancellationToken ct)
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
                        // This is dataset data
                        ms.Write(data.ToArray(), 0, data.Length);
                        lastFragment = isLast;
                    }
                }
            }

            return ms.ToArray();
        }

        private static async Task ReadAndDiscardDatasetAsync(NetworkStream stream, CancellationToken ct)
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

            while (reader.TryReadElementHeader(out var tag, out var vr, out var valueLength))
            {
                if (valueLength == 0xFFFFFFFF)
                {
                    // Undefined length - skip for now (sequences/pixel data)
                    // A full implementation would parse sequences here
                    break;
                }

                if (!reader.TryReadValue(valueLength, out var value))
                    break;

                // Create element based on VR
                var element = new DicomBinaryElement(tag, vr, value.ToArray());
                dataset.Add(element);
            }

            return dataset;
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
            ReadAssociateRequestAsync(NetworkStream stream, CancellationToken ct)
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

        private static async Task<(PduType Type, byte[] Body)> ReadPduAsync(NetworkStream stream, CancellationToken ct)
        {
            // Read 6-byte PDU header
            var header = new byte[Pdu.PduConstants.HeaderLength];
            await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false);

            var pduType = (PduType)header[0];
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));

            // Read PDU body
            var body = new byte[length];
            await ReadExactlyAsync(stream, body, ct).ConfigureAwait(false);

            return (pduType, body);
        }

        private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
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
            NetworkStream stream,
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
            NetworkStream stream,
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

        private static async Task SendReleaseResponseAsync(NetworkStream stream, CancellationToken ct)
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
            NetworkStream stream,
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
            NetworkStream stream,
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

                if (group == 0x0000 && element == 0x0100) // CommandField tag
                {
                    if (length >= 2 && offset + 8 + length <= commandData.Length)
                    {
                        return BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 8));
                    }
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

                if (group == 0x0000 && element == 0x0110) // MessageID tag
                {
                    if (length >= 2 && offset + 8 + length <= commandData.Length)
                    {
                        return BinaryPrimitives.ReadUInt16LittleEndian(commandData.Slice(offset + 8));
                    }
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
            public const ushort CEchoRequest = 0x0030;
            public const ushort CEchoResponse = 0x8030;
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
    }
}
