using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Tls
{
    /// <summary>
    /// Configuration options for client-side TLS in DICOM networking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These options control how the DICOM client establishes TLS connections,
    /// including protocol selection, certificate validation, and client certificate
    /// provisioning for mutual TLS (mTLS).
    /// </para>
    /// <para>
    /// By default, TLS configuration enforces DICOM PS3.15 Annex B.3 requirements
    /// (TLS 1.2+ and BCP 195 compliant cipher suites). Set <see cref="EnforceDicomTlsProfile"/>
    /// to <c>false</c> to allow non-standard configurations.
    /// </para>
    /// </remarks>
    public sealed class TlsOptions
    {
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
        /// Gets or sets the callback for validating the server's certificate.
        /// </summary>
        /// <remarks>
        /// If <c>null</c>, system certificate store validation is used. Set this to implement
        /// custom validation logic such as certificate pinning or custom trust stores.
        /// See CertificateValidator class for pre-built validation strategies.
        /// </remarks>
        public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }

        /// <summary>
        /// Gets or sets the collection of client certificates for mutual TLS (mTLS).
        /// </summary>
        /// <remarks>
        /// For mTLS, add client certificates with private keys to this collection.
        /// The server will select an appropriate certificate during handshake.
        /// </remarks>
        public X509CertificateCollection? ClientCertificates { get; set; }

        /// <summary>
        /// Gets or sets a single client certificate for mutual TLS (mTLS).
        /// </summary>
        /// <remarks>
        /// This is a convenience property that adds the certificate to <see cref="ClientCertificates"/>.
        /// If you have multiple client certificates, add them directly to <see cref="ClientCertificates"/>.
        /// </remarks>
        public X509Certificate2? ClientCertificate
        {
            get => ClientCertificates?.Count > 0 ? ClientCertificates[0] as X509Certificate2 : null;
            set
            {
                if (value == null)
                {
                    ClientCertificates = null;
                }
                else
                {
                    ClientCertificates = new X509CertificateCollection { value };
                }
            }
        }

        /// <summary>
        /// Gets or sets the certificate chain policy for custom trust stores.
        /// </summary>
        /// <remarks>
        /// Use this to configure custom root CA certificates for closed networks.
        /// Set TrustMode to CustomRootTrust and add custom CAs to CustomTrustStore.
        /// </remarks>
        public X509ChainPolicy? CertificateChainPolicy { get; set; }

        /// <summary>
        /// Gets or sets the list of accepted SHA256 certificate thumbprints for self-signed certificates.
        /// </summary>
        /// <remarks>
        /// When populated, certificates matching these thumbprints will be accepted even if
        /// they fail standard validation. Thumbprints should be uppercase hex strings without separators.
        /// </remarks>
        public List<string>? AcceptedThumbprints { get; set; }

        /// <summary>
        /// Gets or sets the list of custom CA certificates to trust.
        /// </summary>
        /// <remarks>
        /// These CAs will be added to the trust store when validating the server certificate.
        /// Useful for closed networks with private PKI infrastructure.
        /// </remarks>
        public List<X509Certificate2>? CustomCAs { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to allow protocol version downgrade.
        /// </summary>
        /// <remarks>
        /// When <c>false</c> (default), the connection is rejected if the negotiated protocol
        /// version is lower than the minimum requested version. Set to <c>true</c> to allow
        /// connections even if a lower protocol version is negotiated (not recommended).
        /// </remarks>
        public bool AllowProtocolDowngrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enforce DICOM TLS Profile compliance.
        /// </summary>
        /// <remarks>
        /// When <c>true</c> (default), the connection is validated after handshake to ensure
        /// it uses TLS 1.2+ and a DICOM-compliant cipher suite per PS3.15 Annex B.3 (BCP 195).
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
        /// Validates the TLS configuration and throws if invalid.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <see cref="HandshakeTimeout"/> is not positive.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when configuration contains conflicting or invalid settings.
        /// </exception>
        public void Validate()
        {
            if (HandshakeTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(HandshakeTimeout), "HandshakeTimeout must be positive.");

            // If client certificate is provided, verify it has a private key for mTLS
            if (ClientCertificates != null)
            {
                foreach (var cert in ClientCertificates)
                {
                    if (cert is X509Certificate2 cert2 && !cert2.HasPrivateKey)
                        throw new InvalidOperationException(
                            $"Client certificate '{cert2.Subject}' does not have a private key. " +
                            "Client certificates for mutual TLS must include the private key.");
                }
            }

            // Validate that EnabledProtocols is DICOM-compliant if enforcement is enabled
            if (EnforceDicomTlsProfile && EnabledProtocols.HasValue)
            {
                if (!DicomTlsProfile.IsCompliantProtocol(EnabledProtocols.Value))
                    throw new InvalidOperationException(
                        $"EnabledProtocols '{EnabledProtocols}' is not DICOM-compliant. " +
                        $"DICOM requires TLS 1.2 or higher. Set EnforceDicomTlsProfile to false to allow non-compliant protocols.");
            }

            // Validate accepted thumbprints format (if provided)
            if (AcceptedThumbprints != null)
            {
                foreach (var thumbprint in AcceptedThumbprints)
                {
                    if (string.IsNullOrWhiteSpace(thumbprint))
                        throw new InvalidOperationException("Accepted thumbprints cannot be null or whitespace.");

                    // SHA256 thumbprints are 64 hex characters
                    if (thumbprint.Length != 64 || !thumbprint.All(c => Uri.IsHexDigit(c)))
                        throw new InvalidOperationException(
                            $"Invalid thumbprint '{thumbprint}'. Thumbprints must be 64-character uppercase hex strings (SHA256).");
                }
            }
        }
    }
}
