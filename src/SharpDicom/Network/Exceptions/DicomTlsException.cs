using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Exceptions
{
    /// <summary>
    /// Base exception class for TLS-related errors in DICOM networking.
    /// </summary>
    /// <remarks>
    /// This exception provides context about TLS failures including the remote
    /// certificate and SSL policy errors that occurred during validation.
    /// More specific exceptions like <see cref="CertificateValidationException"/>
    /// and <see cref="TlsHandshakeException"/> provide additional detail.
    /// </remarks>
    public class DicomTlsException : DicomNetworkException
    {
        /// <summary>
        /// Gets the remote certificate that was presented during the TLS handshake, if available.
        /// </summary>
        public X509Certificate? RemoteCertificate { get; }

        /// <summary>
        /// Gets the SSL policy errors that occurred during certificate validation, if available.
        /// </summary>
        public SslPolicyErrors? PolicyErrors { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTlsException"/> class
        /// with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public DicomTlsException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTlsException"/> class
        /// with a specified error message and a reference to the inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomTlsException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTlsException"/> class
        /// with a specified error message, remote certificate, and policy errors.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that was presented during the TLS handshake.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred during certificate validation.</param>
        public DicomTlsException(string message, X509Certificate? remoteCertificate, SslPolicyErrors policyErrors)
            : base(message)
        {
            RemoteCertificate = remoteCertificate;
            PolicyErrors = policyErrors;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomTlsException"/> class
        /// with a specified error message, remote certificate, policy errors, and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that was presented during the TLS handshake.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred during certificate validation.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public DicomTlsException(string message, X509Certificate? remoteCertificate, SslPolicyErrors policyErrors, Exception innerException)
            : base(message, innerException)
        {
            RemoteCertificate = remoteCertificate;
            PolicyErrors = policyErrors;
        }
    }
}
