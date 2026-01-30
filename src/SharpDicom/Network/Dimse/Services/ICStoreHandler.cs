using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDicom.Data;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Handler for incoming C-STORE requests in buffered mode.
    /// </summary>
    public interface ICStoreHandler
    {
        /// <summary>
        /// Called when a C-STORE request is received with the complete dataset.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="dataset">The complete DICOM dataset including pixel data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Status to return in C-STORE-RSP.</returns>
        ValueTask<DicomStatus> OnCStoreAsync(
            CStoreRequestContext context,
            DicomDataset dataset,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Handler for incoming C-STORE requests in streaming mode.
    /// </summary>
    public interface IStreamingCStoreHandler
    {
        /// <summary>
        /// Called when a C-STORE request is received with streaming pixel data.
        /// </summary>
        /// <param name="context">Request context with association and command info.</param>
        /// <param name="metadata">Dataset containing all elements except pixel data.</param>
        /// <param name="pixelDataStream">Stream for reading pixel data via CopyToAsync.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Status to return in C-STORE-RSP.</returns>
        /// <remarks>
        /// The handler MUST read the pixelDataStream completely before returning.
        /// Incomplete reads will corrupt the association state.
        /// </remarks>
        ValueTask<DicomStatus> OnCStoreStreamingAsync(
            CStoreRequestContext context,
            DicomDataset metadata,
            Stream pixelDataStream,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Context for C-STORE requests.
    /// </summary>
    public sealed class CStoreRequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CStoreRequestContext"/> class.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="sopClassUid">The SOP Class UID being stored.</param>
        /// <param name="sopInstanceUid">The SOP Instance UID being stored.</param>
        /// <param name="messageId">The message ID.</param>
        /// <param name="presentationContextId">The presentation context ID.</param>
        public CStoreRequestContext(
            string callingAE,
            string calledAE,
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort messageId,
            byte presentationContextId)
        {
            CallingAE = callingAE;
            CalledAE = calledAE;
            SOPClassUID = sopClassUid;
            SOPInstanceUID = sopInstanceUid;
            MessageID = messageId;
            PresentationContextId = presentationContextId;
        }

        /// <summary>Gets the calling AE title.</summary>
        public string CallingAE { get; }

        /// <summary>Gets the called AE title.</summary>
        public string CalledAE { get; }

        /// <summary>Gets the SOP Class UID being stored.</summary>
        public DicomUID SOPClassUID { get; }

        /// <summary>Gets the SOP Instance UID being stored.</summary>
        public DicomUID SOPInstanceUID { get; }

        /// <summary>Gets the message ID.</summary>
        public ushort MessageID { get; }

        /// <summary>Gets the presentation context ID.</summary>
        public byte PresentationContextId { get; }
    }
}
