using System;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network.Exceptions
{
    /// <summary>
    /// Exception thrown when a DICOM association is aborted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exception is thrown when an A-ABORT PDU is received,
    /// indicating that the remote peer has abruptly terminated the association.
    /// </para>
    /// <para>
    /// Unlike a graceful release (A-RELEASE), an abort indicates an error condition
    /// and any in-progress operations should be considered failed.
    /// </para>
    /// </remarks>
    public class DicomAbortException : DicomNetworkException
    {
        /// <summary>
        /// Gets the abort source indicating who initiated the abort.
        /// </summary>
        /// <remarks>
        /// Indicates whether the abort originated from the service user (application)
        /// or the service provider (protocol layer).
        /// </remarks>
        public AbortSource AbortSource { get; }

        /// <summary>
        /// Gets the reason for the abort.
        /// </summary>
        /// <remarks>
        /// This value is only meaningful when <see cref="AbortSource"/> is
        /// <see cref="Pdu.AbortSource.ServiceProvider"/>. When the source is
        /// <see cref="Pdu.AbortSource.ServiceUser"/>, this should be
        /// <see cref="AbortReason.NotSpecified"/>.
        /// </remarks>
        public AbortReason Reason { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAbortException"/> class
        /// with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomAbortException(string message) : base(message)
        {
            AbortSource = AbortSource.ServiceUser;
            Reason = AbortReason.NotSpecified;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAbortException"/> class
        /// with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomAbortException(string message, Exception innerException)
            : base(message, innerException)
        {
            AbortSource = AbortSource.ServiceUser;
            Reason = AbortReason.NotSpecified;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAbortException"/> class
        /// with abort details from an A-ABORT PDU.
        /// </summary>
        /// <param name="abortSource">The source of the abort.</param>
        /// <param name="reason">The reason for the abort.</param>
        public DicomAbortException(AbortSource abortSource, AbortReason reason)
            : base(FormatMessage(abortSource, reason))
        {
            AbortSource = abortSource;
            Reason = reason;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAbortException"/> class
        /// with a custom message and abort details.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="abortSource">The source of the abort.</param>
        /// <param name="reason">The reason for the abort.</param>
        public DicomAbortException(string message, AbortSource abortSource, AbortReason reason)
            : base(message)
        {
            AbortSource = abortSource;
            Reason = reason;
        }

        private static string FormatMessage(AbortSource abortSource, AbortReason reason)
        {
            return abortSource == AbortSource.ServiceProvider
                ? $"Association aborted by provider: {reason}"
                : "Association aborted by user";
        }
    }
}
