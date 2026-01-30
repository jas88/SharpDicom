# Phase 10: Network Foundation - Research

**Researched:** 2026-01-27
**Domain:** DICOM Network Protocol (Part 8), TCP Socket Programming
**Confidence:** HIGH

## Summary

This phase establishes the DICOM networking infrastructure for SharpDicom, implementing the Upper Layer Protocol defined in DICOM PS3.8. The implementation requires PDU parsing/building for seven PDU types, a 13-state association state machine, and the C-ECHO verification service (both SCU and SCP).

The standard approach for .NET DICOM networking uses `TcpClient`/`TcpListener` with `async`/`await` patterns, Task-per-association for server handling, and structured logging via `Microsoft.Extensions.Logging`. The existing codebase architecture (Span<T>-first, ref structs, zero-copy) should be extended to PDU parsing.

**Primary recommendation:** Follow the existing `DicomStreamReader` ref struct pattern for PDU parsing, use standard .NET socket APIs with async/await, implement the DICOM state machine as a simple class-based state pattern, and validate against DCMTK tools.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Net.Sockets` | Built-in | TCP connectivity | Standard .NET API, TcpClient/TcpListener with async |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.0 | Logging interface | Zero-dependency logging for libraries |
| `System.Threading.Channels` | Built-in (net8+) | Producer-consumer queues | High-perf async coordination |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.IO.Pipelines` | Built-in (net8+) | High-performance streaming | Zero-copy PDU parsing (optional, per FR-10.12) |
| `System.Buffers` | Built-in | ArrayPool, IBufferWriter | Buffer pooling |
| `NullLogger<T>` | M.E.Logging.Abstractions | No-op logger | Default when consumer doesn't configure logging |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| TcpClient/TcpListener | Raw Socket | More control but more complex |
| Task-per-association | SocketAsyncEventArgs | Higher performance but complex pooling |
| System.IO.Pipelines | NetworkStream + ArrayPool | Pipelines more elegant but adds learning curve |

**Installation:**
```bash
# Production dependencies (abstractions only)
dotnet add package Microsoft.Extensions.Logging.Abstractions

# Already built-in for net8.0+:
# - System.Net.Sockets
# - System.Threading.Channels
# - System.IO.Pipelines
# - System.Buffers
```

## Architecture Patterns

### Recommended Project Structure

```
src/SharpDicom/
├── Network/
│   ├── Pdu/                    # PDU types and parsing
│   │   ├── PduType.cs          # Enum for PDU types (0x01-0x07)
│   │   ├── Pdu.cs              # Abstract base class
│   │   ├── AssociateRequestPdu.cs
│   │   ├── AssociateAcceptPdu.cs
│   │   ├── AssociateRejectPdu.cs
│   │   ├── PDataPdu.cs
│   │   ├── ReleaseRequestPdu.cs
│   │   ├── ReleaseResponsePdu.cs
│   │   ├── AbortPdu.cs
│   │   ├── PduReader.cs        # Ref struct for parsing
│   │   └── PduWriter.cs        # Ref struct for building
│   ├── Items/                  # PDU variable items
│   │   ├── PresentationContext.cs
│   │   ├── PresentationContextResult.cs
│   │   ├── UserInformation.cs
│   │   └── PresentationDataValue.cs
│   ├── Association/
│   │   ├── DicomAssociation.cs # Association state machine
│   │   ├── AssociationState.cs # 13 states enum
│   │   ├── AssociationEvent.cs # Events enum
│   │   └── AssociationOptions.cs
│   ├── Dimse/
│   │   ├── DicomCommand.cs     # Command dataset wrapper
│   │   ├── DicomStatus.cs      # Status struct
│   │   └── DicomRequest.cs     # Base request class
│   ├── DicomClient.cs          # SCU implementation
│   ├── DicomClientOptions.cs
│   ├── DicomServer.cs          # SCP implementation
│   ├── DicomServerOptions.cs
│   └── Exceptions/
│       ├── DicomNetworkException.cs
│       ├── DicomAssociationException.cs
│       └── DicomAbortException.cs
```

### Pattern 1: PDU Parsing with Ref Struct (following DicomStreamReader)

**What:** Use ref struct for zero-allocation PDU header parsing
**When to use:** Always for initial PDU parsing from network buffer
**Example:**
```csharp
// Source: SharpDicom DicomStreamReader pattern + DICOM PS3.8 Section 9.3
public ref struct PduReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public PduReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public bool TryReadPduHeader(out PduType type, out uint length)
    {
        type = default;
        length = 0;

        if (_buffer.Length - _position < 6)
            return false;

        // PDU Type: byte 1
        type = (PduType)_buffer[_position];

        // Reserved: byte 2 (ignored)

        // PDU Length: bytes 3-6 (Big Endian per PS3.8)
        length = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_position + 2));

        _position += 6;
        return true;
    }

    // A-ASSOCIATE-RQ specific parsing
    public bool TryReadAssociateRequest(
        out ushort protocolVersion,
        out string calledAE,
        out string callingAE)
    {
        // Protocol-version: bytes 7-8
        // Reserved: bytes 9-10
        // Called-AE-title: bytes 11-26 (16 chars)
        // Calling-AE-title: bytes 27-42 (16 chars)
        // Reserved: bytes 43-74 (32 bytes)
        // Variable items: bytes 75+
    }
}
```

### Pattern 2: State Machine with Simple Class

**What:** Association state machine as class with current state and transition method
**When to use:** Managing DICOM association lifecycle
**Example:**
```csharp
// Source: DICOM PS3.8 Section 9.2
public enum AssociationState
{
    Sta1_Idle,
    Sta2_TransportConnectionOpen,
    Sta3_AwaitingLocalAssociateResponse,
    Sta4_AwaitingTransportConnectionOpen,
    Sta5_AwaitingAssociateAcOrRj,
    Sta6_AssociationEstablished,
    Sta7_AwaitingReleaseResponse,
    Sta8_AwaitingReleaseResponseLocalUser,
    Sta9_ReleaseCollisionRequestorSide,
    Sta10_ReleaseCollisionAcceptorSide,
    Sta11_ReleaseCollisionRequestorSide2,
    Sta12_ReleaseCollisionAcceptorSide2,
    Sta13_AwaitingTransportConnectionClose
}

public sealed class DicomAssociation
{
    private AssociationState _state = AssociationState.Sta1_Idle;
    private readonly Timer _artimTimer;
    private readonly ILogger<DicomAssociation> _logger;

    public AssociationState State => _state;

    internal void ProcessEvent(AssociationEvent evt, Pdu? pdu = null)
    {
        var (action, nextState) = GetTransition(_state, evt);
        _logger.LogDebug("Association transition: {State} + {Event} -> {NextState}",
            _state, evt, nextState);
        action?.Invoke(pdu);
        _state = nextState;
    }

    private (Action<Pdu?>?, AssociationState) GetTransition(
        AssociationState current, AssociationEvent evt)
    {
        // State transition table from PS3.8 Section 9.2.3
        return (current, evt) switch
        {
            (Sta1_Idle, AssociationEvent.AAssociateRq) =>
                (SendAssociateRq, Sta5_AwaitingAssociateAcOrRj),
            (Sta5_AwaitingAssociateAcOrRj, AssociationEvent.AssociateAcPduReceived) =>
                (AcceptAssociation, Sta6_AssociationEstablished),
            // ... full table
            _ => throw new DicomAssociationException($"Invalid transition: {current} + {evt}")
        };
    }
}
```

### Pattern 3: Task-per-Association Server

**What:** Each accepted connection spawns a Task to handle the association
**When to use:** DicomServer accepting multiple concurrent associations
**Example:**
```csharp
// Source: Microsoft Learn TCP/IP documentation
public sealed class DicomServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _activeTasks = new();

    public async Task StartAsync(CancellationToken ct = default)
    {
        _listener.Start();
        _logger.LogInformation("DICOM server listening on port {Port}", _options.Port);

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                var task = Task.Run(() => HandleAssociationAsync(client, ct), ct);
                lock (_activeTasks)
                    _activeTasks.Add(task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleAssociationAsync(TcpClient client, CancellationToken ct)
    {
        await using var _ = client;
        var association = new DicomAssociation(_options, _loggerFactory);

        try
        {
            await association.RunAsync(client.GetStream(), ct);
        }
        finally
        {
            lock (_activeTasks)
                _activeTasks.Remove(Task.CurrentTask!);
        }
    }

    public async ValueTask StopAsync(TimeSpan timeout)
    {
        _cts.Cancel();
        _listener.Stop();

        // Wait for active associations with timeout
        var completedTask = await Task.WhenAny(
            Task.WhenAll(_activeTasks.ToArray()),
            Task.Delay(timeout));

        // Abort remaining if timeout
        if (completedTask != Task.WhenAll(_activeTasks.ToArray()))
        {
            _logger.LogWarning("Forcing abort of {Count} associations", _activeTasks.Count);
            // Send A-ABORT to remaining
        }
    }
}
```

### Pattern 4: Async Client with CancellationToken

**What:** DicomClient with async methods and proper cancellation
**When to use:** SCU operations
**Example:**
```csharp
// Source: Microsoft Learn async patterns
public sealed class DicomClient : IAsyncDisposable
{
    private TcpClient? _tcp;
    private DicomAssociation? _association;

    public async ValueTask<DicomAssociation> ConnectAsync(CancellationToken ct = default)
    {
        _tcp = new TcpClient();

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ConnectionTimeout);

        await _tcp.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token);

        _association = new DicomAssociation(_options, _loggerFactory);
        await _association.NegotiateAsync(_tcp.GetStream(), ct);

        return _association;
    }

    public async ValueTask<DicomStatus> CEchoAsync(CancellationToken ct = default)
    {
        if (_association?.State != AssociationState.Sta6_AssociationEstablished)
            throw new InvalidOperationException("Not connected");

        using var timeoutCts = CancellationTokenSource
            .CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.DimseTimeout);

        return await _association.SendCEchoAsync(timeoutCts.Token);
    }
}
```

### Anti-Patterns to Avoid

- **Blocking calls in async methods:** Never use `.Result` or `.Wait()` on Tasks
- **Shared mutable state across associations:** Each association must have its own state
- **String concatenation in logging:** Use structured logging with parameters
- **Forgetting CancellationToken:** Every async method must accept and honor cancellation
- **Manual buffer management without pooling:** Always use ArrayPool for large buffers
- **Not validating odd Presentation Context IDs:** DICOM requires odd values 1-255

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Buffer pooling | Custom buffer manager | `ArrayPool<byte>.Shared` | Battle-tested, efficient |
| Async coordination | Custom locking | `Channel<T>`, `SemaphoreSlim` | Built-in, correct |
| Timeouts | Manual timer management | `CancellationTokenSource.CancelAfter` | Integrates with async |
| Logging | Custom logging | `ILogger<T>` from M.E.Logging | Industry standard |
| Big-endian parsing | Byte manipulation | `BinaryPrimitives.ReadUInt32BigEndian` | Built-in, optimized |
| String padding (AE titles) | Manual padding | `String.PadRight(16, ' ')` | Standard .NET |

**Key insight:** DICOM networking has many edge cases (fragmented PDUs, simultaneous release, ARTIM timeouts) that existing code patterns handle correctly. Focus implementation effort on DICOM-specific protocol logic, not generic infrastructure.

## Common Pitfalls

### Pitfall 1: PDU Fragmentation

**What goes wrong:** Assuming a single `ReadAsync` returns a complete PDU
**Why it happens:** TCP is a stream protocol; PDUs can arrive in fragments
**How to avoid:** Buffer incoming data until complete PDU length is received
**Warning signs:** Works locally but fails with real network latency/packet sizes

```csharp
// WRONG: Assumes complete PDU
var buffer = new byte[65536];
var read = await stream.ReadAsync(buffer);
var pdu = ParsePdu(buffer.AsSpan(0, read));

// CORRECT: Buffer until complete
private async ValueTask<Pdu> ReadPduAsync(Stream stream, CancellationToken ct)
{
    // Read 6-byte header first
    var header = new byte[6];
    await stream.ReadExactlyAsync(header, ct);

    var type = (PduType)header[0];
    var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));

    // Then read exact PDU body
    var body = ArrayPool<byte>.Shared.Rent((int)length);
    try
    {
        await stream.ReadExactlyAsync(body.AsMemory(0, (int)length), ct);
        return ParsePduBody(type, body.AsSpan(0, (int)length));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(body);
    }
}
```

### Pitfall 2: ARTIM Timer Misuse

**What goes wrong:** Using ARTIM for all timeouts instead of specific scenarios
**Why it happens:** Misunderstanding DICOM spec; ARTIM is only for specific conditions
**How to avoid:** Use ARTIM only for: waiting for A-ASSOCIATE-RQ, waiting for release/reject close
**Warning signs:** Premature connection drops during normal operation

ARTIM timer ONLY applies to:
1. Waiting for A-ASSOCIATE-RQ after TCP connection accepted
2. Waiting for peer to close after A-ASSOCIATE-RJ sent
3. Waiting for peer to close after A-RELEASE-RP sent

DIMSE timeouts (C-ECHO response, C-STORE response) are separate and implementation-defined.

### Pitfall 3: Big-Endian vs Little-Endian Confusion

**What goes wrong:** Using wrong byte order for PDU headers vs DICOM data
**Why it happens:** PDU headers are Big-Endian, but DICOM dataset encoding is Little-Endian
**How to avoid:** PDU layer always Big-Endian; DIMSE command/data uses transfer syntax endianness
**Warning signs:** Interop failures, incorrect PDU lengths

```csharp
// PDU header fields are ALWAYS Big-Endian (PS3.8)
var pduLength = BinaryPrimitives.ReadUInt32BigEndian(buffer);

// DICOM data inside P-DATA uses transfer syntax (usually Little-Endian)
var commandGroupLength = BinaryPrimitives.ReadUInt32LittleEndian(commandData);
```

### Pitfall 4: Presentation Context ID Validation

**What goes wrong:** Accepting even Presentation Context IDs or values > 255
**Why it happens:** Not validating per DICOM standard
**How to avoid:** Validate: odd integer, 1-255 range
**Warning signs:** Interop failures with strict PACS systems

```csharp
public static bool IsValidPresentationContextId(byte id)
    => id >= 1 && id <= 255 && (id & 1) == 1;  // Odd only
```

### Pitfall 5: P-DATA Fragmentation After Timeout

**What goes wrong:** Sending new request over association after previous request timed out
**Why it happens:** Previous P-DATA-TF fragments may still be in flight
**How to avoid:** After any timeout, close association and create new one
**Warning signs:** SCP receives interleaved fragments, rejects with malformed PDU

Per DICOM: "no fragments of any other message shall be sent until all fragments of the current message have been sent"

### Pitfall 6: Missing Graceful Shutdown

**What goes wrong:** Calling `Dispose` without proper A-RELEASE sequence
**Why it happens:** Forgetting that DICOM requires explicit release
**How to avoid:** `DisposeAsync` should attempt A-RELEASE-RQ/RP, then A-ABORT if timeout
**Warning signs:** Remote PACS logs show aborted associations

## Code Examples

Verified patterns from official sources:

### PDU Header Structure (PS3.8 Section 9.3)

```csharp
// Source: DICOM PS3.8 Section 9.3
public static class PduConstants
{
    // PDU Types
    public const byte AssociateRq = 0x01;
    public const byte AssociateAc = 0x02;
    public const byte AssociateRj = 0x03;
    public const byte PDataTf = 0x04;
    public const byte ReleaseRq = 0x05;
    public const byte ReleaseRp = 0x06;
    public const byte Abort = 0x07;

    // Item Types (in variable fields)
    public const byte ApplicationContext = 0x10;
    public const byte PresentationContextRq = 0x20;
    public const byte PresentationContextAc = 0x21;
    public const byte AbstractSyntax = 0x30;
    public const byte TransferSyntax = 0x40;
    public const byte UserInformation = 0x50;
    public const byte MaximumLength = 0x51;
    public const byte ImplementationClassUid = 0x52;
    public const byte ImplementationVersionName = 0x55;

    // Fixed PDU sizes
    public const int AssociateRjPduLength = 10;
    public const int ReleaseRqPduLength = 10;
    public const int ReleaseRpPduLength = 10;
    public const int AbortPduLength = 10;

    // Header sizes
    public const int PduHeaderLength = 6;  // Type + Reserved + Length
    public const int AssociateFixedFieldsLength = 68;  // Before variable items
}
```

### A-ASSOCIATE-RQ Building

```csharp
// Source: DICOM PS3.8 Section 9.3.2
public sealed class AssociateRequestPduBuilder
{
    public void WriteTo(IBufferWriter<byte> writer,
        string calledAE, string callingAE,
        IReadOnlyList<PresentationContext> contexts,
        int maxPduLength)
    {
        // Calculate total length
        var variableLength = CalculateVariableItemsLength(contexts, maxPduLength);
        var pduLength = 68 + variableLength;  // Fixed fields + variable

        var span = writer.GetSpan(6 + (int)pduLength);
        var pos = 0;

        // PDU Type: 01H
        span[pos++] = 0x01;
        // Reserved
        span[pos++] = 0x00;
        // PDU-length (Big-Endian)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(pos), (uint)pduLength);
        pos += 4;

        // Protocol-version: bit 0 set
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pos), 0x0001);
        pos += 2;
        // Reserved
        pos += 2;

        // Called-AE-title (16 bytes, space-padded)
        WriteAeTitle(span.Slice(pos), calledAE);
        pos += 16;

        // Calling-AE-title (16 bytes, space-padded)
        WriteAeTitle(span.Slice(pos), callingAE);
        pos += 16;

        // Reserved (32 bytes)
        span.Slice(pos, 32).Clear();
        pos += 32;

        // Variable items
        WriteApplicationContext(span.Slice(pos), out var written);
        pos += written;

        foreach (var ctx in contexts)
        {
            WritePresentationContext(span.Slice(pos), ctx, out written);
            pos += written;
        }

        WriteUserInformation(span.Slice(pos), maxPduLength, out written);
        pos += written;

        writer.Advance(pos);
    }

    private static void WriteAeTitle(Span<byte> dest, string aeTitle)
    {
        var padded = aeTitle.PadRight(16, ' ');
        Encoding.ASCII.GetBytes(padded.AsSpan(0, 16), dest);
    }
}
```

### C-ECHO Command Dataset

```csharp
// Source: DICOM PS3.7 Section 9.3.5
public static class CEchoCommand
{
    // C-ECHO-RQ Command Field value
    public const ushort RequestCommandField = 0x0030;
    // C-ECHO-RSP Command Field value
    public const ushort ResponseCommandField = 0x8030;

    public static DicomDataset CreateRequest(ushort messageId)
    {
        var ds = new DicomDataset();
        // Affected SOP Class UID - Verification SOP Class
        ds.Add(DicomTag.AffectedSOPClassUID, DicomUID.Verification);
        // Command Field
        ds.Add(DicomTag.CommandField, RequestCommandField);
        // Message ID
        ds.Add(DicomTag.MessageID, messageId);
        // Data Set Type - 0101H = no dataset
        ds.Add(DicomTag.CommandDataSetType, (ushort)0x0101);
        return ds;
    }

    public static DicomDataset CreateResponse(ushort messageIdBeingRespondedTo,
        DicomStatus status)
    {
        var ds = new DicomDataset();
        ds.Add(DicomTag.AffectedSOPClassUID, DicomUID.Verification);
        ds.Add(DicomTag.CommandField, ResponseCommandField);
        ds.Add(DicomTag.MessageIDBeingRespondedTo, messageIdBeingRespondedTo);
        ds.Add(DicomTag.CommandDataSetType, (ushort)0x0101);
        ds.Add(DicomTag.Status, status.Code);
        return ds;
    }
}
```

### DicomStatus Struct

```csharp
// Source: DICOM PS3.7 Annex C
public readonly struct DicomStatus : IEquatable<DicomStatus>
{
    public ushort Code { get; }
    public StatusCategory Category { get; }
    public string? ErrorComment { get; init; }

    public DicomStatus(ushort code)
    {
        Code = code;
        Category = CategorizeCode(code);
    }

    public bool IsSuccess => Category == StatusCategory.Success;
    public bool IsWarning => Category == StatusCategory.Warning;
    public bool IsFailure => Category == StatusCategory.Failure;
    public bool IsPending => Category == StatusCategory.Pending;
    public bool IsCancel => Category == StatusCategory.Cancel;

    private static StatusCategory CategorizeCode(ushort code) => code switch
    {
        0x0000 => StatusCategory.Success,
        0xFE00 => StatusCategory.Cancel,
        >= 0xFF00 and <= 0xFF01 => StatusCategory.Pending,
        >= 0xB000 and <= 0xBFFF => StatusCategory.Warning,
        >= 0xA000 and <= 0xAFFF => StatusCategory.Failure,
        >= 0xC000 and <= 0xCFFF => StatusCategory.Failure,
        _ when (code & 0xFF00) == 0x0100 => StatusCategory.Warning,  // 01xx
        _ when (code & 0xFF00) == 0x0200 => StatusCategory.Failure,  // 02xx
        _ => StatusCategory.Failure
    };

    // Well-known instances
    public static readonly DicomStatus Success = new(0x0000);
    public static readonly DicomStatus Cancel = new(0xFE00);
    public static readonly DicomStatus Pending = new(0xFF00);
    public static readonly DicomStatus PendingWithWarnings = new(0xFF01);

    // Common failure codes
    public static readonly DicomStatus NoSuchSOPClass = new(0x0118);
    public static readonly DicomStatus ClassInstanceConflict = new(0x0119);
    public static readonly DicomStatus ProcessingFailure = new(0x0110);

    public bool Equals(DicomStatus other) => Code == other.Code;
    public override bool Equals(object? obj) => obj is DicomStatus s && Equals(s);
    public override int GetHashCode() => Code.GetHashCode();
    public static bool operator ==(DicomStatus left, DicomStatus right) => left.Equals(right);
    public static bool operator !=(DicomStatus left, DicomStatus right) => !left.Equals(right);

    public override string ToString() => $"0x{Code:X4} ({Category})";
}

public enum StatusCategory
{
    Success,
    Pending,
    Warning,
    Failure,
    Cancel
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `BeginRead/EndRead` APM | `async/await` with `ReadAsync` | .NET 4.5 (2012) | Simpler async code |
| `SocketAsyncEventArgs` pooling | `ValueTask<T>` with pooling | .NET Core 2.1 | Allocation-free async |
| Manual buffer management | `ArrayPool<byte>.Shared` | .NET Core 2.0 | Standard pooling |
| `ILogger` with string concat | Source-generated `[LoggerMessage]` | .NET 6 | Zero-alloc logging |
| `NetworkStream` reading | `System.IO.Pipelines` | .NET Core 2.1 | Better buffer management |

**Deprecated/outdated:**
- `Begin*/End*` APM pattern: Replaced by `async/await`
- `Socket.Select`: Replaced by async operations
- Manual byte[] allocation: Use `ArrayPool<T>`

## Open Questions

Things that couldn't be fully resolved:

1. **System.IO.Pipelines integration depth**
   - What we know: Pipelines provide excellent zero-copy parsing
   - What's unclear: Whether ref struct PduReader can work seamlessly with PipeReader
   - Recommendation: Start with NetworkStream + ArrayPool; add Pipelines if benchmarks justify

2. **TLS/SSL support**
   - What we know: DICOM supports TLS per PS3.15
   - What's unclear: Best .NET API for wrapping TcpClient with SslStream
   - Recommendation: Defer to Phase 11 or later; focus on unencrypted first

3. **Maximum PDU size negotiation**
   - What we know: Both sides negotiate max PDU length in User Information
   - What's unclear: Optimal default (16KB vs 1MB trade-offs)
   - Recommendation: Default 16KB (conservative), allow configuration up to 1MB

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.8 Section 9.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part08/sect_9.3.html) - PDU structures
- [DICOM PS3.8 Section 9.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part08/sect_9.2.html) - State machine
- [DICOM PS3.7 Section 9.3.5](https://dicom.nema.org/medical/dicom/current/output/chtml/part07/sect_9.3.5.html) - C-ECHO
- [Microsoft Learn: TcpClient/TcpListener](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/sockets/tcp-classes) - .NET TCP patterns
- [Microsoft Learn: Logging for library authors](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging-library-authors) - ILogger patterns

### Secondary (MEDIUM confidence)
- [pynetdicom concepts](https://pydicom.github.io/pynetdicom/1.5/user/concepts.html) - DICOM networking patterns
- [fo-dicom GitHub issues](https://github.com/fo-dicom/fo-dicom/issues/1359) - PDU fragmentation pitfalls

### Tertiary (LOW confidence)
- Various blog posts on .NET socket programming - General patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Built-in .NET APIs, well-documented
- Architecture: HIGH - Based on existing SharpDicom patterns + DICOM spec
- Pitfalls: HIGH - Verified against multiple implementations and spec
- Code examples: HIGH - Based on DICOM spec byte layouts

**Research date:** 2026-01-27
**Valid until:** 2026-03-27 (60 days - stable domain, spec-based)
