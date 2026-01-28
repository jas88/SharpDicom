using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// C-ECHO roundtrip tests using DicomClient and DicomServer.
    /// </summary>
    /// <remarks>
    /// These tests validate the complete networking stack by running client and server
    /// in the same process. They exercise: TCP connection, association negotiation,
    /// PDU exchange, and DIMSE command handling.
    /// </remarks>
    [TestFixture]
    public class CEchoTests
    {
        private const string ServerAE = "TESTSERVER";
        private const string ClientAE = "TESTCLIENT";

        #region C-ECHO Roundtrip Tests

        [Test]
        public async Task CEcho_ClientToServer_ReturnsSuccess()
        {
            // Arrange
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnCEcho = ctx => ValueTask.FromResult(DicomStatus.Success)
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True);
            Assert.That(status.Code, Is.EqualTo(0x0000));
        }

        [Test]
        public async Task CEcho_ServerReturnsCustomStatus_ClientReceivesIt()
        {
            // Arrange - server returns warning status
            var port = GetFreePort();
            var customStatus = new DicomStatus(0xB000); // Warning

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnCEcho = ctx => ValueTask.FromResult(customStatus)
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();

            // Assert
            Assert.That(status.Code, Is.EqualTo(0xB000));
            Assert.That(status.IsWarning, Is.True);
        }

        [Test]
        public async Task CEcho_MultipleTimes_AllSucceed()
        {
            // Arrange
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            await client.ConnectAsync(contexts);

            // Act - send multiple C-ECHOs
            for (int i = 0; i < 5; i++)
            {
                var status = await client.CEchoAsync();
                Assert.That(status.IsSuccess, Is.True, $"C-ECHO {i + 1} should succeed");
            }
        }

        [Test]
        public async Task CEcho_WithExplicitVRLittleEndian_Succeeds()
        {
            // Arrange
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ExplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True);
        }

        #endregion

        #region Association Lifecycle Tests

        [Test]
        public async Task Release_GracefulDisconnect_Works()
        {
            // Arrange
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            Assert.That(client.IsConnected, Is.True);

            await client.ReleaseAsync();

            // Assert
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task AssociationReject_ClientThrowsException()
        {
            // Arrange - server rejects all associations
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnAssociationRequest = ctx => ValueTask.FromResult(
                    AssociationRequestResult.Rejected(
                        RejectResult.PermanentRejection,
                        RejectSource.ServiceUser,
                        RejectReason.CallingAETitleNotRecognized))
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = "UNKNOWN"
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act & Assert
            var ex = Assert.ThrowsAsync<DicomAssociationException>(
                async () => await client.ConnectAsync(contexts));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Reason, Is.EqualTo(RejectReason.CallingAETitleNotRecognized));
        }

        [Test]
        public async Task CEchoAsync_BeforeConnect_ThrowsException()
        {
            // Arrange
            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = 11112,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            // Act & Assert
            var ex = Assert.ThrowsAsync<DicomAssociationException>(
                async () => await client.CEchoAsync());

            Assert.That(ex, Is.Not.Null);
        }

        [Test]
        public async Task CEchoAsync_VerificationNotNegotiated_ThrowsException()
        {
            // Arrange - server accepts but only with a non-Verification SOP Class
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnAssociationRequest = ctx =>
                {
                    // Accept but with no contexts
                    return ValueTask.FromResult(AssociationRequestResult.Accepted(
                        Array.Empty<PresentationContext>()));
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Connect succeeds (association accepted) but no contexts were negotiated
            await client.ConnectAsync(contexts);

            // Act & Assert - C-ECHO should fail because Verification SOP Class not negotiated
            var ex = Assert.ThrowsAsync<DicomAssociationException>(
                async () => await client.CEchoAsync());

            Assert.That(ex, Is.Not.Null);
        }

        #endregion

        #region Server Behavior Tests

        [Test]
        public async Task Server_DefaultOnCEcho_ReturnsSuccess()
        {
            // Arrange - server with no OnCEcho handler (uses default)
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
                // OnCEcho is null - should default to Success
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();

            // Assert - default should return Success
            Assert.That(status.IsSuccess, Is.True);
        }

        [Test]
        public async Task Server_OnCEchoReceivesCorrectMessageId()
        {
            // Arrange
            var port = GetFreePort();
            ushort receivedMessageId = 0;

            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE,
                OnCEcho = ctx =>
                {
                    receivedMessageId = ctx.MessageId;
                    return ValueTask.FromResult(DicomStatus.Success);
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            await client.CEchoAsync();

            // Assert - message ID should be 1 (first message after connect)
            Assert.That(receivedMessageId, Is.EqualTo(1));
        }

        [Test]
        public async Task Server_ActiveAssociations_IncrementsDecrementsCorrectly()
        {
            // Arrange
            var port = GetFreePort();
            var serverOptions = new DicomServerOptions
            {
                Port = port,
                AETitle = ServerAE
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            Assert.That(server.ActiveAssociations, Is.EqualTo(0));

            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = port,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act & Assert
            await client.ConnectAsync(contexts);

            // Give server time to register the association
            await Task.Delay(100);
            Assert.That(server.ActiveAssociations, Is.EqualTo(1));

            await client.ReleaseAsync();

            // Give server time to clean up
            await Task.Delay(100);
            Assert.That(server.ActiveAssociations, Is.EqualTo(0));
        }

        #endregion

        #region Helper Methods

        private static int GetFreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
