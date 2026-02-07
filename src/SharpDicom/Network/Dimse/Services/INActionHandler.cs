using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-ACTION requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-ACTION operations on the SCP side.
    /// N-ACTION is used to request that an action be performed on a SOP instance,
    /// for example requesting a Storage Commitment.
    /// </para>
    /// <para>
    /// The action information dataset contains parameters for the action.
    /// The <see cref="NActionRequestContext.ActionTypeID"/> identifies which
    /// specific action to perform.
    /// </para>
    /// </remarks>
    public interface INActionHandler
    {
        /// <summary>
        /// Called when an N-ACTION request is received.
        /// </summary>
        /// <param name="context">Request context with association info and Action Type ID.</param>
        /// <param name="actionInformation">Optional dataset containing action parameters.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional action reply dataset.</returns>
        ValueTask<NServiceResponse> OnNActionAsync(
            NActionRequestContext context,
            DicomDataset? actionInformation,
            CancellationToken ct);
    }
}
