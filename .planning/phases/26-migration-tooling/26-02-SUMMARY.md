---
phase: 26-migration-tooling
plan: 02
subsystem: network-compat
tags: [dicom, fo-dicom, compatibility, network, cfind, dimse, adapter]

# Dependency graph
requires:
  - phase: 26-01
    provides: FoDicom5.Compat core types (DicomDataset, DicomTag, DicomFile, DicomElement)
provides:
  - Network compat types (DicomCFindRequest, DicomCFindResponse, DicomStatus, DicomQueryRetrieveLevel)
  - DicomClient adapter bridging fo-dicom request-queue to SharpDicom direct async pattern
  - DicomClientFactory static factory matching fo-dicom 5.x API
  - IDicomClient interface for testable client injection
affects: [26-03, 26-04]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Request-queue adapter pattern: fo-dicom AddRequestAsync+SendAsync to SharpDicom ConnectAsync+CFindScu"
    - "Callback translation: OnResponseReceived delegate invoked per async enumerable result"

key-files:
  created:
    - src/SharpDicom.FoDicom5.Compat/Network/DicomQueryRetrieveLevel.cs
    - src/SharpDicom.FoDicom5.Compat/Network/DicomStatus.cs
    - src/SharpDicom.FoDicom5.Compat/Network/DicomRequest.cs
    - src/SharpDicom.FoDicom5.Compat/Network/DicomResponse.cs
    - src/SharpDicom.FoDicom5.Compat/Network/DicomCFindRequest.cs
    - src/SharpDicom.FoDicom5.Compat/Network/DicomCFindResponse.cs
    - src/SharpDicom.FoDicom5.Compat/Network/Client/IDicomClient.cs
    - src/SharpDicom.FoDicom5.Compat/Network/Client/DicomClient.cs
    - src/SharpDicom.FoDicom5.Compat/Network/Client/DicomClientFactory.cs
    - tests/SharpDicom.FoDicom5.Compat.Tests/NetworkCompatTests.cs
  modified: []

key-decisions:
  - "DicomClient.SendAsync creates SharpDicom client per call (vs persistent connection) to match fo-dicom's stateless pattern"
  - "Patient Root Q/R used as default SOP Class for C-FIND presentation contexts"
  - "OnResponseReceived callback invoked with Pending status per result, then Success for final (matches fo-dicom)"

patterns-established:
  - "Adapter pattern: compat network types delegate to SharpDicom Network layer via namespace-qualified references"
  - "Callback translation: async IAsyncEnumerable results mapped to synchronous delegate invocations"

# Metrics
duration: 5min
completed: 2026-02-06
---

# Phase 26 Plan 02: Network Compat Layer Summary

**fo-dicom 5.x network adapter bridging request-queue pattern (AddRequestAsync/SendAsync) to SharpDicom's direct async CFindScu, with DicomCFindRequest OnResponseReceived callback translation**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-06T17:42:51Z
- **Completed:** 2026-02-06T17:48:01Z
- **Tasks:** 2
- **Files created:** 10

## Accomplishments
- Network compat types matching fo-dicom 5.x API: DicomQueryRetrieveLevel, DicomStatus, DicomRequest, DicomResponse, DicomCFindRequest, DicomCFindResponse
- DicomClient adapter that bridges fo-dicom's request-queue pattern to SharpDicom's direct async pattern via CFindScu
- DicomClientFactory.Create matching fo-dicom's factory pattern
- 16 unit tests covering factory creation, request buffering, query key storage, callback delegation, and enum values

## Task Commits

Each task was committed atomically:

1. **Task 1: Create network compat types (requests, responses, status)** - `3d7323c` (feat)
2. **Task 2: Create DicomClient adapter with request-queue pattern and tests** - `41d2a57` (feat)

## Files Created/Modified
- `src/SharpDicom.FoDicom5.Compat/Network/DicomQueryRetrieveLevel.cs` - Enum matching fo-dicom with Patient/Study/Series/Image values and conversion to SharpDicom
- `src/SharpDicom.FoDicom5.Compat/Network/DicomStatus.cs` - Status wrapper with Code, DicomState enum, boolean helpers, well-known statuses
- `src/SharpDicom.FoDicom5.Compat/Network/DicomRequest.cs` - Abstract base with Dataset and DicomRequestType
- `src/SharpDicom.FoDicom5.Compat/Network/DicomResponse.cs` - Base response with Status and optional Dataset
- `src/SharpDicom.FoDicom5.Compat/Network/DicomCFindRequest.cs` - C-FIND request with Level, OnResponseReceived callback, writable Dataset
- `src/SharpDicom.FoDicom5.Compat/Network/DicomCFindResponse.cs` - C-FIND response extending DicomResponse
- `src/SharpDicom.FoDicom5.Compat/Network/Client/IDicomClient.cs` - Interface: AddRequestAsync, SendAsync, NegotiateAsyncOps, IsBusy
- `src/SharpDicom.FoDicom5.Compat/Network/Client/DicomClient.cs` - Adapter bridging request-queue to SharpDicom's ConnectAsync + CFindScu.QueryAsync
- `src/SharpDicom.FoDicom5.Compat/Network/Client/DicomClientFactory.cs` - Static factory matching fo-dicom pattern
- `tests/SharpDicom.FoDicom5.Compat.Tests/NetworkCompatTests.cs` - 16 unit tests

## Decisions Made
- DicomClient.SendAsync creates a fresh SharpDicom DicomClient per invocation and disposes it after processing. This matches fo-dicom's stateless pattern where each SendAsync is a complete connection lifecycle.
- Patient Root Q/R Information Model used as default for C-FIND presentation contexts (most common in real-world usage).
- The OnResponseReceived callback is invoked with Pending status for each result dataset, then with Success status and null Dataset as the final signal -- matching fo-dicom's documented behavior.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Missing `using SharpDicom.Network.Items` for PresentationContext -- added during build verification.
- CA1510 analyzer error on newer TFMs required `#if NET8_0_OR_GREATER` guard for `ArgumentNullException.ThrowIfNull` (netstandard2.0 doesn't have it).
- CA1859 analyzer error required changing `IReadOnlyList<PresentationContext>` return type to `List<PresentationContext>` for internal method.
- CA2263 analyzer error in tests required using generic `Enum.IsDefined<T>` and `Enum.GetValues<T>` overloads.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Network compat layer complete, ready for 26-03 (migration analysis tooling)
- Full integration test (actual DIMSE network communication) deferred to 26-04 (nccid validation)
- All 4559 tests pass (4379 succeeded, 180 skipped, 0 failed)

---
*Phase: 26-migration-tooling*
*Completed: 2026-02-06*
