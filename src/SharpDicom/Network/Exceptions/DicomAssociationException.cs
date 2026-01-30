using System;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network.Exceptions
{
    /// <summary>
    /// Exception thrown when a DICOM association is rejected by the remote peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exception is thrown when an A-ASSOCIATE-RJ PDU is received,
    /// indicating that the remote peer has rejected the association request.
    /// </para>
    /// <para>
    /// The <see cref="Result"/>, <see cref="RejectSource"/>, and <see cref="Reason"/>
    /// properties provide detailed information about why the association was rejected
    /// per DICOM PS3.8 Section 9.3.4.
    /// </para>
    /// </remarks>
    public class DicomAssociationException : DicomNetworkException
    {
        /// <summary>
        /// Gets the rejection result (permanent or transient).
        /// </summary>
        /// <remarks>
        /// If null, the rejection reason is unknown or the exception was thrown
        /// for a reason other than receiving an A-ASSOCIATE-RJ PDU.
        /// </remarks>
        public RejectResult? Result { get; }

        /// <summary>
        /// Gets the rejection source indicating where the rejection originated.
        /// </summary>
        /// <remarks>
        /// Indicates whether the rejection came from the service user,
        /// the ACSE provider, or the presentation layer provider.
        /// </remarks>
        public RejectSource? RejectSource { get; }

        /// <summary>
        /// Gets the reason for the rejection.
        /// </summary>
        /// <remarks>
        /// The interpretation of this value depends on the <see cref="RejectSource"/>.
        /// See <see cref="RejectReason"/> documentation for the mapping.
        /// </remarks>
        public RejectReason? Reason { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAssociationException"/> class
        /// with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomAssociationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAssociationException"/> class
        /// with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomAssociationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAssociationException"/> class
        /// with rejection details from an A-ASSOCIATE-RJ PDU.
        /// </summary>
        /// <param name="result">The rejection result (permanent or transient).</param>
        /// <param name="rejectSource">The source of the rejection.</param>
        /// <param name="reason">The reason for the rejection.</param>
        public DicomAssociationException(RejectResult result, RejectSource rejectSource, RejectReason reason)
            : base(FormatMessage(result, rejectSource, reason))
        {
            Result = result;
            RejectSource = rejectSource;
            Reason = reason;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAssociationException"/> class
        /// with a custom message and rejection details.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="result">The rejection result (permanent or transient).</param>
        /// <param name="rejectSource">The source of the rejection.</param>
        /// <param name="reason">The reason for the rejection.</param>
        public DicomAssociationException(string message, RejectResult result, RejectSource rejectSource, RejectReason reason)
            : base(message)
        {
            Result = result;
            RejectSource = rejectSource;
            Reason = reason;
        }

        private static string FormatMessage(RejectResult result, RejectSource rejectSource, RejectReason reason)
        {
            return $"Association rejected: {result} from {rejectSource}, reason: {reason}";
        }
    }
}
