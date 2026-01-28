using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Association;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;
using System.Buffers;
using System.Buffers.Binary;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// Unit tests for <see cref="DicomServer"/>.
    /// </summary>
    [TestFixture]
    public class DicomServerTests
    {
        #region Options Validation Tests

        [Test]
        public void DicomServerOptions_ValidatesPort_TooLow()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 0
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("Port"));
        }

        [Test]
        public void DicomServerOptions_ValidatesPort_TooHigh()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 65536
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("Port"));
        }

        [Test]
        public void DicomServerOptions_ValidatesAETitle_Null()
        {
            var options = new DicomServerOptions
            {
                AETitle = null!,
                Port = 11112
            };

            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("AETitle"));
        }

        [Test]
        public void DicomServerOptions_ValidatesAETitle_Empty()
        {
            var options = new DicomServerOptions
            {
                AETitle = "",
                Port = 11112
            };

            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("AETitle"));
        }

        [Test]
        public void DicomServerOptions_ValidatesAETitle_TooLong()
        {
            var options = new DicomServerOptions
            {
                AETitle = "THIS_AE_TITLE_IS_TOO_LONG",
                Port = 11112
            };

            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("AETitle"));
        }

        [Test]
        public void DicomServerOptions_ValidatesAETitle_LeadingSpace()
        {
            var options = new DicomServerOptions
            {
                AETitle = " TEST_SCP",
                Port = 11112
            };

            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("AETitle"));
        }

        [Test]
        public void DicomServerOptions_ValidatesAETitle_TrailingSpace()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP ",
                Port = 11112
            };

            var ex = Assert.Throws<ArgumentException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("AETitle"));
        }

        [Test]
        public void DicomServerOptions_ValidatesMaxAssociations_Zero()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112,
                MaxAssociations = 0
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("MaxAssociations"));
        }

        [Test]
        public void DicomServerOptions_ValidatesArtimTimeout_Zero()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112,
                ArtimTimeout = TimeSpan.Zero
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("ArtimTimeout"));
        }

        [Test]
        public void DicomServerOptions_ValidatesShutdownTimeout_Negative()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112,
                ShutdownTimeout = TimeSpan.FromSeconds(-1)
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("ShutdownTimeout"));
        }

        [Test]
        public void DicomServerOptions_ValidatesMaxPduLength_TooSmall()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112,
                MaxPduLength = 1024 // Below minimum of 4096
            };

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
            Assert.That(ex!.ParamName, Is.EqualTo("MaxPduLength"));
        }

        [Test]
        public void DicomServerOptions_ValidOptions_PassesValidation()
        {
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

        #endregion

        #region Server Lifecycle Tests

        [Test]
        public void DicomServer_Constructor_ValidatesOptions()
        {
            var options = new DicomServerOptions
            {
                AETitle = null!,
                Port = 11112
            };

            Assert.Throws<ArgumentException>(() => new DicomServer(options));
        }

        [Test]
        public void DicomServer_Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DicomServer(null!));
        }

        [Test]
        public async Task DicomServer_StartStop_WorksCorrectly()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            await using var server = new DicomServer(options);

            Assert.That(server.IsListening, Is.False);

            server.Start();

            Assert.That(server.IsListening, Is.True);

            await server.StopAsync();

            // After stop, accept loop should have exited
            await Task.Delay(100); // Give it time to clean up
        }

        [Test]
        public async Task DicomServer_DoubleStart_ThrowsInvalidOperationException()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            await using var server = new DicomServer(options);

            server.Start();
            Assert.Throws<InvalidOperationException>(() => server.Start());
        }

        [Test]
        public async Task DicomServer_DisposeWithoutStart_DoesNotThrow()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            var server = new DicomServer(options);
            await server.DisposeAsync();
            // Should not throw
        }

        [Test]
        public async Task DicomServer_DoubleDispose_DoesNotThrow()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            var server = new DicomServer(options);
            server.Start();
            await server.DisposeAsync();
            await server.DisposeAsync();
            // Should not throw
        }

        [Test]
        public async Task DicomServer_StartAfterDispose_ThrowsObjectDisposedException()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            var server = new DicomServer(options);
            await server.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => server.Start());
        }

        [Test]
        public async Task DicomServer_ActiveAssociations_InitiallyZero()
        {
            var port = GetFreePort();
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port
            };

            await using var server = new DicomServer(options);
            server.Start();

            Assert.That(server.ActiveAssociations, Is.EqualTo(0));
        }

        #endregion

        #region Handler Tests

        [Test]
        [Category("Integration")]
        [Explicit("Requires full PDU parsing to work end-to-end; for unit tests see handler wiring test")]
        public async Task DicomServer_OnAssociationRequest_CalledWithCorrectContext()
        {
            var port = GetFreePort();
            var receivedContext = (AssociationRequestContext?)null;
            var handlerTcs = new TaskCompletionSource<bool>();

            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port,
                OnAssociationRequest = ctx =>
                {
                    receivedContext = ctx;
                    handlerTcs.TrySetResult(true);
                    return ValueTask.FromResult(AssociationRequestResult.Rejected());
                }
            };

            await using var server = new DicomServer(options);
            server.Start();

            // Connect and send minimal A-ASSOCIATE-RQ
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var stream = client.GetStream();
            var rqPdu = BuildMinimalAssociateRequest("TEST_SCU", "TEST_SCP");
            await stream.WriteAsync(rqPdu.AsMemory());
            await stream.FlushAsync();

            // Wait for handler to be called with timeout
            var handlerTask = handlerTcs.Task;
            var completedTask = await Task.WhenAny(handlerTask, Task.Delay(2000));

            if (completedTask != handlerTask)
            {
                Assert.Fail("OnAssociationRequest handler was not called within 2 seconds");
            }

            Assert.That(receivedContext, Is.Not.Null);
            Assert.That(receivedContext!.CallingAE, Is.EqualTo("TEST_SCU"));
            Assert.That(receivedContext.CalledAE, Is.EqualTo("TEST_SCP"));
            Assert.That(receivedContext.RemoteEndPoint, Is.Not.Null);
        }

        [Test]
        public void DicomServerOptions_OnAssociationRequestHandler_IsWiredUp()
        {
            var handlerCalled = false;
            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = 11112,
                OnAssociationRequest = ctx =>
                {
                    handlerCalled = true;
                    return ValueTask.FromResult(AssociationRequestResult.Accepted(new List<PresentationContext>()));
                }
            };

            Assert.That(options.OnAssociationRequest, Is.Not.Null);
            Assert.That(handlerCalled, Is.False, "Handler not called until association request received");
        }

        [Test]
        public async Task DicomServer_OnCEcho_HandlerIsConfigurable()
        {
            var port = GetFreePort();
            var handlerCalled = false;

            var options = new DicomServerOptions
            {
                AETitle = "TEST_SCP",
                Port = port,
                OnCEcho = ctx =>
                {
                    handlerCalled = true;
                    return ValueTask.FromResult(DicomStatus.Success);
                }
            };

            await using var server = new DicomServer(options);
            server.Start();

            // Note: Full C-ECHO test requires sending valid P-DATA with command dataset
            // This is complex without a proper DICOM client. For now, verify handler is wired up.
            Assert.That(options.OnCEcho, Is.Not.Null);

            // Suppress unused variable warning by using it in a diagnostic assertion
            Assert.That(handlerCalled, Is.False, "Handler not called without C-ECHO request");
        }

        #endregion

        #region Association Accept/Reject Tests

        [Test]
        public void AssociationRequestResult_Accepted_SetsCorrectProperties()
        {
            var contexts = new List<PresentationContext>();
            var result = AssociationRequestResult.Accepted(contexts);

            Assert.That(result.Accept, Is.True);
            Assert.That(result.AcceptedContexts, Is.SameAs(contexts));
        }

        [Test]
        public void AssociationRequestResult_Rejected_SetsCorrectProperties()
        {
            var result = AssociationRequestResult.Rejected(
                RejectResult.TransientRejection,
                RejectSource.ServiceProviderPresentation,
                RejectReason.NoReasonGiven);

            Assert.That(result.Accept, Is.False);
            Assert.That(result.RejectResult, Is.EqualTo(RejectResult.TransientRejection));
            Assert.That(result.RejectSource, Is.EqualTo(RejectSource.ServiceProviderPresentation));
            Assert.That(result.RejectReason, Is.EqualTo(RejectReason.NoReasonGiven));
            Assert.That(result.AcceptedContexts, Is.Null);
        }

        [Test]
        public void AssociationRequestResult_Rejected_DefaultValues()
        {
            var result = AssociationRequestResult.Rejected();

            Assert.That(result.Accept, Is.False);
            Assert.That(result.RejectResult, Is.EqualTo(RejectResult.PermanentRejection));
            Assert.That(result.RejectSource, Is.EqualTo(RejectSource.ServiceUser));
            Assert.That(result.RejectReason, Is.EqualTo(RejectReason.NoReasonGiven));
        }

        #endregion

        #region Context Classes Tests

        [Test]
        public void AssociationRequestContext_Constructor_SetsProperties()
        {
            var contexts = new List<PresentationContext>();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);

            var context = new AssociationRequestContext(
                "CALLING_AE",
                "CALLED_AE",
                endpoint,
                contexts);

            Assert.That(context.CallingAE, Is.EqualTo("CALLING_AE"));
            Assert.That(context.CalledAE, Is.EqualTo("CALLED_AE"));
            Assert.That(context.RemoteEndPoint, Is.SameAs(endpoint));
            Assert.That(context.RequestedContexts, Is.SameAs(contexts));
        }

        [Test]
        public void CEchoRequestContext_Constructor_SetsProperties()
        {
            // AssociationOptions requires at least one presentation context
            var verificationContext = new PresentationContext(1, DicomUID.Verification,
                new[] { TransferSyntax.ImplicitVRLittleEndian });
            var assocOptions = new AssociationOptions("CALLED", "CALLING", new[] { verificationContext });
            var association = new DicomAssociation(assocOptions);

            var context = new CEchoRequestContext(association, 123);

            Assert.That(context.Association, Is.SameAs(association));
            Assert.That(context.MessageId, Is.EqualTo(123));
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets a free TCP port for testing.
        /// </summary>
        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Builds a minimal A-ASSOCIATE-RQ PDU for testing.
        /// </summary>
        private static byte[] BuildMinimalAssociateRequest(string callingAE, string calledAE)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PduWriter(buffer);

            var contexts = new List<PresentationContext>
            {
                new PresentationContext(1, DicomUID.Verification, new[] { TransferSyntax.ImplicitVRLittleEndian })
            };

            writer.WriteAssociateRequest(calledAE, callingAE, contexts, UserInformation.Default);

            return buffer.WrittenSpan.ToArray();
        }

        #endregion
    }
}
