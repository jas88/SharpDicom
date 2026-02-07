using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// Persistence interface for MPPS instances.
    /// </summary>
    /// <remarks>
    /// Implement this interface to provide custom persistence (e.g., database-backed storage)
    /// for Modality Performed Procedure Step instances. The default implementation
    /// <see cref="InMemoryMppsPersistence"/> stores instances in memory.
    /// </remarks>
    public interface IMppsPersistence
    {
        /// <summary>
        /// Retrieves an MPPS instance by its SOP Instance UID.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID to look up.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The MPPS instance, or null if not found.</returns>
        ValueTask<MppsInstance?> GetAsync(DicomUID sopInstanceUid, CancellationToken ct);

        /// <summary>
        /// Stores a new MPPS instance.
        /// </summary>
        /// <param name="instance">The MPPS instance to store.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if an instance with the same UID already exists.</exception>
        ValueTask PutAsync(MppsInstance instance, CancellationToken ct);

        /// <summary>
        /// Updates an existing MPPS instance by applying a modification list.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID of the instance to update.</param>
        /// <param name="modificationList">The dataset containing attribute modifications.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown if the instance does not exist.</exception>
        ValueTask UpdateAsync(DicomUID sopInstanceUid, DicomDataset modificationList, CancellationToken ct);
    }
}
