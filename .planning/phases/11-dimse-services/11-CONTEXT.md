# Phase 11: DIMSE Services — Context

**Phase Goal**: Complete DIMSE-C services for image storage, query, and retrieval operations

**Date**: 2026-01-28

---

## Streaming Behavior

### Data Flow Model
- **C-STORE SCU**: Stream from source (file/memory) directly to PDUs without full buffering
- **C-STORE SCP**: Configurable - either buffer complete dataset or stream pixels to callback
- **C-FIND/C-MOVE/C-GET**: IAsyncEnumerable for response streaming, yields results as they arrive

### Memory Management
- Large pixel data never fully buffered in memory during transfer
- PDU fragments written directly from source spans
- Received data can be written to disk/stream before association closes

### Backpressure
- Honor TCP backpressure naturally via async/await
- No internal queuing beyond single PDU assembly buffer

---

## Error Recovery

### C-STORE Failures
- **Auto-retry** with configurable retry count and exponential backoff
- Default: 3 retries with 1s/2s/4s backoff
- Configurable via `DicomClientOptions.RetryPolicy`

### Multi-Result Operation Failures (C-FIND/C-MOVE/C-GET)
- **Continue enumeration** on partial failures
- Collect errors in separate collection during enumeration
- **Throw AggregateException** at end of enumeration containing all failures
- Successfully yielded results remain available to caller

### Malformed DIMSE Handling
- **Configurable** via options with sensible defaults:
  - SCU default: **Strict** (abort on malformed response)
  - SCP default: **Lenient** (log warning, attempt to continue)
- `DicomAssociationOptions.MalformedDimseHandling = Strict | Lenient`

### Timeouts (per-operation defaults)
| Operation | Default Timeout | Rationale |
|-----------|----------------|-----------|
| C-STORE | 30 seconds | Single file transfer |
| C-FIND | 10 seconds | Query response expected quickly |
| C-MOVE | 120 seconds | May involve many sub-operations |
| C-GET | 120 seconds | Similar to C-MOVE |

- All configurable via `DicomClientOptions.Timeouts`

---

## Progress & Cancellation

### Progress Reporting

**C-STORE Progress**:
```csharp
IProgress<DicomTransferProgress> progress

public readonly record struct DicomTransferProgress(
    long BytesTransferred,
    long TotalBytes,
    double BytesPerSecond);
```

**C-MOVE/C-GET Progress**:
- Single overall counter: "3 of 10 instances retrieved"
- Reported via same IProgress mechanism with sub-operation counts

### Cancellation Behavior

**C-FIND Cancellation**:
1. Send C-CANCEL-FIND-RQ to peer
2. Wait for acknowledgment (with timeout)
3. Throw OperationCanceledException
4. Association remains usable for subsequent operations

**C-GET Cancellation**:
- Configurable via `CancellationBehavior` option:
  - `RejectInFlight`: Send C-CANCEL, reject incoming C-STORE sub-ops
  - `CompleteInFlight`: Send C-CANCEL, accept already-started C-STORE ops
- Caller decides based on their data integrity requirements

---

## API Patterns

### Service Architecture
Separate service classes, not methods on DicomClient:

```csharp
// SCU Services
var storeScu = new CStoreScu(association);
var findScu = new CFindScu(association);
var moveScu = new CMoveScu(association);
var getScu = new CGetScu(association);

// SCP Handlers (registered on DicomServer)
server.OnCStoreRequest += handler;
server.OnCFindRequest += handler;
```

### C-STORE SCU Input Overloads
```csharp
// From loaded file
Task<CStoreResponse> SendAsync(DicomFile file, IProgress<DicomTransferProgress>? progress = null, CancellationToken ct = default);

// Streaming from disk/network
Task<CStoreResponse> SendAsync(Stream stream, IProgress<DicomTransferProgress>? progress = null, CancellationToken ct = default);

// Separate metadata and pixels (for transcoding scenarios)
Task<CStoreResponse> SendAsync(DicomDataset dataset, IPixelDataSource pixels, IProgress<DicomTransferProgress>? progress = null, CancellationToken ct = default);
```

### C-FIND Query Building
```csharp
// Raw dataset (power users)
var query = new DicomDataset { { DicomTag.PatientName, "Smith*" } };
await foreach (var result in findScu.QueryAsync(QueryRetrieveLevel.Study, query)) { }

// Fluent builder (convenience)
var query = DicomQuery.ForStudies()
    .WithPatientName("Smith*")
    .WithStudyDateRange(DateTime.Today.AddDays(-7), DateTime.Today)
    .WithModality("CT", "MR");
await foreach (var result in findScu.QueryAsync(query)) { }
```

### C-STORE SCP Handler Options
```csharp
// Option A: Full buffer (simple)
server.OnCStoreRequest = async (dataset, ct) => {
    await SaveToDisk(dataset);
    return DicomStatus.Success;
};

// Option B: Streaming pixels (memory efficient)
server.OnCStoreRequest = async (metadata, pixelStream, ct) => {
    await using var file = File.Create(path);
    await WriteMetadata(file, metadata);
    await pixelStream.CopyToAsync(file, ct);
    return DicomStatus.Success;
};

// Selected via DicomServerOptions.StoreHandlerMode = Buffered | Streaming
```

---

## Deferred Ideas

*(None captured during this discussion)*

---

## Next Steps

1. `/gsd:plan-phase 11` — Create detailed execution plans
2. Research C-MOVE third-party destination coordination (marked in roadmap)
