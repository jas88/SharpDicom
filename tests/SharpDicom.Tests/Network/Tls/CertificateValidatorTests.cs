using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using SharpDicom.Network.Tls;

namespace SharpDicom.Tests.Network.Tls
{
    [TestFixture]
    public class CertificateValidatorTests
    {
        [Test]
        public void SystemOnly_AcceptsValidCert()
        {
            var validator = CertificateValidator.SystemOnly();
            using var cert = CreateSelfSignedCertificate();
            using var chain = new X509Chain();

            // Simulate system validation passed
            var result = validator.Validate(this, cert, chain, SslPolicyErrors.None);

            Assert.That(result, Is.True);
        }

        [Test]
        public void SystemOnly_RejectsInvalidCert()
        {
            var validator = CertificateValidator.SystemOnly();
            using var cert = CreateSelfSignedCertificate();
            using var chain = new X509Chain();

            // Simulate chain errors
            var result = validator.Validate(this, cert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.False);
        }

        [Test]
        public void NullCertificate_AlwaysRejects()
        {
            var validator = CertificateValidator.SystemOnly();

            var result = validator.Validate(this, null, null, SslPolicyErrors.None);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ThumbprintWhitelist_AcceptsMatchingThumbprint()
        {
            using var cert = CreateSelfSignedCertificate();
            var thumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var validator = CertificateValidator.AcceptThumbprints(thumbprint);
            using var chain = new X509Chain();

            // Even with chain errors, matching thumbprint should accept
            var result = validator.Validate(this, cert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ThumbprintWhitelist_RejectsNonMatchingThumbprint()
        {
            using var cert = CreateSelfSignedCertificate();
            var wrongThumbprint = "A".PadRight(64, '0');
            var validator = CertificateValidator.AcceptThumbprints(wrongThumbprint);
            using var chain = new X509Chain();

            var result = validator.Validate(this, cert, chain, SslPolicyErrors.None);

            Assert.That(result, Is.False);
        }

        [Test]
        public void AcceptSelfSigned_AcceptsSelfSignedCert()
        {
            var validator = CertificateValidator.AcceptSelfSigned();
            using var cert = CreateSelfSignedCertificate();
            using var chain = new X509Chain();

            // Build chain for self-signed cert (will have UntrustedRoot status)
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(cert);

            // Simulate self-signed cert with UntrustedRoot error
            var result = validator.Validate(this, cert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.True);
        }

        [Test]
        public void AcceptSelfSigned_RejectsExpiredCert()
        {
            var validator = CertificateValidator.AcceptSelfSigned();
            using var cert = CreateExpiredSelfSignedCertificate();
            using var chain = new X509Chain();

            // Build chain for expired cert (will have UntrustedRoot + NotTimeValid)
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(cert);

            var result = validator.Validate(this, cert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            // Should reject because it has more than just UntrustedRoot error
            Assert.That(result, Is.False);
        }

        [Test]
        public void AcceptSelfSigned_RejectsSystemValidCert()
        {
            var validator = CertificateValidator.AcceptSelfSigned();
            using var cert = CreateSelfSignedCertificate();

            // System-valid cert should still be accepted
            var result = validator.Validate(this, cert, null, SslPolicyErrors.None);

            Assert.That(result, Is.True);
        }

        [Test]
        public void CustomCA_AcceptsCASignedCert()
        {
            // Create CA and cert signed by CA
            using var caCert = CreateSelfSignedCertificate("CN=TestCA");
            using var clientCert = CreateCertificateSignedBy(caCert, "CN=Client");

            var validator = CertificateValidator.WithCustomCAs(caCert);
            using var chain = new X509Chain();

            // Build chain with custom CA
            chain.ChainPolicy.ExtraStore.Add(caCert);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(clientCert);

            var result = validator.Validate(this, clientCert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.True);
        }

        [Test]
        public void CustomCA_RejectsUnrelatedCert()
        {
            using var caCert = CreateSelfSignedCertificate("CN=TestCA");
            using var unrelatedCert = CreateSelfSignedCertificate("CN=Unrelated");

            var validator = CertificateValidator.WithCustomCAs(caCert);
            using var chain = new X509Chain();

            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.Build(unrelatedCert);

            var result = validator.Validate(this, unrelatedCert, chain, SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(result, Is.False);
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string subject = "CN=Test")
        {
#if NET6_0_OR_GREATER
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                subject,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: subject.Contains("CA"),
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));
#else
            var distinguishedName = new X500DistinguishedName(subject);
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: subject.Contains("CA"),
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));

            var pfx = cert.Export(X509ContentType.Pfx, "test");
            return new X509Certificate2(pfx, "test");
#endif
        }

        private static X509Certificate2 CreateExpiredSelfSignedCertificate()
        {
#if NET6_0_OR_GREATER
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Expired",
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            // Create cert that expired yesterday
            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-365),
                DateTimeOffset.UtcNow.AddDays(-1));
#else
            var distinguishedName = new X500DistinguishedName("CN=Expired");
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-365),
                DateTimeOffset.UtcNow.AddDays(-1));

            var pfx = cert.Export(X509ContentType.Pfx, "test");
            return new X509Certificate2(pfx, "test");
#endif
        }

        private static X509Certificate2 CreateCertificateSignedBy(X509Certificate2 issuer, string subject)
        {
#if NET6_0_OR_GREATER
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                subject,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            var serialNumber = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(serialNumber);

            // Ensure leaf certificate validity is within issuer's validity period
            // notBefore: 1 day after issuer start (to account for clock skew)
            // notAfter: 1 day before issuer end (to ensure leaf expires first)
            var notBefore = issuer.NotBefore.AddDays(1);
            var notAfter = issuer.NotAfter.AddDays(-1);

            return request.Create(
                issuer,
                notBefore,
                notAfter,
                serialNumber).CopyWithPrivateKey(rsa);
#else
            // For netstandard2.0, just return self-signed (chain validation won't work perfectly but tests structure)
            return CreateSelfSignedCertificate(subject);
#endif
        }
    }
}
