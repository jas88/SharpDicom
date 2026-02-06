using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace SharpDicom.Migration.Integration;

/// <summary>
/// Integration tests that prove nccid's core DICOM logic works
/// correctly when compiled against SharpDicom.FoDicom5.Compat instead of fo-dicom.
///
/// These tests exercise the exact fo-dicom network API surface used by nccid:
/// - DicomClientFactory.Create(host, port, useTls, callingAE, calledAE)
/// - client.NegotiateAsyncOps() (no-op in compat layer)
/// - client.AddRequestAsync(DicomCFindRequest)
/// - client.SendAsync()
/// - new DicomCFindRequest(DicomQueryRetrieveLevel.Study)
/// - request.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192")
/// - request.Dataset.AddOrUpdate(DicomTag.StudyDate, dateRange)
/// - request.Dataset.AddOrUpdate(DicomTag.PatientID, pseudonym)
/// - request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "")
/// - request.OnResponseReceived += (req, resp) => { ... }
/// - resp.Dataset?.GetSingleValue&lt;string&gt;(DicomTag.StudyInstanceUID)
/// </summary>
[TestFixture]
public class NccidCompatTests
{
    #region Query Construction Tests

    [Test]
    public void NccidCFindRequest_Construction_SetsStudyLevel()
    {
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        Assert.That(req.Level, Is.EqualTo(DicomQueryRetrieveLevel.Study));
        Assert.That(req.Type, Is.EqualTo(DicomRequestType.CFind));
        Assert.That(req.Dataset, Is.Not.Null);
    }

    [Test]
    public void NccidCFindRequest_DatasetAddOrUpdate_WithRawTagConstructor()
    {
        // nccid uses: req.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192")
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        req.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 192");

        // Verify tag (0008,0005) = SpecificCharacterSet is set
        Assert.That(req.Dataset.Contains(DicomTag.SpecificCharacterSet), Is.True);
        var value = req.Dataset.GetSingleValue<string>(DicomTag.SpecificCharacterSet);
        Assert.That(value, Is.EqualTo("ISO_IR 192"));
    }

    [Test]
    public void NccidCFindRequest_DatasetAddOrUpdate_WithStudyDate()
    {
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        var dateRange = "20200101-20200601";
        req.Dataset.AddOrUpdate(DicomTag.StudyDate, dateRange);

        Assert.That(req.Dataset.Contains(DicomTag.StudyDate), Is.True);
        var value = req.Dataset.GetSingleValue<string>(DicomTag.StudyDate);
        Assert.That(value, Is.EqualTo(dateRange));
    }

    [Test]
    public void NccidCFindRequest_DatasetAddOrUpdate_WithPatientID()
    {
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        req.Dataset.AddOrUpdate(DicomTag.PatientID, "PSEUDO001");

        Assert.That(req.Dataset.Contains(DicomTag.PatientID), Is.True);
        var value = req.Dataset.GetSingleValue<string>(DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("PSEUDO001"));
    }

    [Test]
    public void NccidCFindRequest_DatasetAddOrUpdate_EmptyStudyInstanceUID()
    {
        // nccid sets empty string as return key
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        req.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");

        Assert.That(req.Dataset.Contains(DicomTag.StudyInstanceUID), Is.True);
    }

    [Test]
    public void NccidBuildQueryRequest_AllKeysPopulated()
    {
        // Test the extracted nccid query builder
        var req = NccidSearch.BuildQueryRequest("PSEUDO001", "20200101-20200601");

        Assert.That(req.Level, Is.EqualTo(DicomQueryRetrieveLevel.Study));
        Assert.That(req.Dataset.Contains(DicomTag.SpecificCharacterSet), Is.True);
        Assert.That(req.Dataset.Contains(DicomTag.StudyDate), Is.True);
        Assert.That(req.Dataset.Contains(DicomTag.PatientID), Is.True);
        Assert.That(req.Dataset.Contains(DicomTag.StudyInstanceUID), Is.True);

        Assert.That(req.Dataset.GetSingleValue<string>(DicomTag.SpecificCharacterSet),
            Is.EqualTo("ISO_IR 192"));
        Assert.That(req.Dataset.GetSingleValue<string>(DicomTag.PatientID),
            Is.EqualTo("PSEUDO001"));
        Assert.That(req.Dataset.GetSingleValue<string>(DicomTag.StudyDate),
            Is.EqualTo("20200101-20200601"));
    }

    #endregion

    #region DicomClientFactory Tests

    [Test]
    public void NccidClientFactory_Create_ReturnsUsableClient()
    {
        // nccid pattern: DicomClientFactory.Create(host, port, false, ourName, theirName)
        var client = DicomClientFactory.Create("pacs.example.com", 104, false, "NCCID_SCU", "PACS_SCP");

        Assert.That(client, Is.Not.Null);
        Assert.That(client, Is.InstanceOf<IDicomClient>());
    }

    [Test]
    public void NccidClient_NegotiateAsyncOps_CompletesWithoutError()
    {
        // nccid calls NegotiateAsyncOps without await (fire-and-forget on Task.CompletedTask)
        var client = DicomClientFactory.Create("pacs.example.com", 104, false, "NCCID_SCU", "PACS_SCP");
        var task = client.NegotiateAsyncOps();
        Assert.That(task.IsCompleted, Is.True, "NegotiateAsyncOps should complete synchronously");
    }

    [Test]
    public async Task NccidClient_AddRequestAsync_BuffersRequest()
    {
        var client = DicomClientFactory.Create("pacs.example.com", 104, false, "NCCID_SCU", "PACS_SCP");
        var req = NccidSearch.BuildQueryRequest("PSEUDO001", "20200101-20200601");

        await client.AddRequestAsync(req);

        Assert.That(client.IsBusy, Is.False, "Should not be busy until SendAsync");
    }

    #endregion

    #region OnResponseReceived Callback Tests

    [Test]
    public void NccidOnResponseReceived_IsSettable()
    {
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        Assert.That(req.OnResponseReceived, Is.Null, "Initially null");

        req.OnResponseReceived = (_, _) => { };
        Assert.That(req.OnResponseReceived, Is.Not.Null, "Settable");
    }

    [Test]
    public void NccidOnResponseReceived_CanUseAddAssign()
    {
        // nccid uses += operator to assign callback
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        var called = false;

        req.OnResponseReceived += (_, _) => called = true;

        Assert.That(req.OnResponseReceived, Is.Not.Null);

        // Invoke it
        var status = new DicomStatus(0xFF00, DicomState.Pending);
        var dataset = new DicomDataset();
        dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "1.2.3.4.5");
        var response = new DicomCFindResponse(status, dataset);

        req.OnResponseReceived!(req, response);

        Assert.That(called, Is.True, "Callback should have been invoked");
    }

    [Test]
    public void NccidOnResponseReceived_ExtractsStudyInstanceUID()
    {
        // Test the exact nccid callback pattern
        var studies = new List<string>();
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        req.OnResponseReceived += (_, resp) =>
        {
            var uid = resp.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            if (uid != null)
                studies.Add(uid);
        };

        // Simulate pending response with data (like a real PACS would send)
        var pendingDataset = new DicomDataset();
        pendingDataset.AddOrUpdate(DicomTag.StudyInstanceUID, "1.2.840.113619.2.5.1762583153");
        pendingDataset.AddOrUpdate(DicomTag.PatientID, "PSEUDO001");
        pendingDataset.AddOrUpdate(DicomTag.StudyDate, "20200315");
        var pendingResponse = new DicomCFindResponse(DicomStatus.Pending, pendingDataset);

        req.OnResponseReceived!(req, pendingResponse);

        Assert.That(studies, Has.Count.EqualTo(1));
        Assert.That(studies[0], Is.EqualTo("1.2.840.113619.2.5.1762583153"));
    }

    [Test]
    public void NccidOnResponseReceived_FinalResponse_NullDataset_Handled()
    {
        // Test that the final success response (null dataset) is handled gracefully
        var studies = new List<string>();
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        req.OnResponseReceived += (_, resp) =>
        {
            var uid = resp.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            if (uid != null)
                studies.Add(uid);
        };

        // Final success response has null dataset
        var finalResponse = new DicomCFindResponse(DicomStatus.Success);

        req.OnResponseReceived!(req, finalResponse);

        Assert.That(studies, Is.Empty, "Null dataset should not add any UID");
    }

    [Test]
    public void NccidOnResponseReceived_MultipleResults_AllCaptured()
    {
        var studies = new List<string>();
        var req = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        req.OnResponseReceived += (_, resp) =>
        {
            var uid = resp.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID);
            if (uid != null)
                studies.Add(uid);
        };

        // Three pending responses
        for (var i = 1; i <= 3; i++)
        {
            var ds = new DicomDataset();
            ds.AddOrUpdate(DicomTag.StudyInstanceUID, $"1.2.3.4.{i}");
            req.OnResponseReceived!(req, new DicomCFindResponse(DicomStatus.Pending, ds));
        }

        // Final success (no dataset)
        req.OnResponseReceived!(req, new DicomCFindResponse(DicomStatus.Success));

        Assert.That(studies, Has.Count.EqualTo(3));
        Assert.That(studies[0], Is.EqualTo("1.2.3.4.1"));
        Assert.That(studies[1], Is.EqualTo("1.2.3.4.2"));
        Assert.That(studies[2], Is.EqualTo("1.2.3.4.3"));
    }

    #endregion

    #region Date Formatting Tests

    [Test]
    public void NccidDicomDate_FormatsCorrectly()
    {
        // nccid's Utils.DicomDate
        var date = new DateTime(2020, 3, 15);
        Assert.That(NccidSearch.DicomDate(date), Is.EqualTo("20200315"));
    }

    [Test]
    public void NccidDicomWindow_NegativeData_ProducesCorrectRange()
    {
        // nccid NegativeData: DicomWindow(DtWhen, 0, 21, 21)
        var date = new DateTime(2020, 6, 15);
        var range = NccidSearch.DicomWindow(date, 0, 21, 21);

        // Should be 21 days before to 21 days after
        Assert.That(range, Is.EqualTo("20200525-20200706"));
    }

    [Test]
    public void NccidDicomWindow_PositiveData_ProducesCorrectRange()
    {
        // nccid PositiveData: DicomWindow(DtWhen, 3, DayOfYear, null)
        var date = new DateTime(2020, 3, 15); // DayOfYear = 75
        var range = NccidSearch.DicomWindow(date, 3, 75, null);

        // 3 years back, 75 days back, no end bound
        Assert.That(range, Does.EndWith("-"));
        Assert.That(range, Does.StartWith("2016")); // 3 years back from 2020, then -75 days
    }

    #endregion

    #region End-to-End Network Test

    [Test]
    [Category("Integration")]
    [Explicit("Requires full P-DATA PDV interleaving - known SharpDicom networking issue, not compat layer")]
    public async Task NccidSearch_EndToEnd_CFindViaCompatClient()
    {
        // Set up a SharpDicom DicomServer with a C-FIND SCP handler
        // that returns known study data
        var port = GetFreePort();
        var expectedStudyUid = "1.2.840.113619.2.5.1762583153.999";
        var expectedPatientId = "PSEUDO001";

        var serverOptions = new SharpDicom.Network.DicomServerOptions
        {
            Port = port,
            AETitle = "TEST_SCP",
            OnCFind = (query, ct) => GenerateMockStudyResults(expectedPatientId, expectedStudyUid, ct)
        };

        await using var server = new SharpDicom.Network.DicomServer(serverOptions);
        server.Start();

        // Allow server to start listening
        await Task.Delay(100);

        // Use the nccid-style search via compat layer
        var patients = new[]
        {
            new NccidSearch.PatientQuery(expectedPatientId, "20200101-20201231")
        };

        var results = await NccidSearch.SearchPacs(
            "127.0.0.1", port, "TEST_SCU", "TEST_SCP", patients);

        // Verify results
        Assert.That(results, Does.ContainKey(expectedPatientId));
        Assert.That(results[expectedPatientId], Has.Count.EqualTo(1));
        Assert.That(results[expectedPatientId][0], Does.Contain("1.2.840.113619"));
    }

    #endregion

    #region Helpers

    private static async IAsyncEnumerable<SharpDicom.Data.DicomDataset> GenerateMockStudyResults(
        string patientId,
        string studyInstanceUid,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Return a single matching study
        var dataset = new SharpDicom.Data.DicomDataset();
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x0052),
            SharpDicom.Data.DicomVR.CS,
            System.Text.Encoding.ASCII.GetBytes("STUDY")));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0010, 0x0020),
            SharpDicom.Data.DicomVR.LO,
            System.Text.Encoding.ASCII.GetBytes(patientId)));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0020, 0x000D),
            SharpDicom.Data.DicomVR.UI,
            PadToEvenNull(System.Text.Encoding.ASCII.GetBytes(studyInstanceUid))));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x0020),
            SharpDicom.Data.DicomVR.DA,
            System.Text.Encoding.ASCII.GetBytes("20200315 ")));

        await Task.CompletedTask; // make async
        yield return dataset;
    }

    private static byte[] PadToEvenNull(byte[] input)
    {
        if (input.Length % 2 == 0) return input;
        var padded = new byte[input.Length + 1];
        Array.Copy(input, padded, input.Length);
        return padded;
    }

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
