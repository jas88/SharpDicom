using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpDicom.Data;
using SharpDicom.Network.Exceptions;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// C-FIND Service Class User (SCU) for querying remote PACS/RIS systems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CFindScu provides query functionality using the DICOM C-FIND operation.
    /// It supports both Patient Root and Study Root information models and
    /// returns results as an <see cref="IAsyncEnumerable{T}"/> for efficient
    /// streaming of query results.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var client = new DicomClient(options);
    /// await client.ConnectAsync(contexts, cancellationToken);
    ///
    /// var findScu = new CFindScu(client);
    /// var query = DicomQuery.ForStudies()
    ///     .WithPatientName("Smith*")
    ///     .WithModality("CT");
    ///
    /// await foreach (var result in findScu.QueryAsync(query))
    /// {
    ///     Console.WriteLine(result.GetString(DicomTag.PatientName));
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class CFindScu
    {
        private readonly DicomClient _client;
        private readonly CFindOptions _options;
        private int _messageIdCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="CFindScu"/> class.
        /// </summary>
        /// <param name="client">The DICOM client to use for network communication.</param>
        /// <param name="options">Optional C-FIND options. Uses defaults if null.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public CFindScu(DicomClient client, CFindOptions? options = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(client);
#else
            if (client == null)
                throw new ArgumentNullException(nameof(client));
#endif
            _client = client;
            _options = options ?? CFindOptions.Default;
        }

        /// <summary>
        /// Gets the C-FIND options used by this SCU.
        /// </summary>
        public CFindOptions Options => _options;

        /// <summary>
        /// Queries the remote AE using the specified query parameters.
        /// </summary>
        /// <param name="level">Query/Retrieve level.</param>
        /// <param name="identifier">Query identifier dataset containing match keys and return keys.</param>
        /// <param name="ct">Cancellation token. Triggers C-CANCEL when cancelled.</param>
        /// <returns>Async enumerable of matching datasets.</returns>
        /// <exception cref="DicomNetworkException">Thrown when query fails or receives unexpected response.</exception>
        /// <exception cref="OperationCanceledException">Thrown when query is cancelled.</exception>
        /// <remarks>
        /// <para>
        /// The method yields results as they arrive from the remote AE. Each yielded
        /// dataset represents a single matching record. When the enumeration completes
        /// without exception, all matches have been received.
        /// </para>
        /// <para>
        /// If the <paramref name="ct"/> is cancelled during query execution, a C-CANCEL
        /// request is sent to the remote AE before throwing <see cref="OperationCanceledException"/>.
        /// </para>
        /// </remarks>
        public async IAsyncEnumerable<DicomDataset> QueryAsync(
            QueryRetrieveLevel level,
            DicomDataset identifier,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var messageId = NextMessageId();
            var sopClassUid = GetSopClassUid(level);

            // Get presentation context for C-FIND
            var context = _client.GetAcceptedContext(sopClassUid);
            if (context == null)
            {
                throw new DicomNetworkException(
                    $"C-FIND SOP Class {sopClassUid} not negotiated. " +
                    $"Ensure the association includes the appropriate Q/R presentation context.");
            }

            // Send C-FIND-RQ with identifier
            var request = DicomCommand.CreateCFindRequest(messageId, sopClassUid, _options.Priority);
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
                    // Pending - yield the identifier dataset
                    if (dataset != null)
                    {
                        yield return dataset;
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
                    throw new OperationCanceledException("C-FIND cancelled");
                }
                else
                {
                    // Failure
                    throw new DicomNetworkException(
                        $"C-FIND failed with status 0x{command.Status.Code:X4}" +
                        (command.Status.ErrorComment != null ? $": {command.Status.ErrorComment}" : ""));
                }
            }
        }

        /// <summary>
        /// Queries the remote AE using a fluent DicomQuery.
        /// </summary>
        /// <param name="query">The fluent query builder.</param>
        /// <param name="ct">Cancellation token. Triggers C-CANCEL when cancelled.</param>
        /// <returns>Async enumerable of matching datasets.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        public IAsyncEnumerable<DicomDataset> QueryAsync(
            DicomQuery query,
            CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(query);
#else
            if (query == null)
                throw new ArgumentNullException(nameof(query));
#endif
            return QueryAsync(query.Level, query.ToDataset(), ct);
        }

        /// <summary>
        /// Gets the appropriate SOP Class UID based on options and level.
        /// </summary>
        private DicomUID GetSopClassUid(QueryRetrieveLevel level)
        {
            return _options.UsePatientRoot
                ? level.GetPatientRootFindSopClassUid()
                : level.GetStudyRootFindSopClassUid();
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
}
