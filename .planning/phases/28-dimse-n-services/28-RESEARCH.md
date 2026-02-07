# Phase 28: DIMSE-N Services - Research

**Researched:** 2026-02-07
**Domain:** DICOM Normalized DIMSE Services (N-CREATE, N-SET, N-GET, N-DELETE, N-ACTION, N-EVENT-REPORT), MPPS, Storage Commitment, Async Operations Window
**Confidence:** HIGH

## Summary

Phase 28 adds the six DICOM normalized (N-*) services to SharpDicom's network layer, along with three concrete application-level features built on them: MPPS workflow, Storage Commitment Push Model, and Async Operations Window negotiation with enforcement.

The existing codebase is exceptionally well-prepared for this phase. All six N-Service command field values already exist in `CommandField.cs`. All N-Service-specific command tags (`EventTypeID`, `ActionTypeID`, `AttributeIdentifierList`, `RequestedSOPInstanceUID`) already exist in `DicomTag.WellKnown.cs`. The VR mapping in `DicomClient.GetCommandVR()` already handles all N-Service command elements. The `ItemType.AsynchronousOperationsWindow` (0x53) enum value already exists in `ItemType.cs`. The primary work is building the request/response types, handler interfaces, DicomCommand factory methods, server dispatch logic, and the three application-level features.

System.Threading.Channels (for Async Operations Window enforcement) supports netstandard2.0 via the NuGet package and requires no polyfills. The project already uses Central Package Management so the version reference goes in `Directory.Packages.props`.

**Primary recommendation:** Follow the established C-Service patterns exactly (typed Options, SCU classes taking DicomClient, handler interfaces, DicomCommand factory methods, server dispatch in RunDimseLoop), adding N-Service-specific command fields (EventTypeID, ActionTypeID, AttributeIdentifierList). Build MPPS and Storage Commitment as higher-level service classes on top of the N-Service primitives.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Threading.Channels | 10.0.2 | Bounded async pipeline for Async Ops Window | Built-in on net8.0+, NuGet package for netstandard2.0. Natural backpressure via BoundedChannelOptions. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| (none) | - | - | No additional dependencies needed. All N-Service logic is pure DICOM protocol. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.Threading.Channels | SemaphoreSlim + ConcurrentQueue | Channels provide cleaner async reader/writer semantics and built-in bounded backpressure. SemaphoreSlim would require manual correlation. |
| Separate MPPS/StorageCommitment assemblies | Single SharpDicom assembly | Keep in main assembly -- these are core DICOM network services, not optional features. |

**Installation:**
```xml
<!-- In Directory.Packages.props -->
<PackageVersion Include="System.Threading.Channels" Version="10.0.2" />

<!-- In SharpDicom.csproj, conditional for netstandard2.0 only -->
<PackageReference Include="System.Threading.Channels" Condition="'$(TargetFramework)' == 'netstandard2.0'" />
```

Note: On net8.0 and net9.0, `System.Threading.Channels` is inbox (part of the runtime). Only netstandard2.0 needs the NuGet package.

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/Network/
├── Dimse/
│   ├── CommandField.cs          # (existing) already has all N-* values
│   ├── DicomCommand.cs          # (extend) add N-* factory methods
│   └── Services/
│       ├── NCreateRequest.cs    # Typed N-CREATE request
│       ├── NSetRequest.cs       # Typed N-SET request
│       ├── NGetRequest.cs       # Typed N-GET request
│       ├── NDeleteRequest.cs    # Typed N-DELETE request
│       ├── NActionRequest.cs    # Typed N-ACTION request
│       ├── NEventReportRequest.cs # Typed N-EVENT-REPORT request
│       ├── NServiceResponse.cs  # Common N-Service response (status + optional dataset)
│       ├── NServiceScu.cs       # Generic N-Service SCU (send any N-request, receive response)
│       ├── INCreateHandler.cs   # Server-side handler interface
│       ├── INSetHandler.cs
│       ├── INGetHandler.cs
│       ├── INDeleteHandler.cs
│       ├── INActionHandler.cs
│       ├── INEventReportHandler.cs
│       ├── Mpps/
│       │   ├── MppsStatus.cs         # InProgress / Completed / Discontinued enum
│       │   ├── MppsInstance.cs        # Typed IOD helper
│       │   ├── MppsScu.cs            # MPPS SCU (N-CREATE + N-SET convenience)
│       │   ├── MppsScpHandler.cs     # MPPS SCP with state machine
│       │   └── IMppsPersistence.cs   # Pluggable persistence interface
│       └── StorageCommitment/
│           ├── StorageCommitmentRequest.cs   # Typed request
│           ├── StorageCommitmentResult.cs    # Typed result
│           ├── StorageCommitmentScu.cs       # SCU (N-ACTION + receive N-EVENT-REPORT)
│           ├── StorageCommitmentScpHandler.cs # SCP handler
│           └── IStorageVerifier.cs           # User-implemented verification callback
├── Items/
│   └── UserInformation.cs       # (extend) add MaxOperationsInvoked/Performed
├── Pdu/
│   ├── PduWriter.cs             # (extend) write 0x53 sub-item
│   └── PduReader.cs             # (extend) read 0x53 sub-item
├── AsyncOperations/
│   ├── AsyncOperationsWindow.cs # Negotiation and Channel-based enforcement
│   └── OperationSlot.cs         # Outstanding operation tracking
├── DicomClient.cs               # (extend) support N-Service dispatch
├── DicomClientOptions.cs        # (extend) AsyncOperationsInvoked/Performed
├── DicomServer.cs               # (extend) N-Service dispatch in DIMSE loop
└── DicomServerOptions.cs        # (extend) N-Service handler registration
```

### Pattern 1: N-Service Command Factory Methods
**What:** Extend `DicomCommand` with static factory methods for all six N-Services, following the exact pattern of existing C-Service factories.
**When to use:** Every N-Service request and response.
**Example:**
```csharp
// Source: Existing DicomCommand.CreateCStoreRequest pattern
public static DicomCommand CreateNCreateRequest(
    ushort messageId,
    DicomUID affectedSopClassUid,
    DicomUID? affectedSopInstanceUid = null)
{
    var ds = new DicomDataset();
    AddUidElement(ds, DicomTag.AffectedSOPClassUID, affectedSopClassUid);
    AddUInt16Element(ds, DicomTag.CommandField, CommandField.NCreateRequest);
    AddUInt16Element(ds, DicomTag.MessageID, messageId);
    AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
    if (affectedSopInstanceUid.HasValue)
        AddUidElement(ds, DicomTag.AffectedSOPInstanceUID, affectedSopInstanceUid.Value);
    return new DicomCommand(ds);
}
```

### Pattern 2: Separate Handler Interfaces per N-Service
**What:** Each N-Service gets its own handler interface. Implementations register only the interfaces they support.
**When to use:** Server-side handler dispatch for N-Service requests.
**Example:**
```csharp
// Source: Existing ICStoreHandler pattern
public interface INCreateHandler
{
    ValueTask<NServiceResponse> OnNCreateAsync(
        NCreateRequestContext context,
        DicomDataset? dataset,
        CancellationToken ct);
}

public interface INSetHandler
{
    ValueTask<NServiceResponse> OnNSetAsync(
        NSetRequestContext context,
        DicomDataset? modificationList,
        CancellationToken ct);
}
```

### Pattern 3: Typed SCU Classes Taking DicomClient
**What:** Each application-level feature (MPPS, Storage Commitment) gets a typed SCU class that wraps DicomClient.
**When to use:** Client-side DIMSE operations.
**Example:**
```csharp
// Source: Existing CStoreScu, CFindScu pattern
public sealed class MppsScu
{
    private readonly DicomClient _client;
    public MppsScu(DicomClient client) { _client = client; }

    public async ValueTask<NServiceResponse> CreateAsync(
        MppsInstance instance,
        CancellationToken ct = default)
    {
        var context = _client.GetAcceptedContext(DicomUID.ModalityPerformedProcedureStep);
        // ... N-CREATE request ...
    }
}
```

### Pattern 4: N-EVENT-REPORT as Server-Initiated Push
**What:** The server can push N-EVENT-REPORT messages to a connected SCU at any time during an established association.
**When to use:** Storage Commitment results, MPPS notifications.
**Key consideration:** This is the ONLY DIMSE message that can be initiated by the SCP side. The DicomServer needs a mechanism to send unsolicited messages on an active association, and the DicomClient needs a callback/event mechanism to receive them.

### Pattern 5: Channel-Based Async Operations Window
**What:** Use `System.Threading.Channels.Channel<T>.CreateBounded()` to enforce the negotiated async operations window.
**When to use:** When `MaxOperationsInvoked > 1` is negotiated.
**Example:**
```csharp
// Bounded channel enforces window size naturally
var channel = Channel.CreateBounded<OperationSlot>(
    new BoundedChannelOptions(negotiatedWindowSize)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
```

### Anti-Patterns to Avoid
- **Mixing Affected vs Requested SOP UIDs:** N-CREATE and N-EVENT-REPORT use `AffectedSOPClassUID`/`AffectedSOPInstanceUID`. N-SET, N-GET, N-DELETE, N-ACTION use `RequestedSOPClassUID`/`RequestedSOPInstanceUID`. Getting this wrong causes interop failures.
- **Unbounded async ops buffering:** Never queue more requests than the negotiated window allows. Use BoundedChannel, not ConcurrentQueue.
- **Blocking on N-EVENT-REPORT delivery:** Storage Commitment results may arrive on a different association. The SCU must be prepared to receive them asynchronously.
- **Mutable MPPS state without thread safety:** Multiple associations may try to update the same MPPS instance concurrently. The persistence layer must handle this.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Async backpressure pipeline | Custom semaphore + queue | System.Threading.Channels BoundedChannel | Edge cases in cancellation, completion, thread safety. Channels handle all of this. |
| MPPS state machine | Ad-hoc if/else chains | Explicit enum + validated transitions | State machines with ad-hoc logic become untestable. A simple enum + switch is deterministic. |
| UID generation for MPPS instances | Custom random string | `DicomUID.Generate()` or UUID-based scheme | Must be globally unique. The existing DicomUID struct already has generation support. |
| Transaction UID correlation | Dictionary<string, TaskCompletionSource> | Keep it simple -- but ensure thread-safe. Use ConcurrentDictionary. | Storage Commitment results may arrive on different threads/associations. |

**Key insight:** The N-Service DIMSE protocol is mechanically identical to C-Services (command dataset + optional data dataset in P-DATA PDVs). The complexity is in the application-level semantics (MPPS state machine, Storage Commitment correlation), not the wire protocol.

## Common Pitfalls

### Pitfall 1: Affected vs Requested SOP Instance UID Confusion
**What goes wrong:** Using AffectedSOPInstanceUID (0000,1000) when the spec requires RequestedSOPInstanceUID (0000,1001) or vice versa.
**Why it happens:** C-Services exclusively use Affected UIDs. Developers assume N-Services are the same.
**How to avoid:** N-CREATE and N-EVENT-REPORT use Affected UIDs. N-SET, N-GET, N-DELETE, N-ACTION use Requested UIDs. Encode this rule in the typed request classes -- each request type sets the correct tag.
**Warning signs:** Remote PACS returns "Invalid SOP Instance" or ignores the instance reference entirely.

### Pitfall 2: Storage Commitment Reverse Association
**What goes wrong:** The SCU sends N-ACTION but never receives the N-EVENT-REPORT result because it closed the association and isn't listening for incoming connections.
**Why it happens:** Storage Commitment results can arrive on a different association initiated by the SCP connecting back to the SCU.
**How to avoid:** Implement both modes: (1) same-association mode where the SCU waits on the same association for N-EVENT-REPORT, and (2) reverse-association mode where the SCU starts a mini DicomServer to accept the callback. Make the default same-association for simplicity.
**Warning signs:** N-ACTION returns success but no result is ever received.

### Pitfall 3: MPPS State Machine Violations
**What goes wrong:** Allowing N-SET to change status from Completed back to InProgress, or setting Completed without required attributes.
**Why it happens:** Missing validation on the SCP side.
**How to avoid:** Framework-enforced state machine: only InProgress -> Completed and InProgress -> Discontinued transitions are valid. Return status 0x0106 (Invalid Attribute Value) for invalid transitions. Validate PerformedSeriesSequence is non-empty before accepting Completed.
**Warning signs:** MPPS instance stuck in InProgress state, or inconsistent state across systems.

### Pitfall 4: Async Operations Window Negotiation Asymmetry
**What goes wrong:** Client proposes MaxOperationsInvoked=10 but server doesn't support it, responds with MaxOperationsInvoked=1. Client ignores the response and sends 10 concurrent operations, causing the server to abort.
**Why it happens:** Not reading the A-ASSOCIATE-AC 0x53 sub-item to determine the negotiated (possibly reduced) values.
**How to avoid:** The actual window size is the MINIMUM of what was proposed and what was accepted. Parse the 0x53 sub-item from A-ASSOCIATE-AC. If absent from the response, default is 1 (synchronous only).
**Warning signs:** Association aborts after second concurrent DIMSE operation.

### Pitfall 5: System.Threading.Channels on netstandard2.0 Missing ReadAllAsync
**What goes wrong:** Using `ChannelReader.ReadAllAsync()` which is not available on netstandard2.0 because it requires `IAsyncEnumerable<T>`.
**Why it happens:** API exists on net8.0+ but not netstandard2.0.
**How to avoid:** Use `reader.WaitToReadAsync()` + `reader.TryRead()` loop instead of `ReadAllAsync()`. The project already has `Microsoft.Bcl.AsyncInterfaces` as a dependency for `IAsyncEnumerable` support, but `ReadAllAsync` extension method is still missing in the Channel package for ns2.0.
**Warning signs:** Compilation error on netstandard2.0 target.

### Pitfall 6: N-EVENT-REPORT SCP-to-SCU Direction
**What goes wrong:** Treating N-EVENT-REPORT like other N-Services where the SCU initiates and SCP responds. In practice, the SCP initiates N-EVENT-REPORT and the SCU responds.
**Why it happens:** All other DIMSE services follow SCU-initiates/SCP-responds. N-EVENT-REPORT is the exception.
**How to avoid:** Both DicomClient and DicomServer need bidirectional message handling capability. The DicomClient must be able to receive unsolicited N-EVENT-REPORT-RQ and send N-EVENT-REPORT-RSP. The DicomServer must be able to send N-EVENT-REPORT-RQ.
**Warning signs:** Storage Commitment N-EVENT-REPORT never delivered because server can't send requests, or client can't handle receiving requests.

## Code Examples

Verified patterns from the existing codebase:

### DicomCommand Factory Pattern (from existing DicomCommand.cs)
```csharp
// Source: src/SharpDicom/Network/Dimse/DicomCommand.cs lines 296-310
public static DicomCommand CreateCStoreRequest(
    ushort messageId,
    DicomUID sopClassUid,
    DicomUID sopInstanceUid,
    ushort priority = 0)
{
    var ds = new DicomDataset();
    AddUidElement(ds, DicomTag.AffectedSOPClassUID, sopClassUid);
    AddUInt16Element(ds, DicomTag.CommandField, Dimse.CommandField.CStoreRequest);
    AddUInt16Element(ds, DicomTag.MessageID, messageId);
    AddUInt16Element(ds, DicomTag.Priority, priority);
    AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
    AddUidElement(ds, DicomTag.AffectedSOPInstanceUID, sopInstanceUid);
    return new DicomCommand(ds);
}
```

### SCU Class Pattern (from existing CFindScu.cs)
```csharp
// Source: src/SharpDicom/Network/Dimse/Services/CFindScu.cs lines 38-61
public sealed class CFindScu
{
    private readonly DicomClient _client;
    private readonly CFindOptions _options;
    private int _messageIdCounter;

    public CFindScu(DicomClient client, CFindOptions? options = null)
    {
        _client = client;
        _options = options ?? CFindOptions.Default;
    }

    private ushort NextMessageId() =>
        (ushort)System.Threading.Interlocked.Increment(ref _messageIdCounter);
}
```

### Server Handler Dispatch Pattern (from existing DicomServer.cs)
```csharp
// Source: src/SharpDicom/Network/DicomServer.cs lines 434-456
// In HandlePDataAsync, command field is checked and dispatched:
switch (qrCmd.CommandFieldValue)
{
    case CommandFields.CFindRequest:
        await HandleCFindAsync(stream, association, qrCmd, ct);
        break;
    case CommandFields.CMoveRequest:
        await HandleCMoveAsync(stream, association, qrCmd, ct);
        break;
    // ... add N-Service cases here
}
```

### UserInformation Sub-Item Writing Pattern (from existing PduWriter.cs)
```csharp
// Source: src/SharpDicom/Network/Pdu/PduWriter.cs lines 409-416
// Pattern for writing a sub-item:
private void WriteMaxPduLength(uint maxLength)
{
    WriteVariableItemHeader(ItemType.MaximumLength, 4);
    var span = _writer.GetSpan(4);
    BinaryPrimitives.WriteUInt32BigEndian(span, maxLength);
    _writer.Advance(4);
}
// 0x53 sub-item follows identical pattern with 4 bytes (2x uint16 BE)
```

### N-CREATE Command (to implement)
```csharp
// N-CREATE-RQ per PS3.7 Section 10.3.5
// Uses AffectedSOPClassUID and optionally AffectedSOPInstanceUID
// CommandDataSetType indicates attribute list present
public static DicomCommand CreateNCreateRequest(
    ushort messageId,
    DicomUID affectedSopClassUid,
    DicomUID? affectedSopInstanceUid = null)
{
    var ds = new DicomDataset();
    AddUidElement(ds, DicomTag.AffectedSOPClassUID, affectedSopClassUid);
    AddUInt16Element(ds, DicomTag.CommandField, CommandField.NCreateRequest);
    AddUInt16Element(ds, DicomTag.MessageID, messageId);
    AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
    if (affectedSopInstanceUid.HasValue)
        AddUidElement(ds, DicomTag.AffectedSOPInstanceUID, affectedSopInstanceUid.Value);
    return new DicomCommand(ds);
}
```

### N-SET Command (to implement)
```csharp
// N-SET-RQ per PS3.7 Section 10.3.3
// Uses RequestedSOPClassUID and RequestedSOPInstanceUID (NOT Affected)
public static DicomCommand CreateNSetRequest(
    ushort messageId,
    DicomUID requestedSopClassUid,
    DicomUID requestedSopInstanceUid)
{
    var ds = new DicomDataset();
    AddUidElement(ds, DicomTag.RequestedSOPClassUID, requestedSopClassUid);
    AddUInt16Element(ds, DicomTag.CommandField, CommandField.NSetRequest);
    AddUInt16Element(ds, DicomTag.MessageID, messageId);
    AddUInt16Element(ds, DicomTag.CommandDataSetType, DataSetPresent);
    AddUidElement(ds, DicomTag.RequestedSOPInstanceUID, requestedSopInstanceUid);
    return new DicomCommand(ds);
}
```

### Async Operations Window Sub-Item (0x53, to implement)
```csharp
// Per PS3.7 D.3.3.3: 0x53 sub-item is 4 bytes
// MaxOperationsInvoked (2 bytes, BE) + MaxOperationsPerformed (2 bytes, BE)
// Value of 0 means unlimited
private void WriteAsyncOperationsWindow(ushort invoked, ushort performed)
{
    WriteVariableItemHeader(ItemType.AsynchronousOperationsWindow, 4);
    var span = _writer.GetSpan(4);
    BinaryPrimitives.WriteUInt16BigEndian(span, invoked);
    BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2), performed);
    _writer.Advance(4);
}
```

## DICOM Protocol Details

### N-Service Command Field Summary

| Service | Request Code | Response Code | Uses Affected UIDs | Uses Requested UIDs | Special Fields |
|---------|-------------|---------------|--------------------|--------------------|----------------|
| N-EVENT-REPORT | 0x0100 | 0x8100 | Yes (Class + Instance) | No | EventTypeID (M) |
| N-GET | 0x0110 | 0x8110 | No | Yes (Class + Instance) | AttributeIdentifierList (U) |
| N-SET | 0x0120 | 0x8120 | No | Yes (Class + Instance) | (none) |
| N-ACTION | 0x0130 | 0x8130 | No | Yes (Class + Instance) | ActionTypeID (M) |
| N-CREATE | 0x0140 | 0x8140 | Yes (Class + Instance) | No | (none) |
| N-DELETE | 0x0150 | 0x8150 | No | Yes (Class + Instance) | (none) |

### MPPS SOP Class UIDs
| SOP Class | UID |
|-----------|-----|
| Modality Performed Procedure Step SOP Class | 1.2.840.10008.3.1.2.3.3 |

### Storage Commitment SOP Class UIDs
| SOP Class | UID |
|-----------|-----|
| Storage Commitment Push Model SOP Class | 1.2.840.10008.1.20.1.1 |
| Storage Commitment Push Model SOP Instance | 1.2.840.10008.1.20.1 (well-known instance) |

### Storage Commitment Event Type IDs
| Event Type ID | Meaning |
|--------------|---------|
| 1 | Storage Commitment Request Successful |
| 2 | Storage Commitment Request - Failures Exist |

### Storage Commitment Action Type ID
| Action Type ID | Meaning |
|---------------|---------|
| 1 | Storage Commitment Request |

### MPPS State Machine
```
        N-CREATE (status=IN PROGRESS)
               |
               v
         [IN PROGRESS]
           /         \
     N-SET            N-SET
   (COMPLETED)    (DISCONTINUED)
       /                 \
      v                   v
 [COMPLETED]        [DISCONTINUED]
  (terminal)          (terminal)
```

### Async Operations Window Sub-Item (0x53)
```
Byte 1:    Item Type (0x53)
Byte 2:    Reserved (0x00)
Bytes 3-4: Item Length (0x0004, Big-Endian)
Bytes 5-6: Maximum Number Operations Invoked (Big-Endian uint16)
Bytes 7-8: Maximum Number Operations Performed (Big-Endian uint16)
```
- Value of 0 = unlimited operations
- Default (if sub-item absent) = 1 invoked, 1 performed (synchronous only)
- Negotiation: SCU proposes in A-ASSOCIATE-RQ, SCP may reduce in A-ASSOCIATE-AC

## Existing Infrastructure Inventory

### Already Exists (No Changes Needed)
| Component | Location | What's There |
|-----------|----------|-------------|
| CommandField constants | `Network/Dimse/CommandField.cs` | All 6 N-Service request/response values (NCreateRequest=0x0140, etc.) |
| DicomTag well-known tags | `Data/DicomTag.WellKnown.cs` | RequestedSOPClassUID, RequestedSOPInstanceUID, EventTypeID, AttributeIdentifierList, ActionTypeID |
| VR mapping for command elements | `DicomClient.GetCommandVR()` | Elements 0x1001 (UI), 0x1002 (US), 0x1005 (AT), 0x1008 (US) all mapped |
| ItemType.AsynchronousOperationsWindow | `Network/Pdu/ItemType.cs` | 0x53 enum value defined |
| IsRequest/IsResponse helpers | `CommandField.cs` | Bit 15 check works for N-Services too |
| PduReader/PduWriter ref structs | `Network/Pdu/` | Variable item header read/write |

### Needs Extension
| Component | Location | What to Add |
|-----------|----------|------------|
| DicomCommand | `Network/Dimse/DicomCommand.cs` | 12 factory methods (6 request + 6 response), convenience properties (IsNCreateRequest, etc.) |
| DicomCommand | `Network/Dimse/DicomCommand.cs` | Properties: `EventTypeID`, `ActionTypeID`, `RequestedSOPInstanceUID`, `RequestedSOPClassUID` |
| UserInformation | `Network/Items/UserInformation.cs` | `MaxOperationsInvoked` and `MaxOperationsPerformed` properties |
| PduWriter | `Network/Pdu/PduWriter.cs` | `WriteAsyncOperationsWindow()` method, update `WriteUserInformation()` and `CalculateVariableItemsLength()` |
| PduReader | `Network/Pdu/PduReader.cs` | `TryReadAsyncOperationsWindow()` method |
| DicomClient | `Network/DicomClient.cs` | Parse 0x53 from A-ASSOCIATE-AC, N-EVENT-REPORT reception |
| DicomClientOptions | `Network/DicomClientOptions.cs` | `AsyncOperationsInvoked`, `AsyncOperationsPerformed` properties |
| DicomServer | `Network/DicomServer.cs` | N-Service command dispatch in `HandlePDataAsync`, N-EVENT-REPORT sending |
| DicomServerOptions | `Network/DicomServerOptions.cs` | N-Service handler registration properties |
| DicomUID.WellKnown | `Data/DicomUID.WellKnown.cs` | MPPS and Storage Commitment SOP Class/Instance UIDs |
| DicomStatus | `Network/DicomStatus.cs` | N-Service specific status codes (0x0106 Invalid Attribute Value, etc.) |
| FoDicom5.Compat DicomClient | `SharpDicom.FoDicom5.Compat/Network/Client/DicomClient.cs` | Wire `NegotiateAsyncOps()` to actual negotiation instead of throwing |

### New Files Needed
| Component | Purpose |
|-----------|---------|
| 6 typed request classes | NCreateRequest, NSetRequest, NGetRequest, NDeleteRequest, NActionRequest, NEventReportRequest |
| NServiceResponse | Common response type for N-Services |
| 6 handler interfaces | INCreateHandler through INEventReportHandler |
| NServiceScu | Generic N-Service SCU built on DicomClient primitives |
| MPPS types | MppsStatus enum, MppsInstance, MppsScu, MppsScpHandler, IMppsPersistence, InMemoryMppsPersistence |
| Storage Commitment types | StorageCommitmentRequest, StorageCommitmentResult, SopInstanceReference, StorageCommitmentScu, StorageCommitmentScpHandler, IStorageVerifier |
| Async Operations | AsyncOperationsWindow, operation slot tracking |

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Storage Commitment Pull Model | Push Model only | DICOM Supplement 68 (retired Pull) | Only implement Push Model. Pull Model was retired. |
| Synchronous-only DIMSE (1:1 ops) | Async Operations Window | Part of original standard, rarely used | Most PACS default to 1:1. Phase 28 adds support but defaults remain synchronous. |
| fo-dicom NegotiateAsyncOps as no-op | Actual negotiation + enforcement | Phase 28 | SharpDicom will actually negotiate and enforce, unlike fo-dicom which treats it as hint-only. |

**Deprecated/outdated:**
- Storage Commitment Pull Model (retired per Supplement 68): Do not implement.
- Asynchronous Operations with values of 0,0 in fo-dicom meaning "use defaults": In SharpDicom, 0 means unlimited per the DICOM spec.

## Open Questions

1. **DicomCommand helper duplication**
   - What we know: `DicomClient` has `GetCommandVR()` and `SerializeCommand()`/`ParseCommandDataset()` as private methods. The `DicomServer` has its own command parsing (`ParseCommandField`, `ParseMessageId`).
   - What's unclear: Should N-Service command creation/parsing reuse these or should there be a shared utility?
   - Recommendation: The existing duplication should be addressed, but that's a refactoring concern outside Phase 28 scope. For Phase 28, extend DicomCommand with factory methods and add convenience properties -- this is the established pattern.

2. **N-EVENT-REPORT bidirectional message flow**
   - What we know: Currently DicomClient can only send requests and receive responses. DicomServer can only receive requests and send responses. N-EVENT-REPORT requires the SCP to send a request-like message and the SCU to respond.
   - What's unclear: How to retrofit this into the existing DIMSE loop without a major refactor.
   - Recommendation: For same-association Storage Commitment, the simplest approach is to have the SCU's receive loop check for incoming N-EVENT-REPORT-RQ alongside expected responses. For reverse-association mode, the SCU starts a temporary DicomServer.

3. **Async Operations Window -- scope of enforcement**
   - What we know: The 0x53 sub-item sets a per-association limit on outstanding operations.
   - What's unclear: Whether enforcement should be at the DicomClient level (queue requests) or at a higher layer (per-SCU class).
   - Recommendation: Enforce at DicomClient level. All DIMSE operations go through DicomClient, so it's the natural choke point. Use a bounded Channel to gate `SendDimseRequestAsync`.

## Sources

### Primary (HIGH confidence)
- DICOM PS3.7 Section 10 (N-DIMSE Services): Command field values, Affected vs Requested UID rules, EventTypeID/ActionTypeID semantics -- verified against existing CommandField.cs values
- DICOM PS3.7 Annex D.3.3.3 (Async Operations Window): 0x53 sub-item encoding (4 bytes: 2x uint16 BE) -- verified ItemType.cs enum value exists
- DICOM PS3.7 Annex E (Command Dictionary): All command element tags/VRs -- verified against DicomTag.WellKnown.cs and DicomClient.GetCommandVR()
- Existing SharpDicom codebase: All pattern analysis based on direct file reads of CommandField.cs, DicomCommand.cs, DicomClient.cs, DicomServer.cs, CFindScu.cs, CStoreScu.cs, ICStoreHandler.cs, PduWriter.cs, PduReader.cs, ItemType.cs, UserInformation.cs, DicomClientOptions.cs, DicomServerOptions.cs

### Secondary (MEDIUM confidence)
- [DICOM PS3.4 Section F.7](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_F.7.2.html) - MPPS N-CREATE/N-SET operations, state machine, SOP Class UID
- [DICOM PS3.4 Section J.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_j.3.2.html) - Storage Commitment Push Model, N-ACTION/N-EVENT-REPORT, Transaction UID
- [NuGet System.Threading.Channels](https://www.nuget.org/packages/System.Threading.Channels) - Confirmed netstandard2.0 support, version 10.0.2

### Tertiary (LOW confidence)
- pynetdicom MPPS examples at https://pydicom.github.io/pynetdicom/stable/examples/mpps.html - Implementation reference (Python, not .NET, but useful for protocol understanding)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Only one NuGet dependency (System.Threading.Channels), confirmed netstandard2.0 support
- Architecture: HIGH - Direct extension of established, working patterns in the codebase
- Protocol details: HIGH - All command field values verified against existing code; N-Service wire protocol is mechanically identical to C-Services
- MPPS workflow: MEDIUM - State machine and SOP Class UIDs verified from NEMA docs, but IOD attribute details need validation during implementation
- Storage Commitment: MEDIUM - Protocol flow verified, but reverse-association implementation complexity requires care during design
- Async Operations Window: HIGH - Simple 4-byte sub-item, encoding verified from DICOM PS3.7 D.3.3.3

**Research date:** 2026-02-07
**Valid until:** 2026-03-09 (stable domain -- DICOM standard changes infrequently)
