using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Exceptions
{
    /// <summary>
    /// Exception thrown when certificate validation fails during TLS handshake.
    /// </summary>
    /// <remarks>
    /// This exception includes detailed information about the certificate chain
    /// validation failures, including chain status information from <see cref="X509Chain"/>.
    /// </remarks>
    public class CertificateValidationException : DicomTlsException
    {
        /// <summary>
        /// Gets the certificate chain status information from the failed validation.
        /// </summary>
        /// <remarks>
        /// Contains detailed information about why the certificate chain failed validation,
        /// such as expired certificates, untrusted roots, or revocation failures.
        /// </remarks>
        public IReadOnlyList<X509ChainStatus>? ChainStatus { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationException"/> class
        /// with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public CertificateValidationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationException"/> class
        /// with a specified error message and certificate details.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that failed validation.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred.</param>
        public CertificateValidationException(string message, X509Certificate? remoteCertificate, SslPolicyErrors policyErrors)
            : base(message, remoteCertificate, policyErrors)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationException"/> class
        /// with a specified error message, certificate details, and chain status.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="remoteCertificate">The remote certificate that failed validation.</param>
        /// <param name="policyErrors">The SSL policy errors that occurred.</param>
        /// <param name="chainStatus">The certificate chain status information.</param>
        public CertificateValidationException(
            string message,
            X509Certificate? remoteCertificate,
            SslPolicyErrors policyErrors,
            X509ChainStatus[]? chainStatus)
            : base(message, remoteCertificate, policyErrors)
        {
            ChainStatus = chainStatus?.ToList();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidationException"/> class
        /// with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public CertificateValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
