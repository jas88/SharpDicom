using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network
{
    /// <summary>
    /// Unit tests for <see cref="DicomClient"/> and <see cref="DicomClientOptions"/>.
    /// </summary>
    /// <remarks>
    /// These are unit tests that don't require an external DICOM server.
    /// Integration tests against DCMTK are in a separate test class.
    /// </remarks>
    [TestFixture]
    public class DicomClientTests
    {
        #region DicomClientOptions Validation Tests

        [Test]
        public void Validate_EmptyHost_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_NullHost_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = null!,
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_WhitespaceHost_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "   ",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_InvalidPort_Zero_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 0,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_InvalidPort_Negative_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = -1,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_InvalidPort_TooHigh_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 65536,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_EmptyCalledAE_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "",
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_CalledAE_TooLong_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "12345678901234567", // 17 characters
                CallingAE = "CALLING"
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_EmptyCallingAE_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = ""
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_CallingAE_TooLong_ThrowsArgumentException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "12345678901234567" // 17 characters
            };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }

        [Test]
        public void Validate_ZeroConnectionTimeout_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING",
                ConnectionTimeout = TimeSpan.Zero
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_NegativeConnectionTimeout_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING",
                ConnectionTimeout = TimeSpan.FromSeconds(-1)
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_TooSmallMaxPduLength_ThrowsArgumentOutOfRangeException()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING",
                MaxPduLength = 1024 // Less than 4096
            };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void Validate_ValidOptions_DoesNotThrow()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

        [Test]
        public void Validate_ValidOptions_MaxAELength_DoesNotThrow()
        {
            var options = new DicomClientOptions
            {
                Host = "localhost",
                Port = 104,
                CalledAE = "1234567890123456", // Exactly 16 characters
                CallingAE = "1234567890123456"
            };

            Assert.DoesNotThrow(() => options.Validate());
        }

        #endregion

        #region DicomClient State Tests

        [Test]
        public void Constructor_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DicomClient(null!));
        }

        [Test]
        public void Constructor_InvalidOptions_ThrowsArgumentException()
        {
            var options = new DicomClientOptions(); // Empty host
            Assert.Throws<ArgumentException>(() => new DicomClient(options));
        }

        [Test]
        public async Task IsConnected_BeforeConnect_ReturnsFalse()
        {
            var options = CreateValidOptions();
            await using var client = new DicomClient(options);

            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task Association_BeforeConnect_ReturnsNull()
        {
            var options = CreateValidOptions();
            await using var client = new DicomClient(options);

            Assert.That(client.Association, Is.Null);
        }

        [Test]
        public async Task DisposeAsync_CanBeCalledMultipleTimes()
        {
            var options = CreateValidOptions();
            var client = new DicomClient(options);

            await client.DisposeAsync();
            await client.DisposeAsync(); // Should not throw
        }

        [Test]
        public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var options = CreateValidOptions();
            var client = new DicomClient(options);
            await client.DisposeAsync();

            var contexts = new[]
            {
                new PresentationContext(1, DicomUID.Verification, TransferSyntax.ImplicitVRLittleEndian)
            };

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await client.ConnectAsync(contexts));
        }

        [Test]
        public async Task CEchoAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var options = CreateValidOptions();
            var client = new DicomClient(options);
            await client.DisposeAsync();

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await client.CEchoAsync());
        }

        #endregion

        #region DicomCommand Tests

        [Test]
        public void CreateCEchoRequest_CreatesValidCommand()
        {
            var command = DicomCommand.CreateCEchoRequest(1);

            Assert.That(command.AffectedSOPClassUID, Is.EqualTo(DicomUID.Verification));
            Assert.That(command.CommandFieldValue, Is.EqualTo(CommandField.CEchoRequest));
            Assert.That(command.MessageID, Is.EqualTo(1));
            Assert.That(command.HasDataset, Is.False);
            Assert.That(command.IsCEchoRequest, Is.True);
            Assert.That(command.IsRequest, Is.True);
            Assert.That(command.IsResponse, Is.False);
        }

        [Test]
        public void CreateCEchoResponse_CreatesValidCommand()
        {
            var command = DicomCommand.CreateCEchoResponse(42, DicomStatus.Success);

            Assert.That(command.AffectedSOPClassUID, Is.EqualTo(DicomUID.Verification));
            Assert.That(command.CommandFieldValue, Is.EqualTo(CommandField.CEchoResponse));
            Assert.That(command.MessageIDBeingRespondedTo, Is.EqualTo(42));
            Assert.That(command.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
            Assert.That(command.HasDataset, Is.False);
            Assert.That(command.IsCEchoResponse, Is.True);
            Assert.That(command.IsResponse, Is.True);
            Assert.That(command.IsRequest, Is.False);
        }

        [Test]
        public void CreateCStoreRequest_CreatesValidCommand()
        {
            var sopClassUid = DicomUID.CTImageStorage;
            var sopInstanceUid = new DicomUID("1.2.3.4.5");
            var command = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

            Assert.That(command.AffectedSOPClassUID, Is.EqualTo(sopClassUid));
            Assert.That(command.CommandFieldValue, Is.EqualTo(CommandField.CStoreRequest));
            Assert.That(command.MessageID, Is.EqualTo(1));
            Assert.That(command.AffectedSOPInstanceUID, Is.EqualTo(sopInstanceUid));
            Assert.That(command.HasDataset, Is.True);
            Assert.That(command.IsCStoreRequest, Is.True);
        }

        [Test]
        public void CreateCStoreResponse_CreatesValidCommand()
        {
            var sopClassUid = DicomUID.CTImageStorage;
            var sopInstanceUid = new DicomUID("1.2.3.4.5");
            var command = DicomCommand.CreateCStoreResponse(42, sopClassUid, sopInstanceUid, DicomStatus.Success);

            Assert.That(command.AffectedSOPClassUID, Is.EqualTo(sopClassUid));
            Assert.That(command.CommandFieldValue, Is.EqualTo(CommandField.CStoreResponse));
            Assert.That(command.MessageIDBeingRespondedTo, Is.EqualTo(42));
            Assert.That(command.AffectedSOPInstanceUID, Is.EqualTo(sopInstanceUid));
            Assert.That(command.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
            Assert.That(command.HasDataset, Is.False);
            Assert.That(command.IsCStoreResponse, Is.True);
        }

        #endregion

        #region CommandField Tests

        [Test]
        public void CommandField_IsRequest_ReturnsTrue_ForRequestCommands()
        {
            Assert.That(CommandField.IsRequest(CommandField.CEchoRequest), Is.True);
            Assert.That(CommandField.IsRequest(CommandField.CStoreRequest), Is.True);
            Assert.That(CommandField.IsRequest(CommandField.CFindRequest), Is.True);
            Assert.That(CommandField.IsRequest(CommandField.CMoveRequest), Is.True);
            Assert.That(CommandField.IsRequest(CommandField.CGetRequest), Is.True);
        }

        [Test]
        public void CommandField_IsResponse_ReturnsTrue_ForResponseCommands()
        {
            Assert.That(CommandField.IsResponse(CommandField.CEchoResponse), Is.True);
            Assert.That(CommandField.IsResponse(CommandField.CStoreResponse), Is.True);
            Assert.That(CommandField.IsResponse(CommandField.CFindResponse), Is.True);
            Assert.That(CommandField.IsResponse(CommandField.CMoveResponse), Is.True);
            Assert.That(CommandField.IsResponse(CommandField.CGetResponse), Is.True);
        }

        [Test]
        public void CommandField_ToResponse_ConvertsCorrectly()
        {
            Assert.That(CommandField.ToResponse(CommandField.CEchoRequest), Is.EqualTo(CommandField.CEchoResponse));
            Assert.That(CommandField.ToResponse(CommandField.CStoreRequest), Is.EqualTo(CommandField.CStoreResponse));
            Assert.That(CommandField.ToResponse(CommandField.CFindRequest), Is.EqualTo(CommandField.CFindResponse));
        }

        [Test]
        public void CommandField_ToRequest_ConvertsCorrectly()
        {
            Assert.That(CommandField.ToRequest(CommandField.CEchoResponse), Is.EqualTo(CommandField.CEchoRequest));
            Assert.That(CommandField.ToRequest(CommandField.CStoreResponse), Is.EqualTo(CommandField.CStoreRequest));
            Assert.That(CommandField.ToRequest(CommandField.CFindResponse), Is.EqualTo(CommandField.CFindRequest));
        }

        #endregion

        #region Helper Methods

        private static DicomClientOptions CreateValidOptions()
        {
            return new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "ANY-SCP",
                CallingAE = "TEST-SCU"
            };
        }

        #endregion
    }

    /// <summary>
    /// Integration tests for <see cref="DicomClient"/> that require a running DICOM server.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Explicit("Requires DCMTK or compatible DICOM server running")]
    public class DicomClientIntegrationTests
    {
        // These tests are explicit and require a DICOM server
        // See Plan 07 for integration testing with DCMTK
    }
}
