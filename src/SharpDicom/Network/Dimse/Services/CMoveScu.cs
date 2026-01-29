using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpDicom.Data;
using SharpDicom.Network.Exceptions;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// C-MOVE Service Class User (SCU) for initiating retrieval to a third-party destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-MOVE sends a retrieval request to the SCP, which then sends the matching
    /// instances to the specified MoveDestination AE via C-STORE sub-operations.
    /// The SCU does not receive the data directly - it only receives progress updates.
    /// </para>
    /// <para>
    /// The MoveDestination must be configured on the SCP - the SCP needs to know
    /// the network address of the destination AE to send the C-STORE operations.
    /// If the destination is unknown, the SCP returns status 0xA801.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var client = new DicomClient(options);
    /// await client.ConnectAsync(contexts, ct);
    ///
    /// var moveScu = new CMoveScu(client);
    /// var query = DicomQuery.ForStudies().WithStudyInstanceUid(studyUid);
    ///
    /// await foreach (var progress in moveScu.MoveAsync(query, "DESTINATION_AE", ct))
    /// {
    ///     Console.WriteLine($"Progress: {progress.SubOperations.Completed}/{progress.SubOperations.Total}");
    ///     if (progress.IsFinal)
    ///     {
    ///         Console.WriteLine(progress.IsSuccess ? "Success" : "Failed");
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class CMoveScu
    {
        private readonly DicomClient _client;
        private readonly CMoveOptions _options;
        private int _messageIdCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="CMoveScu"/> class.
        /// </summary>
        /// <param name="client">The DICOM client to use for network communication.</param>
        /// <param name="options">Optional C-MOVE options. Uses defaults if null.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public CMoveScu(DicomClient client, CMoveOptions? options = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(client);
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
#endif
            _client = client;
            _options = options ?? CMoveOptions.Default;
        }

        /// <summary>
        /// Gets the C-MOVE options used by this SCU.
        /// </summary>
        public CMoveOptions Options => _options;

        /// <summary>
        /// Initiates a C-MOVE operation to send matching instances to the destination AE.
        /// </summary>
        /// <param name="level">Query/Retrieve level.</param>
        /// <param name="identifier">Keys identifying instances to retrieve.</param>
        /// <param name="destinationAE">AE Title where SCP will send via C-STORE.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async enumerable of progress updates.</returns>
        /// <exception cref="ArgumentException">Thrown when destinationAE is null or empty.</exception>
        /// <exception cref="DicomNetworkException">
        /// Thrown when the SCP returns an error, including:
        /// <list type="bullet">
        ///   <item><description>0xA801 - Move destination unknown to SCP</description></item>
        ///   <item><description>0xC000+ - Various failure codes</description></item>
        /// </list>
        /// </exception>
        /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
        /// <remarks>
        /// <para>
        /// The destinationAE must be known to the SCP (configured in its AE table).
        /// If unknown, the SCP will return status 0xA801 (Move Destination Unknown).
        /// </para>
        /// <para>
        /// Each yielded CMoveProgress contains cumulative sub-operation counts.
        /// The final progress will have IsFinal=true and aggregate completion status.
        /// </para>
        /// </remarks>
        public async IAsyncEnumerable<CMoveProgress> MoveAsync(
            QueryRetrieveLevel level,
            DicomDataset identifier,
            string destinationAE,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(destinationAE))
                throw new ArgumentException("Destination AE is required", nameof(destinationAE));

            var messageId = NextMessageId();
            var sopClassUid = GetSopClassUid(level);

            // Get presentation context for C-MOVE
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                throw new DicomNetworkException(
                    $"C-MOVE SOP Class {sopClassUid} not negotiated. " +
                    "Ensure the association includes the appropriate Q/R presentation context.");
            }

            // Send C-MOVE-RQ with MoveDestination
            var request = DicomCommand.CreateCMoveRequest(messageId, sopClassUid, destinationAE, _options.Priority);
            await _client.SendDimseRequestAsync(context.Id, request, identifier, ct).ConfigureAwait(false);

            // Track if we've sent a cancel request
            var cancelSent = false;

            // Receive responses until final
            while (true)
            {
                // Check for cancellation before each receive
                if (ct.IsCancellationRequested && !cancelSent)
                {
                    // Send C-CANCEL before throwing
                    await SendCCancelAsync(context.Id, messageId, ct).ConfigureAwait(false);
                    cancelSent = true;
                }

                var (command, _) = await _client.ReceiveDimseResponseAsync(ct).ConfigureAwait(false);

                // Verify it's a C-MOVE-RSP
                if (!command.IsCMoveResponse)
                {
                    throw new DicomNetworkException(
                        $"Expected C-MOVE-RSP, received command field 0x{command.CommandFieldValue:X4}");
                }

                var progress = new CMoveProgress(
                    command.GetSubOperationProgress(),
                    command.Status);

                yield return progress;

                // Check for completion
                if (!command.Status.IsPending)
                {
                    // Final response received
                    // Check for specific error codes
                    if (command.Status.Code == 0xA801)
                    {
                        throw new DicomNetworkException(
                            $"Move destination '{destinationAE}' unknown to SCP (status 0xA801)");
                    }

                    if (cancelSent)
                    {
                        throw new OperationCanceledException(ct);
                    }

                    yield break;
                }
            }
        }

        /// <summary>
        /// Initiates a C-MOVE operation using a fluent DicomQuery.
        /// </summary>
        /// <param name="query">The fluent query builder specifying what to retrieve.</param>
        /// <param name="destinationAE">AE Title where SCP will send via C-STORE.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Async enumerable of progress updates.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        /// <exception cref="ArgumentException">Thrown when destinationAE is null or empty.</exception>
        public IAsyncEnumerable<CMoveProgress> MoveAsync(
            DicomQuery query,
            string destinationAE,
            CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(query);
#else
            if (query == null)
                throw new ArgumentNullException(nameof(query));
#endif
            return MoveAsync(query.Level, query.ToDataset(), destinationAE, ct);
        }

        /// <summary>
        /// Gets the appropriate SOP Class UID based on options and level.
        /// </summary>
        private DicomUID GetSopClassUid(QueryRetrieveLevel level)
        {
            return _options.UsePatientRoot
                ? level.GetPatientRootMoveSopClassUid()
                : level.GetStudyRootMoveSopClassUid();
        }

        /// <summary>
        /// Gets the next unique message ID.
        /// </summary>
        private ushort NextMessageId() => (ushort)System.Threading.Interlocked.Increment(ref _messageIdCounter);

        /// <summary>
        /// Sends a C-CANCEL request to abort the current move operation.
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
}
