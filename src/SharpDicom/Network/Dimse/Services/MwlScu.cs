using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpDicom.Data;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Modality Worklist SCU (Service Class User) for querying scheduled procedures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MwlScu provides Modality Worklist query functionality using the DICOM C-FIND
    /// operation with the Modality Worklist Information Model SOP Class. It allows
    /// modalities to query RIS/HIS systems for scheduled procedures.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var client = new DicomClient(options);
    /// await client.ConnectAsync(MwlScu.CreatePresentationContexts(), cancellationToken);
    ///
    /// var mwlScu = new MwlScu(client);
    /// var query = DicomWorklistQuery.ForToday()
    ///     .WithScheduledProcedureStep(sps => sps.Modality("CT"));
    ///
    /// await foreach (var item in mwlScu.QueryAsync(query))
    /// {
    ///     Console.WriteLine($"Patient: {item.PatientName}, Modality: {item.Modality}");
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class MwlScu
    {
        private readonly DicomClient _client;
        private readonly MwlScuOptions _options;
        private int _messageIdCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="MwlScu"/> class.
        /// </summary>
        /// <param name="client">The DICOM client to use for network communication.</param>
        /// <param name="options">Optional MWL SCU options. Uses defaults if null.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public MwlScu(DicomClient client, MwlScuOptions? options = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(client);
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
#endif
            _client = client;
            _options = options ?? MwlScuOptions.Default;
        }

        /// <summary>
        /// Gets the MWL SCU options used by this instance.
        /// </summary>
        public MwlScuOptions Options => _options;

        /// <summary>
        /// Queries the worklist using the specified query builder.
        /// </summary>
        /// <param name="query">The worklist query builder.</param>
        /// <param name="ct">Cancellation token. Triggers C-CANCEL when cancelled.</param>
        /// <returns>Async enumerable of worklist items.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        /// <exception cref="DicomNetworkException">Thrown when query fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when query is cancelled.</exception>
        public IAsyncEnumerable<WorklistItem> QueryAsync(
            DicomWorklistQuery query,
            CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(query);
#else
            if (query == null)
                throw new ArgumentNullException(nameof(query));
#endif
            return QueryAsync(query.ToDataset(), ct);
        }

        /// <summary>
        /// Queries the worklist using a raw identifier dataset.
        /// </summary>
        /// <param name="identifier">Query identifier dataset containing match keys and return keys.</param>
        /// <param name="ct">Cancellation token. Triggers C-CANCEL when cancelled.</param>
        /// <returns>Async enumerable of worklist items.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identifier is null.</exception>
        /// <exception cref="DicomNetworkException">Thrown when query fails or receives unexpected response.</exception>
        /// <exception cref="OperationCanceledException">Thrown when query is cancelled.</exception>
        /// <remarks>
        /// <para>
        /// Results are yielded as they arrive from the remote AE. Each yielded
        /// <see cref="WorklistItem"/> represents a single scheduled procedure.
        /// </para>
        /// <para>
        /// If the <paramref name="ct"/> is cancelled during query execution, a C-CANCEL
        /// request is sent to the remote AE before throwing <see cref="OperationCanceledException"/>.
        /// </para>
        /// </remarks>
        public async IAsyncEnumerable<WorklistItem> QueryAsync(
            DicomDataset identifier,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(identifier);
#else
            if (identifier == null)
                throw new ArgumentNullException(nameof(identifier));
#endif

            var messageId = NextMessageId();
            var sopClassUid = DicomUID.ModalityWorklistFind;

            // Get presentation context for MWL C-FIND
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                throw new DicomNetworkException(
                    $"Modality Worklist SOP Class {sopClassUid} not negotiated. " +
                    $"Ensure the association includes the MWL presentation context.");
            }

            // Send C-FIND-RQ with identifier
            var request = DicomCommand.CreateCFindRequest(messageId, sopClassUid, _options.Priority);
            await _client.SendDimseRequestAsync(context.Id, request, identifier, ct).ConfigureAwait(false);

            // Track if we've sent a cancel request
            var cancelSent = false;

            // Track result count for client-side limiting
            var resultCount = 0;

            // Receive responses until final
            while (true)
            {
                // Check for cancellation before each receive
                if (ct.IsCancellationRequested && !cancelSent)
                {
                    await SendCCancelAsync(context.Id, messageId, ct).ConfigureAwait(false);
                    cancelSent = true;
                }

                var (command, dataset) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

                // Verify it's a C-FIND-RSP
                if (!command.IsCFindResponse)
                {
                    throw new DicomNetworkException(
                        $"Expected C-FIND-RSP, received command field 0x{command.CommandFieldValue:X4}");
                }

                // Check status category
                if (command.Status.IsPending)
                {
                    // Pending - yield the worklist item
                    if (dataset != null)
                    {
                        yield return new WorklistItem(dataset);
                        resultCount++;

                        // Check if we've reached the client-side limit
                        if (_options.MaxResults > 0 && resultCount >= _options.MaxResults && !cancelSent)
                        {
                            await SendCCancelAsync(context.Id, messageId, ct).ConfigureAwait(false);
                            cancelSent = true;
                        }
                    }
                }
                else if (command.Status.IsSuccess)
                {
                    // Success - no more matches
                    yield break;
                }
                else if (command.Status.IsCancel)
                {
                    // Cancelled by SCP (or our cancel was acknowledged)
                    throw new OperationCanceledException("MWL query cancelled");
                }
                else
                {
                    // Failure
                    throw new DicomNetworkException(
                        $"MWL query failed with status 0x{command.Status.Code:X4}" +
                        (command.Status.ErrorComment != null ? $": {command.Status.ErrorComment}" : ""));
                }
            }
        }

        /// <summary>
        /// Creates the standard presentation contexts for MWL operations.
        /// </summary>
        /// <returns>List of presentation contexts including MWL SOP Class with common transfer syntaxes.</returns>
        /// <remarks>
        /// Returns presentation contexts for:
        /// - Modality Worklist Information Model - FIND (1.2.840.10008.5.1.4.31)
        ///
        /// Transfer syntaxes proposed (in order of preference):
        /// - Explicit VR Little Endian
        /// - Implicit VR Little Endian
        /// </remarks>
        public static IReadOnlyList<PresentationContext> CreatePresentationContexts()
        {
            return new[]
            {
                new PresentationContext(
                    1,
                    DicomUID.ModalityWorklistFind,
                    TransferSyntax.ExplicitVRLittleEndian,
                    TransferSyntax.ImplicitVRLittleEndian)
            };
        }

        /// <summary>
        /// Gets the next unique message ID.
        /// </summary>
        private ushort NextMessageId() => (ushort)System.Threading.Interlocked.Increment(ref _messageIdCounter);

        /// <summary>
        /// Sends a C-CANCEL request to abort the current query.
        /// </summary>
        private async System.Threading.Tasks.ValueTask SendCCancelAsync(
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

    /// <summary>
    /// Options for MWL SCU operations.
    /// </summary>
    public sealed class MwlScuOptions
    {
        /// <summary>
        /// Default MWL SCU options (medium priority).
        /// </summary>
        public static readonly MwlScuOptions Default = new();

        /// <summary>
        /// Gets or sets the priority for MWL operations.
        /// </summary>
        /// <remarks>
        /// Priority values per DICOM PS3.7:
        /// - 0 = MEDIUM (default)
        /// - 1 = HIGH
        /// - 2 = LOW
        /// </remarks>
        public ushort Priority { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of results to return.
        /// </summary>
        /// <remarks>
        /// If set to 0 (default), no limit is applied.
        /// Note: This is client-side limiting only. The SCP may return fewer results.
        /// </remarks>
        public int MaxResults { get; set; }
    }
}
