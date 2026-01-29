---
phase: 11-dimse-services
plan: 01
subsystem: network
tags: [dimse, query-retrieve, progress, command]
dependency-graph:
  requires: [phase-10]
  provides: [dimse-types, command-factory, client-primitives]
  affects: [11-02, 11-03, 11-04, 11-05]
tech-stack:
  added: []
  patterns: [readonly-record-struct, extension-methods, factory-pattern]
key-files:
  created:
    - src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs
    - src/SharpDicom/Network/Dimse/SubOperationProgress.cs
    - src/SharpDicom/Network/Dimse/DicomTransferProgress.cs
    - tests/SharpDicom.Tests/Network/Dimse/DimseTypesTests.cs
  modified:
    - src/SharpDicom/Network/Dimse/DicomCommand.cs
    - src/SharpDicom/Network/DicomClient.cs
decisions:
  - id: 11-01-01
    title: "Readonly record struct for progress types"
    choice: "SubOperationProgress and DicomTransferProgress as readonly record struct"
    rationale: "Value semantics, immutable, built-in equality, zero-allocation for high-frequency progress reporting"
  - id: 11-01-02
    title: "Extension methods for QueryRetrieveLevel"
    choice: "Static extension class instead of instance methods on enum"
    rationale: "Enums cannot have methods; extensions provide fluent API for ToDicomValue(), Parse(), GetSopClassUid()"
  - id: 11-01-03
    title: "Internal visibility for DicomClient DIMSE primitives"
    choice: "SendDimseRequestAsync, ReceiveDimseResponseAsync, SendCCancelAsync marked internal"
    rationale: "SCU service classes in same assembly can use them; public API will be the service classes (CFindScu, CMoveScu, etc.)"
  - id: 11-01-04
    title: "Existing well-known tags and UIDs verified"
    choice: "No changes needed to DicomTag.WellKnown.cs or DicomUID.WellKnown.cs"
    rationale: "All required command tags (MoveDestination, NumberOf*Suboperations) and Q/R SOP Class UIDs already present from Phase 10"
metrics:
  duration: "8 minutes"
  completed: "2026-01-29"
---

# Phase 11 Plan 01: Common DIMSE Types Summary

**One-liner**: QueryRetrieveLevel enum, progress structs, and DicomCommand factory methods for C-FIND/C-MOVE/C-GET operations.

## What Was Built

### 1. QueryRetrieveLevel Enum (QueryRetrieveLevel.cs)

Enum defining the four Q/R levels with extension methods:

```csharp
public enum QueryRetrieveLevel { Patient, Study, Series, Image }

// Extension methods
level.ToDicomValue()                              // "PATIENT", "STUDY", etc.
QueryRetrieveLevelExtensions.Parse("STUDY")       // Study level
QueryRetrieveLevelExtensions.TryParse("IMAGE", out var l)
level.GetPatientRootSopClassUid(CommandField.CFindRequest)  // Q/R SOP Class UIDs
level.GetStudyRootSopClassUid(CommandField.CMoveRequest)
```

### 2. SubOperationProgress Struct (SubOperationProgress.cs)

Tracks C-MOVE/C-GET sub-operation counts:

```csharp
public readonly record struct SubOperationProgress(
    ushort Remaining,
    ushort Completed,
    ushort Failed,
    ushort Warning)
{
    public ushort Total { get; }          // Sum of all counts
    public bool IsFinal { get; }          // Remaining == 0
    public bool HasErrors { get; }        // Failed > 0
    public bool HasWarnings { get; }      // Warning > 0

    public static SubOperationProgress Empty { get; }
    public static SubOperationProgress Successful(ushort count);
}
```

### 3. DicomTransferProgress Struct (DicomTransferProgress.cs)

Reports C-STORE transfer progress:

```csharp
public readonly record struct DicomTransferProgress(
    long BytesTransferred,
    long TotalBytes,
    double BytesPerSecond)
{
    public double PercentComplete { get; }           // 0-100
    public TimeSpan? EstimatedTimeRemaining { get; } // Null if unknown
    public bool IsComplete { get; }

    public static DicomTransferProgress Initial(long totalBytes);
    public static DicomTransferProgress Completed(long totalBytes, double rate);
}
```

### 4. DicomCommand Extensions (DicomCommand.cs)

Added properties for sub-operation tracking:

```csharp
public string? MoveDestination { get; }
public ushort Priority { get; }
public ushort NumberOfRemainingSuboperations { get; }
public ushort NumberOfCompletedSuboperations { get; }
public ushort NumberOfFailedSuboperations { get; }
public ushort NumberOfWarningSuboperations { get; }
public SubOperationProgress GetSubOperationProgress();
```

Added factory methods for remaining DIMSE-C commands:

```csharp
// C-FIND
DicomCommand.CreateCFindRequest(messageId, sopClassUid, priority)
DicomCommand.CreateCFindResponse(messageId, sopClassUid, status)

// C-MOVE
DicomCommand.CreateCMoveRequest(messageId, sopClassUid, moveDestination, priority)
DicomCommand.CreateCMoveResponse(messageId, sopClassUid, status, progress)

// C-GET
DicomCommand.CreateCGetRequest(messageId, sopClassUid, priority)
DicomCommand.CreateCGetResponse(messageId, sopClassUid, status, progress)

// C-CANCEL
DicomCommand.CreateCCancelRequest(messageIdBeingCancelled)
```

### 5. DicomClient DIMSE Primitives (DicomClient.cs)

Internal methods for SCU service classes:

```csharp
internal ValueTask SendDimseRequestAsync(pcid, command, dataset, ct);
internal ValueTask<(DicomCommand, DicomDataset?)> ReceiveDimseResponseAsync(ct);
internal ValueTask SendCCancelAsync(pcid, messageIdBeingCancelled, ct);
internal ushort NextMessageId();
internal PresentationContext GetFirstAcceptedContext();
internal PresentationContext? GetAcceptedContext(sopClassUid);
```

Supporting private methods:
- `SendDatasetAsync` - Serializes dataset with negotiated transfer syntax
- `ReceiveDatasetAsync` - Receives and parses data PDVs
- `SerializeDataset` - Uses DicomStreamWriter for encoding
- `ParseDataset` - Simple Implicit VR LE parser for datasets

### 6. Unit Tests (DimseTypesTests.cs)

45 comprehensive tests covering:
- QueryRetrieveLevel enum and extensions
- SubOperationProgress calculated properties
- DicomTransferProgress calculated properties
- All DicomCommand factory methods
- GetSubOperationProgress extraction

## Implementation Notes

### Transfer Syntax Handling

- Commands are always Implicit VR Little Endian per PS3.7
- Datasets use negotiated transfer syntax from presentation context
- Dataset parsing currently simplified (Implicit VR LE fallback)

### Well-Known Tags (Already Present)

Verified that Phase 10 already added all required command tags:
- MoveDestination (0000,0600)
- Priority (0000,0700)
- NumberOfRemainingSuboperations (0000,1020)
- NumberOfCompletedSuboperations (0000,1021)
- NumberOfFailedSuboperations (0000,1022)
- NumberOfWarningSuboperations (0000,1023)

And Q/R SOP Class UIDs (Patient Root and Study Root for FIND/MOVE/GET).

## Deviations from Plan

None - plan executed exactly as written.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 64e5c33 | feat | QueryRetrieveLevel enum and progress structs |
| f41d2f6 | feat | DicomCommand factory methods for C-FIND/C-MOVE/C-GET/C-CANCEL |
| d8e55bc | feat | Internal DIMSE primitive methods in DicomClient |
| 42c9fe1 | test | Comprehensive unit tests for DIMSE types |

## Test Results

- **New tests**: 45
- **All tests passing**: 1357 succeeded, 5 skipped (DCMTK integration)
- **Build**: Zero warnings in Release configuration

## Next Phase Readiness

This plan provides the foundation for:
- **11-02**: C-STORE SCU (uses DicomTransferProgress, SendDimseRequestAsync)
- **11-03**: C-STORE SCP (uses DicomCommand.CreateCStoreResponse)
- **11-04**: C-FIND SCU (uses QueryRetrieveLevel, CreateCFindRequest, ReceiveDimseResponseAsync)
- **11-05**: C-MOVE/C-GET SCU (uses SubOperationProgress, CreateCMoveRequest/CreateCGetRequest)

No blockers identified.
