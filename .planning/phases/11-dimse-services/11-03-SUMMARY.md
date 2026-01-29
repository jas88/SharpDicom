---
phase: 11
plan: 03
status: complete
subsystem: network/dimse-services
tags: [cfind, query, scu, async-enumerable, fluent-builder]

dependency-graph:
  requires: [11-01]
  provides: [CFindScu, DicomQuery, CFindOptions]
  affects: [11-07]

tech-stack:
  added: []
  patterns: [IAsyncEnumerable, fluent-builder, cancellation-token]

key-files:
  created:
    - src/SharpDicom/Network/Dimse/Services/CFindOptions.cs
    - src/SharpDicom/Network/Dimse/Services/DicomQuery.cs
    - src/SharpDicom/Network/Dimse/Services/CFindScu.cs
    - tests/SharpDicom.Tests/Network/Dimse/CFindScuTests.cs
  modified:
    - src/SharpDicom/Data/DicomTag.WellKnown.cs
    - src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs

decisions:
  - id: 11-03-01
    decision: "Fluent builder pattern for DicomQuery"
    rationale: "Provides intuitive API for common query patterns without manual dataset construction"
  - id: 11-03-02
    decision: "IAsyncEnumerable for query results"
    rationale: "Enables streaming of results as they arrive; efficient memory usage for large result sets"
  - id: 11-03-03
    decision: "C-CANCEL on CancellationToken"
    rationale: "Proper DICOM protocol compliance; gracefully stops remote enumeration"
  - id: 11-03-04
    decision: "Convenience Find SOP Class UID methods"
    rationale: "GetPatientRootFindSopClassUid() simpler than GetPatientRootSopClassUid(CommandField)"

metrics:
  duration: 7m
  completed: 2026-01-29
  tests-added: 31
  tests-total: 1416
  tests-passing: 1411
  tests-skipped: 5
---

# Phase 11 Plan 03: C-FIND SCU Summary

C-FIND SCU service class with fluent query builder and IAsyncEnumerable result streaming

## What Was Built

### CFindOptions
Options for C-FIND operations:
- `Timeout` (default 10s)
- `Priority` (0=MEDIUM, 1=HIGH, 2=LOW)
- `UsePatientRoot` (true for Patient Root, false for Study Root)

### DicomQuery Fluent Builder
Convenience builder for constructing C-FIND query identifiers:
- `ForPatients()`, `ForStudies()`, `ForSeries()`, `ForImages()` - level selection
- `WithPatientName()`, `WithPatientId()` - patient criteria
- `WithStudyDate()`, `WithStudyDateRange()` - date matching
- `WithModality()` - single or multiple modalities
- `WithAccessionNumber()` - accession matching
- `WithStudyInstanceUid()`, `WithSeriesInstanceUid()`, `WithSopInstanceUid()` - UID matching
- `ReturnField()` - request additional fields in results
- `ToDataset()` - convert to DicomDataset

### CFindScu Service Class
C-FIND SCU implementation:
- `QueryAsync(QueryRetrieveLevel, DicomDataset, CancellationToken)` - raw query
- `QueryAsync(DicomQuery, CancellationToken)` - fluent query
- Returns `IAsyncEnumerable<DicomDataset>` for streaming results
- C-CANCEL sent on CancellationToken cancellation
- Patient Root and Study Root information model support

### Supporting Changes
- Added well-known tags: `QueryRetrieveLevel`, `StudyDate`, `AccessionNumber`, `ModalitiesInStudy`, `StudyInstanceUID`, `SeriesInstanceUID`
- Added `GetPatientRootFindSopClassUid()` and `GetStudyRootFindSopClassUid()` extension methods

## API Examples

```csharp
// Create client and establish association
var client = new DicomClient(options);
await client.ConnectAsync(contexts, ct);

// Create C-FIND SCU
var findScu = new CFindScu(client);

// Fluent query
var query = DicomQuery.ForStudies()
    .WithPatientName("Smith*")
    .WithStudyDateRange(DateTime.Today.AddDays(-7), DateTime.Today)
    .WithModality("CT", "MR")
    .ReturnField(DicomTag.StudyDescription);

// Stream results
await foreach (var result in findScu.QueryAsync(query, ct))
{
    Console.WriteLine(result.GetString(DicomTag.PatientName));
}
```

## Commits

| Hash | Type | Description |
|------|------|-------------|
| e2f8eff | feat | Add CFindOptions and DicomQuery builder |
| 2cb4adf | feat | Add CFindScu service class with IAsyncEnumerable |
| 074efbb | test | Add CFindScu and DicomQuery unit tests (31 tests) |

## Test Coverage

- 31 new tests for CFindOptions, DicomQuery, and CFindScu
- Tests cover all query levels, builder methods, date formatting
- Tests verify zero-length return fields, constructor validation
- Full test suite: 1416 tests (1411 pass, 5 skipped DCMTK tests)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing Q/R well-known tags**
- **Found during:** Task 1
- **Issue:** DicomTag.WellKnown.cs lacked QueryRetrieveLevel, StudyDate, AccessionNumber, ModalitiesInStudy, StudyInstanceUID, SeriesInstanceUID
- **Fix:** Added all required tags to DicomTag.WellKnown.cs
- **Files modified:** src/SharpDicom/Data/DicomTag.WellKnown.cs

**2. [Rule 3 - Blocking] Missing convenience SOP Class UID methods**
- **Found during:** Task 2
- **Issue:** Existing GetPatientRootSopClassUid(ushort commandField) required CommandField parameter; CFindScu needed simpler API
- **Fix:** Added GetPatientRootFindSopClassUid() and GetStudyRootFindSopClassUid() extension methods
- **Files modified:** src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs

## Next Phase Readiness

CFindScu is complete and ready for integration testing in Plan 11-07. The following are ready for subsequent plans:

- C-MOVE SCU (Plan 11-04) - uses same QueryRetrieveLevel extensions
- C-GET SCU (Plan 11-05) - similar pattern to CFindScu
- Integration tests (Plan 11-07) - will test CFindScu with DCMTK
