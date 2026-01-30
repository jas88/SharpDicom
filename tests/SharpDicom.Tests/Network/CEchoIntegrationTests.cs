using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// Integration tests for DCMTK interoperability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These tests require DCMTK tools to be installed and available in PATH.
    /// They are marked as Explicit and in the "Integration" category to prevent
    /// them from running during normal test execution.
    /// </para>
    /// <para>
    /// To run these tests manually:
    /// <code>
    /// dotnet test --filter "Category=Integration&amp;Category=DCMTK"
    /// </code>
    /// </para>
    /// <para>
    /// DCMTK can be installed via:
    /// - macOS: brew install dcmtk
    /// - Ubuntu: apt-get install dcmtk
    /// - Windows: Download from https://dicom.offis.de/dcmtk.php.en
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    [Category("DCMTK")]
    public class CEchoIntegrationTests
    {
        // Default DCMTK ports - use different ports to avoid conflicts
        private const int DCMTKStoreScpPort = 11113;
        private const int SharpDicomServerPort = 11114;

        #region SharpDicom Client -> DCMTK Server Tests

        /// <summary>
        /// Tests SharpDicom DicomClient against DCMTK storescp.
        /// </summary>
        /// <remarks>
        /// <para>Prerequisites: Run in a terminal:</para>
        /// <code>storescp -v 11113</code>
        /// <para>
        /// The storescp command starts a DICOM SCP that accepts associations
        /// and responds to C-ECHO requests.
        /// </para>
        /// </remarks>
        [Test]
        [Explicit("Requires DCMTK storescp running: storescp -v 11113")]
        public async Task CEcho_ToDCMTKStoreScp_Succeeds()
        {
            // Arrange
            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = DCMTKStoreScpPort,
                CalledAE = "STORESCP",
                CallingAE = "SHARPDICOM"
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
            Assert.That(status.IsSuccess, Is.True, "C-ECHO to DCMTK storescp should succeed");
        }

        /// <summary>
        /// Tests SharpDicom DicomClient with Explicit VR Little Endian against DCMTK.
        /// </summary>
        [Test]
        [Explicit("Requires DCMTK storescp running: storescp -v 11113")]
        public async Task CEcho_ToDCMTKStoreScp_WithExplicitVR_Succeeds()
        {
            // Arrange
            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = DCMTKStoreScpPort,
                CalledAE = "STORESCP",
                CallingAE = "SHARPDICOM"
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ExplicitVRLittleEndian)
            };

            // Act
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            await client.ReleaseAsync();

            // Assert
            Assert.That(status.IsSuccess, Is.True, "C-ECHO with Explicit VR LE should succeed");
        }

        #endregion

        #region DCMTK Client -> SharpDicom Server Tests

        /// <summary>
        /// Tests that SharpDicom DicomServer can accept C-ECHO from DCMTK echoscu.
        /// </summary>
        /// <remarks>
        /// <para>This test starts a SharpDicom server and waits for DCMTK echoscu to connect.</para>
        /// <para>After starting the test, run in a terminal:</para>
        /// <code>echoscu -v 127.0.0.1 11114</code>
        /// </remarks>
        [Test]
        [Explicit("Requires DCMTK echoscu to connect: echoscu -v 127.0.0.1 11114")]
        public async Task DicomServer_AcceptsDCMTKEchoScu()
        {
            // Arrange
            var echoReceived = new TaskCompletionSource<bool>();

            var serverOptions = new DicomServerOptions
            {
                Port = SharpDicomServerPort,
                AETitle = "SHARPDICOM",
                OnCEcho = ctx =>
                {
                    echoReceived.TrySetResult(true);
                    return ValueTask.FromResult(DicomStatus.Success);
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            Console.WriteLine($"Server started on port {SharpDicomServerPort}.");
            Console.WriteLine($"Run: echoscu -v 127.0.0.1 {SharpDicomServerPort}");

            // Act - wait for DCMTK echoscu to connect (with 30 second timeout)
            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(echoReceived.Task, timeout);

            // Assert
            Assert.That(completedTask, Is.EqualTo(echoReceived.Task), "Expected C-ECHO from echoscu within 30 seconds");
            Assert.That(echoReceived.Task.Result, Is.True);
        }

        #endregion

        #region Bidirectional Tests

        /// <summary>
        /// Tests both directions of DCMTK interoperability.
        /// </summary>
        /// <remarks>
        /// <para>Prerequisites:</para>
        /// <list type="bullet">
        /// <item>Terminal 1: <c>storescp -v 11115</c></item>
        /// <item>After starting this test, in Terminal 2: <c>echoscu -v 127.0.0.1 11116</c></item>
        /// </list>
        /// </remarks>
        [Test]
        [Explicit("Requires both directions of DCMTK interop")]
        public async Task BidirectionalCEcho_WithDCMTK()
        {
            const int dcmtkServerPort = 11115;
            const int sharpdicomServerPort = 11116;

            // Start our server
            var echoReceived = new TaskCompletionSource<bool>();

            var serverOptions = new DicomServerOptions
            {
                Port = sharpdicomServerPort,
                AETitle = "SHARPDICOM_SCP",
                OnCEcho = ctx =>
                {
                    echoReceived.TrySetResult(true);
                    return ValueTask.FromResult(DicomStatus.Success);
                }
            };

            await using var server = new DicomServer(serverOptions);
            server.Start();

            // Our client to DCMTK
            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = dcmtkServerPort,
                CalledAE = "STORESCP",
                CallingAE = "SHARPDICOM_SCU"
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            // Act - Our SCU -> DCMTK SCP
            await client.ConnectAsync(contexts);
            var status = await client.CEchoAsync();
            await client.ReleaseAsync();

            Assert.That(status.IsSuccess, Is.True, "Our SCU -> DCMTK SCP should succeed");

            Console.WriteLine($"C-ECHO to DCMTK succeeded. Now run: echoscu -v 127.0.0.1 {sharpdicomServerPort}");

            // Wait for DCMTK to connect to our server
            var timeout = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(echoReceived.Task, timeout);

            Assert.That(completedTask, Is.EqualTo(echoReceived.Task), "Expected C-ECHO from DCMTK echoscu");
        }

        #endregion
    }

    /// <summary>
    /// Documentation for manual DCMTK testing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The following commands can be used for manual interoperability testing:
    /// </para>
    /// <code>
    /// # Start DCMTK server:
    /// storescp -v 11113
    ///
    /// # Test SharpDicom client against DCMTK:
    /// # (Run the CEcho_ToDCMTKStoreScp_Succeeds test)
    ///
    /// # Start SharpDicom server (via test):
    /// # (Run the DicomServer_AcceptsDCMTKEchoScu test)
    ///
    /// # Then test DCMTK client against SharpDicom:
    /// echoscu -v 127.0.0.1 11114
    /// </code>
    /// <para>
    /// For verbose DCMTK output, add <c>--debug</c> flag:
    /// </para>
    /// <code>
    /// storescp --debug -v 11113
    /// echoscu --debug -v 127.0.0.1 11114
    /// </code>
    /// </remarks>
    internal static class DCMTKTestingGuide
    {
        // This class exists only for documentation purposes.
        // It provides a central location for DCMTK testing instructions.
    }
}
