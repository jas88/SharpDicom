using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Network.Exceptions;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// C-GET Service Class User (SCU) for direct retrieval via C-STORE sub-operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike C-MOVE, C-GET receives data directly on the same association via
    /// C-STORE sub-operations. The SCU must accept SCP role for Storage SOP Classes.
    /// </para>
    /// <para>
    /// Association negotiation must include Storage SOP Classes with SCP Role Selection
    /// (using <see cref="Network.Items.PresentationContext.WithScpRole()"/>) for C-GET to work.
    /// The SCP will send C-STORE-RQ messages on the same association to deliver the
    /// requested instances.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var client = new DicomClient(options);
    ///
    /// // Include Storage SOP Classes with SCP role for receiving sub-operations
    /// var contexts = new[]
    /// {
    ///     new PresentationContext(1, DicomUID.PatientRootQueryRetrieveGet, TransferSyntax.ImplicitVRLittleEndian),
    ///     new PresentationContext(3, DicomUID.CTImageStorage, TransferSyntax.ImplicitVRLittleEndian).WithScpRole()
    /// };
    ///
    /// await client.ConnectAsync(contexts, ct);
    ///
    /// var getScu = new CGetScu(client, HandleStoreAsync);
    /// var query = DicomQuery.ForStudies().WithStudyInstanceUid(studyUid);
    ///
    /// await foreach (var progress in getScu.GetAsync(query, ct))
    /// {
    ///     if (progress.HasReceivedDataset)
    ///     {
    ///         // Process received instance
    ///         SaveInstance(progress.ReceivedDataset!);
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class CGetScu
    {
        private readonly DicomClient _client;
        private readonly CGetOptions _options;
        private readonly Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> _storeHandler;
        private int _messageIdCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="CGetScu"/> class.
        /// </summary>
        /// <param name="client">Connected DicomClient with Storage SOP Classes in SCP role.</param>
        /// <param name="storeHandler">
        /// Handler invoked for each received C-STORE sub-operation.
        /// Parameters: (command dataset, data dataset, cancellation token).
        /// Returns: Status to send in C-STORE-RSP.
        /// </param>
        /// <param name="options">Optional get options.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="client"/> or <paramref name="storeHandler"/> is null.
        /// </exception>
        public CGetScu(
            DicomClient client,
            Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> storeHandler,
            CGetOptions? options = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(storeHandler);
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (storeHandler == null)
                throw new ArgumentNullException(nameof(storeHandler));
#endif
            _client = client;
            _storeHandler = storeHandler;
            _options = options ?? CGetOptions.Default;
        }

        /// <summary>
        /// Gets the C-GET options used by this SCU.
        /// </summary>
        public CGetOptions Options => _options;

        /// <summary>
        /// Initiates a C-GET operation to retrieve instances directly.
        /// </summary>
        /// <param name="level">Query/Retrieve level.</param>
        /// <param name="identifier">Keys identifying instances to retrieve.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async enumerable of progress updates.</returns>
        /// <exception cref="DicomNetworkException">Thrown when retrieval fails or receives unexpected response.</exception>
        /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
        /// <remarks>
        /// <para>
        /// The message loop handles interleaved C-STORE-RQ (sub-operations) and
        /// C-GET-RSP (progress/completion) messages.
        /// </para>
        /// <para>
        /// Each C-STORE sub-operation invokes the storeHandler and sends C-STORE-RSP.
        /// The storeHandler should save the received data and return success status.
        /// </para>
        /// <para>
        /// Progress updates are yielded both when a C-STORE sub-operation completes
        /// (with the received dataset) and when a C-GET-RSP is received (with cumulative
        /// sub-operation counts).
        /// </para>
        /// </remarks>
        public async IAsyncEnumerable<CGetProgress> GetAsync(
            QueryRetrieveLevel level,
            DicomDataset identifier,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var messageId = NextMessageId();
            var sopClassUid = GetSopClassUid(level);

            // Get presentation context for C-GET
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                throw new DicomNetworkException(
                    $"C-GET SOP Class {sopClassUid} not negotiated. " +
                    "Ensure the association includes the appropriate Q/R presentation context.");
            }

            // Send C-GET-RQ with identifier
            var request = DicomCommand.CreateCGetRequest(messageId, sopClassUid, _options.Priority);
            await _client.SendDimseRequestAsync(context.Id, request, identifier, ct).ConfigureAwait(false);

            bool cancelled = false;

            // Message loop: handle interleaved C-STORE-RQ and C-GET-RSP
            while (true)
            {
                // Check for cancellation
                if (ct.IsCancellationRequested && !cancelled)
                {
                    await SendCCancelAsync(context.Id, messageId, ct).ConfigureAwait(false);
                    cancelled = true;

                    if (_options.CancellationBehavior == CGetCancellationBehavior.RejectInFlight)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    // CompleteInFlight: continue processing until final response
                }

                var (command, dataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

                if (command.IsCStoreRequest)
                {
                    // Incoming C-STORE sub-operation from SCP
                    DicomStatus storeStatus;

                    if (cancelled && _options.CancellationBehavior == CGetCancellationBehavior.RejectInFlight)
                    {
                        // Reject with cancel status
                        storeStatus = DicomStatus.Cancel;
                    }
                    else
                    {
                        // Delegate to handler
                        storeStatus = await _storeHandler(command.Dataset, dataset, ct).ConfigureAwait(false);

                        // Yield progress with received dataset
                        // SubOperations will be placeholder (0,0,0,0) - actual counts come in C-GET-RSP
                        yield return new CGetProgress(
                            SubOperationProgress.Empty,
                            DicomStatus.Pending,
                            dataset);
                    }

                    // Send C-STORE-RSP
                    await SendCStoreResponseAsync(
                        command.MessageID,
                        command.AffectedSOPClassUID,
                        command.AffectedSOPInstanceUID,
                        storeStatus,
                        ct).ConfigureAwait(false);
                }
                else if (command.IsCGetResponse)
                {
                    // C-GET-RSP with progress/completion
                    var progress = new CGetProgress(
                        command.GetSubOperationProgress(),
                        command.Status);

                    yield return progress;

                    if (!command.Status.IsPending)
                    {
                        // Final response received
                        if (cancelled)
                        {
                            throw new OperationCanceledException(ct);
                        }
                        yield break;
                    }
                }
                else
                {
                    throw new DicomNetworkException(
                        $"Unexpected command during C-GET: 0x{command.CommandFieldValue:X4}. " +
                        "Expected C-STORE-RQ or C-GET-RSP.");
                }
            }
        }

        /// <summary>
        /// Initiates a C-GET operation using a fluent DicomQuery.
        /// </summary>
        /// <param name="query">The fluent query builder specifying what to retrieve.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async enumerable of progress updates.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        public IAsyncEnumerable<CGetProgress> GetAsync(
            DicomQuery query,
            CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(query);
#else
            if (query == null)
                throw new ArgumentNullException(nameof(query));
#endif
            return GetAsync(query.Level, query.ToDataset(), ct);
        }

        /// <summary>
        /// Gets the appropriate SOP Class UID based on options and level.
        /// </summary>
        private DicomUID GetSopClassUid(QueryRetrieveLevel level)
        {
            return _options.UsePatientRoot
                ? level.GetPatientRootGetSopClassUid()
                : level.GetStudyRootGetSopClassUid();
        }

        /// <summary>
        /// Gets the next unique message ID.
        /// </summary>
        private ushort NextMessageId() => (ushort)Interlocked.Increment(ref _messageIdCounter);

        /// <summary>
        /// Sends a C-STORE-RSP for a received C-STORE sub-operation.
        /// </summary>
        private async ValueTask SendCStoreResponseAsync(
            ushort messageId,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            DicomStatus status,
            CancellationToken ct)
        {
            // Find presentation context for this Storage SOP Class
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                // Fall back to first accepted context (should not happen in normal use)
                context = _client.GetFirstAcceptedContext();
            }

            // Build C-STORE-RSP
            var response = DicomCommand.CreateCStoreResponse(messageId, sopClassUid, sopInstanceUid, status);

            // Send command-only (no dataset for response)
            await _client.SendDimseRequestAsync(context.Id, response, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a C-CANCEL request to abort the current retrieval.
        /// </summary>
        private async ValueTask SendCCancelAsync(
            byte presentationContextId,
            ushort messageIdBeingCancelled,
            CancellationToken ct)
        {
            try
            {
                await _client.SendCCancelAsync(presentationContextId, messageIdBeingCancelled, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort - ignore errors when sending cancel
                // The association may already be closing
            }
        }
    }
}
