using System.Threading;
using System.Threading.Tasks;

namespace FellowOakDicom.Network.Client
{
    /// <summary>
    /// Interface matching fo-dicom 5.x IDicomClient.
    /// Provides request-queue pattern: AddRequestAsync -> SendAsync.
    /// </summary>
    public interface IDicomClient
    {
        /// <summary>
        /// Buffers a DIMSE request for later execution.
        /// </summary>
        /// <param name="request">The DIMSE request to buffer.</param>
        /// <returns>A completed task.</returns>
        Task AddRequestAsync(DicomRequest request);

        /// <summary>
        /// Executes all buffered requests against the remote DICOM AE.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when all requests have been processed.</returns>
        Task SendAsync(CancellationToken ct = default);

        /// <summary>
        /// Negotiates asynchronous operations window.
        /// </summary>
        /// <param name="invoked">Maximum number of outstanding operations invoked.</param>
        /// <param name="performed">Maximum number of outstanding operations performed.</param>
        /// <returns>A completed task.</returns>
        Task NegotiateAsyncOps(int invoked = 0, int performed = 0);

        /// <summary>
        /// Gets a value indicating whether the client is currently processing requests.
        /// </summary>
        bool IsBusy { get; }
    }
}
