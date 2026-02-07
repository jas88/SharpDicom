using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// SCU convenience wrapper for Storage Commitment Push Model operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wraps <see cref="NServiceScu"/> to provide a typed method for requesting
    /// Storage Commitment via N-ACTION per DICOM PS3.4 Annex J.
    /// </para>
    /// <para>
    /// The Storage Commitment workflow:
    /// <list type="number">
    ///   <item><description>SCU sends N-ACTION (Action Type ID = 1) with referenced instances</description></item>
    ///   <item><description>SCP returns N-ACTION response (success = request accepted)</description></item>
    ///   <item><description>SCP later sends N-EVENT-REPORT with commitment results</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class StorageCommitmentScu
    {
        private readonly NServiceScu _scu;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageCommitmentScu"/> class.
        /// </summary>
        /// <param name="scu">The N-Service SCU to use for DIMSE operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when scu is null.</exception>
        public StorageCommitmentScu(NServiceScu scu)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(scu);
#else
            if (scu == null)
                throw new ArgumentNullException(nameof(scu));
#endif
            _scu = scu;
        }

        /// <summary>
        /// Sends a Storage Commitment request via N-ACTION.
        /// </summary>
        /// <param name="request">The commitment request containing Transaction UID and referenced instances.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Response containing status. A success status means the SCP accepted the request
        /// and will later send an N-EVENT-REPORT with the commitment results.
        /// </returns>
        public async ValueTask<NServiceResponse> RequestCommitmentAsync(
            StorageCommitmentRequest request,
            CancellationToken ct = default)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(request);
#else
            if (request == null)
                throw new ArgumentNullException(nameof(request));
#endif

            var actionInfo = request.ToDataset();
            return await _scu.NActionAsync(
                DicomUID.StorageCommitmentPushModel,
                DicomUID.StorageCommitmentPushModelInstance,
                1, // Action Type ID 1 = Storage Commitment Request
                actionInfo,
                ct).ConfigureAwait(false);
        }
    }
}
