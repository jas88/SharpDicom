using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Tls
{
    /// <summary>
    /// Contains information about an established TLS connection.
    /// </summary>
    /// <remarks>
    /// This type captures the negotiated TLS parameters after a successful handshake,
    /// including protocol version, cipher suite, and certificate information.
    /// </remarks>
#if NET6_0_OR_GREATER
    public readonly record struct TlsConnectionInfo
#else
    public sealed class TlsConnectionInfo
#endif
    {
        /// <summary>
        /// Gets the negotiated TLS protocol version.
        /// </summary>
        public SslProtocols Protocol { get; init; }

        /// <summary>
        /// Gets the name of the negotiated cipher suite.
        /// </summary>
        /// <remarks>
        /// The string representation is used instead of the TlsCipherSuite enum
        /// to maintain compatibility with netstandard2.0 where the enum is not available.
        /// </remarks>
        public string CipherSuiteName { get; init; }

        /// <summary>
        /// Gets the remote party's certificate, if available.
        /// </summary>
        public X509Certificate2? RemoteCertificate { get; init; }

        /// <summary>
        /// Gets a value indicating whether the connection is mutually authenticated.
        /// </summary>
        /// <remarks>
        /// This is <c>true</c> when both client and server have presented certificates
        /// that passed validation (mutual TLS / mTLS).
        /// </remarks>
        public bool IsMutuallyAuthenticated { get; init; }

        /// <summary>
        /// Gets the target host name used for certificate validation, if available.
        /// </summary>
        public string? TargetHost { get; init; }

#if !NET6_0_OR_GREATER
        /// <summary>
        /// Initializes a new instance of the <see cref="TlsConnectionInfo"/> class.
        /// </summary>
        /// <param name="protocol">The negotiated TLS protocol version.</param>
        /// <param name="cipherSuiteName">The name of the negotiated cipher suite.</param>
        /// <param name="remoteCertificate">The remote party's certificate.</param>
        /// <param name="isMutuallyAuthenticated">Whether the connection is mutually authenticated.</param>
        /// <param name="targetHost">The target host name used for certificate validation.</param>
        public TlsConnectionInfo(
            SslProtocols protocol,
            string cipherSuiteName,
            X509Certificate2? remoteCertificate = null,
            bool isMutuallyAuthenticated = false,
            string? targetHost = null)
        {
            Protocol = protocol;
            CipherSuiteName = cipherSuiteName ?? string.Empty;
            RemoteCertificate = remoteCertificate;
            IsMutuallyAuthenticated = isMutuallyAuthenticated;
            TargetHost = targetHost;
        }
#endif

        /// <summary>
        /// Creates a <see cref="TlsConnectionInfo"/> instance from an <see cref="SslStream"/>.
        /// </summary>
        /// <param name="sslStream">The SSL stream to extract information from.</param>
        /// <returns>A new <see cref="TlsConnectionInfo"/> instance with the connection details.</returns>
        public static TlsConnectionInfo FromSslStream(SslStream sslStream)
        {
            // Convert to X509Certificate2 to preserve certificate data
            var remoteCert = sslStream.RemoteCertificate != null
                ? sslStream.RemoteCertificate as X509Certificate2
                  ?? new X509Certificate2(sslStream.RemoteCertificate)
                : null;

#if NET6_0_OR_GREATER
            // On .NET 6+, we can get the actual cipher suite enum
            var cipherSuiteName = sslStream.NegotiatedCipherSuite.ToString();
            var targetHost = sslStream.TargetHostName;

            return new TlsConnectionInfo
            {
                Protocol = sslStream.SslProtocol,
                CipherSuiteName = cipherSuiteName,
                RemoteCertificate = remoteCert,
                IsMutuallyAuthenticated = sslStream.IsMutuallyAuthenticated,
                TargetHost = targetHost
            };
#else
            // On netstandard2.0, cipher suite information is not available
            return new TlsConnectionInfo(
                sslStream.SslProtocol,
                "Unknown",
                remoteCert,
                sslStream.IsMutuallyAuthenticated,
                null);
#endif
        }
    }
}
