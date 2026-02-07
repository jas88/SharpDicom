using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-SET requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-SET operations on the SCP side.
    /// N-SET is used to modify attribute values of an existing SOP instance,
    /// for example updating an MPPS instance when a procedure completes.
    /// </para>
    /// <para>
    /// The modification list dataset contains the attribute values to set.
    /// The handler should apply the modifications and return an
    /// <see cref="NServiceResponse"/> with the resulting attribute values.
    /// </para>
    /// </remarks>
    public interface INSetHandler
    {
        /// <summary>
        /// Called when an N-SET request is received.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="modificationList">Dataset containing the attribute values to set.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional modified attribute values.</returns>
        ValueTask<NServiceResponse> OnNSetAsync(
            NSetRequestContext context,
            DicomDataset? modificationList,
            CancellationToken ct);
    }
}
