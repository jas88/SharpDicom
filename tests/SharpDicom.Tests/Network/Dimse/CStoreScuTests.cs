using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Tests.Network.Dimse;

/// <summary>
/// Unit tests for CStoreScu, CStoreOptions, and CStoreResponse.
/// </summary>
[TestFixture]
public class CStoreScuTests
{
    #region Constructor Tests

    [Test]
    public void CStoreScu_NullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CStoreScu(null!));
    }

    [Test]
    public void CStoreScu_NullOptions_UsesDefault()
    {
        var clientOptions = new DicomClientOptions { Host = "localhost", Port = 104, CalledAE = "CALLED", CallingAE = "CALLING" };
        var client = new DicomClient(clientOptions);

        var scu = new CStoreScu(client, null);

        Assert.That(scu.Options, Is.SameAs(CStoreOptions.Default));
    }

    [Test]
    public void CStoreScu_CustomOptions_StoresOptions()
    {
        var clientOptions = new DicomClientOptions { Host = "localhost", Port = 104, CalledAE = "CALLED", CallingAE = "CALLING" };
        var client = new DicomClient(clientOptions);
        var storeOptions = new CStoreOptions { Priority = 1 };

        var scu = new CStoreScu(client, storeOptions);

        Assert.That(scu.Options, Is.SameAs(storeOptions));
        Assert.That(scu.Options.Priority, Is.EqualTo(1));
    }

    #endregion

    #region CStoreOptions Tests

    [Test]
    public void CStoreOptions_Default_HasExpectedValues()
    {
        var options = CStoreOptions.Default;

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(options.Priority, Is.EqualTo(0));
        Assert.That(options.MaxRetries, Is.EqualTo(0));
        Assert.That(options.RetryDelay, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(options.ReportProgress, Is.True);
    }

    [Test]
    public void CStoreOptions_Properties_AreSettable()
    {
        var options = new CStoreOptions
        {
            Timeout = TimeSpan.FromMinutes(2),
            Priority = 1,
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromSeconds(5),
            ReportProgress = false
        };

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(options.Priority, Is.EqualTo(1));
        Assert.That(options.MaxRetries, Is.EqualTo(3));
        Assert.That(options.RetryDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(options.ReportProgress, Is.False);
    }

    [Test]
    public void CStoreOptions_Default_IsSingleton()
    {
        Assert.That(CStoreOptions.Default, Is.SameAs(CStoreOptions.Default));
    }

    #endregion

    #region CStoreResponse Tests

    [Test]
    public void CStoreResponse_Success_IsSuccessTrue()
    {
        var response = new CStoreResponse(
            DicomStatus.Success,
            new DicomUID("1.2.3"),
            new DicomUID("1.2.3.4"));

        Assert.That(response.IsSuccess, Is.True);
        Assert.That(response.IsWarning, Is.False);
        Assert.That(response.IsFailure, Is.False);
        Assert.That(response.IsSuccessOrWarning, Is.True);
    }

    [Test]
    public void CStoreResponse_Warning_IsWarningTrue()
    {
        var response = new CStoreResponse(
            DicomStatus.CoercionOfDataElements,
            new DicomUID("1.2.3"),
            new DicomUID("1.2.3.4"));

        Assert.That(response.IsSuccess, Is.False);
        Assert.That(response.IsWarning, Is.True);
        Assert.That(response.IsFailure, Is.False);
        Assert.That(response.IsSuccessOrWarning, Is.True);
    }

    [Test]
    public void CStoreResponse_Failure_IsFailureTrue()
    {
        var response = new CStoreResponse(
            DicomStatus.ProcessingFailure,
            new DicomUID("1.2.3"),
            new DicomUID("1.2.3.4"));

        Assert.That(response.IsSuccess, Is.False);
        Assert.That(response.IsWarning, Is.False);
        Assert.That(response.IsFailure, Is.True);
        Assert.That(response.IsSuccessOrWarning, Is.False);
    }

    [Test]
    public void CStoreResponse_Properties_SetCorrectly()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4.5.6.7.8.9");
        var errorComment = "Test error";

        var response = new CStoreResponse(
            DicomStatus.ProcessingFailure,
            sopClassUid,
            sopInstanceUid,
            errorComment);

        Assert.That(response.Status, Is.EqualTo(DicomStatus.ProcessingFailure));
        Assert.That(response.SOPClassUID, Is.EqualTo(sopClassUid));
        Assert.That(response.SOPInstanceUID, Is.EqualTo(sopInstanceUid));
        Assert.That(response.ErrorComment, Is.EqualTo(errorComment));
    }

    [Test]
    public void CStoreResponse_NullErrorComment_AllowedForSuccess()
    {
        var response = new CStoreResponse(
            DicomStatus.Success,
            new DicomUID("1.2.3"),
            new DicomUID("1.2.3.4"));

        Assert.That(response.ErrorComment, Is.Null);
    }

    [Test]
    public void CStoreResponse_ToString_ContainsStatus()
    {
        var response = new CStoreResponse(
            DicomStatus.Success,
            new DicomUID("1.2.3"),
            new DicomUID("1.2.3.4.5"));

        var str = response.ToString();

        Assert.That(str, Does.Contain("Success"));
        Assert.That(str, Does.Contain("1.2.3.4.5"));
    }

    #endregion

    #region DicomCommand C-STORE Tests

    [Test]
    public void CreateCStoreRequest_IncludesSOPClassUID()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

        Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(sopClassUid));
    }

    [Test]
    public void CreateCStoreRequest_IncludesSOPInstanceUID()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4.5.6");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

        Assert.That(cmd.AffectedSOPInstanceUID, Is.EqualTo(sopInstanceUid));
    }

    [Test]
    public void CreateCStoreRequest_IncludesPriority()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid, priority: 1);

        Assert.That(cmd.Priority, Is.EqualTo(1));
    }

    [Test]
    public void CreateCStoreRequest_DefaultPriority_IsMedium()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

        Assert.That(cmd.Priority, Is.EqualTo(0)); // MEDIUM
    }

    [Test]
    public void CreateCStoreRequest_HasDataset()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

        Assert.That(cmd.HasDataset, Is.True);
    }

    [Test]
    public void CreateCStoreRequest_IsCStoreRequest()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreRequest(1, sopClassUid, sopInstanceUid);

        Assert.That(cmd.IsCStoreRequest, Is.True);
        Assert.That(cmd.IsCStoreResponse, Is.False);
        Assert.That(cmd.IsRequest, Is.True);
        Assert.That(cmd.IsResponse, Is.False);
    }

    [Test]
    public void CreateCStoreRequest_MessageID_SetCorrectly()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd1 = DicomCommand.CreateCStoreRequest(42, sopClassUid, sopInstanceUid);
        var cmd2 = DicomCommand.CreateCStoreRequest(100, sopClassUid, sopInstanceUid);

        Assert.That(cmd1.MessageID, Is.EqualTo(42));
        Assert.That(cmd2.MessageID, Is.EqualTo(100));
    }

    [Test]
    public void CreateCStoreResponse_Success_HasNoDataset()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreResponse(1, sopClassUid, sopInstanceUid, DicomStatus.Success);

        Assert.That(cmd.HasDataset, Is.False);
    }

    [Test]
    public void CreateCStoreResponse_IsCStoreResponse()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmd = DicomCommand.CreateCStoreResponse(1, sopClassUid, sopInstanceUid, DicomStatus.Success);

        Assert.That(cmd.IsCStoreResponse, Is.True);
        Assert.That(cmd.IsCStoreRequest, Is.False);
        Assert.That(cmd.IsResponse, Is.True);
        Assert.That(cmd.IsRequest, Is.False);
    }

    [Test]
    public void CreateCStoreResponse_Status_SetCorrectly()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4");

        var cmdSuccess = DicomCommand.CreateCStoreResponse(1, sopClassUid, sopInstanceUid, DicomStatus.Success);
        var cmdWarning = DicomCommand.CreateCStoreResponse(1, sopClassUid, sopInstanceUid, DicomStatus.CoercionOfDataElements);

        Assert.That(cmdSuccess.Status.IsSuccess, Is.True);
        Assert.That(cmdWarning.Status.IsWarning, Is.True);
    }

    #endregion

    #region Message ID Increment Tests

    [Test]
    public void CStoreScu_NextMessageId_Increments()
    {
        var clientOptions = new DicomClientOptions { Host = "localhost", Port = 104, CalledAE = "CALLED", CallingAE = "CALLING" };
        var client = new DicomClient(clientOptions);
        var scu = new CStoreScu(client);

        var id1 = scu.NextMessageId();
        var id2 = scu.NextMessageId();
        var id3 = scu.NextMessageId();

        Assert.That(id1, Is.EqualTo(1));
        Assert.That(id2, Is.EqualTo(2));
        Assert.That(id3, Is.EqualTo(3));
    }

    #endregion
}
