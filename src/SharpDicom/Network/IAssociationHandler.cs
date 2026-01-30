using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using SharpDicom.Network.Association;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network
{
    /// <summary>
    /// Context for association request handling.
    /// </summary>
    /// <remarks>
    /// Provides information about an incoming A-ASSOCIATE-RQ PDU for the server
    /// to decide whether to accept or reject the association.
    /// </remarks>
    public sealed class AssociationRequestContext
    {
        /// <summary>
        /// Gets the calling AE title (remote client).
        /// </summary>
        public string CallingAE { get; }

        /// <summary>
        /// Gets the called AE title (this server).
        /// </summary>
        public string CalledAE { get; }

        /// <summary>
        /// Gets the remote endpoint of the client.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Gets the list of presentation contexts requested by the client.
        /// </summary>
        public IReadOnlyList<PresentationContext> RequestedContexts { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="AssociationRequestContext"/>.
        /// </summary>
        /// <param name="callingAE">The calling AE title.</param>
        /// <param name="calledAE">The called AE title.</param>
        /// <param name="remoteEndPoint">The remote endpoint.</param>
        /// <param name="requestedContexts">The requested presentation contexts.</param>
        public AssociationRequestContext(
            string callingAE,
            string calledAE,
            IPEndPoint remoteEndPoint,
            IReadOnlyList<PresentationContext> requestedContexts)
        {
            CallingAE = callingAE;
            CalledAE = calledAE;
            RemoteEndPoint = remoteEndPoint;
            RequestedContexts = requestedContexts;
        }
    }

    /// <summary>
    /// Result of association request handling.
    /// </summary>
    /// <remarks>
    /// Returned by the OnAssociationRequest handler to indicate whether to accept
    /// or reject the association, and with what parameters.
    /// </remarks>
    public sealed class AssociationRequestResult
    {
        /// <summary>
        /// Gets a value indicating whether to accept the association.
        /// </summary>
        public bool Accept { get; }

        /// <summary>
        /// Gets the rejection result (permanent or transient), if rejecting.
        /// </summary>
        public RejectResult RejectResult { get; }

        /// <summary>
        /// Gets the rejection source, if rejecting.
        /// </summary>
        public RejectSource RejectSource { get; }

        /// <summary>
        /// Gets the rejection reason, if rejecting.
        /// </summary>
        public RejectReason RejectReason { get; }

        /// <summary>
        /// Gets the list of accepted presentation contexts, if accepting.
        /// </summary>
        /// <remarks>
        /// When accepting, this specifies which presentation contexts are accepted
        /// and with which transfer syntax. Contexts not in this list are rejected.
        /// </remarks>
        public IReadOnlyList<PresentationContext>? AcceptedContexts { get; }

        private AssociationRequestResult(
            bool accept,
            RejectResult rejectResult,
            RejectSource rejectSource,
            RejectReason rejectReason,
            IReadOnlyList<PresentationContext>? acceptedContexts)
        {
            Accept = accept;
            RejectResult = rejectResult;
            RejectSource = rejectSource;
            RejectReason = rejectReason;
            AcceptedContexts = acceptedContexts;
        }

        /// <summary>
        /// Creates an accepted association result with the specified presentation contexts.
        /// </summary>
        /// <param name="contexts">The accepted presentation contexts.</param>
        /// <returns>An accepted <see cref="AssociationRequestResult"/>.</returns>
        public static AssociationRequestResult Accepted(IReadOnlyList<PresentationContext> contexts)
            => new(true, default, default, default, contexts);

        /// <summary>
        /// Creates a rejected association result.
        /// </summary>
        /// <param name="result">The rejection result (permanent or transient).</param>
        /// <param name="source">The rejection source.</param>
        /// <param name="reason">The rejection reason.</param>
        /// <returns>A rejected <see cref="AssociationRequestResult"/>.</returns>
        public static AssociationRequestResult Rejected(
            RejectResult result = RejectResult.PermanentRejection,
            RejectSource source = RejectSource.ServiceUser,
            RejectReason reason = RejectReason.NoReasonGiven)
            => new(false, result, source, reason, null);
    }

    /// <summary>
    /// Context for C-ECHO request handling.
    /// </summary>
    /// <remarks>
    /// Provides information about an incoming C-ECHO-RQ for the server to handle.
    /// </remarks>
    public sealed class CEchoRequestContext
    {
        /// <summary>
        /// Gets the association on which the C-ECHO was received.
        /// </summary>
        public DicomAssociation Association { get; }

        /// <summary>
        /// Gets the message ID from the C-ECHO request.
        /// </summary>
        public ushort MessageId { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="CEchoRequestContext"/>.
        /// </summary>
        /// <param name="association">The association.</param>
        /// <param name="messageId">The message ID.</param>
        public CEchoRequestContext(DicomAssociation association, ushort messageId)
        {
            Association = association;
            MessageId = messageId;
        }
    }
}
