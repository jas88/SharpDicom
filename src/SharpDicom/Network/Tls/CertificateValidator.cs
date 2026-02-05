using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Network.Tls
{
    /// <summary>
    /// Provides reusable certificate validation strategies for TLS connections.
    /// </summary>
    /// <remarks>
    /// This class implements the <see cref="RemoteCertificateValidationCallback"/> delegate
    /// pattern with support for multiple validation strategies including thumbprint whitelisting,
    /// custom CA trust, and self-signed certificate acceptance.
    /// </remarks>
    public sealed class CertificateValidator
    {
        private readonly HashSet<string>? _acceptedThumbprints;
        private readonly List<X509Certificate2>? _customCAs;
        private readonly bool _allowSelfSigned;

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateValidator"/> class
        /// with the specified validation options.
        /// </summary>
        /// <param name="acceptedThumbprints">SHA256 certificate thumbprints to accept (uppercase hex without separators).</param>
        /// <param name="customCAs">Custom CA certificates to trust.</param>
        /// <param name="allowSelfSigned">Whether to accept self-signed certificates.</param>
        public CertificateValidator(
            IEnumerable<string>? acceptedThumbprints = null,
            IEnumerable<X509Certificate2>? customCAs = null,
            bool allowSelfSigned = false)
        {
            _acceptedThumbprints = acceptedThumbprints != null
                ? new HashSet<string>(acceptedThumbprints, StringComparer.OrdinalIgnoreCase)
                : null;
            _customCAs = customCAs?.ToList();
            _allowSelfSigned = allowSelfSigned;
        }

        /// <summary>
        /// Creates a validator that uses only system certificate store validation.
        /// </summary>
        /// <returns>A new <see cref="CertificateValidator"/> that accepts only system-trusted certificates.</returns>
        public static CertificateValidator SystemOnly() => new CertificateValidator();

        /// <summary>
        /// Creates a validator that accepts certificates matching the specified thumbprints.
        /// </summary>
        /// <param name="thumbprints">SHA256 certificate thumbprints to accept (uppercase hex without separators).</param>
        /// <returns>A new <see cref="CertificateValidator"/> that uses thumbprint whitelisting.</returns>
        public static CertificateValidator AcceptThumbprints(params string[] thumbprints)
            => new CertificateValidator(acceptedThumbprints: thumbprints);

        /// <summary>
        /// Creates a validator that accepts self-signed certificates.
        /// </summary>
        /// <returns>A new <see cref="CertificateValidator"/> that accepts self-signed certificates.</returns>
        /// <remarks>
        /// This validator will accept certificates that are self-signed (chain length == 1)
        /// and have only the UntrustedRoot chain status. Other validation errors will still
        /// cause rejection (e.g., expired certificates, revocation failures).
        /// </remarks>
        public static CertificateValidator AcceptSelfSigned()
            => new CertificateValidator(allowSelfSigned: true);

        /// <summary>
        /// Creates a validator that trusts the specified custom CA certificates.
        /// </summary>
        /// <param name="customCAs">Custom CA certificates to trust.</param>
        /// <returns>A new <see cref="CertificateValidator"/> that uses custom CA trust.</returns>
        public static CertificateValidator WithCustomCAs(params X509Certificate2[] customCAs)
            => new CertificateValidator(customCAs: customCAs);

        /// <summary>
        /// Validates a certificate using the configured validation strategies.
        /// </summary>
        /// <param name="sender">The object that is initiating the validation.</param>
        /// <param name="certificate">The certificate to validate.</param>
        /// <param name="chain">The certificate chain.</param>
        /// <param name="sslPolicyErrors">SSL policy errors from the initial validation.</param>
        /// <returns><c>true</c> if the certificate is valid; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// This method implements the validation pipeline:
        /// 1. Reject if no certificate provided
        /// 2. If thumbprint whitelist configured: accept if match, reject if not
        /// 3. If system validation passed: accept
        /// 4. If custom CAs configured: build chain with custom trust, accept if valid
        /// 5. If self-signed allowed: check for single cert with only UntrustedRoot error
        /// 6. Reject otherwise
        /// </remarks>
        public bool Validate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // 1. Reject if no certificate provided
            if (certificate == null)
                return false;

            // Convert to X509Certificate2 if needed, disposing wrapper if created
            X509Certificate2 cert2;
            bool createdWrapper;

            if (certificate is X509Certificate2 x509Cert2)
            {
                cert2 = x509Cert2;
                createdWrapper = false;
            }
            else
            {
                cert2 = new X509Certificate2(certificate);
                createdWrapper = true;
            }

            try
            {
                // 2. Thumbprint whitelist has highest priority (pin-based trust)
                if (_acceptedThumbprints != null)
                {
#if NET6_0_OR_GREATER
                    var thumbprint = cert2.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
#else
                    // On netstandard2.0, compute SHA256 thumbprint manually
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var hash = sha256.ComputeHash(cert2.RawData);
                    var thumbprint = BitConverter.ToString(hash).Replace("-", "");
#endif
                    return _acceptedThumbprints.Contains(thumbprint);
                }

                // 3. If system validation passed, accept
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                // 4. If custom CAs configured, try building chain with custom trust
                if (_customCAs != null && _customCAs.Count > 0)
                {
                    using var customChain = new X509Chain();
#if NET5_0_OR_GREATER
                    customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    customChain.ChainPolicy.CustomTrustStore.AddRange(_customCAs.ToArray());
#else
                    // On netstandard2.0, add custom CAs to ExtraStore (less reliable)
                    customChain.ChainPolicy.ExtraStore.AddRange(_customCAs.ToArray());
#endif
                    customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                    // Build the chain - may return false even with valid custom CA
                    var buildResult = customChain.Build(cert2);

                    // Check if chain was built and terminates at one of our custom CAs
                    if (customChain.ChainElements.Count > 0)
                    {
                        var rootCert = customChain.ChainElements[customChain.ChainElements.Count - 1].Certificate;
                        bool chainEndsWithCustomCA = _customCAs.Any(ca => ca.Thumbprint == rootCert.Thumbprint);

                        if (chainEndsWithCustomCA)
                        {
#if NET5_0_OR_GREATER
                            // With CustomRootTrust, the build should succeed
                            return buildResult;
#else
                            // On netstandard2.0, build may fail with UntrustedRoot even with valid chain
                            // Accept if the only error is UntrustedRoot
                            if (buildResult)
                                return true;

                            var statuses = customChain.ChainStatus;
                            if (statuses.Length == 1 && statuses[0].Status == X509ChainStatusFlags.UntrustedRoot)
                                return true;
#endif
                        }
                    }
                }

                // 5. If self-signed allowed, check specific conditions
                if (_allowSelfSigned)
                {
                    // Self-signed cert must:
                    // - Have only the RemoteCertificateChainErrors flag
                    // - Have chain length == 1 (self-signed)
                    // - Have only UntrustedRoot as the chain status
                    if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors &&
                        chain != null &&
                        chain.ChainElements.Count == 1)
                    {
                        // Check that the only error is UntrustedRoot
                        var statuses = chain.ChainStatus;
                        if (statuses.Length == 1 &&
                            statuses[0].Status == X509ChainStatusFlags.UntrustedRoot)
                        {
                            // Additionally verify subject == issuer (self-signed)
                            return cert2.Subject == cert2.Issuer;
                        }
                    }
                }

                // 6. Reject otherwise
                return false;
            }
            finally
            {
                // Dispose X509Certificate2 wrapper if we created it
                if (createdWrapper)
                    cert2.Dispose();
            }
        }
    }
}
