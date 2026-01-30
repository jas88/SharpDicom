---
phase: 11-dimse-services
plan: 05
name: C-MOVE SCU Service
subsystem: network-dimse
tags: [c-move, scu, qr, dimse, retrieval]
dependency-graph:
  requires: ["11-01", "11-03"]
  provides: ["CMoveScu", "CMoveOptions", "CMoveProgress"]
  affects: ["11-07"]
tech-stack:
  added: []
  patterns: ["IAsyncEnumerable streaming", "third-party destination retrieval"]
key-files:
  created:
    - src/SharpDicom/Network/Dimse/Services/CMoveOptions.cs
    - src/SharpDicom/Network/Dimse/Services/CMoveProgress.cs
    - src/SharpDicom/Network/Dimse/Services/CMoveScu.cs
    - tests/SharpDicom.Tests/Network/Dimse/CMoveScuTests.cs
  modified: []
decisions:
  - id: cmove-no-receiver
    choice: "C-MOVE SCU does not receive data"
    rationale: "C-MOVE sends data to third-party destination; SCU only gets progress updates"
  - id: consistent-patterns
    choice: "Follow CFindScu/CGetScu patterns"
    rationale: "Consistent API across all Q/R SCU services"
  - id: destination-validation
    choice: "Validate destinationAE early"
    rationale: "Fail fast on empty destination rather than network error"
metrics:
  duration: 3m 11s
  completed: 2026-01-29
  tests-added: 35
  tests-total: 1516
---

# Phase 11 Plan 05: C-MOVE SCU Service Summary

C-MOVE SCU service for retrieving DICOM data via third-party destination with IAsyncEnumerable progress streaming.

## What Was Built

### CMoveOptions (`src/SharpDicom/Network/Dimse/Services/CMoveOptions.cs`)
Configuration class for C-MOVE operations:
- **Timeout**: 120 seconds default (same as C-GET, longer than C-FIND due to sub-operations)
- **Priority**: MEDIUM (0) default, supports HIGH (1) and LOW (2)
- **UsePatientRoot**: true default, false for Study Root information model

### CMoveProgress (`src/SharpDicom/Network/Dimse/Services/CMoveProgress.cs`)
Progress tracking class for C-MOVE responses:
- **SubOperations**: Wraps SubOperationProgress (Remaining, Completed, Failed, Warning)
- **Status**: DIMSE status from response
- **IsFinal**: True when Remaining=0 or status is not Pending
- **IsSuccess**: True when IsFinal, Success status, and no failures
- **IsPartialSuccess**: True when IsFinal with some failures but also some completions

### CMoveScu (`src/SharpDicom/Network/Dimse/Services/CMoveScu.cs`)
C-MOVE Service Class User implementation:
- **MoveAsync(level, identifier, destinationAE, ct)**: IAsyncEnumerable streaming of progress
- **MoveAsync(query, destinationAE, ct)**: Fluent DicomQuery overload
- **C-CANCEL support**: Sends C-CANCEL on cancellation token
- **Status 0xA801 handling**: Throws DicomNetworkException for Move Destination Unknown

### CMoveScuTests (`tests/SharpDicom.Tests/Network/Dimse/CMoveScuTests.cs`)
35 unit tests covering:
- CMoveOptions defaults and modification
- CMoveProgress IsFinal, IsSuccess, IsPartialSuccess logic
- CMoveScu constructor validation
- DicomCommand.CreateCMoveRequest with MoveDestination field
- QueryRetrieveLevel Move SOP Class UID extensions
- Status 0xA801 verification

## Key Implementation Details

### C-MOVE vs C-GET Difference
C-MOVE differs from C-GET in that data is sent to a third-party destination AE, not returned to the SCU. The SCU only receives progress updates (C-MOVE-RSP with sub-operation counts).

```
SCU ----C-MOVE-RQ (dest=STORAGE)----> SCP
                                       |
                                       v
                               C-STORE-RQ to STORAGE AE
                                       |
SCU <---C-MOVE-RSP (Pending)---------- SCP
SCU <---C-MOVE-RSP (Pending)---------- SCP
SCU <---C-MOVE-RSP (Success)---------- SCP
```

### MoveDestination Field
The C-MOVE-RQ command includes MoveDestination (0000,0600) AE tag identifying where the SCP should send data. The destination must be configured in the SCP's AE table.

### Status 0xA801
When the SCP doesn't recognize the destination AE, it returns status 0xA801 (Move Destination Unknown). CMoveScu throws a DicomNetworkException with a clear message.

## Task Commits

| Task | Description | Commit |
|------|-------------|--------|
| 1 | CMoveOptions and CMoveProgress types | 03bf3c8 |
| 2 | CMoveScu service class | cad3bb1 |
| 3 | CMoveScuTests unit tests | a357c75 |

## Deviations from Plan

**None** - Plan executed exactly as written. The extension methods for QueryRetrieveLevel (GetPatientRootMoveSopClassUid, GetStudyRootMoveSopClassUid) already existed from Plan 11-01.

## Test Results

- **Tests added**: 35
- **Total tests**: 1516
- **Passed**: 1511
- **Skipped**: 5 (DCMTK integration tests)
- **Failed**: 0

## Files Changed

```
 src/SharpDicom/Network/Dimse/Services/CMoveOptions.cs    | 52 ++++++
 src/SharpDicom/Network/Dimse/Services/CMoveProgress.cs   | 99 ++++++++++++
 src/SharpDicom/Network/Dimse/Services/CMoveScu.cs        | 232 ++++++++++++++++++++++++++
 tests/SharpDicom.Tests/Network/Dimse/CMoveScuTests.cs    | 412 ++++++++++++++++++++++++++++++++++++++++++++
 4 files changed, 795 insertions(+)
```

## API Usage Example

```csharp
var client = new DicomClient(new DicomClientOptions
{
    Host = "pacs.hospital.org",
    Port = 104,
    CalledAE = "PACS",
    CallingAE = "WORKSTATION"
});

var contexts = new[]
{
    new PresentationContext(1, DicomUID.PatientRootQueryRetrieveMove,
        TransferSyntax.ImplicitVRLittleEndian)
};

await client.ConnectAsync(contexts, ct);

var moveScu = new CMoveScu(client);
var query = DicomQuery.ForStudies()
    .WithStudyInstanceUid("1.2.3.4.5.6.7.8.9");

await foreach (var progress in moveScu.MoveAsync(query, "STORAGE_AE", ct))
{
    var ops = progress.SubOperations;
    Console.WriteLine($"Progress: {ops.Completed}/{ops.Total} " +
                      $"(Failed: {ops.Failed}, Remaining: {ops.Remaining})");

    if (progress.IsFinal)
    {
        Console.WriteLine(progress.IsSuccess ? "All sent successfully" :
                         progress.IsPartialSuccess ? "Partial success" : "Failed");
    }
}
```

## Next Phase Readiness

Plan 11-05 is complete. C-MOVE SCU provides the standard PACS retrieval mechanism for sending data to third-party destinations. The remaining DIMSE services (C-FIND SCP, N-services) and integration tests can proceed.

**Requirements Addressed**:
- FR-10.8: C-MOVE SCU operations
