using System;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using SharpDicom.Network.Tls;

namespace SharpDicom.Tests.Network.Tls
{
    [TestFixture]
    public class TlsServerOptionsTests
    {
        [Test]
        public void Validate_MissingServerCert_Throws()
        {
            var options = new TlsServerOptions();

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex!.Message, Does.Contain("ServerCertificate"));
        }

        [Test]
        public void Validate_ServerCertWithoutPrivateKey_Throws()
        {
            var options = new TlsServerOptions();
            using var certWithKey = CreateSelfSignedCertificate();
            // Create cert without private key
#pragma warning disable SYSLIB0057 // X509Certificate2(byte[]) is obsolete - test needs to create cert without key
            using var certWithoutKey = new X509Certificate2(certWithKey.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057

            options.ServerCertificate = certWithoutKey;

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex!.Message, Does.Contain("must include the private key"));
        }

        [Test]
        public void Validate_ServerCertWithPrivateKey_Succeeds()
        {
            var options = new TlsServerOptions();
            using var cert = CreateSelfSignedCertificate();

            options.ServerCertificate = cert;

            Assert.DoesNotThrow(() => options.Validate());
        }

        [Test]
        public void RequireClientCertificate_DefaultsFalse()
        {
            var options = new TlsServerOptions();

            Assert.That(options.RequireClientCertificate, Is.False);
        }

        [Test]
        public void EnforceDicomTlsProfile_DefaultsTrue()
        {
            var options = new TlsServerOptions();

            Assert.That(options.EnforceDicomTlsProfile, Is.True);
        }

        [Test]
        public void HandshakeTimeout_DefaultsTo30Seconds()
        {
            var options = new TlsServerOptions();

            Assert.That(options.HandshakeTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void Validate_HandshakeTimeoutZero_Throws()
        {
            var options = new TlsServerOptions
            {
                ServerCertificate = CreateSelfSignedCertificate(),
                HandshakeTimeout = TimeSpan.Zero
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("HandshakeTimeout"));
        }

#pragma warning disable CS0618, CA5397, SYSLIB0039 // Obsolete protocols - used for validation testing only
        [Test]
        public void Validate_NonCompliantProtocol_WithEnforcement_Throws()
        {
            var options = new TlsServerOptions
            {
                ServerCertificate = CreateSelfSignedCertificate(),
                EnabledProtocols = SslProtocols.Tls11,
                EnforceDicomTlsProfile = true
            };

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex!.Message, Does.Contain("not DICOM-compliant"));
        }
#pragma warning restore CS0618, CA5397, SYSLIB0039

#pragma warning disable CS0618, CA5397, SYSLIB0039 // Obsolete protocols - used for validation testing only
        [Test]
        public void Validate_NonCompliantProtocol_WithoutEnforcement_Succeeds()
        {
            var options = new TlsServerOptions
            {
                ServerCertificate = CreateSelfSignedCertificate(),
                EnabledProtocols = SslProtocols.Tls11,
                EnforceDicomTlsProfile = false
            };

            Assert.DoesNotThrow(() => options.Validate());
        }
#pragma warning restore CS0618, CA5397, SYSLIB0039

        [Test]
        public void Validate_Tls12Protocol_Succeeds()
        {
            var options = new TlsServerOptions
            {
                ServerCertificate = CreateSelfSignedCertificate(),
                EnabledProtocols = SslProtocols.Tls12
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

#if NET5_0_OR_GREATER
        [Test]
        public void Validate_ServerCertificateContext_Succeeds()
        {
            using var cert = CreateSelfSignedCertificate();
            var context = System.Net.Security.SslStreamCertificateContext.Create(cert, null);

            var options = new TlsServerOptions
            {
                ServerCertificateContext = context
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

        [Test]
        public void Validate_BothServerCertAndContext_Succeeds()
        {
            using var cert = CreateSelfSignedCertificate();
            var context = System.Net.Security.SslStreamCertificateContext.Create(cert, null);

            var options = new TlsServerOptions
            {
                ServerCertificate = cert,
                ServerCertificateContext = context
            };

            Assert.DoesNotThrow(() => options.Validate());
        }
#endif

        private static X509Certificate2 CreateSelfSignedCertificate()
        {
#if NET6_0_OR_GREATER
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=TestServer",
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));
#else
            var distinguishedName = new X500DistinguishedName("CN=TestServer");
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: true));

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));

            var pfx = cert.Export(X509ContentType.Pfx, "test");
            return new X509Certificate2(pfx, "test");
#endif
        }
    }
}
