---
phase: 24-server-side-dimse
plan: 01
subsystem: network
tags: [dicom, scp, c-find, dimse, query-matching, wildcard, date-range]

# Dependency graph
requires:
  - phase: 10-network-association
    provides: DicomServer, DicomAssociation, PDU I/O, DIMSE dispatch loop
  - phase: 12-dimse-scu
    provides: DicomCommand factory methods (CreateCFindResponse, etc.), CommandField constants
provides:
  - C-FIND SCP handler in DicomServer with streaming IAsyncEnumerable results
  - DIMSE dispatch for C-FIND/C-MOVE/C-GET/C-CANCEL in ExtractDimseRequests
  - DicomQueryMatcher (wildcard-to-SQL, return key filtering, in-memory matching)
  - DicomDateRange (DICOM date range parsing)
  - OnCFind/OnCMoveRetrieve/OnCGetRetrieve/OnResolveMoveDestination callbacks on DicomServerOptions
  - SendDimseResponseWithDatasetAsync for sending DIMSE responses with identifier datasets
  - SerializeDatasetBytes for dataset serialization with correct transfer syntax
affects:
  - 24-02 (C-MOVE/C-GET SCP implementation uses dispatch loop and callbacks)
  - 24-03 (FileSystemDicomStore implements OnCFind callback)
  - 24-04 (integration tests verify C-FIND SCP end-to-end)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Callback delegate SCP registration: Func<DicomDataset, CancellationToken, IAsyncEnumerable<DicomDataset>> for streaming query results"
    - "Return key filtering: DicomQueryMatcher.FilterReturnKeys limits response to requested tags per PS3.4 C.2.2"
    - "QRCommandInfo struct for parsed Q/R command data alongside CStoreCommandInfo"

key-files:
  created:
    - src/SharpDicom/Network/Dimse/Services/DicomQueryMatcher.cs
    - src/SharpDicom/Network/Dimse/Services/DicomDateRange.cs
  modified:
    - src/SharpDicom/Network/DicomServer.cs
    - src/SharpDicom/Network/DicomServerOptions.cs

key-decisions:
  - "C-FIND streaming uses IAsyncEnumerable<DicomDataset> for memory-efficient result delivery"
  - "Return key filtering is applied server-side in HandleCFindStreamingAsync, not delegated to callbacks"
  - "C-MOVE/C-GET dispatch stubs return 0xA900 until Plan 02 implements them"
  - "C-CANCEL-RQ received outside active Q/R operations is silently ignored"

patterns-established:
  - "QR dispatch pattern: ExtractDimseRequests returns (echo, store, qr) tuple, HandlePDataAsync processes each list"
  - "DIMSE response with dataset: command PDV + dataset PDV in single P-DATA-TF via SendDimseResponseWithDatasetAsync"
  - "Failure response pattern: BuildQRResponseCommand for command-only responses, BuildQRResponseCommandWithDataset for responses with identifier"

# Metrics
duration: 7min
completed: 2026-02-06
---

# Phase 24 Plan 01: C-FIND SCP Handler and Query Matching Infrastructure Summary

**C-FIND SCP with streaming IAsyncEnumerable results, DICOM wildcard/date range matching, and extended DIMSE dispatch for C-FIND/C-MOVE/C-GET/C-CANCEL**

## Performance

- **Duration:** 7 min
- **Started:** 2026-02-06T04:11:50Z
- **Completed:** 2026-02-06T04:19:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- DicomServer now dispatches C-FIND/C-MOVE/C-GET/C-CANCEL requests through ExtractDimseRequests
- HandleCFindAsync reads identifier dataset, invokes OnCFind callback, streams Pending responses with filtered return keys, sends final Success
- DicomQueryMatcher provides DICOM wildcard-to-SQL-LIKE translation, in-memory wildcard matching, and return key filtering per PS3.4 C.2.2
- DicomDateRange parses all four DICOM date range formats (single, range, open-start, open-end)
- DicomServerOptions exposes OnCFind, OnCMoveRetrieve, OnCGetRetrieve, OnResolveMoveDestination callback delegates

## Task Commits

Each task was committed atomically:

1. **Task 1: DIMSE dispatch extension, callback delegates, query matching infrastructure** - `2e65f3c` (feat)
2. **Task 2: HandleCFindAsync with streaming results and DIMSE response helpers** - `d81c64b` (feat)

## Files Created/Modified
- `src/SharpDicom/Network/Dimse/Services/DicomDateRange.cs` - Structured date range for DA/DT range matching with Parse, Contains, IsUniversal
- `src/SharpDicom/Network/Dimse/Services/DicomQueryMatcher.cs` - DICOM wildcard to SQL LIKE translation, return key filtering, in-memory wildcard matching
- `src/SharpDicom/Network/DicomServerOptions.cs` - Added OnCFind, OnCMoveRetrieve, OnCGetRetrieve, OnResolveMoveDestination callback properties with HasCFindHandler/HasCMoveHandler/HasCGetHandler
- `src/SharpDicom/Network/DicomServer.cs` - Extended DIMSE dispatch loop, QRCommandInfo struct, HandleCFindAsync, SendDimseResponseWithDatasetAsync, SerializeDatasetBytes, BuildQRResponseCommand helpers

## Decisions Made
- C-FIND callback returns IAsyncEnumerable for streaming, not List, allowing memory-efficient delivery of large result sets
- Return key filtering is server-side: callbacks return full datasets, server filters to requested tags before sending
- Unregistered handlers return 0xA900 (Unable to Process) after reading and discarding the identifier dataset (per Pitfall 6 and 7 from RESEARCH.md)
- C-CANCEL received outside active operations is silently ignored rather than returning an error

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- CA1822 warning on HandleCFindStreamingAsync stub: resolved by implementing full streaming in single commit rather than deferring to a separate step

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- C-FIND SCP handler is functional, ready for C-MOVE/C-GET implementation in Plan 02
- Query matching infrastructure (DicomQueryMatcher, DicomDateRange) is reusable for FileSystemDicomStore in Plan 03
- All 2089 existing tests pass without modification

---
*Phase: 24-server-side-dimse*
*Completed: 2026-02-06*
