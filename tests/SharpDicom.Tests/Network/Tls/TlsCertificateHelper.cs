using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpDicom.Tests.Network.Tls
{
    /// <summary>
    /// Helper class for generating test certificates programmatically.
    /// </summary>
    /// <remarks>
    /// Provides utilities for creating self-signed certificates, CA-signed certificates,
    /// and expired certificates for TLS integration testing.
    /// </remarks>
    internal static class TlsCertificateHelper
    {
#if NET6_0_OR_GREATER
        /// <summary>
        /// Creates a self-signed certificate for testing.
        /// </summary>
        /// <param name="subjectName">The subject name (CN) for the certificate.</param>
        /// <param name="validity">How long the certificate should be valid.</param>
        /// <returns>A self-signed certificate with private key.</returns>
        public static X509Certificate2 CreateSelfSignedCertificate(string subjectName, TimeSpan validity)
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Add basic constraints (CA=false)
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Add key usage
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Add enhanced key usage for server authentication
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                    },
                    false));

            // Add Subject Alternative Name with localhost and 127.0.0.1
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = notBefore.Add(validity);

            var cert = request.CreateSelfSigned(notBefore, notAfter);

            // Export and re-import to ensure private key is exportable
            var exported = cert.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(exported, "", X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(exported, "", X509KeyStorageFlags.Exportable);
#endif
        }

        /// <summary>
        /// Creates a CA certificate and a leaf certificate signed by that CA.
        /// </summary>
        /// <param name="caSubject">The subject name for the CA certificate.</param>
        /// <param name="leafSubject">The subject name for the leaf certificate.</param>
        /// <returns>A tuple containing the CA certificate and the leaf certificate.</returns>
        public static (X509Certificate2 CA, X509Certificate2 Leaf) CreateCASignedCertificate(
            string caSubject,
            string leafSubject)
        {
            // Create CA certificate
            using var caRsa = RSA.Create(2048);
            var caRequest = new CertificateRequest(
                $"CN={caSubject}",
                caRsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // CA basic constraints
            caRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));

            // CA key usage
            caRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));

            var caNotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var caNotAfter = caNotBefore.AddYears(1);

            var caCert = caRequest.CreateSelfSigned(caNotBefore, caNotAfter);
            var exportedCa = caCert.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
            var ca = X509CertificateLoader.LoadPkcs12(exportedCa, "", X509KeyStorageFlags.Exportable);
#else
            var ca = new X509Certificate2(exportedCa, "", X509KeyStorageFlags.Exportable);
#endif

            // Create leaf certificate signed by CA
            using var leafRsa = RSA.Create(2048);
            var leafRequest = new CertificateRequest(
                $"CN={leafSubject}",
                leafRsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Leaf basic constraints (not a CA)
            leafRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Leaf key usage
            leafRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Enhanced key usage for server authentication
            leafRequest.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                    },
                    false));

            // Add SAN
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            leafRequest.CertificateExtensions.Add(sanBuilder.Build());

            var leafNotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var leafNotAfter = leafNotBefore.AddYears(1);

            // Sign the leaf certificate with the CA
            var leafSerial = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(leafSerial);
            }

            using var leafCertWithoutKey = leafRequest.Create(
                ca.SubjectName,
                X509SignatureGenerator.CreateForRSA(ca.GetRSAPrivateKey()!, RSASignaturePadding.Pkcs1),
                leafNotBefore,
                leafNotAfter,
                leafSerial);

            // Combine the leaf cert with its private key
            var leafWithKey = leafCertWithoutKey.CopyWithPrivateKey(leafRsa);
            var exportedLeaf = leafWithKey.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
            var leaf = X509CertificateLoader.LoadPkcs12(exportedLeaf, "", X509KeyStorageFlags.Exportable);
#else
            var leaf = new X509Certificate2(exportedLeaf, "", X509KeyStorageFlags.Exportable);
#endif

            return (ca, leaf);
        }

        /// <summary>
        /// Creates a certificate that expired yesterday.
        /// </summary>
        /// <param name="subjectName">The subject name for the certificate.</param>
        /// <returns>An expired certificate.</returns>
        public static X509Certificate2 CreateExpiredCertificate(string subjectName)
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Basic constraints
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Key usage
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Enhanced key usage for server authentication
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
                    },
                    false));

            // Add SAN
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
            sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(sanBuilder.Build());

            // Expired: valid from 2 days ago to yesterday
            var notBefore = DateTimeOffset.UtcNow.AddDays(-2);
            var notAfter = DateTimeOffset.UtcNow.AddDays(-1);

            var cert = request.CreateSelfSigned(notBefore, notAfter);

            // Export and re-import to ensure private key is exportable
            var exported = cert.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(exported, "", X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(exported, "", X509KeyStorageFlags.Exportable);
#endif
        }

        /// <summary>
        /// Creates a client certificate for mutual TLS authentication.
        /// </summary>
        /// <param name="subjectName">The subject name for the certificate.</param>
        /// <param name="signerCert">Optional CA certificate to sign the client cert. If null, creates self-signed.</param>
        /// <returns>A client certificate with private key.</returns>
        public static X509Certificate2 CreateClientCertificate(string subjectName, X509Certificate2? signerCert = null)
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest(
                $"CN={subjectName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Basic constraints (not a CA)
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, true));

            // Key usage for client certificate
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));

            // Enhanced key usage for client authentication
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.2") // Client Authentication
                    },
                    false));

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = notBefore.AddYears(1);

            X509Certificate2 clientCert;

            if (signerCert != null)
            {
                // Sign with CA
                var serial = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(serial);
                }

                using var certWithoutKey = request.Create(
                    signerCert.SubjectName,
                    X509SignatureGenerator.CreateForRSA(signerCert.GetRSAPrivateKey()!, RSASignaturePadding.Pkcs1),
                    notBefore,
                    notAfter,
                    serial);

                var withKey = certWithoutKey.CopyWithPrivateKey(rsa);
                var exported = withKey.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
                clientCert = X509CertificateLoader.LoadPkcs12(exported, "", X509KeyStorageFlags.Exportable);
#else
                clientCert = new X509Certificate2(exported, "", X509KeyStorageFlags.Exportable);
#endif
            }
            else
            {
                // Self-signed
                var cert = request.CreateSelfSigned(notBefore, notAfter);
                var exported = cert.Export(X509ContentType.Pfx, "");
#if NET9_0_OR_GREATER
                clientCert = X509CertificateLoader.LoadPkcs12(exported, "", X509KeyStorageFlags.Exportable);
#else
                clientCert = new X509Certificate2(exported, "", X509KeyStorageFlags.Exportable);
#endif
            }

            return clientCert;
        }
#endif
    }
}
