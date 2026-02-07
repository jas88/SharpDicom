using System.Threading;
using System.Threading.Tasks;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming N-GET requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement this interface to handle N-GET operations on the SCP side.
    /// N-GET is used to retrieve attribute values from an existing SOP instance.
    /// </para>
    /// <para>
    /// The <see cref="NGetRequestContext.AttributeIdentifierList"/> specifies which
    /// attributes to return. If null, all attributes should be returned.
    /// The response dataset should contain the requested attribute values.
    /// </para>
    /// </remarks>
    public interface INGetHandler
    {
        /// <summary>
        /// Called when an N-GET request is received.
        /// </summary>
        /// <param name="context">Request context with association info and optional attribute identifier list.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response containing status and attribute values dataset.</returns>
        ValueTask<NServiceResponse> OnNGetAsync(
            NGetRequestContext context,
            CancellationToken ct);
    }
}
