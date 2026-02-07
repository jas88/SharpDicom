using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-EVENT-REPORT requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-EVENT-REPORT operations on the SCP side.
    /// N-EVENT-REPORT is used by the SCP to notify the SCU of events, for example
    /// reporting Storage Commitment results.
    /// </para>
    /// <para>
    /// The event information dataset contains details about the event.
    /// The <see cref="NEventReportRequestContext.EventTypeID"/> identifies the
    /// type of event being reported.
    /// </para>
    /// </remarks>
    public interface INEventReportHandler
    {
        /// <summary>
        /// Called when an N-EVENT-REPORT request is received.
        /// </summary>
        /// <param name="context">Request context with association info and Event Type ID.</param>
        /// <param name="eventInformation">Optional dataset containing event details.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and optional event reply dataset.</returns>
        ValueTask<NServiceResponse> OnNEventReportAsync(
            NEventReportRequestContext context,
            DicomDataset? eventInformation,
            CancellationToken ct);
    }
}
