using System;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;
using SharpDicom.Network.Tls;

namespace SharpDicom.Tests.Network.Tls
{
    /// <summary>
    /// Integration tests for TLS networking between DicomClient and DicomServer.
    /// </summary>
    /// <remarks>
    /// These tests verify end-to-end TLS functionality including encryption,
    /// certificate validation, mutual TLS, and error scenarios.
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    public class TlsIntegrationTests
    {
        private const string ServerAE = "TLS_SERVER";
        private const string ClientAE = "TLS_CLIENT";

        #region Happy Path Tests

#if NET6_0_OR_GREATER
        [Test]
        public async Task CEcho_OverTls_Succeeds()
        {
            // Arrange
            var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));
            var thumbprint = serverCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false, // Disable for self-signed cert testing
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100); // Give server time to start

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    AcceptedThumbprints = new System.Collections.Generic.List<string> { thumbprint },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True, "C-ECHO over TLS should succeed");
        }

        [Test]
        public async Task CEcho_OverTls_TlsConnectionInfoPopulated()
        {
            // Arrange
            var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));
            var thumbprint = serverCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    AcceptedThumbprints = new System.Collections.Generic.List<string> { thumbprint },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var association = client.Association;
            await client.ReleaseAsync();

            // Assert
            Assert.That(association, Is.Not.Null, "Association should exist");
            Assert.That(association!.TlsInfo, Is.Not.Null, "TlsInfo should be populated");
#if NET6_0_OR_GREATER
            Assert.That(association.TlsInfo!.Value.Protocol, Is.Not.EqualTo(SslProtocols.None),
                "Protocol should be set");
            Assert.That(association.TlsInfo.Value.CipherSuiteName, Is.Not.Null.And.Not.Empty,
                "Cipher suite should be set");
            Assert.That(association.TlsInfo.Value.RemoteCertificate, Is.Not.Null,
                "Remote certificate should be available");
#else
            Assert.That(association.TlsInfo!.Protocol, Is.Not.EqualTo(SslProtocols.None),
                "Protocol should be set");
            Assert.That(association.TlsInfo.CipherSuiteName, Is.Not.Null.And.Not.Empty,
                "Cipher suite should be set");
            Assert.That(association.TlsInfo.RemoteCertificate, Is.Not.Null,
                "Remote certificate should be available");
#endif
        }

        [Test]
        public async Task CStore_OverTls_RoundtripPreservesData()
        {
            // Arrange
            var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));
            var thumbprint = serverCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

            DicomDataset? receivedDataset = null;
            var receivedEvent = new System.Threading.Tasks.TaskCompletionSource<bool>();

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                },
                OnCStoreRequest = (ctx, ds, ct) =>
                {
                    receivedDataset = ds;
                    receivedEvent.TrySetResult(true);
                    return new ValueTask<DicomStatus>(DicomStatus.Success);
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    AcceptedThumbprints = new System.Collections.Generic.List<string> { thumbprint },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            // Create dataset to send
            var dataset = new DicomDataset();
            dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, DicomUID.CTImageStorage.ToString()));
            dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, $"1.2.3.4.{DateTime.UtcNow.Ticks}"));
            dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
            dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "12345"));

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.CTImageStorage, TransferSyntax.ExplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var storeScu = new CStoreScu(client);
            var response = await storeScu.SendAsync(dataset, null);
            await client.ReleaseAsync();

            // Wait for server to receive
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(receivedEvent.Task, timeoutTask);

            // Assert
            Assert.That(response.IsSuccess, Is.True, "C-STORE over TLS should succeed");
            Assert.That(completedTask, Is.EqualTo(receivedEvent.Task), "Should receive dataset within timeout");
            Assert.That(receivedDataset, Is.Not.Null, "Dataset should be received");
            Assert.That(receivedDataset!.Contains(DicomTag.PatientName), Is.True,
                "Dataset should contain PatientName");
            Assert.That(receivedDataset.GetString(DicomTag.PatientName), Is.EqualTo("Test^Patient"),
                "PatientName should match");
        }

        [Test]
        public async Task MutualTls_BothCertsValid_Succeeds()
        {
            // Arrange - Create CA and sign both server and client certs
            using var ca = TlsCertificateHelper.CreateSelfSignedCertificate("TestCA", TimeSpan.FromHours(1));
            using var serverCert = TlsCertificateHelper.CreateClientCertificate("localhost", ca);
            using var clientCert = TlsCertificateHelper.CreateClientCertificate("TestClient", ca);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    RequireClientCertificate = true,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck,
                    ClientCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        // Accept client certs signed by our test CA
                        if (chain != null)
                        {
#if NET9_0_OR_GREATER
                            using var caCopy = X509CertificateLoader.LoadCertificate(ca.RawData);
#else
                            using var caCopy = new X509Certificate2(ca.RawData);
#endif
                            chain.ChainPolicy.ExtraStore.Add(caCopy);
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Add(caCopy);
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            return chain.Build((X509Certificate2)cert!);
                        }
                        return false;
                    }
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

#if NET9_0_OR_GREATER
            var caCopy2 = X509CertificateLoader.LoadCertificate(ca.RawData);
#else
            var caCopy2 = new X509Certificate2(ca.RawData);
#endif

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    ClientCertificate = clientCert,
                    CustomCAs = new System.Collections.Generic.List<X509Certificate2> { caCopy2 },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck,
                    ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        // Accept server certs signed by our test CA
                        if (chain != null)
                        {
#if NET9_0_OR_GREATER
                            using var caCopy3 = X509CertificateLoader.LoadCertificate(ca.RawData);
#else
                            using var caCopy3 = new X509Certificate2(ca.RawData);
#endif
                            chain.ChainPolicy.ExtraStore.Add(caCopy3);
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Add(caCopy3);
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            return chain.Build((X509Certificate2)cert!);
                        }
                        return false;
                    }
                }
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            var association = client.Association;
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True, "C-ECHO with mutual TLS should succeed");
#if NET6_0_OR_GREATER
            Assert.That(association!.TlsInfo!.Value.IsMutuallyAuthenticated, Is.True,
                "Connection should be mutually authenticated");
#else
            Assert.That(association!.TlsInfo!.IsMutuallyAuthenticated, Is.True,
                "Connection should be mutually authenticated");
#endif
        }

        [Test]
        public async Task PlainTcp_StillWorks_WhenTlsNotConfigured()
        {
            // Arrange - No TLS configured on either side
            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
                // No Tls property set
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
                // No Tls property set
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            var association = client.Association;
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True, "Plain TCP C-ECHO should still work");
            Assert.That(association!.TlsInfo, Is.Null, "TlsInfo should be null for plain TCP");
        }
#endif

        #endregion

        #region Certificate Validation Tests

#if NET6_0_OR_GREATER
        [Test]
        public async Task SelfSigned_AcceptedViaThumbprint()
        {
            // Arrange
            var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));
            var thumbprint = serverCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    AcceptedThumbprints = new System.Collections.Generic.List<string> { thumbprint },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True,
                "Self-signed cert should be accepted when thumbprint matches");
        }

        [Test]
        public void SelfSigned_RejectedWithoutThumbprint()
        {
            // Arrange
            var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            var server = new DicomServer(serverOptions);
            server.Start();
            Task.Delay(100).Wait();

            try
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = "localhost",
                    Port = port,
                    CalledAE = ServerAE,
                    CallingAE = ClientAE,
                    Tls = new TlsOptions
                    {
                        // No AcceptedThumbprints - should use system validation
                        EnforceDicomTlsProfile = false,
                        RevocationMode = X509RevocationMode.NoCheck
                    }
                };

                var client = new DicomClient(clientOptions);

                var contexts = new[]
                {
                    new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
                };

                // Act & Assert
                Assert.ThrowsAsync<TlsHandshakeException>(async () =>
                {
                    await using (client)
                    {
                        await client.ConnectAsync(contexts);
                    }
                }, "Self-signed cert without thumbprint should fail");
            }
            finally
            {
                server.StopAsync().Wait();
                server.DisposeAsync().AsTask().Wait();
            }
        }

        [Test]
        public async Task CustomCA_AcceptedWithCAInTrustStore()
        {
            // Arrange
            using var ca = TlsCertificateHelper.CreateSelfSignedCertificate("TestCA", TimeSpan.FromHours(1));
            using var serverCert = TlsCertificateHelper.CreateClientCertificate("localhost", ca);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();
            await Task.Delay(100);

#if NET9_0_OR_GREATER
            var caCopy = X509CertificateLoader.LoadCertificate(ca.RawData);
#else
            var caCopy = new X509Certificate2(ca.RawData);
#endif

            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE,
                Tls = new TlsOptions
                {
                    CustomCAs = new System.Collections.Generic.List<X509Certificate2> { caCopy },
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck,
                    ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        // Custom validation with CA trust
                        if (chain != null)
                        {
#if NET9_0_OR_GREATER
                            using var caCopy2 = X509CertificateLoader.LoadCertificate(ca.RawData);
#else
                            using var caCopy2 = new X509Certificate2(ca.RawData);
#endif
                            chain.ChainPolicy.ExtraStore.Add(caCopy2);
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Add(caCopy2);
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            return chain.Build((X509Certificate2)cert!);
                        }
                        return false;
                    }
                }
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True,
                "CA-signed cert should be accepted when CA is in custom trust store");
        }
#endif

        #endregion

        #region Error Handling Tests

#if NET6_0_OR_GREATER
        [Test]
        public void MutualTls_MissingClientCert_Fails()
        {
            // Arrange
            using var serverCert = TlsCertificateHelper.CreateSelfSignedCertificate("localhost", TimeSpan.FromHours(1));
            var thumbprint = serverCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = serverCert,
                    RequireClientCertificate = true, // Server requires client cert
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            var server = new DicomServer(serverOptions);
            server.Start();
            Task.Delay(100).Wait();

            try
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = "localhost",
                    Port = port,
                    CalledAE = ServerAE,
                    CallingAE = ClientAE,
                    Tls = new TlsOptions
                    {
                        AcceptedThumbprints = new System.Collections.Generic.List<string> { thumbprint },
                        // No ClientCertificate set - mutual TLS should fail
                        EnforceDicomTlsProfile = false,
                        RevocationMode = X509RevocationMode.NoCheck
                    }
                };

                var client = new DicomClient(clientOptions);

                var contexts = new[]
                {
                    new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
                };

                // Act & Assert - Server will close connection when client doesn't provide cert
                // This can manifest as TlsHandshakeException or EndOfStreamException
                Assert.CatchAsync<Exception>(async () =>
                {
                    await using (client)
                    {
                        await client.ConnectAsync(contexts);
                    }
                }, "Missing client cert should cause connection failure");
            }
            finally
            {
                server.StopAsync().Wait();
                server.DisposeAsync().AsTask().Wait();
            }
        }

        [Test]
        public void InvalidCert_Rejected()
        {
            // Arrange - Use expired certificate
            var expiredCert = TlsCertificateHelper.CreateExpiredCertificate("localhost");

            var port = GetFreePort();

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                Tls = new TlsServerOptions
                {
                    ServerCertificate = expiredCert,
                    EnforceDicomTlsProfile = false,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            };

            var server = new DicomServer(serverOptions);
            server.Start();
            Task.Delay(100).Wait();

            try
            {
                var clientOptions = new DicomClientOptions
                {
                    Host = "localhost",
                    Port = port,
                    CalledAE = ServerAE,
                    CallingAE = ClientAE,
                    Tls = new TlsOptions
                    {
                        // No AcceptedThumbprints - system validation should reject expired cert
                        EnforceDicomTlsProfile = false,
                        RevocationMode = X509RevocationMode.NoCheck
                    }
                };

                var client = new DicomClient(clientOptions);

                var contexts = new[]
                {
                    new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
                };

                // Act & Assert
                var ex = Assert.ThrowsAsync<TlsHandshakeException>(async () =>
                {
                    await using (client)
                    {
                        await client.ConnectAsync(contexts);
                    }
                });

                Assert.That(ex, Is.Not.Null, "Expired certificate should cause handshake exception");
            }
            finally
            {
                server.StopAsync().Wait();
                server.DisposeAsync().AsTask().Wait();
            }
        }
#endif

        #endregion

        #region Helper Methods

        private static int GetFreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(value);
            return new DicomStringElement(tag, vr, bytes);
        }

        #endregion
    }
}
