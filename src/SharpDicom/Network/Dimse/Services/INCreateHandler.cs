using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-CREATE requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-CREATE operations on the SCP side.
    /// N-CREATE is used to create new SOP instances on the SCP, for example
    /// creating an MPPS instance when a procedure begins.
    /// </para>
    /// <para>
    /// The attribute list dataset contains the initial attribute values for the
    /// new SOP instance. The handler should create the instance and return
    /// an <see cref="NServiceResponse"/> with the assigned SOP Instance UID
    /// and any created attribute values.
    /// </para>
    /// </remarks>
    public interface INCreateHandler
    {
        /// <summary>
        /// Called when an N-CREATE request is received.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="attributeList">Optional attribute list dataset with initial values for the new instance.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status, optional attribute values, and Affected SOP Instance UID.</returns>
        ValueTask<NServiceResponse> OnNCreateAsync(
            NCreateRequestContext context,
            DicomDataset? attributeList,
            CancellationToken ct);
    }
}
