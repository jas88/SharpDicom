using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;

namespace SharpDicom.Network.Tls
{
    /// <summary>
    /// Defines DICOM-compliant TLS configuration based on DICOM PS3.15 Annex B.3 (BCP 195).
    /// </summary>
    /// <remarks>
    /// <para>
    /// DICOM requires TLS 1.2 or higher and restricts cipher suites to those providing
    /// strong encryption and forward secrecy. This class provides validation methods to
    /// ensure TLS configuration meets DICOM security requirements.
    /// </para>
    /// <para>
    /// The cipher suite restrictions are based on IETF BCP 195 which specifies recommended
    /// cipher suites for healthcare applications to protect patient data in transit.
    /// </para>
    /// </remarks>
    public static class DicomTlsProfile
    {
        /// <summary>
        /// Minimum acceptable TLS protocol version for DICOM communication.
        /// </summary>
        public static readonly SslProtocols MinimumProtocol = SslProtocols.Tls12;

        /// <summary>
        /// Recommended TLS protocol versions for DICOM communication (TLS 1.2 and TLS 1.3).
        /// </summary>
        public static readonly SslProtocols RecommendedProtocols =
#if NET6_0_OR_GREATER
            SslProtocols.Tls12 | SslProtocols.Tls13;
#else
            SslProtocols.Tls12;
#endif

        /// <summary>
        /// DICOM-compliant cipher suite names for TLS 1.2.
        /// </summary>
        /// <remarks>
        /// These cipher suites provide forward secrecy (ECDHE key exchange) and strong
        /// encryption (AES-GCM or ChaCha20-Poly1305).
        /// </remarks>
        private static readonly HashSet<string> _tls12CompliantCipherSuites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // ECDHE with AES-GCM (preferred)
            "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
            "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
            "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
            "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",

            // ChaCha20-Poly1305 (modern alternative to AES)
            "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",
            "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
        };

        /// <summary>
        /// DICOM-compliant cipher suite names for TLS 1.3.
        /// </summary>
        /// <remarks>
        /// TLS 1.3 removed weak cipher suites, so all TLS 1.3 cipher suites are acceptable.
        /// </remarks>
        private static readonly HashSet<string> _tls13CompliantCipherSuites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TLS_AES_128_GCM_SHA256",
            "TLS_AES_256_GCM_SHA384",
            "TLS_CHACHA20_POLY1305_SHA256",
            "TLS_AES_128_CCM_SHA256",
            "TLS_AES_128_CCM_8_SHA256",
        };

#if NET6_0_OR_GREATER
        /// <summary>
        /// Gets TLS cipher suites allowed for DICOM communication on .NET 6+.
        /// </summary>
        /// <remarks>
        /// This array can be used to construct a <see cref="CipherSuitesPolicy"/> for enforcing
        /// DICOM-compliant cipher suites. Note that not all suites may be supported on all platforms.
        /// </remarks>
        public static readonly TlsCipherSuite[] CompliantCipherSuites = new[]
        {
            // TLS 1.3 suites
            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,

            // TLS 1.2 suites - ECDHE with AES-GCM
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        };
#endif

        /// <summary>
        /// Determines whether the specified cipher suite name is DICOM-compliant.
        /// </summary>
        /// <param name="cipherSuiteName">The cipher suite name to check.</param>
        /// <returns>
        /// <c>true</c> if the cipher suite is allowed for DICOM communication; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsCompliant(string cipherSuiteName)
        {
            if (string.IsNullOrWhiteSpace(cipherSuiteName))
                return false;

            return _tls12CompliantCipherSuites.Contains(cipherSuiteName) ||
                   _tls13CompliantCipherSuites.Contains(cipherSuiteName);
        }

        /// <summary>
        /// Determines whether the specified TLS protocol version is DICOM-compliant.
        /// </summary>
        /// <param name="protocol">The protocol version to check.</param>
        /// <returns>
        /// <c>true</c> if the protocol version is TLS 1.2 or higher; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// DICOM PS3.15 Annex B.3 requires TLS 1.2 or higher. TLS 1.0 and TLS 1.1 are not allowed.
        /// </remarks>
        public static bool IsCompliantProtocol(SslProtocols protocol)
        {
            // Check if any non-compliant protocols are present
#pragma warning disable CS0618, CA5397, SYSLIB0039 // Obsolete protocols - used for validation only
            var nonCompliantProtocols = SslProtocols.Ssl2 | SslProtocols.Ssl3 |
                                       SslProtocols.Tls | SslProtocols.Tls11;
#pragma warning restore CS0618, CA5397, SYSLIB0039

            if ((protocol & nonCompliantProtocols) != 0)
                return false;

            // Check if at least TLS 1.2 or higher is present
#if NET6_0_OR_GREATER
            var compliantProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
#else
            var compliantProtocols = SslProtocols.Tls12;
#endif

            return (protocol & compliantProtocols) != 0;
        }
    }
}
