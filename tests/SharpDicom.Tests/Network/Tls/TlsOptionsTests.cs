using System;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using SharpDicom.Network.Tls;

namespace SharpDicom.Tests.Network.Tls
{
    [TestFixture]
    public class TlsOptionsTests
    {
        [Test]
        public void Validate_DefaultOptions_Succeeds()
        {
            var options = new TlsOptions();

            Assert.DoesNotThrow(() => options.Validate());
        }

        [Test]
        public void EnabledProtocols_DefaultsToNull()
        {
            var options = new TlsOptions();

            Assert.That(options.EnabledProtocols, Is.Null);
        }

        [Test]
        public void HandshakeTimeout_DefaultsTo30Seconds()
        {
            var options = new TlsOptions();

            Assert.That(options.HandshakeTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void AllowProtocolDowngrade_DefaultsFalse()
        {
            var options = new TlsOptions();

            Assert.That(options.AllowProtocolDowngrade, Is.False);
        }

        [Test]
        public void EnforceDicomTlsProfile_DefaultsTrue()
        {
            var options = new TlsOptions();

            Assert.That(options.EnforceDicomTlsProfile, Is.True);
        }

        [Test]
        public void Validate_HandshakeTimeoutZero_Throws()
        {
            var options = new TlsOptions { HandshakeTimeout = TimeSpan.Zero };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("HandshakeTimeout"));
        }

        [Test]
        public void Validate_HandshakeTimeoutNegative_Throws()
        {
            var options = new TlsOptions { HandshakeTimeout = TimeSpan.FromSeconds(-1) };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("HandshakeTimeout"));
        }

        [Test]
        public void Validate_InvalidThumbprint_Throws()
        {
            var options = new TlsOptions
            {
                AcceptedThumbprints = new System.Collections.Generic.List<string> { "invalid" }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex!.Message, Does.Contain("Invalid thumbprint"));
        }

        [Test]
        public void Validate_ValidThumbprint_Succeeds()
        {
            var options = new TlsOptions
            {
                AcceptedThumbprints = new System.Collections.Generic.List<string>
                {
                    "A".PadRight(64, '0') // 64-character hex string
                }
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

#pragma warning disable CS0618, CA5397, SYSLIB0039 // Obsolete protocols - used for validation testing only
        [Test]
        public void Validate_NonCompliantProtocol_WithEnforcement_Throws()
        {
            var options = new TlsOptions
            {
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
            var options = new TlsOptions
            {
                EnabledProtocols = SslProtocols.Tls11,
                EnforceDicomTlsProfile = false
            };

            Assert.DoesNotThrow(() => options.Validate());
        }
#pragma warning restore CS0618, CA5397, SYSLIB0039

        [Test]
        public void Validate_Tls12Protocol_Succeeds()
        {
            var options = new TlsOptions
            {
                EnabledProtocols = SslProtocols.Tls12
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

#if NET6_0_OR_GREATER
        [Test]
        public void Validate_Tls13Protocol_Succeeds()
        {
            var options = new TlsOptions
            {
                EnabledProtocols = SslProtocols.Tls13
            };

            Assert.DoesNotThrow(() => options.Validate());
        }
#endif

        [Test]
        public void ClientCertificate_SetGet_WorksCorrectly()
        {
            var options = new TlsOptions();
            using var cert = CreateSelfSignedCertificate();

            options.ClientCertificate = cert;

            Assert.That(options.ClientCertificate, Is.EqualTo(cert));
            Assert.That(options.ClientCertificates, Is.Not.Null);
            Assert.That(options.ClientCertificates!.Count, Is.EqualTo(1));
        }

        [Test]
        public void ClientCertificate_SetNull_ClearsCollection()
        {
            var options = new TlsOptions();
            using var cert = CreateSelfSignedCertificate();

            options.ClientCertificate = cert;
            options.ClientCertificate = null;

            Assert.That(options.ClientCertificate, Is.Null);
            Assert.That(options.ClientCertificates, Is.Null);
        }

        [Test]
        public void Validate_ClientCertWithoutPrivateKey_Throws()
        {
            var options = new TlsOptions();
            using var certWithKey = CreateSelfSignedCertificate();
            // Create a cert without private key
#pragma warning disable SYSLIB0057 // X509Certificate2(byte[]) is obsolete - test needs to create cert without key
            using var certWithoutKey = new X509Certificate2(certWithKey.Export(X509ContentType.Cert));
#pragma warning restore SYSLIB0057

            options.ClientCertificate = certWithoutKey;

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex!.Message, Does.Contain("does not have a private key"));
        }

        private static X509Certificate2 CreateSelfSignedCertificate()
        {
#if NET6_0_OR_GREATER
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Test",
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));
#else
            // For netstandard2.0, create a simpler test cert
            // This is just for testing - in real code you'd load from file
            var distinguishedName = new X500DistinguishedName("CN=Test");
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new CertificateRequest(
                distinguishedName,
                rsa,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));

            // Export and reimport to ensure we have private key access
            var pfx = cert.Export(X509ContentType.Pfx, "test");
            return new X509Certificate2(pfx, "test");
#endif
        }
    }
}
