using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Tls
{
    /// <summary>
    /// Configuration options for server-side TLS in DICOM networking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control how the DICOM server establishes TLS connections,
    /// including server certificate provisioning, client certificate requirements,
    /// and protocol selection.
    /// </para>
    /// <para>
    /// By default, TLS configuration enforces DICOM PS3.15 Annex B.3 requirements
    /// (TLS 1.2+ and BCP 195 compliant cipher suites). Set <see cref="EnforceDicomTlsProfile"/>
    /// to <c>false</c> to allow non-standard configurations.
    /// </para>
    /// </remarks>
    public sealed class TlsServerOptions
    {
        /// <summary>
        /// Gets or sets the server certificate with private key.
        /// </summary>
        /// <remarks>
        /// Required for TLS server operation. The certificate must include the private key
        /// and should be trusted by clients (either via system CA or custom trust).
        /// </remarks>
        public X509Certificate2? ServerCertificate { get; set; }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Gets or sets the server certificate context for improved performance and session resumption.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Available on .NET 5.0 and later. Use SslStreamCertificateContext.Create method
        /// to create a context with intermediate certificates and OCSP response stapling.
        /// </para>
        /// <para>
        /// If both <see cref="ServerCertificate"/> and <see cref="ServerCertificateContext"/> are set,
        /// <see cref="ServerCertificateContext"/> takes precedence as it provides better performance.
        /// </para>
        /// </remarks>
        public SslStreamCertificateContext? ServerCertificateContext { get; set; }
#endif

        /// <summary>
        /// Gets or sets a value indicating whether to require client certificates (mutual TLS).
        /// </summary>
        /// <remarks>
        /// When <c>true</c>, clients must present a valid certificate during handshake.
        /// When <c>false</c> (default), client certificates are optional.
        /// </remarks>
        public bool RequireClientCertificate { get; set; }

        /// <summary>
        /// Gets or sets the callback for validating client certificates.
        /// </summary>
        /// <remarks>
        /// If <c>null</c>, system certificate store validation is used when <see cref="RequireClientCertificate"/>
        /// is <c>true</c>. Set this to implement custom validation logic such as DN pattern matching
        /// or application-specific authorization checks.
        /// </remarks>
        public RemoteCertificateValidationCallback? ClientCertificateValidationCallback { get; set; }

        /// <summary>
        /// Gets or sets the enabled TLS protocol versions.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <c>null</c> (default), the operating system will choose the protocol version.
        /// For maximum compatibility, use <see cref="DicomTlsProfile.RecommendedProtocols"/>.
        /// </para>
        /// <para>
        /// DICOM requires TLS 1.2 or higher. TLS 1.0 and TLS 1.1 are deprecated and not allowed.
        /// </para>
        /// </remarks>
        public SslProtocols? EnabledProtocols { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enforce DICOM TLS Profile compliance.
        /// </summary>
        /// <remarks>
        /// When <c>true</c> (default), connections are validated after handshake to ensure
        /// they use TLS 1.2+ and a DICOM-compliant cipher suite per PS3.15 Annex B.3 (BCP 195).
        /// Set to <c>false</c> for non-standard scenarios.
        /// </remarks>
        public bool EnforceDicomTlsProfile { get; set; } = true;

        /// <summary>
        /// Gets or sets the timeout for the TLS handshake.
        /// </summary>
        /// <remarks>
        /// Default is 30 seconds. If the handshake does not complete within this time,
        /// the connection attempt is aborted.
        /// </remarks>
        public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the certificate revocation checking mode.
        /// </summary>
        /// <remarks>
        /// Default is <see cref="X509RevocationMode.Online"/>. Set to <see cref="X509RevocationMode.NoCheck"/>
        /// for closed networks without revocation services (not recommended for production).
        /// </remarks>
        public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Online;

#if NET6_0_OR_GREATER
        /// <summary>
        /// Gets or sets the cipher suites policy for restricting allowed cipher suites.
        /// </summary>
        /// <remarks>
        /// Available on .NET 6.0 and later. Use <see cref="DicomTlsProfile.CompliantCipherSuites"/>
        /// for DICOM-compliant cipher suite enforcement.
        /// </remarks>
        public CipherSuitesPolicy? CipherSuitesPolicy { get; set; }
#endif

        /// <summary>
        /// Validates the TLS server configuration and throws if invalid.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <see cref="ServerCertificate"/> is missing or does not have a private key.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <see cref="HandshakeTimeout"/> is not positive.
        /// </exception>
        public void Validate()
        {
#if NET5_0_OR_GREATER
            // On .NET 5+, either ServerCertificate or ServerCertificateContext must be set
            if (ServerCertificate == null && ServerCertificateContext == null)
                throw new InvalidOperationException(
                    "Either ServerCertificate or ServerCertificateContext must be set for TLS server.");

            // If both are set, ensure they're consistent
            if (ServerCertificate != null && ServerCertificateContext != null)
            {
                // ServerCertificateContext will take precedence, just validate ServerCertificate has private key
                if (!ServerCertificate.HasPrivateKey)
                    throw new InvalidOperationException(
                        "ServerCertificate must include the private key.");
            }
            else if (ServerCertificateContext != null)
            {
                // ServerCertificateContext is set, ServerCertificate is not - this is valid
                // The context was created with SslStreamCertificateContext.Create which validates the cert
            }
            else if (ServerCertificate != null)
            {
                // Only ServerCertificate is set
                if (!ServerCertificate.HasPrivateKey)
                    throw new InvalidOperationException(
                        "ServerCertificate must include the private key.");
            }
#else
            // On netstandard2.0, only ServerCertificate is available
            if (ServerCertificate == null)
                throw new InvalidOperationException("ServerCertificate is required for TLS server.");

            if (!ServerCertificate.HasPrivateKey)
                throw new InvalidOperationException(
                    "ServerCertificate must include the private key.");
#endif

            if (HandshakeTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(HandshakeTimeout), "HandshakeTimeout must be positive.");

            // Validate that EnabledProtocols is DICOM-compliant if enforcement is enabled
            if (EnforceDicomTlsProfile && EnabledProtocols.HasValue)
            {
                if (!DicomTlsProfile.IsCompliantProtocol(EnabledProtocols.Value))
                    throw new InvalidOperationException(
                        $"EnabledProtocols '{EnabledProtocols}' is not DICOM-compliant. " +
                        $"DICOM requires TLS 1.2 or higher. Set EnforceDicomTlsProfile to false to allow non-compliant protocols.");
            }
        }
    }
}
