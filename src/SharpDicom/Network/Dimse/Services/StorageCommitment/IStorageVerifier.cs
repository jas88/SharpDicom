using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// Interface for verifying that SOP Instances are safely committed to storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to provide custom storage verification logic.
    /// The SCP calls <see cref="VerifyAsync"/> to determine which instances are
    /// successfully committed and which have failed.
    /// </para>
    /// <para>
    /// Instances not returned in the failure list are considered successfully committed.
    /// </para>
    /// </remarks>
    public interface IStorageVerifier
    {
        /// <summary>
        /// Verifies whether the specified SOP Instances are committed to storage.
        /// </summary>
        /// <param name="instances">The SOP Instance references to verify.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A list of failed instances with failure reasons. An empty list indicates
        /// all instances are successfully committed.
        /// </returns>
        ValueTask<IReadOnlyList<FailedSopInstanceReference>> VerifyAsync(
            IReadOnlyList<SopInstanceReference> instances,
            CancellationToken ct);
    }
}
