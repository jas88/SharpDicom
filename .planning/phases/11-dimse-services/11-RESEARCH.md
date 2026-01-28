# Phase 11: DIMSE Services - Research

**Researched:** 2026-01-28
**Domain:** DICOM DIMSE-C Services (C-STORE, C-FIND, C-MOVE, C-GET), Streaming Network I/O
**Confidence:** HIGH

## Summary

Phase 11 implements the full suite of DIMSE-C services for DICOM image storage, query, and retrieval operations. This builds directly on the Phase 10 foundation (PDU parsing, association state machine, C-ECHO) to add the remaining composite services.

The key architectural challenge is supporting **streaming** for large datasets while maintaining protocol correctness. C-STORE must stream pixel data without full buffering. C-FIND/C-MOVE/C-GET return multiple responses via `IAsyncEnumerable<T>`. C-MOVE coordinates third-party retrieval with sub-operation tracking, and C-GET handles the complex case of receiving C-STORE sub-operations on the same association.

The existing infrastructure (PduReader/PduWriter ref structs, DicomAssociation state machine, DicomCommand class, IPixelDataSource interface) provides a solid foundation. The primary additions are service-specific classes, response streaming via `IAsyncEnumerable<T>`, and optional System.IO.Pipelines integration for zero-copy PDU parsing.

**Primary recommendation:** Implement separate service classes (CStoreScu, CFindScu, etc.) rather than methods on DicomClient. Use `IAsyncEnumerable<T>` for multi-response operations. Support both buffered and streaming C-STORE SCP handlers. Leverage existing `IPixelDataSource` for SCU streaming sends.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Net.Sockets` | Built-in | TCP connectivity | Already used in Phase 10 |
| `System.Threading.Channels` | Built-in (net8+) | Response queuing | High-perf async coordination |
| `System.IO.Pipelines` | Built-in (net8+) | Zero-copy PDU parsing | FR-10.12 requirement |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Buffers.ArrayPool` | Built-in | Buffer pooling | Large PDU assembly |
| `IAsyncEnumerable<T>` | Built-in (net8+) | Response streaming | C-FIND/MOVE/GET results |

### No New External Dependencies

This phase adds no new external dependencies. All required functionality is available in the .NET BCL and existing SharpDicom infrastructure from Phases 1-10.

## Architecture Patterns

### Recommended Project Structure

```
src/SharpDicom/
├── Network/
│   ├── Dimse/
│   │   ├── CommandField.cs         # Already exists
│   │   ├── DicomCommand.cs         # Already exists
│   │   ├── QueryRetrieveLevel.cs   # NEW: enum for PATIENT/STUDY/SERIES/IMAGE
│   │   ├── SubOperationProgress.cs # NEW: struct for tracking sub-ops
│   │   └── Services/
│   │       ├── CStoreScu.cs        # NEW: C-STORE SCU
│   │       ├── CStoreScpHandler.cs # NEW: C-STORE SCP handler interface
│   │       ├── CFindScu.cs         # NEW: C-FIND SCU
│   │       ├── CMoveScu.cs         # NEW: C-MOVE SCU
│   │       ├── CGetScu.cs          # NEW: C-GET SCU
│   │       └── DicomQuery.cs       # NEW: fluent query builder
│   ├── Streaming/                   # NEW: Optional Pipelines integration
│   │   ├── PipelinePduReader.cs    # PipeReader-based PDU reading
│   │   └── StreamingDatasetWriter.cs
│   └── ...existing files...
```

### Pattern 1: Separate Service Classes

**What:** Each DIMSE service as its own class taking DicomAssociation
**When to use:** All SCU operations
**Why:** Clean separation of concerns, testable, follows context decisions

```csharp
// Source: 11-CONTEXT.md decisions
public sealed class CStoreScu
{
    private readonly DicomAssociation _association;
    private readonly CStoreOptions _options;

    public CStoreScu(DicomAssociation association, CStoreOptions? options = null)
    {
        _association = association ?? throw new ArgumentNullException(nameof(association));
        _options = options ?? CStoreOptions.Default;
    }

    // Overload 1: From loaded file
    public ValueTask<CStoreResponse> SendAsync(
        DicomFile file,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default);

    // Overload 2: Streaming from disk/network
    public ValueTask<CStoreResponse> SendAsync(
        Stream stream,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default);

    // Overload 3: Separate metadata and pixels (transcoding scenarios)
    public ValueTask<CStoreResponse> SendAsync(
        DicomDataset dataset,
        IPixelDataSource pixels,
        IProgress<DicomTransferProgress>? progress = null,
        CancellationToken ct = default);
}
```

### Pattern 2: IAsyncEnumerable for Multi-Response Operations

**What:** Yield results as they arrive from network
**When to use:** C-FIND, C-MOVE, C-GET responses

```csharp
// Source: IAsyncEnumerable best practices + 11-CONTEXT.md
public sealed class CFindScu
{
    public async IAsyncEnumerable<DicomDataset> QueryAsync(
        QueryRetrieveLevel level,
        DicomDataset query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Send C-FIND-RQ
        var messageId = NextMessageId();
        await SendCFindRequestAsync(level, query, messageId, ct);

        // Yield pending responses
        while (true)
        {
            var (command, dataset) = await ReceiveDimseMessageAsync(ct);

            if (!command.IsCFindResponse)
                throw new DicomNetworkException($"Expected C-FIND-RSP");

            if (command.Status.IsPending)
            {
                yield return dataset!;
            }
            else if (command.Status.IsSuccess)
            {
                // No more results
                yield break;
            }
            else if (command.Status.IsCancel)
            {
                throw new OperationCanceledException("C-FIND cancelled by SCP");
            }
            else
            {
                throw new DicomNetworkException($"C-FIND failed: {command.Status}");
            }
        }
    }
}
```

### Pattern 3: C-MOVE Third-Party Coordination

**What:** C-MOVE sends to third-party destination via MoveDestination field
**When to use:** C-MOVE operations where caller specifies destination AE

```csharp
// Source: DICOM PS3.7 Section 9.3.4, Command Dictionary
public sealed class CMoveScu
{
    /// <summary>
    /// Initiates C-MOVE to send matching instances to destination AE.
    /// </summary>
    /// <param name="level">Query/Retrieve level</param>
    /// <param name="query">Matching keys</param>
    /// <param name="destinationAE">AE Title where SCP sends via C-STORE</param>
    /// <param name="progress">Progress callback for sub-operation counts</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Stream of progress updates, final result has SubOperationProgress.IsFinal=true</returns>
    public async IAsyncEnumerable<CMoveProgress> MoveAsync(
        QueryRetrieveLevel level,
        DicomDataset query,
        string destinationAE,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Build C-MOVE-RQ with MoveDestination (0000,0600)
        var command = CreateCMoveRequest(level, query, destinationAE);
        await SendCommandAsync(command, ct);

        // Receive pending responses with sub-operation counts
        while (true)
        {
            var response = await ReceiveCMoveResponseAsync(ct);

            yield return new CMoveProgress(
                response.NumberOfRemainingSuboperations,
                response.NumberOfCompletedSuboperations,
                response.NumberOfFailedSuboperations,
                response.NumberOfWarningSuboperations,
                response.Status);

            if (!response.Status.IsPending)
                yield break;
        }
    }
}

// C-MOVE-RQ command elements per PS3.7 Table 9.3-9
private DicomCommand CreateCMoveRequest(
    QueryRetrieveLevel level,
    DicomDataset query,
    string destinationAE)
{
    var ds = new DicomDataset();
    ds.Add(DicomTag.AffectedSOPClassUID, GetQRSopClassUid(level));
    ds.Add(DicomTag.CommandField, CommandField.CMoveRequest);  // 0x0021
    ds.Add(DicomTag.MessageID, NextMessageId());
    ds.Add(DicomTag.Priority, (ushort)0);  // MEDIUM
    ds.Add(DicomTag.CommandDataSetType, (ushort)0x0102);  // dataset present
    ds.Add(DicomTag.MoveDestination, destinationAE);  // (0000,0600) AE VR
    return new DicomCommand(ds);
}
```

### Pattern 4: C-GET Sub-Operation Handling

**What:** Receive C-STORE sub-operations on same association
**When to use:** C-GET operations where data returns on same connection

```csharp
// Source: DICOM PS3.4 Section C.4.3
public sealed class CGetScu
{
    private readonly DicomAssociation _association;
    private readonly Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> _storeHandler;

    /// <summary>
    /// Creates C-GET SCU with handler for incoming C-STORE sub-operations.
    /// </summary>
    /// <param name="association">Established association (must have Storage SOP Classes in SCP role)</param>
    /// <param name="storeHandler">Handler invoked for each received instance</param>
    public CGetScu(
        DicomAssociation association,
        Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> storeHandler)
    {
        _association = association;
        _storeHandler = storeHandler;
    }

    public async IAsyncEnumerable<CGetProgress> GetAsync(
        QueryRetrieveLevel level,
        DicomDataset query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Send C-GET-RQ
        await SendCGetRequestAsync(level, query, ct);

        // Message loop: handle interleaved C-STORE-RQ and C-GET-RSP
        while (true)
        {
            var (command, dataset) = await ReceiveDimseMessageAsync(ct);

            if (command.IsCStoreRequest)
            {
                // Incoming sub-operation: delegate to handler
                var status = await _storeHandler(command.Dataset, dataset, ct);
                await SendCStoreResponseAsync(command.MessageID, status, ct);
            }
            else if (command.IsCGetResponse)
            {
                yield return new CGetProgress(
                    command.NumberOfRemainingSuboperations,
                    command.NumberOfCompletedSuboperations,
                    command.NumberOfFailedSuboperations,
                    command.NumberOfWarningSuboperations,
                    command.Status);

                if (!command.Status.IsPending)
                    yield break;
            }
            else
            {
                throw new DicomNetworkException($"Unexpected command: 0x{command.CommandFieldValue:X4}");
            }
        }
    }
}
```

### Pattern 5: Streaming C-STORE SCP Handler

**What:** Handle incoming C-STORE without buffering entire dataset
**When to use:** C-STORE SCP for large images

```csharp
// Source: 11-CONTEXT.md decisions, DCMTK --bit-preserving pattern
public interface ICStoreHandler
{
    /// <summary>
    /// Handle incoming C-STORE request with full dataset buffered.
    /// </summary>
    ValueTask<DicomStatus> OnCStoreAsync(
        DicomDataset metadata,
        DicomDataset dataset,
        CancellationToken ct);
}

public interface IStreamingCStoreHandler
{
    /// <summary>
    /// Handle incoming C-STORE request with streaming pixel data.
    /// Metadata available immediately; pixel data streams via provided source.
    /// </summary>
    ValueTask<DicomStatus> OnCStoreStreamingAsync(
        DicomDataset metadata,
        Stream pixelDataStream,
        CancellationToken ct);
}

// Selection via DicomServerOptions
public class DicomServerOptions
{
    public CStoreHandlerMode StoreHandlerMode { get; set; } = CStoreHandlerMode.Buffered;
    // ...
}

public enum CStoreHandlerMode
{
    /// <summary>Full dataset buffered before handler invoked.</summary>
    Buffered,
    /// <summary>Metadata first, then pixel stream via CopyToAsync pattern.</summary>
    Streaming
}
```

### Pattern 6: C-CANCEL Message

**What:** Cancel in-progress C-FIND/C-MOVE/C-GET operations
**When to use:** When CancellationToken is triggered during multi-response operation

```csharp
// Source: DICOM PS3.7, fo-dicom issue #748
public static class CCancelCommand
{
    /// <summary>
    /// Creates C-CANCEL-RQ to cancel an in-progress operation.
    /// </summary>
    /// <param name="messageIdBeingCancelled">MessageID of the original request</param>
    public static DicomCommand CreateCancelRequest(ushort messageIdBeingCancelled)
    {
        var ds = new DicomDataset();
        // C-CANCEL uses CommandField 0x0FFF
        ds.Add(DicomTag.CommandField, CommandField.CCancelRequest);
        ds.Add(DicomTag.MessageIDBeingRespondedTo, messageIdBeingCancelled);
        ds.Add(DicomTag.CommandDataSetType, (ushort)0x0101);  // no dataset
        return new DicomCommand(ds);
    }
}

// Usage in IAsyncEnumerable implementation
private async ValueTask SendCancelAsync(ushort messageId, CancellationToken ct)
{
    var cancel = CCancelCommand.CreateCancelRequest(messageId);
    await SendCommandAsync(cancel, ct);

    // Wait for C-FIND-RSP/C-MOVE-RSP/C-GET-RSP with Cancel status (0xFE00)
    var response = await ReceiveCommandAsync(ct);
    if (!response.Status.IsCancel)
    {
        // SCP may not honor cancel; log and continue
    }
}
```

### Anti-Patterns to Avoid

- **Blocking on IAsyncEnumerable:** Never call `.ToListAsync()` on potentially large result sets without pagination
- **Full buffering for streaming:** C-STORE SCP should not buffer entire dataset when streaming mode is selected
- **Ignoring sub-operation counts:** C-MOVE/C-GET pending responses provide progress; don't discard
- **Single message loop for C-GET:** Must handle interleaved C-STORE-RQ and C-GET-RSP on same association
- **Hardcoded timeouts:** Use configurable per-operation timeouts from `DicomClientOptions`
- **Association reuse after timeout:** After any DIMSE timeout, mark association corrupted and create new one

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Response streaming | Custom callback system | `IAsyncEnumerable<T>` | Standard .NET pattern, LINQ support |
| Buffer management | Manual byte[] tracking | `ArrayPool<byte>.Shared` | Already used in Phase 10 |
| Cancellation coordination | Manual flags | `CancellationToken` + C-CANCEL | Protocol-correct cancellation |
| Pixel streaming | Custom chunking | `IPixelDataSource.CopyToAsync` | Already implemented in Phase 5 |
| Command VR lookup | Hard-coded per tag | `DicomClient.GetCommandVR()` | Already implemented in Phase 10 |
| Timeout handling | Manual timers | `CancellationTokenSource.CancelAfter` | Integrates with async/await |

**Key insight:** The Phase 10 foundation already provides most infrastructure. Focus on DIMSE-specific protocol logic, not generic infrastructure.

## Common Pitfalls

### Pitfall 1: C-GET Association Negotiation

**What goes wrong:** C-GET fails because Storage SOP Classes not negotiated in SCP role
**Why it happens:** Unlike C-MOVE (separate association), C-GET sub-ops use the same association
**How to avoid:** SCU must propose Storage SOP Classes with SCP Role Selection; SCP must accept
**Warning signs:** "No presentation context for C-STORE" during C-GET

```csharp
// CORRECT: Propose Storage SOP Classes in SCP role for C-GET
var contexts = new List<PresentationContext>
{
    // C-GET QR
    new PresentationContext(1, DicomUID.PatientRootQueryRetrieveInformationModelGET,
        TransferSyntax.ImplicitVRLittleEndian, TransferSyntax.ExplicitVRLittleEndian),

    // Storage SOP Classes in SCP role for receiving sub-operations
    new PresentationContext(3, DicomUID.CTImageStorage,
        TransferSyntax.ExplicitVRLittleEndian)
        .WithScpRole()  // Critical: accept SCP role for this context
};
```

### Pitfall 2: Sub-Operation Count Tracking

**What goes wrong:** C-MOVE/C-GET progress is incomplete or incorrect
**Why it happens:** Sub-operation counts in Pending responses are cumulative, not incremental
**How to avoid:** Track running totals; final response contains totals, not deltas
**Warning signs:** Progress appears to go backwards or reports wrong totals

```csharp
// PS3.7: Pending responses include counts of ALL sub-operations
public readonly record struct SubOperationProgress(
    ushort Remaining,   // Not yet started
    ushort Completed,   // Success
    ushort Failed,      // Error
    ushort Warning      // Warning status
)
{
    public ushort Total => (ushort)(Remaining + Completed + Failed + Warning);
    public bool IsFinal => Remaining == 0;
}
```

### Pitfall 3: C-FIND Identifier Encoding

**What goes wrong:** C-FIND query returns no matches or fails
**Why it happens:** Identifier dataset uses negotiated transfer syntax, not Implicit VR LE
**How to avoid:** Data set in C-FIND uses presentation context transfer syntax; only command is Implicit VR LE
**Warning signs:** Works with some PACS but not others

```csharp
// Command set: ALWAYS Implicit VR Little Endian
// Data set (Identifier): Per negotiated transfer syntax of presentation context

// CORRECT: Use presentation context transfer syntax for identifier
var context = _association.GetPresentationContext(contextId);
var ts = context.TransferSyntax;
var identifierBytes = SerializeDataset(query, ts);  // NOT Implicit VR LE
```

### Pitfall 4: MoveDestination Must Be Known to SCP

**What goes wrong:** C-MOVE returns "Move Destination Unknown" (0xA801)
**Why it happens:** SCP doesn't have AE configuration for destination
**How to avoid:** Document that SCP must be configured with destination AE -> host:port mapping
**Warning signs:** C-MOVE works for some destinations but not others

### Pitfall 5: C-STORE Streaming Chunk Boundaries

**What goes wrong:** Streaming SCP receives corrupted pixel data
**Why it happens:** Chunks written don't align with PDV boundaries
**How to avoid:** Process complete PDVs; only assemble complete fragments before writing
**Warning signs:** Works with small images, fails with large multi-frame

### Pitfall 6: Message ID Overflow

**What goes wrong:** Message ID wraps after 65535 operations
**Why it happens:** Message ID is UInt16; long-running associations can overflow
**How to avoid:** Allow wraparound; ensure no in-flight operations have same ID
**Warning signs:** "Message ID mismatch" after extensive use

```csharp
// Thread-safe message ID generation with wraparound
private int _messageIdCounter;

private ushort NextMessageId()
{
    return (ushort)Interlocked.Increment(ref _messageIdCounter);
}
```

## Code Examples

### Query/Retrieve Level Enum

```csharp
// Source: DICOM PS3.4 Section C.3, Tag (0008,0052)
public enum QueryRetrieveLevel
{
    Patient,
    Study,
    Series,
    Image
}

public static class QueryRetrieveLevelExtensions
{
    public static string ToDicomValue(this QueryRetrieveLevel level) => level switch
    {
        QueryRetrieveLevel.Patient => "PATIENT",
        QueryRetrieveLevel.Study => "STUDY",
        QueryRetrieveLevel.Series => "SERIES",
        QueryRetrieveLevel.Image => "IMAGE",
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
```

### C-FIND-RSP Status Codes

```csharp
// Source: DICOM PS3.4 Section C.4.1.1.4
public static class CFindStatus
{
    // Success
    public static readonly DicomStatus Success = new(0x0000);

    // Pending - more matches coming
    public static readonly DicomStatus Pending = new(0xFF00);
    public static readonly DicomStatus PendingWarning = new(0xFF01);  // Optional keys not supported

    // Cancel
    public static readonly DicomStatus Cancel = new(0xFE00);

    // Failure
    public static readonly DicomStatus OutOfResources = new(0xA700);
    public static readonly DicomStatus IdentifierDoesNotMatchSOPClass = new(0xA900);
    public static readonly DicomStatus UnableToProcess = new(0xC000);
}
```

### C-MOVE-RSP/C-GET-RSP Status Codes

```csharp
// Source: DICOM PS3.4 Section C.4.2.1.5, C.4.3.1.4
public static class CMoveStatus
{
    // Success - all sub-operations complete
    public static readonly DicomStatus Success = new(0x0000);

    // Pending - sub-operations in progress
    public static readonly DicomStatus Pending = new(0xFF00);

    // Warning - some sub-operations had warnings
    public static readonly DicomStatus WarningSubOperationsComplete = new(0xB000);

    // Cancel
    public static readonly DicomStatus Cancel = new(0xFE00);

    // Failure
    public static readonly DicomStatus UnableToCalculateNumberOfMatches = new(0xA701);
    public static readonly DicomStatus UnableToPerformSubOperations = new(0xA702);
    public static readonly DicomStatus MoveDestinationUnknown = new(0xA801);
    public static readonly DicomStatus IdentifierDoesNotMatchSOPClass = new(0xA900);
}
```

### DicomTransferProgress Struct

```csharp
// Source: 11-CONTEXT.md decisions
public readonly record struct DicomTransferProgress(
    long BytesTransferred,
    long TotalBytes,
    double BytesPerSecond)
{
    public double PercentComplete => TotalBytes > 0
        ? (double)BytesTransferred / TotalBytes * 100
        : 0;

    public TimeSpan? EstimatedTimeRemaining => BytesPerSecond > 0
        ? TimeSpan.FromSeconds((TotalBytes - BytesTransferred) / BytesPerSecond)
        : null;
}
```

### Fluent Query Builder

```csharp
// Source: 11-CONTEXT.md decisions
public sealed class DicomQuery
{
    private readonly DicomDataset _dataset = new();
    private QueryRetrieveLevel _level;

    private DicomQuery(QueryRetrieveLevel level)
    {
        _level = level;
        _dataset.Add(DicomTag.QueryRetrieveLevel, level.ToDicomValue());
    }

    public static DicomQuery ForPatients() => new(QueryRetrieveLevel.Patient);
    public static DicomQuery ForStudies() => new(QueryRetrieveLevel.Study);
    public static DicomQuery ForSeries() => new(QueryRetrieveLevel.Series);
    public static DicomQuery ForImages() => new(QueryRetrieveLevel.Image);

    public DicomQuery WithPatientName(string pattern)
    {
        _dataset.Add(DicomTag.PatientName, pattern);
        return this;
    }

    public DicomQuery WithPatientId(string id)
    {
        _dataset.Add(DicomTag.PatientID, id);
        return this;
    }

    public DicomQuery WithStudyDate(DateTime date)
    {
        _dataset.Add(DicomTag.StudyDate, date.ToString("yyyyMMdd"));
        return this;
    }

    public DicomQuery WithStudyDateRange(DateTime from, DateTime to)
    {
        _dataset.Add(DicomTag.StudyDate, $"{from:yyyyMMdd}-{to:yyyyMMdd}");
        return this;
    }

    public DicomQuery WithModality(params string[] modalities)
    {
        _dataset.Add(DicomTag.ModalitiesInStudy, string.Join("\\", modalities));
        return this;
    }

    public DicomQuery WithStudyInstanceUid(string uid)
    {
        _dataset.Add(DicomTag.StudyInstanceUID, uid);
        return this;
    }

    public DicomQuery WithSeriesInstanceUid(string uid)
    {
        _dataset.Add(DicomTag.SeriesInstanceUID, uid);
        return this;
    }

    public DicomQuery WithSopInstanceUid(string uid)
    {
        _dataset.Add(DicomTag.SOPInstanceUID, uid);
        return this;
    }

    public DicomQuery ReturnField(DicomTag tag)
    {
        if (!_dataset.Contains(tag))
            _dataset.Add(new DicomStringElement(tag, DicomVR.UN, Array.Empty<byte>()));
        return this;
    }

    public QueryRetrieveLevel Level => _level;
    public DicomDataset ToDataset() => _dataset;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Callback-based responses | `IAsyncEnumerable<T>` | .NET Core 3.0 (2019) | Cleaner streaming API |
| Manual PDU buffering | System.IO.Pipelines | .NET Core 2.1 (2018) | Zero-copy, backpressure |
| Blocking receive loops | async/await throughout | .NET 4.5+ (established) | Non-blocking I/O |
| Single handler mode | Buffered + Streaming options | Current practice | Memory efficiency |

**Deprecated/outdated:**
- Synchronous DIMSE operations: Use async throughout
- Single-threaded association handling: Modern servers use Task-per-association
- Fixed buffer sizes: Use ArrayPool with configurable PDU sizes

## Open Questions

### 1. System.IO.Pipelines Integration Depth

**What we know:** Pipelines provide excellent zero-copy parsing with backpressure
**What's unclear:** Whether PipeReader integrates cleanly with existing ref struct PduReader
**Recommendation:** Start with NetworkStream + ArrayPool (proven in Phase 10); add Pipelines wrapper as optional optimization layer. If benchmarks show significant benefit, promote to default.

### 2. Transcoding During C-STORE

**What we know:** Transfer syntax negotiation may result in different TS than source file
**What's unclear:** Whether transcoding should happen automatically or be caller's responsibility
**Recommendation:** For Phase 11, require caller to provide data in negotiated transfer syntax. Defer automatic transcoding to Phase 12 (codec integration). Document this limitation.

### 3. Multiple Presentation Contexts per SOP Class

**What we know:** Can propose same SOP Class with multiple transfer syntaxes
**What's unclear:** Selection strategy when SCP accepts multiple
**Recommendation:** Accept first match (by context ID order); provide explicit selection API for advanced users.

### 4. C-STORE Retry Behavior on Transient Failure

**What we know:** 11-CONTEXT.md specifies auto-retry with exponential backoff
**What's unclear:** Whether to retry on warning status or only on retryable failures
**Recommendation:** Retry only on specific failure codes (0xA700 Out of Resources, network errors); do not retry on warning or permanent failures.

## Sources

### Primary (HIGH confidence)

- [DICOM PS3.7 2025e - Message Exchange](https://dicom.nema.org/medical/dicom/current/output/chtml/part07/chapter_9.html) - DIMSE service definitions
- [DICOM PS3.7 2025e - Command Dictionary (Annex E)](https://dicom.nema.org/medical/dicom/current/output/chtml/part07/chapter_e.html) - Command elements including MoveDestination, sub-operation counts
- [DICOM PS3.4 2025e - Query/Retrieve Service Class](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/chapter_c.html) - C-FIND/MOVE/GET behavior
- [DICOM PS3.4 2025e - C-GET Association Negotiation](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_C.5.3.html) - SCP/SCU role selection
- [Microsoft Learn - System.IO.Pipelines](https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/) - Zero-copy parsing patterns

### Secondary (MEDIUM confidence)

- [fo-dicom GitHub Issues #748](https://github.com/fo-dicom/fo-dicom/issues/748) - C-CANCEL implementation challenges
- [DCMTK storescp --bit-preserving](https://support.dcmtk.org/docs/storescp.html) - Streaming receive pattern
- Existing Phase 10 implementation in SharpDicom codebase - Proven patterns

### Tertiary (LOW confidence)

- Various blog posts on IAsyncEnumerable patterns - General guidance

## Metadata

**Confidence breakdown:**

- DIMSE protocol structure: HIGH - Official DICOM standard
- Command elements and status codes: HIGH - DICOM PS3.7 Annex E
- C-GET sub-operation handling: HIGH - DICOM PS3.4 explicit specification
- Streaming patterns: MEDIUM - Based on established .NET patterns, not DICOM-specific
- System.IO.Pipelines integration: MEDIUM - Deferred to should-have, not critical path

**Research date:** 2026-01-28
**Valid until:** 2026-03-28 (60 days - stable domain, spec-based)
