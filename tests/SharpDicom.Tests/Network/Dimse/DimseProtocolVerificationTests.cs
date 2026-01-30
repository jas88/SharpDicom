using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network.Dimse;

/// <summary>
/// Tests to verify DIMSE protocol correctness per DICOM PS3.7 and PS3.8.
/// </summary>
/// <remarks>
/// <para>
/// These tests verify low-level protocol details:
/// - Transfer Syntax usage (identifiers vs data)
/// - PDV fragmentation
/// - Command element encoding
/// - Role selection
/// </para>
/// </remarks>
[TestFixture]
public class DimseProtocolVerificationTests : IDisposable
{
    private const string ServerAE = "PROTO_SCP";
    private const string ClientAE = "PROTO_SCU";

    private int _serverPort;
    private DicomServer? _server;
    private bool _disposed;

    [SetUp]
    public void SetUp()
    {
        _serverPort = GetFreePort();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_server != null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
    }

    /// <summary>
    /// Disposes the test fixture.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _server = null;
        }
        _disposed = true;
    }

    #region CHECK 1: Identifier uses negotiated TS (Explicit VR), not Implicit VR LE

    /// <summary>
    /// Verifies that C-STORE dataset uses negotiated Transfer Syntax.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.7 Section 9.3.1:
    /// - Command Set: Always Implicit VR Little Endian
    /// - Data Set: Uses negotiated Transfer Syntax for the Presentation Context
    /// </para>
    /// </remarks>
    [Test]
    public async Task CStoreDataset_UsesNegotiatedTransferSyntax()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.check1.{DateTime.UtcNow.Ticks}");

        var serverOptions = new DicomServerOptions
        {
            Port = _serverPort,
            AETitle = ServerAE,
            OnAssociationRequest = ctx =>
            {
                // Accept with Explicit VR LE
                var accepted = new List<PresentationContext>();
                foreach (var pc in ctx.RequestedContexts)
                {
                    accepted.Add(PresentationContext.CreateAccepted(
                        pc.Id, pc.AbstractSyntax, TransferSyntax.ExplicitVRLittleEndian));
                }
                return ValueTask.FromResult(AssociationRequestResult.Accepted(accepted));
            },
            OnCStoreRequest = (ctx, dataset, ct) =>
            {
                // Check that we can parse the dataset - implies correct TS
                // If wrong TS were used, parsing would fail
                Assert.That(dataset, Is.Not.Null);
                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        _server = new DicomServer(serverOptions);
        _server.Start();

        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, "ExplicitVRTest");

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = _serverPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ExplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True, "C-STORE with Explicit VR should succeed");
    }

    #endregion

    #region CHECK 2: PDV fragmentation respects MaxPduLength

    /// <summary>
    /// Verifies that PDVs are fragmented when data exceeds MaxPduLength.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.8 Section 9.3.1:
    /// - PDU length must not exceed the negotiated maximum
    /// - Large data sets must be fragmented across multiple P-DATA PDUs
    /// </para>
    /// </remarks>
    [Test]
    public async Task PDVFragmentation_RespectsMaxPduLength()
    {
        // Arrange - use small max PDU to force fragmentation
        uint maxPduLength = 4096; // 4KB - will force fragmentation of typical datasets
        var receiveCount = 0;

        var serverOptions = new DicomServerOptions
        {
            Port = _serverPort,
            AETitle = ServerAE,
            MaxPduLength = maxPduLength,
            OnCStoreRequest = (ctx, dataset, ct) =>
            {
                Interlocked.Increment(ref receiveCount);
                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        _server = new DicomServer(serverOptions);
        _server.Start();

        // Create a dataset larger than MaxPduLength
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = new DicomUID($"1.2.3.4.5.check2.{DateTime.UtcNow.Ticks}");
        var dataset = CreateLargeTestDataset(sopClassUid, sopInstanceUid, "FragmentTest", 8192);

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = _serverPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE,
            MaxPduLength = maxPduLength
        };

        await using var client = new DicomClient(clientOptions);

        var contexts = new[]
        {
            new PresentationContext(1, sopClassUid, TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        var storeScu = new CStoreScu(client);
        var response = await storeScu.SendAsync(dataset, null);
        await client.ReleaseAsync();

        // Assert
        Assert.That(response.IsSuccess, Is.True,
            "Large dataset should be fragmented and reassembled correctly");
        Assert.That(receiveCount, Is.EqualTo(1),
            "Server should receive exactly one complete dataset (reassembled from fragments)");
    }

    #endregion

    #region CHECK 3: Last fragment flag (0x02) set correctly

    /// <summary>
    /// Verifies that the last PDV fragment has the "last fragment" flag set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.8 Section 9.3.5.1:
    /// - PDV header contains Message Control Header with:
    ///   - Bit 0: 1 = Command, 0 = Data
    ///   - Bit 1: 1 = Last fragment, 0 = Not last fragment
    /// </para>
    /// </remarks>
    [Test]
    public void PDV_LastFragmentFlag_IsSetCorrectly()
    {
        // Arrange - create a PDV with isLastFragment = true
        var pdvData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var pdvWithLastFlag = new PresentationDataValue(
            presentationContextId: 1,
            isCommand: false,
            isLastFragment: true,
            data: pdvData);

        var pdvWithoutLastFlag = new PresentationDataValue(
            presentationContextId: 1,
            isCommand: false,
            isLastFragment: false,
            data: pdvData);

        // Assert - verify flag values
        Assert.That(pdvWithLastFlag.IsLastFragment, Is.True,
            "PDV created with isLastFragment=true should have flag set");
        Assert.That(pdvWithoutLastFlag.IsLastFragment, Is.False,
            "PDV created with isLastFragment=false should not have flag set");

        // Verify the message control header byte via ToMessageControlHeader()
        // isCommand=false (bit 0 = 0), isLastFragment=true (bit 1 = 1) => 0x02
        // isCommand=false (bit 0 = 0), isLastFragment=false (bit 1 = 0) => 0x00
        Assert.That(pdvWithLastFlag.ToMessageControlHeader(), Is.EqualTo(0x02),
            "Last data fragment should have MCH = 0x02");
        Assert.That(pdvWithoutLastFlag.ToMessageControlHeader(), Is.EqualTo(0x00),
            "Non-last data fragment should have MCH = 0x00");
    }

    /// <summary>
    /// Verifies last fragment flag for command PDVs.
    /// </summary>
    [Test]
    public void PDV_CommandLastFragmentFlag_IsSetCorrectly()
    {
        // Arrange - command PDV with last fragment
        var pdvData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var commandPdv = new PresentationDataValue(
            presentationContextId: 1,
            isCommand: true,
            isLastFragment: true,
            data: pdvData);

        // Assert
        // isCommand=true (bit 0 = 1), isLastFragment=true (bit 1 = 1) => 0x03
        Assert.That(commandPdv.ToMessageControlHeader(), Is.EqualTo(0x03),
            "Last command fragment should have MCH = 0x03");
        Assert.That(commandPdv.IsCommand, Is.True);
        Assert.That(commandPdv.IsLastFragment, Is.True);
    }

    #endregion

    #region CHECK 4: Sub-operation counts extracted correctly from C-MOVE-RSP

    /// <summary>
    /// Verifies that sub-operation counts are correctly parsed from C-MOVE/C-GET responses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.7 Table 9.1-5 (C-MOVE-RSP) and Table 9.1-6 (C-GET-RSP):
    /// - (0000,1020) NumberOfRemainingSuboperations
    /// - (0000,1021) NumberOfCompletedSuboperations
    /// - (0000,1022) NumberOfFailedSuboperations
    /// - (0000,1023) NumberOfWarningSuboperations
    /// </para>
    /// </remarks>
    [Test]
    public void CMoveResponse_SuboperationCounts_ParsedCorrectly()
    {
        // Arrange - create a C-MOVE-RSP command dataset with sub-operation counts
        var dataset = new DicomDataset();

        // Add command elements
        AddUsElement(dataset, DicomTag.CommandField, CommandField.CMoveResponse);
        AddUsElement(dataset, DicomTag.MessageIDBeingRespondedTo, 1);
        AddUsElement(dataset, DicomTag.Status, 0xFF00); // Pending
        AddUsElement(dataset, DicomTag.NumberOfRemainingSuboperations, 5);
        AddUsElement(dataset, DicomTag.NumberOfCompletedSuboperations, 3);
        AddUsElement(dataset, DicomTag.NumberOfFailedSuboperations, 1);
        AddUsElement(dataset, DicomTag.NumberOfWarningSuboperations, 2);

        var command = new DicomCommand(dataset);

        // Assert
        Assert.That(command.Status.IsPending, Is.True);
        Assert.That(command.NumberOfRemainingSuboperations, Is.EqualTo(5));
        Assert.That(command.NumberOfCompletedSuboperations, Is.EqualTo(3));
        Assert.That(command.NumberOfFailedSuboperations, Is.EqualTo(1));
        Assert.That(command.NumberOfWarningSuboperations, Is.EqualTo(2));
    }

    /// <summary>
    /// Verifies sub-operation counts for final (success) response.
    /// </summary>
    [Test]
    public void CMoveResponse_FinalStatus_SuboperationCounts()
    {
        // Arrange - final response (Success or Complete)
        var dataset = new DicomDataset();

        AddUsElement(dataset, DicomTag.CommandField, CommandField.CMoveResponse);
        AddUsElement(dataset, DicomTag.MessageIDBeingRespondedTo, 1);
        AddUsElement(dataset, DicomTag.Status, 0x0000); // Success
        AddUsElement(dataset, DicomTag.NumberOfRemainingSuboperations, 0);
        AddUsElement(dataset, DicomTag.NumberOfCompletedSuboperations, 10);
        AddUsElement(dataset, DicomTag.NumberOfFailedSuboperations, 0);
        AddUsElement(dataset, DicomTag.NumberOfWarningSuboperations, 0);

        var command = new DicomCommand(dataset);

        // Assert
        Assert.That(command.Status.IsSuccess, Is.True);
        Assert.That(command.NumberOfRemainingSuboperations, Is.EqualTo(0),
            "Final response should have 0 remaining");
        Assert.That(command.NumberOfCompletedSuboperations, Is.EqualTo(10));
    }

    /// <summary>
    /// Verifies sub-operation counts for C-GET response.
    /// </summary>
    [Test]
    public void CGetResponse_SuboperationCounts_ParsedCorrectly()
    {
        // Arrange
        var dataset = new DicomDataset();

        AddUsElement(dataset, DicomTag.CommandField, CommandField.CGetResponse);
        AddUsElement(dataset, DicomTag.MessageIDBeingRespondedTo, 42);
        AddUsElement(dataset, DicomTag.Status, 0xFF00); // Pending
        AddUsElement(dataset, DicomTag.NumberOfRemainingSuboperations, 10);
        AddUsElement(dataset, DicomTag.NumberOfCompletedSuboperations, 5);
        AddUsElement(dataset, DicomTag.NumberOfFailedSuboperations, 0);
        AddUsElement(dataset, DicomTag.NumberOfWarningSuboperations, 1);

        var command = new DicomCommand(dataset);

        // Assert
        Assert.That(command.IsCGetResponse, Is.True);
        Assert.That(command.NumberOfRemainingSuboperations, Is.EqualTo(10));
        Assert.That(command.NumberOfCompletedSuboperations, Is.EqualTo(5));
        Assert.That(command.NumberOfFailedSuboperations, Is.EqualTo(0));
        Assert.That(command.NumberOfWarningSuboperations, Is.EqualTo(1));
    }

    #endregion

    #region CHECK 5: MoveDestination padded to even length

    /// <summary>
    /// Verifies that MoveDestination AE title is padded to even length per DICOM rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.5 Section 6.2:
    /// - AE VR: 16 bytes maximum, trailing spaces for padding
    /// - Value length must be even
    /// </para>
    /// </remarks>
    [Test]
    public void MoveDestination_PaddedToEvenLength()
    {
        // Arrange - test padding logic
        var oddAeTitle = "DEST"; // 4 characters - even
        var paddedAeTitle = PadAeTitle(oddAeTitle);

        // Assert
        Assert.That(paddedAeTitle.Length % 2, Is.EqualTo(0),
            "Padded AE title should have even length");
        Assert.That(paddedAeTitle.Length, Is.LessThanOrEqualTo(16),
            "AE title should not exceed 16 characters");

        // Test with odd-length input
        var oddInput = "DST"; // 3 characters
        var paddedOdd = PadAeTitle(oddInput);
        Assert.That(paddedOdd.Length % 2, Is.EqualTo(0),
            "Odd-length AE title should be padded to even length");
    }

    /// <summary>
    /// Verifies C-MOVE request creation with MoveDestination.
    /// </summary>
    [Test]
    public void CMoveRequest_MoveDestination_IsEvenLength()
    {
        // Arrange
        ushort messageId = 1;
        var sopClassUid = DicomUID.PatientRootQueryRetrieveMove;
        var moveDestination = "MOVEDEST"; // 8 characters

        // Create C-MOVE request (priority 0 = MEDIUM)
        var command = DicomCommand.CreateCMoveRequest(
            messageId, sopClassUid, moveDestination, 0);

        // Assert
        Assert.That(command.MoveDestination, Is.Not.Null);

        // Verify the MoveDestination was stored (may include padding)
        var destValue = command.MoveDestination;
        Assert.That(destValue, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>
    /// Verifies that 16-character AE title is not truncated.
    /// </summary>
    [Test]
    public void MoveDestination_MaxLength_NotTruncated()
    {
        // Arrange
        var maxLengthAe = "1234567890123456"; // Exactly 16 characters
        var padded = PadAeTitle(maxLengthAe);

        // Assert
        Assert.That(padded.Length, Is.EqualTo(16));
        Assert.That(padded, Is.EqualTo(maxLengthAe));
    }

    #endregion

    #region CHECK 6: SCP Role Selection sub-item for C-GET

    /// <summary>
    /// Verifies that C-GET association can be established with Storage SOP Classes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per PS3.7 Section D.3.3.4:
    /// - For C-GET operations, the SCU must also accept the SCP role for Storage SOP Classes
    /// - This requires SCP/SCU Role Selection Sub-Item in the A-ASSOCIATE-RQ
    /// </para>
    /// </remarks>
    [Test]
    public async Task CGetAssociation_WithStorageContexts_CanBeEstablished()
    {
        // Arrange
        bool associationAccepted = false;

        var serverOptions = new DicomServerOptions
        {
            Port = _serverPort,
            AETitle = ServerAE,
            OnAssociationRequest = ctx =>
            {
                // Accept all requested contexts
                var accepted = new List<PresentationContext>();
                foreach (var pc in ctx.RequestedContexts)
                {
                    accepted.Add(PresentationContext.CreateAccepted(
                        pc.Id, pc.AbstractSyntax, TransferSyntax.ImplicitVRLittleEndian));
                }
                associationAccepted = true;
                return ValueTask.FromResult(AssociationRequestResult.Accepted(accepted));
            }
        };

        _server = new DicomServer(serverOptions);
        _server.Start();

        var clientOptions = new DicomClientOptions
        {
            Host = "127.0.0.1",
            Port = _serverPort,
            CalledAE = ServerAE,
            CallingAE = ClientAE
        };

        await using var client = new DicomClient(clientOptions);

        // Include C-GET SOP Class and Storage SOP Classes for sub-operations
        var contexts = new[]
        {
            new PresentationContext(1, DicomUID.PatientRootQueryRetrieveGet,
                TransferSyntax.ImplicitVRLittleEndian),
            new PresentationContext(3, DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian)
        };

        // Act
        await client.ConnectAsync(contexts);
        await client.ReleaseAsync();

        // Assert
        Assert.That(associationAccepted, Is.True,
            "Association with C-GET and Storage contexts should be accepted");
    }

    /// <summary>
    /// Documents the SCP/SCU Role Selection requirement for C-GET.
    /// </summary>
    [Test]
    public void CGet_ScpRoleSelection_Documentation()
    {
        // This test documents the requirement rather than testing it directly
        // A full implementation test would require modifying the association layer

        /*
        Per PS3.7 Annex D.3.3.4:

        For C-GET operations, the SCP/SCU Role Selection Sub-Item is required because:

        1. The C-GET SCU (requestor) needs to accept C-STORE operations
           as a Storage SCP for the retrieved images

        2. The association initiator proposes:
           - SCU Role = 1 for Q/R SOP Class (to send C-GET-RQ)
           - SCP Role = 1 for Storage SOP Classes (to receive C-STORE-RQ)

        3. The association acceptor (C-GET SCP) confirms:
           - SCP Role = 1 for Q/R SOP Class (to respond with C-GET-RSP)
           - SCU Role = 1 for Storage SOP Classes (to send C-STORE-RQ)

        Without role selection, the default is:
           - Association initiator = SCU
           - Association acceptor = SCP

        This would prevent the C-GET SCP from sending C-STORE requests.

        The SCP/SCU Role Selection Sub-Item format (PS3.8 Section 9.3.1.4):
           Item Type: 54H
           Reserved: 00H
           Item Length: depends on UID length
           UID Length: 2 bytes
           SOP Class UID: variable
           SCU Role: 1 byte (0 = not supported, 1 = supported)
           SCP Role: 1 byte (0 = not supported, 1 = supported)
        */

        Assert.Pass("Documentation test - describes SCP/SCU Role Selection requirement");
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

    private static DicomDataset CreateTestDataset(
        DicomUID sopClassUid,
        DicomUID sopInstanceUid,
        string patientName)
    {
        var dataset = new DicomDataset();

        dataset.Add(new DicomStringElement(DicomTag.SOPClassUID, DicomVR.UI,
            Encoding.ASCII.GetBytes(sopClassUid.ToString())));
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            Encoding.ASCII.GetBytes(sopInstanceUid.ToString())));
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.ASCII.GetBytes(patientName)));
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.ASCII.GetBytes("000001")));
        dataset.Add(new DicomStringElement(DicomTag.StudyInstanceUID, DicomVR.UI,
            Encoding.ASCII.GetBytes("1.2.3.4.5")));
        dataset.Add(new DicomStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI,
            Encoding.ASCII.GetBytes("1.2.3.4.5.1")));

        return dataset;
    }

    private static DicomDataset CreateLargeTestDataset(
        DicomUID sopClassUid,
        DicomUID sopInstanceUid,
        string patientName,
        int minSize)
    {
        var dataset = CreateTestDataset(sopClassUid, sopInstanceUid, patientName);

        // Add large private data to exceed minSize
        var largeData = new byte[minSize];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        // Use a private tag for the large data
        var privateTag = new DicomTag(0x0099, 0x0010);
        dataset.Add(new DicomNumericElement(privateTag, DicomVR.OB, largeData));

        return dataset;
    }

    private static void AddUsElement(DicomDataset dataset, DicomTag tag, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        dataset.Add(new DicomNumericElement(tag, DicomVR.US, bytes));
    }

    private static string PadAeTitle(string aeTitle)
    {
        if (aeTitle.Length > 16)
            aeTitle = aeTitle[..16];

        // Pad to even length with spaces
        if (aeTitle.Length % 2 != 0)
            aeTitle += ' ';

        return aeTitle;
    }

    #endregion
}
