---
phase: 10-network-foundation
plan: 06
subsystem: Network
tags: [dicom-server, scp, c-echo, tcp-listener, async]

# Dependency Graph
requires: ["10-03", "10-04"]
provides: ["DicomServer", "DicomServerOptions", "IAssociationHandler", "C-ECHO-SCP"]
affects: ["10-07"]

# Tech Stack Tracking
tech-stack:
  added: []
  patterns: ["task-per-association", "semaphore-throttling", "artim-timer"]

# File Tracking
key-files:
  created:
    - src/SharpDicom/Network/DicomServer.cs
    - src/SharpDicom/Network/DicomServerOptions.cs
    - src/SharpDicom/Network/IAssociationHandler.cs
    - src/SharpDicom/Internal/ArrayBufferWriterPolyfill.cs
    - tests/SharpDicom.Tests/Network/DicomServerTests.cs
  modified: []

# Decisions
decisions:
  - id: no-logging-framework
    context: Plan suggested ILogger but project has no Microsoft.Extensions.Logging dependency
    decision: Implemented without logging to avoid architectural change
    rationale: Adding logging framework would be Rule 4 (architectural decision)
  - id: polyfill-arraybufferwriter
    context: ArrayBufferWriter<T> not available in netstandard2.0
    decision: Created internal polyfill in Internal/ArrayBufferWriterPolyfill.cs
    rationale: Required for PDU building without adding external dependencies
  - id: inline-cecho-parsing
    context: DicomCommand from 10-05 not available in stated dependencies
    decision: Inline DIMSE command parsing for C-ECHO only
    rationale: Plan 10-06 depends on 10-03/10-04, DIMSE infrastructure added but used inline

# Metrics
duration: 15m
completed: 2026-01-28
---

# Phase 10 Plan 06: DicomServer C-ECHO SCP Summary

DicomServer provides async SCP functionality with task-per-association model and C-ECHO verification support.

## Tasks Completed

- [x] Task 1: Handler interfaces and options (IAssociationHandler.cs, DicomServerOptions.cs)
- [x] Task 2: DicomServer implementation (DicomServer.cs, ArrayBufferWriterPolyfill.cs)
- [x] Task 3: DicomServer unit tests (DicomServerTests.cs)

## Implementation Details

### Handler Interfaces (`IAssociationHandler.cs`)

Created context and result types for handler callbacks:

- `AssociationRequestContext` - Provides CallingAE, CalledAE, RemoteEndPoint, RequestedContexts
- `AssociationRequestResult` - Static factory methods: `Accepted(contexts)`, `Rejected(result, source, reason)`
- `CEchoRequestContext` - Provides Association and MessageId for C-ECHO handling

### Server Options (`DicomServerOptions.cs`)

Configuration with validation:

- `Port` (1-65535), `BindAddress` (default Any)
- `AETitle` (1-16 chars, no leading/trailing spaces)
- `MaxAssociations` (default 100)
- `ArtimTimeout`, `ShutdownTimeout` (defaults 30s each)
- `MaxPduLength` (minimum 4096)
- `OnAssociationRequest` and `OnCEcho` Func delegates

### DicomServer (`DicomServer.cs`)

SCP implementation with:

- `IAsyncDisposable` with graceful shutdown
- Task-per-association model using `SemaphoreSlim` for throttling
- ARTIM timer enforcement via `CancellationTokenSource.CancelAfter`
- PDU handling: A-ASSOCIATE-RQ/AC/RJ, P-DATA-TF, A-RELEASE-RQ/RP, A-ABORT
- Inline C-ECHO request/response building without full DIMSE infrastructure

Key methods:
```csharp
public void Start();
public Task StopAsync();
public ValueTask DisposeAsync();
public bool IsListening { get; }
public int ActiveAssociations { get; }
```

### ArrayBufferWriter Polyfill

Created `ArrayBufferWriterPolyfill.cs` for netstandard2.0 compatibility since `ArrayBufferWriter<T>` is only available in .NET Core 2.1+ / .NET Standard 2.1+.

## Tests Added

27 new tests in `DicomServerTests.cs`:

- Options validation (12 tests): port, AE title, max associations, timeouts, PDU length
- Server lifecycle (6 tests): start/stop, dispose, double operations
- Handler wiring (3 tests): OnAssociationRequest, OnCEcho configuration
- Result/context classes (6 tests): factory methods, property initialization

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ArrayBufferWriter polyfill for netstandard2.0**
- **Found during:** Task 2
- **Issue:** ArrayBufferWriter<T> not available in netstandard2.0
- **Fix:** Created Internal/ArrayBufferWriterPolyfill.cs with compatible implementation
- **Files created:** src/SharpDicom/Internal/ArrayBufferWriterPolyfill.cs
- **Commit:** ebba59a

**2. [Rule 2 - Missing Critical] Inline C-ECHO command parsing**
- **Found during:** Task 2
- **Issue:** Plan code used DicomCommand from 10-05 but dependencies listed 10-03/10-04 only
- **Fix:** Inline DIMSE command parsing for C-ECHO (ParseCommandField, ParseMessageId, BuildCEchoResponseCommand)
- **Files modified:** src/SharpDicom/Network/DicomServer.cs
- **Commit:** ebba59a

**3. [Rule 1 - Bug] PduReader ref struct across await**
- **Found during:** Task 2
- **Issue:** PduReader is ref struct, cannot be preserved across await boundaries
- **Fix:** Extracted `ExtractCEchoRequests` method to complete parsing before any await
- **Files modified:** src/SharpDicom/Network/DicomServer.cs
- **Commit:** ebba59a

## Commits

| Hash | Message |
|------|---------|
| 58a7049 | feat(10-06): add handler interfaces and DicomServerOptions |
| ebba59a | feat(10-06): add DicomServer for SCP operations |
| 051b241 | feat(10-06): add DicomServer unit tests |

## Verification

```bash
# Build passes
dotnet build src/SharpDicom/SharpDicom.csproj --configuration Release

# All tests pass
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj
# Result: 1189 passed, 0 failed, 1 skipped

# DicomServer tests
dotnet test --project tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "DicomServerTests"
# Result: 27 passed, 0 failed
```

## Next Phase Readiness

Plan 10-07 (Integration Tests) can proceed. The DicomServer:
- Implements IAsyncDisposable
- Has task-per-association model with MaxAssociations throttling
- Supports C-ECHO via OnCEcho handler
- Enforces ARTIM timer
- Has graceful shutdown with configurable timeout

For full DCMTK integration testing, the inline C-ECHO handling should work, but more complex DIMSE operations will need the DicomCommand infrastructure from 10-05.
