using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Exceptions
{
    /// <summary>
    /// Exception thrown when the TLS handshake fails.
    /// </summary>
    /// <remarks>
    /// This exception is thrown for handshake-level failures such as protocol
    /// version mismatches, cipher suite negotiation failures, authentication
    /// timeouts, or other TLS protocol errors.
    /// </remarks>
    public class TlsHandshakeException : DicomTlsException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TlsHandshakeException"/> class
        /// with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public TlsHandshakeException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TlsHandshakeException"/> class
        /// with a specified error message and a reference to the inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public TlsHandshakeException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TlsHandshakeException"/> class
        /// with a specified error message and certificate details.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that was presented during the failed handshake.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred.</param>
        public TlsHandshakeException(string message, X509Certificate? remoteCertificate, SslPolicyErrors policyErrors)
            : base(message, remoteCertificate, policyErrors)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TlsHandshakeException"/> class
        /// with a specified error message, certificate details, and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that was presented during the failed handshake.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public TlsHandshakeException(
            string message,
            X509Certificate? remoteCertificate,
            SslPolicyErrors policyErrors,
            Exception innerException)
            : base(message, remoteCertificate, policyErrors, innerException)
        {
        }
    }
}
