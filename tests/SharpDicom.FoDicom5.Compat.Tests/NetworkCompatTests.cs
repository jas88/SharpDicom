using System;
using System.Threading.Tasks;
using NUnit.Framework;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace SharpDicom.FoDicom5.Compat.Tests;

/// <summary>
/// Tests for network compat types: DicomClient, DicomClientFactory, DicomCFindRequest,
/// DicomQueryRetrieveLevel, and DicomStatus.
/// </summary>
[TestFixture]
public class NetworkCompatTests
{
    [Test]
    public void DicomClientFactory_Create_ReturnsNonNull()
    {
        var client = DicomClientFactory.Create("localhost", 104, false, "CALLING", "CALLED");
        Assert.That(client, Is.Not.Null);
        Assert.That(client, Is.InstanceOf<IDicomClient>());
    }

    [Test]
    public async Task AddRequestAsync_BuffersRequest()
    {
        var client = DicomClientFactory.Create("localhost", 104, false, "CALLING", "CALLED");
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        await client.AddRequestAsync(request);

        // Client should not be busy until SendAsync is called
        Assert.That(client.IsBusy, Is.False);
    }

    [Test]
    public void DicomCFindRequest_Constructor_SetsLevelAndEmptyDataset()
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        Assert.That(request.Level, Is.EqualTo(DicomQueryRetrieveLevel.Study));
        Assert.That(request.Dataset, Is.Not.Null);
        Assert.That(request.Type, Is.EqualTo(DicomRequestType.CFind));
    }

    [Test]
    public void DicomCFindRequest_Dataset_AddOrUpdate_StoresQueryKeys()
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        request.Dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientName, "Smith*");
        request.Dataset.AddOrUpdate(FellowOakDicom.DicomTag.StudyDate, "20200101-20201231");

        Assert.That(request.Dataset.Contains(FellowOakDicom.DicomTag.PatientName), Is.True);
        Assert.That(request.Dataset.Contains(FellowOakDicom.DicomTag.StudyDate), Is.True);
        Assert.That(request.Dataset.GetString(FellowOakDicom.DicomTag.PatientName), Is.EqualTo("Smith*"));
    }

    [Test]
    public void DicomCFindRequest_OnResponseReceived_IsStoredAndAccessible()
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        Assert.That(request.OnResponseReceived, Is.Null);

        var callCount = 0;
        request.OnResponseReceived = (req, resp) => callCount++;

        Assert.That(request.OnResponseReceived, Is.Not.Null);

        // Invoke the delegate to verify it works
        var status = new DicomStatus(0x0000, DicomState.Success);
        request.OnResponseReceived(request, new DicomCFindResponse(status));
        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void DicomQueryRetrieveLevel_HasAllExpectedValues()
    {
        Assert.That(Enum.IsDefined<DicomQueryRetrieveLevel>(DicomQueryRetrieveLevel.Patient), Is.True);
        Assert.That(Enum.IsDefined<DicomQueryRetrieveLevel>(DicomQueryRetrieveLevel.Study), Is.True);
        Assert.That(Enum.IsDefined<DicomQueryRetrieveLevel>(DicomQueryRetrieveLevel.Series), Is.True);
        Assert.That(Enum.IsDefined<DicomQueryRetrieveLevel>(DicomQueryRetrieveLevel.Image), Is.True);
    }

    [Test]
    public void DicomCFindRequest_AllLevels_CanBeCreated()
    {
        foreach (var level in Enum.GetValues<DicomQueryRetrieveLevel>())
        {
            var request = new DicomCFindRequest(level);
            Assert.That(request.Level, Is.EqualTo(level));
        }
    }

    [Test]
    public void DicomStatus_WellKnownStatuses_HaveCorrectCodes()
    {
        Assert.That(DicomStatus.Success.Code, Is.EqualTo((ushort)0x0000));
        Assert.That(DicomStatus.Success.State, Is.EqualTo(DicomState.Success));
        Assert.That(DicomStatus.Success.IsSuccess, Is.True);

        Assert.That(DicomStatus.Pending.Code, Is.EqualTo((ushort)0xFF00));
        Assert.That(DicomStatus.Pending.State, Is.EqualTo(DicomState.Pending));
        Assert.That(DicomStatus.Pending.IsPending, Is.True);

        Assert.That(DicomStatus.Cancel.Code, Is.EqualTo((ushort)0xFE00));
        Assert.That(DicomStatus.Cancel.State, Is.EqualTo(DicomState.Cancel));
    }

    [Test]
    public void DicomStatus_BooleanHelpers_WorkCorrectly()
    {
        var success = new DicomStatus(0x0000, DicomState.Success);
        Assert.That(success.IsSuccess, Is.True);
        Assert.That(success.IsPending, Is.False);
        Assert.That(success.IsWarning, Is.False);
        Assert.That(success.IsFailure, Is.False);

        var failure = new DicomStatus(0xC001, DicomState.Failure);
        Assert.That(failure.IsFailure, Is.True);
        Assert.That(failure.IsSuccess, Is.False);
    }

    [Test]
    public void DicomCFindResponse_Constructor_SetsStatusAndDataset()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "TEST123");

        var response = new DicomCFindResponse(DicomStatus.Pending, dataset);

        Assert.That(response.Status, Is.SameAs(DicomStatus.Pending));
        Assert.That(response.Dataset, Is.Not.Null);
        Assert.That(response.Dataset!.GetString(FellowOakDicom.DicomTag.PatientID), Is.EqualTo("TEST123"));
    }

    [Test]
    public void DicomCFindResponse_FinalResponse_HasNullDataset()
    {
        var response = new DicomCFindResponse(DicomStatus.Success);

        Assert.That(response.Status, Is.SameAs(DicomStatus.Success));
        Assert.That(response.Dataset, Is.Null);
    }

    [Test]
    public async Task NegotiateAsyncOps_DefaultValues_CompletesWithoutError()
    {
        var client = DicomClientFactory.Create("localhost", 104, false, "CALLING", "CALLED");
        await client.NegotiateAsyncOps();
        // Default (0,0) means 1:1 window — accepted without negotiation
        Assert.Pass();
    }

    [Test]
    public async Task NegotiateAsyncOps_NonZeroValues_StoresValues()
    {
        var client = DicomClientFactory.Create("localhost", 104, false, "CALLING", "CALLED");
        await client.NegotiateAsyncOps(5, 3);

        // Verify values were stored via reflection on the concrete type
        var clientType = client.GetType();
        var invokedField = clientType.GetField("_asyncOpsInvoked",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var performedField = clientType.GetField("_asyncOpsPerformed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.That(invokedField, Is.Not.Null, "Field _asyncOpsInvoked not found");
        Assert.That(performedField, Is.Not.Null, "Field _asyncOpsPerformed not found");
        Assert.That(invokedField!.GetValue(client), Is.EqualTo((ushort)5));
        Assert.That(performedField!.GetValue(client), Is.EqualTo((ushort)3));
    }

    [Test]
    public void DicomRequest_Type_ReturnsCorrectValue()
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Patient);
        Assert.That(request.Type, Is.EqualTo(DicomRequestType.CFind));
    }

    [Test]
    public async Task SendAsync_WithNoRequests_CompletesImmediately()
    {
        var client = DicomClientFactory.Create("localhost", 104, false, "CALLING", "CALLED");
        // SendAsync with no buffered requests should return immediately
        await client.SendAsync();
        Assert.That(client.IsBusy, Is.False);
    }

    [Test]
    public void DicomCFindRequest_Dataset_CanBeReassigned()
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        var newDataset = new FellowOakDicom.DicomDataset();
        newDataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "NEW");

        request.Dataset = newDataset;

        Assert.That(request.Dataset.GetString(FellowOakDicom.DicomTag.PatientID), Is.EqualTo("NEW"));
    }

    [Test]
    public void DicomStatus_ToString_ReturnsFormattedString()
    {
        var status = new DicomStatus(0xFF00, DicomState.Pending);
        Assert.That(status.ToString(), Does.Contain("FF00"));
        Assert.That(status.ToString(), Does.Contain("Pending"));
    }
}
