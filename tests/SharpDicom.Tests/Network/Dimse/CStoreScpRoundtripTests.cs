using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
/// Roundtrip fidelity tests for C-STORE SCP to verify complete element preservation.
/// </summary>
/// <remarks>
/// These tests verify that all DICOM elements (including sequences, nested sequences,
/// and private tags) are correctly preserved when sent via C-STORE.
/// Uses in-process loopback communication for fully automated testing.
/// </remarks>
[TestFixture]
public class CStoreScpRoundtripTests
{
    private const string ServerAE = "ROUNDTRIP_SCP";
    private const string ClientAE = "ROUNDTRIP_SCU";

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Tests that a simple dataset with basic elements roundtrips correctly.
    /// </summary>
    [Test]
    public async Task CStoreScp_SimpleDataset_PreservesAllElements()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");
        AssertDatasetsMatch(dataset, receivedDataset!);
    }

    /// <summary>
    /// Tests that a dataset with a single sequence is preserved correctly.
    /// </summary>
    [Test]
    public async Task CStoreScp_DatasetWithSequence_SequencePreserved()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateDatasetWithSequence(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");
        AssertDatasetsMatch(dataset, receivedDataset!);

        // Specifically verify sequence
        var seqTag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var originalSeq = dataset.GetSequence(seqTag);
        var receivedSeq = receivedDataset!.GetSequence(seqTag);

        Assert.That(receivedSeq, Is.Not.Null, "Sequence should be preserved");
        Assert.That(receivedSeq!.Items.Count, Is.EqualTo(originalSeq!.Items.Count),
            "Sequence should have same number of items");
    }

    /// <summary>
    /// Tests that nested sequences (3 levels deep) are preserved correctly.
    /// </summary>
    [Test]
    public async Task CStoreScp_NestedSequences_AllLevelsPreserved()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateDatasetWithNestedSequences(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");
        AssertDatasetsMatch(dataset, receivedDataset!);

        // Verify nesting depth
        var level1Tag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var level2Tag = new DicomTag(0x0008, 0x1140); // ReferencedImageSequence
        var level3Tag = new DicomTag(0x0040, 0x100A); // ReasonForRequestedProcedureCodeSequence (nested example)

        var level1 = receivedDataset!.GetSequence(level1Tag);
        Assert.That(level1, Is.Not.Null, "Level 1 sequence should exist");
        Assert.That(level1!.Items.Count, Is.GreaterThan(0), "Level 1 should have items");

        var level2 = level1.Items[0].GetSequence(level2Tag);
        Assert.That(level2, Is.Not.Null, "Level 2 sequence should exist");
        Assert.That(level2!.Items.Count, Is.GreaterThan(0), "Level 2 should have items");

        var level3 = level2.Items[0].GetSequence(level3Tag);
        Assert.That(level3, Is.Not.Null, "Level 3 sequence should exist");
        Assert.That(level3!.Items.Count, Is.GreaterThan(0), "Level 3 should have items");
    }

    /// <summary>
    /// Tests that private tags are preserved during transmission.
    /// </summary>
    [Test]
    public async Task CStoreScp_PrivateTags_Preserved()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateDatasetWithPrivateTags(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");
        AssertDatasetsMatch(dataset, receivedDataset!);

        // Verify specific private tags
        var privateTag1 = new DicomTag(0x0009, 0x1001);
        var privateTag2 = new DicomTag(0x0019, 0x1002);

        Assert.That(receivedDataset!.Contains(privateTag1), Is.True,
            "Private tag (0009,1001) should be preserved");
        Assert.That(receivedDataset.Contains(privateTag2), Is.True,
            "Private tag (0019,1002) should be preserved");
    }

    /// <summary>
    /// Tests that an empty sequence is preserved as empty (not removed).
    /// </summary>
    [Test]
    public async Task CStoreScp_EmptySequence_PreservedAsEmpty()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateDatasetWithEmptySequence(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");

        var seqTag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var receivedSeq = receivedDataset!.GetSequence(seqTag);
        Assert.That(receivedSeq, Is.Not.Null, "Empty sequence should be preserved");
        Assert.That(receivedSeq!.Items.Count, Is.EqualTo(0),
            "Sequence should remain empty");
    }

    /// <summary>
    /// Tests full roundtrip comparing byte-level fidelity of datasets.
    /// </summary>
    [Test]
    public async Task CStoreScp_FullRoundtrip_ElementByElementIdentical()
    {
        // Arrange
        var sopClassUid = DicomUID.CTImageStorage;
        var sopInstanceUid = CreateUniqueUID();
        var dataset = CreateComplexDataset(sopClassUid, sopInstanceUid);

        // Act
        var receivedDataset = await SendAndReceiveDataset(dataset, sopClassUid);

        // Assert
        Assert.That(receivedDataset, Is.Not.Null, "Should receive dataset");

        // Element-by-element comparison
        AssertElementByElementMatch(dataset, receivedDataset!);
    }

    #region Helper Methods

    private static async Task<DicomDataset?> SendAndReceiveDataset(
        DicomDataset dataset,
        DicomUID sopClassUid)
    {
        DicomDataset? receivedDataset = null;
        var receivedEvent = new TaskCompletionSource<bool>();

        // Get a free port for this test
        var testPort = GetFreePort();

        // Start server
        var serverOptions = new DicomServerOptions
        {
            Port = testPort,
            AETitle = ServerAE,
            OnCStoreRequest = (ctx, ds, ct) =>
            {
                receivedDataset = ds;
                receivedEvent.TrySetResult(true);
                return new ValueTask<DicomStatus>(DicomStatus.Success);
            }
        };

        await using var server = new DicomServer(serverOptions);
        server.Start();

        // Give server time to start
        await Task.Delay(100);

        try
        {
            // Connect client and send
            var clientOptions = new DicomClientOptions
            {
                Host = "127.0.0.1",
                Port = testPort,
                CalledAE = ServerAE,
                CallingAE = ClientAE
            };

            await using var client = new DicomClient(clientOptions);

            var contexts = new[]
            {
                // Use Explicit VR Little Endian for roundtrip tests
                // This preserves VR information for all tags including private tags
                new PresentationContext(1, sopClassUid, TransferSyntax.ExplicitVRLittleEndian)
            };

            await client.ConnectAsync(contexts);
            var storeScu = new CStoreScu(client);
            var response = await storeScu.SendAsync(dataset, null);
            await client.ReleaseAsync();

            Assert.That(response.IsSuccess, Is.True, $"C-STORE should succeed. Status: 0x{response.Status.Code:X4}, Message: {response.ErrorComment}");

            // Wait for server to receive
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(receivedEvent.Task, timeoutTask);

            Assert.That(completedTask, Is.EqualTo(receivedEvent.Task),
                "Should receive dataset within timeout");

            return receivedDataset;
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static DicomDataset CreateSimpleDataset(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = new DicomDataset();

        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, sopClassUid.ToString()));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, sopInstanceUid.ToString()));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "12345"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.5"));
        dataset.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.3.4.5.1"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));

        return dataset;
    }

    private static DicomDataset CreateDatasetWithSequence(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        // Add a sequence with one item
        var seqItem = new DicomDataset();
        seqItem.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.999"));
        seqItem.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.3.4.999.1"));

        var seqTag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var sequence = new DicomSequence(seqTag, new[] { seqItem });
        dataset.Add(sequence);

        return dataset;
    }

    private static DicomDataset CreateDatasetWithNestedSequences(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        var level1Tag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var level2Tag = new DicomTag(0x0008, 0x1140); // ReferencedImageSequence
        var level3Tag = new DicomTag(0x0040, 0x100A); // ReasonForRequestedProcedureCodeSequence

        // Level 3: Innermost sequence
        var level3Item = new DicomDataset();
        level3Item.Add(CreateStringElement(new DicomTag(0x0008, 0x0100), DicomVR.SH, "CODE123")); // CodeValue
        level3Item.Add(CreateStringElement(new DicomTag(0x0008, 0x0102), DicomVR.SH, "TEST")); // CodingSchemeDesignator
        var level3Seq = new DicomSequence(level3Tag, new[] { level3Item });

        // Level 2: Middle sequence
        var level2Item = new DicomDataset();
        level2Item.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, sopClassUid.ToString()));
        level2Item.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4.999.999"));
        level2Item.Add(level3Seq);
        var level2Seq = new DicomSequence(level2Tag, new[] { level2Item });

        // Level 1: Outer sequence
        var level1Item = new DicomDataset();
        level1Item.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.999"));
        level1Item.Add(level2Seq);
        var level1Seq = new DicomSequence(level1Tag, new[] { level1Item });

        dataset.Add(level1Seq);

        return dataset;
    }

    private static DicomDataset CreateDatasetWithPrivateTags(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        // Add private tags (odd group numbers)
        var privateTag1 = new DicomTag(0x0009, 0x1001);
        var privateTag2 = new DicomTag(0x0019, 0x1002);

        dataset.Add(CreateStringElement(privateTag1, DicomVR.LO, "PrivateData1"));
        dataset.Add(CreateStringElement(privateTag2, DicomVR.LO, "PrivateData2"));

        return dataset;
    }

    private static DicomDataset CreateDatasetWithEmptySequence(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        // Add an empty sequence (zero items)
        var seqTag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var emptySequence = new DicomSequence(seqTag, Array.Empty<DicomDataset>());
        dataset.Add(emptySequence);

        return dataset;
    }

    private static DicomDataset CreateComplexDataset(DicomUID sopClassUid, DicomUID sopInstanceUid)
    {
        var dataset = CreateSimpleDataset(sopClassUid, sopInstanceUid);

        // Add multiple sequences
        var seqItem1 = new DicomDataset();
        seqItem1.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.111"));

        var seqItem2 = new DicomDataset();
        seqItem2.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.222"));

        var seqTag = new DicomTag(0x0008, 0x1110); // ReferencedStudySequence
        var sequence = new DicomSequence(seqTag, new[] { seqItem1, seqItem2 });
        dataset.Add(sequence);

        // Add private tags
        dataset.Add(CreateStringElement(new DicomTag(0x0009, 0x1001), DicomVR.LO, "PrivateValue"));

        // Add various VR types
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240101"));
        dataset.Add(CreateStringElement(new DicomTag(0x0008, 0x0030), DicomVR.TM, "120000")); // StudyTime
        dataset.Add(CreateStringElement(DicomTag.AccessionNumber, DicomVR.SH, "ACC123456"));

        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    private static DicomUID CreateUniqueUID()
    {
        return new DicomUID($"1.2.3.4.5.6.7.8.{DateTime.UtcNow.Ticks}");
    }

    private static void AssertDatasetsMatch(DicomDataset expected, DicomDataset actual)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count),
            $"Dataset should have same number of elements. Expected {expected.Count}, got {actual.Count}");

        foreach (var expectedElement in expected)
        {
            Assert.That(actual.Contains(expectedElement.Tag), Is.True,
                $"Dataset should contain tag {expectedElement.Tag}");

            var hasElement = actual.TryGetElement(expectedElement.Tag, out var actualElement);
            Assert.That(hasElement, Is.True,
                $"Element {expectedElement.Tag} should be retrievable");
            Assert.That(actualElement, Is.Not.Null,
                $"Element {expectedElement.Tag} should not be null");

            Assert.That(actualElement!.VR, Is.EqualTo(expectedElement.VR),
                $"Element {expectedElement.Tag} should have same VR");
        }
    }

    private static void AssertElementByElementMatch(DicomDataset expected, DicomDataset actual)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count),
            "Datasets should have same element count");

        foreach (var expectedElement in expected)
        {
            var hasElement = actual.TryGetElement(expectedElement.Tag, out var actualElement);
            Assert.That(hasElement, Is.True,
                $"Missing element {expectedElement.Tag}");
            Assert.That(actualElement, Is.Not.Null,
                $"Element {expectedElement.Tag} should not be null");

            // Compare tag, VR
            Assert.That(actualElement!.Tag, Is.EqualTo(expectedElement.Tag));
            Assert.That(actualElement.VR, Is.EqualTo(expectedElement.VR));

            // Compare value bytes for non-sequence elements
            if (expectedElement is DicomSequence expectedSeq)
            {
                Assert.That(actualElement, Is.InstanceOf<DicomSequence>(),
                    $"Element {expectedElement.Tag} should be a sequence");

                var actualSeq = (DicomSequence)actualElement;
                Assert.That(actualSeq.Items.Count, Is.EqualTo(expectedSeq.Items.Count),
                    $"Sequence {expectedElement.Tag} should have same item count");

                // Recursively compare items
                for (int i = 0; i < expectedSeq.Items.Count; i++)
                {
                    AssertElementByElementMatch(expectedSeq.Items[i], actualSeq.Items[i]);
                }
            }
            else if (expectedElement is DicomBinaryElement expectedBinary &&
                     actualElement is DicomBinaryElement actualBinary)
            {
                var expectedBytes = expectedBinary.GetBytes();
                var actualBytes = actualBinary.GetBytes();

                Assert.That(actualBytes, Is.EqualTo(expectedBytes),
                    $"Element {expectedElement.Tag} should have identical byte values");
            }
        }
    }

    #endregion
}
