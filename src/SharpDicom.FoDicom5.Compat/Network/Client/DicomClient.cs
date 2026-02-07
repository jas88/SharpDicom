using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;

namespace FellowOakDicom.Network.Client
{
    /// <summary>
    /// DicomClient adapter that bridges fo-dicom's request-queue pattern
    /// (AddRequestAsync + SendAsync) to SharpDicom's direct async pattern
    /// (ConnectAsync + service calls).
    /// </summary>
    public sealed class DicomClient : IDicomClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly bool _useTls;
        private readonly string _callingAE;
        private readonly string _calledAE;
        private readonly List<DicomRequest> _requests = new List<DicomRequest>();
        private volatile bool _isBusy;
        private ushort _asyncOpsInvoked = 1;
        private ushort _asyncOpsPerformed = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomClient"/> class.
        /// </summary>
        /// <param name="host">The remote host.</param>
        /// <param name="port">The remote port.</param>
        /// <param name="useTls">Whether to use TLS.</param>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        internal DicomClient(string host, int port, bool useTls, string callingAE, string calledAE)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _useTls = useTls;
            _callingAE = callingAE ?? throw new ArgumentNullException(nameof(callingAE));
            _calledAE = calledAE ?? throw new ArgumentNullException(nameof(calledAE));
        }

        /// <inheritdoc />
        public bool IsBusy => _isBusy;

        /// <inheritdoc />
        public Task AddRequestAsync(DicomRequest request)
        {
#if NET8_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(request);
#else
            if (request == null)
                throw new ArgumentNullException(nameof(request));
#endif

            _requests.Add(request);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Maps fo-dicom convention (0 = default/synchronous) to the DICOM spec convention
        /// (0 = unlimited, 1 = synchronous). The values are stored and applied to the
        /// underlying <see cref="DicomClientOptions"/> when <see cref="SendAsync"/> is called,
        /// enabling the 0x53 Asynchronous Operations Window sub-item in association negotiation.
        /// </remarks>
        public Task NegotiateAsyncOps(int invoked = 0, int performed = 0)
        {
            // fo-dicom convention: 0 means "use defaults" (synchronous)
            // DICOM spec / SharpDicom convention: 0 means unlimited, 1 means synchronous
            // Map fo-dicom's 0 to SharpDicom's 1 (default/synchronous)
            _asyncOpsInvoked = invoked == 0 ? (ushort)1 : (ushort)invoked;
            _asyncOpsPerformed = performed == 0 ? (ushort)1 : (ushort)performed;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SendAsync(CancellationToken ct = default)
        {
            if (_requests.Count == 0)
                return;

            var snapshot = new List<DicomRequest>(_requests);
            _requests.Clear();

            _isBusy = true;
            try
            {
                var options = new DicomClientOptions
                {
                    Host = _host,
                    Port = _port,
                    CallingAE = _callingAE,
                    CalledAE = _calledAE,
                    AsyncOperationsInvoked = _asyncOpsInvoked,
                    AsyncOperationsPerformed = _asyncOpsPerformed,
                };

                if (_useTls)
                {
                    options.Tls = new SharpDicom.Network.Tls.TlsOptions();
                }

                // Build presentation contexts for requested services
                var contexts = BuildPresentationContexts(snapshot);

#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task
                await using var client = new SharpDicom.Network.DicomClient(options);
#pragma warning restore CA2007

                await client.ConnectAsync(contexts, ct).ConfigureAwait(false);

                foreach (var request in snapshot)
                {
                    ct.ThrowIfCancellationRequested();

                    if (request is DicomCFindRequest cfind)
                    {
                        await ExecuteCFindAsync(client, cfind, ct).ConfigureAwait(false);
                    }
                    // Future: handle CStore, CMove, CGet, CEcho here
                }
            }
            finally
            {
                _isBusy = false;
            }
        }

        /// <summary>
        /// Executes a C-FIND request by translating from the compat request-queue model
        /// to SharpDicom's direct async enumeration model.
        /// </summary>
        private static async Task ExecuteCFindAsync(
            SharpDicom.Network.DicomClient client,
            DicomCFindRequest request,
            CancellationToken ct)
        {
            var scu = new CFindScu(client);
            var level = request.Level.ToSharpDicom();

            // Extract the query identifier dataset from the compat request
            var identifier = request.Dataset.Unwrap();

            // Execute the C-FIND and invoke callbacks per result
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task
            await foreach (var resultDataset in scu.QueryAsync(level, identifier, ct).ConfigureAwait(false))
#pragma warning restore CA2007
            {
                // Wrap result as compat response with Pending status
                var compatDataset = new FellowOakDicom.DicomDataset(resultDataset);
                var response = new DicomCFindResponse(DicomStatus.Pending, compatDataset);
                request.OnResponseReceived?.Invoke(request, response);
            }

            // Send final response with Success status and null Dataset
            var finalResponse = new DicomCFindResponse(DicomStatus.Success);
            request.OnResponseReceived?.Invoke(request, finalResponse);
        }

        /// <summary>
        /// Builds presentation contexts for the buffered requests.
        /// </summary>
        private static List<PresentationContext> BuildPresentationContexts(List<DicomRequest> requests)
        {
            var contexts = new List<PresentationContext>();
            byte pcId = 1;

            foreach (var request in requests)
            {
                if (request is DicomCFindRequest cfind)
                {
                    // Use Patient Root Q/R Find SOP Class (most common)
                    var sopClassUid = cfind.Level.ToSharpDicom().GetPatientRootFindSopClassUid();
                    contexts.Add(new PresentationContext(
                        pcId,
                        sopClassUid,
                        new[] { TransferSyntax.ImplicitVRLittleEndian, TransferSyntax.ExplicitVRLittleEndian }));
                    pcId += 2; // Presentation context IDs must be odd
                }
            }

            return contexts;
        }
    }
}
