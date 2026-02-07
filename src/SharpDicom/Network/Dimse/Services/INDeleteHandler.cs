using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-DELETE requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-DELETE operations on the SCP side.
    /// N-DELETE is used to delete an existing SOP instance.
    /// </para>
    /// <para>
    /// No dataset accompanies the request or response. The handler should
    /// delete the identified SOP instance and return a status.
    /// </para>
    /// </remarks>
    public interface INDeleteHandler
    {
        /// <summary>
        /// Called when an N-DELETE request is received.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status.</returns>
        ValueTask<NServiceResponse> OnNDeleteAsync(
            NDeleteRequestContext context,
            CancellationToken ct);
    }
}
