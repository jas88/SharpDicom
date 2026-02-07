using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// SCP handler for Storage Commitment Push Model N-ACTION operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the Storage Commitment Push Model SCP role per DICOM PS3.4 Annex J.
    /// Handles N-ACTION requests (Action Type ID = 1) to verify storage commitment of SOP Instances.
    /// </para>
    /// <para>
    /// The handler delegates actual storage verification to an <see cref="IStorageVerifier"/>
    /// implementation, allowing pluggable verification strategies.
    /// </para>
    /// <para>
    /// After verification, the result is stored for later delivery via N-EVENT-REPORT.
    /// Call <see cref="TakeResult"/> to retrieve the pending result.
    /// </para>
    /// </remarks>
    public sealed class StorageCommitmentScpHandler : INActionHandler
    {
        private readonly IStorageVerifier _verifier;
        private readonly object _resultLock = new();
        private StorageCommitmentResult? _pendingResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageCommitmentScpHandler"/> class.
        /// </summary>
        /// <param name="verifier">The storage verifier to use for checking instance commitment.</param>
        /// <exception cref="ArgumentNullException">Thrown when verifier is null.</exception>
        public StorageCommitmentScpHandler(IStorageVerifier verifier)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(verifier);
#else
            if (verifier == null)
                throw new ArgumentNullException(nameof(verifier));
#endif
            _verifier = verifier;
        }

        /// <summary>
        /// Gets a value indicating whether there is a pending result awaiting N-EVENT-REPORT delivery.
        /// </summary>
        public bool HasPendingResult
        {
            get
            {
                lock (_resultLock)
                {
                    return _pendingResult != null;
                }
            }
        }

        /// <summary>
        /// Takes the pending result for N-EVENT-REPORT delivery. Returns null if no result is pending.
        /// </summary>
        /// <returns>The pending result, or null if no result is available.</returns>
        public StorageCommitmentResult? TakeResult()
        {
            lock (_resultLock)
            {
                var result = _pendingResult;
                _pendingResult = null;
                return result;
            }
        }

        /// <summary>
        /// Handles an N-ACTION request for Storage Commitment.
        /// </summary>
        /// <param name="context">Request context with association info and Action Type ID.</param>
        /// <param name="actionInformation">The action information dataset containing the commitment request.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        public async ValueTask<NServiceResponse> OnNActionAsync(
            NActionRequestContext context,
            DicomDataset? actionInformation,
            CancellationToken ct)
        {
            // Verify SOP Class UID is Storage Commitment Push Model
            if (context.SOPClassUID != DicomUID.StorageCommitmentPushModel)
            {
                return new NServiceResponse(DicomStatus.NoSuchSOPClass);
            }

            // Action Type ID must be 1 (Storage Commitment Request)
            if (context.ActionTypeID != 1)
            {
                return new NServiceResponse(DicomStatus.NoSuchActionType);
            }

            if (actionInformation == null)
            {
                return new NServiceResponse(DicomStatus.ProcessingFailure);
            }

            // Parse the request
            StorageCommitmentRequest request;
            try
            {
                request = StorageCommitmentRequest.FromDataset(actionInformation);
            }
            catch (InvalidOperationException)
            {
                return new NServiceResponse(DicomStatus.ProcessingFailure);
            }

            // Verify storage
            var failures = await _verifier.VerifyAsync(request.ReferencedInstances, ct).ConfigureAwait(false);

            // Build the result
            var failedSet = new HashSet<DicomUID>();
            foreach (var failure in failures)
            {
                failedSet.Add(failure.Reference.SOPInstanceUID);
            }

            var successInstances = new List<SopInstanceReference>();
            foreach (var instance in request.ReferencedInstances)
            {
                if (!failedSet.Contains(instance.SOPInstanceUID))
                {
                    successInstances.Add(instance);
                }
            }

            var eventTypeId = failures.Count == 0
                ? StorageCommitmentResult.EventTypeAllSuccess
                : StorageCommitmentResult.EventTypeFailures;

            var result = new StorageCommitmentResult(
                request.TransactionUID,
                eventTypeId,
                successInstances,
                failures);

            // Store for later N-EVENT-REPORT delivery
            lock (_resultLock)
            {
                _pendingResult = result;
            }

            return new NServiceResponse(DicomStatus.Success);
        }
    }
}
