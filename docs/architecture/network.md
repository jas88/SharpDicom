# Network Layer

DICOM networking implementation following Part 8 of the DICOM standard.

## Client (SCU)

```csharp
public sealed class DicomClient : IAsyncDisposable
{
    public DicomClient(DicomClientOptions options);

    public ValueTask<DicomAssociation> ConnectAsync(CancellationToken ct = default);

    // DIMSE operations
    public ValueTask<DicomResponse> CEchoAsync(CancellationToken ct = default);
    public ValueTask<DicomResponse> CStoreAsync(DicomFile file, CancellationToken ct = default);
    public IAsyncEnumerable<DicomDataset> CFindAsync(DicomDataset query, CancellationToken ct = default);
    public IAsyncEnumerable<DicomResponse> CMoveAsync(DicomDataset query, string destinationAE, CancellationToken ct = default);
}

public class DicomClientOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string CallingAE { get; init; }
    public required string CalledAE { get; init; }

    // TLS (optional)
    public bool UseTls { get; init; } = false;
    public SslClientAuthenticationOptions? TlsOptions { get; init; }

    // Timeouts
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan AssociationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DimseTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
```

## Server (SCP)

Event-based handlers:

```csharp
public sealed class DicomServer : IAsyncDisposable
{
    public DicomServer(DicomServerOptions options);

    public ValueTask StartAsync(CancellationToken ct = default);
    public ValueTask StopAsync(CancellationToken ct = default);

    // Association events
    public event Func<AssociationRequest, ValueTask<AssociationResponse>>? OnAssociationRequest;
    public event Func<DicomAssociation, ValueTask>? OnAssociationReleased;

    // DIMSE events
    public event Func<CEchoRequest, ValueTask<DicomStatus>>? OnCEcho;
    public event Func<CStoreRequest, ValueTask<DicomStatus>>? OnCStore;
    public event Func<CFindRequest, IAsyncEnumerable<DicomDataset>>? OnCFind;
    public event Func<CMoveRequest, IAsyncEnumerable<DicomStatus>>? OnCMove;
}

public class DicomServerOptions
{
    public required int Port { get; init; }
    public required string AeTitle { get; init; }

    // TLS (optional)
    public bool UseTls { get; init; } = false;
    public SslServerAuthenticationOptions? TlsOptions { get; init; }

    // Limits
    public int MaxAssociations { get; init; } = 100;
    public int MaxPduSize { get; init; } = 16384;
}
```

## C-STORE Streaming

Handler decides storage destination:

```csharp
public sealed class CStoreRequest
{
    public DicomAssociation Association { get; }
    public DicomUID SopClassUID { get; }
    public DicomUID SopInstanceUID { get; }
    public TransferSyntax TransferSyntax { get; }
    public DicomDataset Metadata { get; }         // Elements before pixel data

    // Stream to destination (disk, memory, etc.)
    public ValueTask CopyToAsync(Stream destination, CancellationToken ct = default);

    // Or load as DicomFile if small enough
    public ValueTask<DicomFile> ToFileAsync(CancellationToken ct = default);
}

// Example: stream to disk
server.OnCStore = async request =>
{
    var path = $"/storage/{request.SopInstanceUID}.dcm";
    await using var fs = File.Create(path);
    await request.CopyToAsync(fs);
    return DicomStatus.Success;
};

// Example: load to memory
server.OnCStore = async request =>
{
    var file = await request.ToFileAsync();
    await _database.IndexAsync(file);
    return DicomStatus.Success;
};
```

## DicomStatus

```csharp
public readonly struct DicomStatus
{
    public ushort Code { get; }
    public StatusCategory Category { get; }
    public string? ErrorComment { get; init; }

    public static readonly DicomStatus Success = new(0x0000);
    public static readonly DicomStatus Pending = new(0xFF00);
    public static readonly DicomStatus Cancel = new(0xFE00);
    public static readonly DicomStatus NoSuchSOPClass = new(0x0118);
    public static readonly DicomStatus StorageOutOfResources = new(0xA700);
    // ... all standard status codes
}

public enum StatusCategory { Success, Pending, Warning, Failure, Cancel }
```

## PDU (Protocol Data Unit)

### PDU Types (Part 8)

| Type | Name | Direction |
|------|------|-----------|
| 0x01 | A-ASSOCIATE-RQ | SCU → SCP |
| 0x02 | A-ASSOCIATE-AC | SCP → SCU |
| 0x03 | A-ASSOCIATE-RJ | SCP → SCU |
| 0x04 | P-DATA-TF | Both |
| 0x05 | A-RELEASE-RQ | Both |
| 0x06 | A-RELEASE-RP | Both |
| 0x07 | A-ABORT | Both |

### PDU Size

1MB default (larger = fewer round trips = better throughput):

```csharp
public class DicomClientOptions
{
    public int MaxPduSize { get; init; } = 1024 * 1024;  // 1MB default
    public int MinPduSize { get; init; } = 4096;         // 4KB minimum (DICOM required)
}
```

### PDU Base Structure

```csharp
public abstract class Pdu
{
    public abstract PduType Type { get; }
    internal ReadOnlyMemory<byte> RawBuffer { get; }  // Zero-copy slice

    public int GetEncodedLength();
    public void WriteTo(IBufferWriter<byte> writer);

    // Explicit copy when escaping scope
    public abstract Pdu ToOwned();
}

public enum PduType : byte
{
    AssociateRequest = 0x01,
    AssociateAccept = 0x02,
    AssociateReject = 0x03,
    PData = 0x04,
    ReleaseRequest = 0x05,
    ReleaseResponse = 0x06,
    Abort = 0x07
}
```

### P-DATA PDU

Main data transfer:

```csharp
public sealed class PDataPdu : Pdu
{
    public override PduType Type => PduType.PData;
    public IReadOnlyList<PresentationDataValue> Values { get; }

    public override PDataPdu ToOwned()
    {
        var copy = new byte[RawBuffer.Length];
        RawBuffer.CopyTo(copy);
        return new PDataPdu(copy, Values);
    }
}

public readonly struct PresentationDataValue
{
    public byte PresentationContextId { get; }
    public bool IsCommand { get; }
    public bool IsLastFragment { get; }
    public ReadOnlyMemory<byte> Data { get; }  // Slice of pooled buffer

    public PresentationDataValue ToOwned()
        => new(PresentationContextId, IsCommand, IsLastFragment, Data.ToArray());
}
```

### Association Negotiation PDUs

```csharp
public sealed class AssociateRequestPdu : Pdu
{
    public string CalledAE { get; }
    public string CallingAE { get; }
    public IReadOnlyList<PresentationContext> PresentationContexts { get; }
    public int MaxPduLength { get; }
    public string ImplementationClassUID { get; }
    public string? ImplementationVersionName { get; }
}

public sealed class AssociateAcceptPdu : Pdu
{
    public IReadOnlyList<PresentationContextResult> PresentationContexts { get; }
    public int MaxPduLength { get; }  // min(requested, supported)
}

public sealed class PresentationContext
{
    public byte Id { get; }                                   // Odd 1-255
    public DicomUID AbstractSyntax { get; }                   // SOP Class
    public IReadOnlyList<TransferSyntax> TransferSyntaxes { get; }
}

public sealed class PresentationContextResult
{
    public byte Id { get; }
    public PresentationContextResultReason Result { get; }
    public TransferSyntax? AcceptedTransferSyntax { get; }
}

public enum PresentationContextResultReason : byte
{
    Acceptance = 0,
    UserRejection = 1,
    NoReason = 2,
    AbstractSyntaxNotSupported = 3,
    TransferSyntaxesNotSupported = 4
}
```

## Zero-Copy Parsing

PDUs reference pooled buffer - valid until next Read:

```csharp
// PDUs reference pooled buffer - valid until next Read
await foreach (var pdu in connection.ReadPdusAsync(ct))
{
    await ProcessAsync(pdu);  // Use immediately, no copy
}   // Buffer recycled

// Explicit copy when data must outlive scope
PDataPdu? saved = null;
await foreach (var pdu in connection.ReadPdusAsync(ct))
{
    if (NeedToKeep(pdu))
        saved = pdu.ToOwned();  // Copy to owned buffer
}
// saved still valid
```

DicomDataset follows same pattern:

```csharp
public sealed class DicomDataset
{
    // Deep-copy all elements to owned buffers
    public DicomDataset ToOwned()
    {
        var copy = new DicomDataset();
        foreach (var element in this)
            copy.Add(element.ToOwned());
        return copy;
    }
}
```

## Network Exceptions

```csharp
public class DicomNetworkException : DicomException
{
    public string? RemoteHost { get; init; }
    public int? RemotePort { get; init; }

    public DicomNetworkException(string message) : base(message) { }
    public DicomNetworkException(string message, Exception inner) : base(message, inner) { }
}

public class DicomAssociationException : DicomNetworkException
{
    public AssociationRejectReason? RejectReason { get; init; }
    public AssociationRejectSource? RejectSource { get; init; }

    public DicomAssociationException(string message) : base(message) { }
}

public class DicomAbortException : DicomNetworkException
{
    public AbortSource Source { get; }
    public AbortReason Reason { get; }

    public DicomAbortException(AbortSource source, AbortReason reason)
        : base($"Association aborted: {source} - {reason}")
    {
        Source = source;
        Reason = reason;
    }
}

public class DicomDimseException : DicomNetworkException
{
    public DicomStatus Status { get; }

    public DicomDimseException(DicomStatus status)
        : base($"DIMSE operation failed: {status}")
        => Status = status;
}
```
