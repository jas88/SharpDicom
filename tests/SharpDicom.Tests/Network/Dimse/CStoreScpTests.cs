using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Tests.Network.Dimse;

/// <summary>
/// Tests for C-STORE SCP handler interfaces and configuration.
/// </summary>
[TestFixture]
public class CStoreScpTests
{
    #region CStoreHandlerMode Tests

    [Test]
    public void CStoreHandlerMode_BufferedIsDefaultValue()
    {
        // Buffered should be the first enum value (0)
        Assert.That((int)CStoreHandlerMode.Buffered, Is.EqualTo(0));
    }

    [Test]
    public void CStoreHandlerMode_HasBothValues()
    {
        Assert.That(CStoreHandlerMode.Buffered, Is.Not.EqualTo(CStoreHandlerMode.Streaming));
    }

    #endregion

    #region CStoreRequestContext Tests

    [Test]
    public void CStoreRequestContext_AllPropertiesSetFromConstructor()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.2");
        var sopInstanceUid = new DicomUID("1.2.3.4.5.6.7.8.9");

        var context = new CStoreRequestContext(
            callingAE: "SCU_AE",
            calledAE: "SCP_AE",
            sopClassUid: sopClassUid,
            sopInstanceUid: sopInstanceUid,
            messageId: 1234,
            presentationContextId: 3);

        Assert.Multiple(() =>
        {
            Assert.That(context.CallingAE, Is.EqualTo("SCU_AE"));
            Assert.That(context.CalledAE, Is.EqualTo("SCP_AE"));
            Assert.That(context.SOPClassUID, Is.EqualTo(sopClassUid));
            Assert.That(context.SOPInstanceUID, Is.EqualTo(sopInstanceUid));
            Assert.That(context.MessageID, Is.EqualTo(1234));
            Assert.That(context.PresentationContextId, Is.EqualTo(3));
        });
    }

    [Test]
    public void CStoreRequestContext_SOPClassUIDAccessible()
    {
        var sopClassUid = new DicomUID("1.2.840.10008.5.1.4.1.1.7");
        var context = new CStoreRequestContext(
            "SCU", "SCP", sopClassUid, new DicomUID("1.2.3"), 1, 1);

        Assert.That(context.SOPClassUID.ToString(), Is.EqualTo("1.2.840.10008.5.1.4.1.1.7"));
    }

    [Test]
    public void CStoreRequestContext_SOPInstanceUIDAccessible()
    {
        var sopInstanceUid = new DicomUID("1.2.3.4.5.6.7.8.9.10.11.12");
        var context = new CStoreRequestContext(
            "SCU", "SCP", new DicomUID("1.2.3"), sopInstanceUid, 1, 1);

        Assert.That(context.SOPInstanceUID.ToString(), Is.EqualTo("1.2.3.4.5.6.7.8.9.10.11.12"));
    }

    #endregion

    #region DicomServerOptions C-STORE Tests

    [Test]
    public void DicomServerOptions_StoreHandlerModeDefaultIsBuffered()
    {
        var options = new DicomServerOptions { AETitle = "TEST" };
        Assert.That(options.StoreHandlerMode, Is.EqualTo(CStoreHandlerMode.Buffered));
    }

    [Test]
    public void DicomServerOptions_MaxBufferedDatasetSizeDefaultIs512MB()
    {
        var options = new DicomServerOptions { AETitle = "TEST" };
        Assert.That(options.MaxBufferedDatasetSize, Is.EqualTo(512 * 1024 * 1024));
    }

    [Test]
    public void DicomServerOptions_CStoreHandlerPropertySettable()
    {
        var handler = new MockCStoreHandler();
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            CStoreHandler = handler
        };

        Assert.That(options.CStoreHandler, Is.SameAs(handler));
    }

    [Test]
    public void DicomServerOptions_StreamingCStoreHandlerPropertySettable()
    {
        var handler = new MockStreamingCStoreHandler();
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            StoreHandlerMode = CStoreHandlerMode.Streaming,
            StreamingCStoreHandler = handler
        };

        Assert.That(options.StreamingCStoreHandler, Is.SameAs(handler));
    }

    [Test]
    public void DicomServerOptions_OnCStoreRequestDelegateSettable()
    {
        DicomStatus handlerResult = DicomStatus.Success;
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            OnCStoreRequest = (ctx, ds, ct) => new ValueTask<DicomStatus>(handlerResult)
        };

        Assert.That(options.OnCStoreRequest, Is.Not.Null);
    }

    [Test]
    public void DicomServerOptions_HasCStoreHandler_TrueWhenDelegateSet()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            OnCStoreRequest = (ctx, ds, ct) => new ValueTask<DicomStatus>(DicomStatus.Success)
        };

        Assert.That(options.HasCStoreHandler, Is.True);
    }

    [Test]
    public void DicomServerOptions_HasCStoreHandler_TrueWhenInterfaceHandlerSet()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            CStoreHandler = new MockCStoreHandler()
        };

        Assert.That(options.HasCStoreHandler, Is.True);
    }

    [Test]
    public void DicomServerOptions_HasCStoreHandler_TrueWhenStreamingHandlerSet()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            StoreHandlerMode = CStoreHandlerMode.Streaming,
            StreamingCStoreHandler = new MockStreamingCStoreHandler()
        };

        Assert.That(options.HasCStoreHandler, Is.True);
    }

    [Test]
    public void DicomServerOptions_HasCStoreHandler_FalseWhenNoHandlerSet()
    {
        var options = new DicomServerOptions { AETitle = "TEST" };
        Assert.That(options.HasCStoreHandler, Is.False);
    }

    [Test]
    public void DicomServerOptions_Validate_ThrowsWhenStreamingModeButNoHandler()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            StoreHandlerMode = CStoreHandlerMode.Streaming
            // No StreamingCStoreHandler set
        };

        Assert.Throws<System.ArgumentException>(() => options.Validate());
    }

    [Test]
    public void DicomServerOptions_Validate_SucceedsWithStreamingModeAndHandler()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            StoreHandlerMode = CStoreHandlerMode.Streaming,
            StreamingCStoreHandler = new MockStreamingCStoreHandler()
        };

        Assert.DoesNotThrow(() => options.Validate());
    }

    [Test]
    public void DicomServerOptions_Validate_ThrowsWhenMaxBufferedDatasetSizeIsZero()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            MaxBufferedDatasetSize = 0
        };

        Assert.Throws<System.ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Test]
    public void DicomServerOptions_Validate_ThrowsWhenMaxBufferedDatasetSizeIsNegative()
    {
        var options = new DicomServerOptions
        {
            AETitle = "TEST",
            MaxBufferedDatasetSize = -1
        };

        Assert.Throws<System.ArgumentOutOfRangeException>(() => options.Validate());
    }

    #endregion

    #region ICStoreHandler Tests

    [Test]
    public void ICStoreHandler_InterfaceMethodSignatureCorrect()
    {
        // Verify that the interface method signature is as expected
        var handler = new MockCStoreHandler();
        var context = new CStoreRequestContext(
            "SCU", "SCP",
            new DicomUID("1.2.3"),
            new DicomUID("4.5.6"),
            1, 1);
        var dataset = new DicomDataset();

        // Should be callable
        var result = handler.OnCStoreAsync(context, dataset, CancellationToken.None);
        Assert.That(result.IsCompleted, Is.True);
    }

    [Test]
    public async Task ICStoreHandler_CanCreateMockImplementation()
    {
        var handler = new MockCStoreHandler { ExpectedStatus = DicomStatus.Success };
        var context = new CStoreRequestContext(
            "SCU", "SCP",
            new DicomUID("1.2.3"),
            new DicomUID("4.5.6"),
            1, 1);

        var status = await handler.OnCStoreAsync(context, new DicomDataset(), CancellationToken.None);
        Assert.That(status, Is.EqualTo(DicomStatus.Success));
    }

    #endregion

    #region IStreamingCStoreHandler Tests

    [Test]
    public void IStreamingCStoreHandler_InterfaceMethodSignatureCorrect()
    {
        var handler = new MockStreamingCStoreHandler();
        var context = new CStoreRequestContext(
            "SCU", "SCP",
            new DicomUID("1.2.3"),
            new DicomUID("4.5.6"),
            1, 1);
        var dataset = new DicomDataset();
        using var stream = new System.IO.MemoryStream();

        // Should be callable
        var result = handler.OnCStoreStreamingAsync(context, dataset, stream, CancellationToken.None);
        Assert.That(result.IsCompleted, Is.True);
    }

    [Test]
    public async Task IStreamingCStoreHandler_CanCreateMockImplementation()
    {
        var handler = new MockStreamingCStoreHandler { ExpectedStatus = DicomStatus.Success };
        var context = new CStoreRequestContext(
            "SCU", "SCP",
            new DicomUID("1.2.3"),
            new DicomUID("4.5.6"),
            1, 1);
        using var stream = new System.IO.MemoryStream();

        var status = await handler.OnCStoreStreamingAsync(
            context, new DicomDataset(), stream, CancellationToken.None);
        Assert.That(status, Is.EqualTo(DicomStatus.Success));
    }

    #endregion

    #region Mock Implementations

    private sealed class MockCStoreHandler : ICStoreHandler
    {
        public DicomStatus ExpectedStatus { get; set; } = DicomStatus.Success;
        public CStoreRequestContext? LastContext { get; private set; }
        public DicomDataset? LastDataset { get; private set; }

        public ValueTask<DicomStatus> OnCStoreAsync(
            CStoreRequestContext context,
            DicomDataset dataset,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            LastDataset = dataset;
            return new ValueTask<DicomStatus>(ExpectedStatus);
        }
    }

    private sealed class MockStreamingCStoreHandler : IStreamingCStoreHandler
    {
        public DicomStatus ExpectedStatus { get; set; } = DicomStatus.Success;
        public CStoreRequestContext? LastContext { get; private set; }
        public DicomDataset? LastMetadata { get; private set; }

        public ValueTask<DicomStatus> OnCStoreStreamingAsync(
            CStoreRequestContext context,
            DicomDataset metadata,
            System.IO.Stream pixelDataStream,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            LastMetadata = metadata;
            // In a real implementation, would read from pixelDataStream
            return new ValueTask<DicomStatus>(ExpectedStatus);
        }
    }

    #endregion
}
