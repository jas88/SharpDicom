---
phase: 24-server-side-dimse
plan: 02
subsystem: network
tags: [dicom, scp, c-move, c-get, dimse, sub-operations, forwarding]

# Dependency graph
requires:
  - phase: 24-01
    provides: C-FIND SCP handler, DIMSE dispatch, QRCommandInfo, SendDimseResponseWithDatasetAsync, SerializeDatasetBytes
  - phase: 10-network-association
    provides: DicomServer, DicomClient, DicomAssociation, PresentationContext, PDU I/O
  - phase: 12-dimse-scu
    provides: CStoreScu for forwarding, DicomCommand factory methods, SubOperationProgress
provides:
  - HandleCMoveAsync with destination resolution, separate forwarding association, and C-STORE sub-ops
  - HandleCGetAsync with same-association C-STORE sub-ops
  - SubOperationProgress tracking in Pending responses for both handlers
  - C-CANCEL support for in-progress C-MOVE and C-GET operations
  - SendQRResponseWithProgressAsync for DIMSE responses with sub-op counts
  - BuildCStoreRequestCommand for C-STORE-RQ sub-operation command construction
  - MoveDestinationUnknown (0xA801), SubOperationsCompleteWithFailures (0xB000), UnableToProcess (0xC000) status constants
affects:
  - 24-03 (FileSystemDicomStore implements OnCMoveRetrieve/OnCGetRetrieve callbacks)
  - 24-04 (integration tests verify C-MOVE/C-GET SCP end-to-end)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Separate forwarding association for C-MOVE per PS3.4 C.4.2: new DicomClient + CStoreScu"
    - "Same-association C-STORE for C-GET per PS3.4 C.4.3: raw PDV writing on original stream"
    - "SubOperationProgress reporting in Pending responses with remaining/completed/failed/warning counts"

key-files:
  created: []
  modified:
    - src/SharpDicom/Network/DicomServer.cs
    - src/SharpDicom/Network/DicomStatus.cs

key-decisions:
  - "C-MOVE uses CStoreScu on DicomClient for forwarding (high-level, reuses existing SCU infrastructure)"
  - "C-GET uses raw PDV building on same stream (low-level, necessary because SCP sends C-STORE on same association)"
  - "Match collection is capped at 10000 to prevent memory exhaustion during C-MOVE/C-GET"
  - "Forwarding presentation contexts are derived from match SOP Class UIDs, not hardcoded"
  - "Individual sub-op failures increment failed count without terminating the operation"

patterns-established:
  - "Sub-op progress pattern: BuildQRResponseCommandWithProgress includes (0000,1020-1023) sub-operation count elements"
  - "Same-association C-STORE: BuildCStoreRequestCommand + SerializeDatasetBytes + ReadCStoreSubOpResponseAsync"
  - "FindAcceptedContextForSopClass scans association accepted contexts to match SOP Class for sub-ops"

# Metrics
duration: 8min
completed: 2026-02-06
---

# Phase 24 Plan 02: C-MOVE SCP and C-GET SCP Handlers Summary

**HandleCMoveAsync with separate forwarding association and HandleCGetAsync with same-association C-STORE sub-operations, both with SubOperationProgress tracking**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-06T04:23:10Z
- **Completed:** 2026-02-06T04:30:42Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- HandleCMoveAsync resolves destination AE via callback, opens SEPARATE DicomClient for forwarding, sends C-STORE sub-ops via CStoreScu, reports progress in Pending C-MOVE-RSP messages
- HandleCGetAsync collects matches, sends C-STORE sub-operations on the SAME association by building raw C-STORE-RQ command/dataset PDVs, reads C-STORE-RSP from SCU
- Both handlers support C-CANCEL via CancellationToken, gracefully handle individual sub-op failures, and send final status (Success/Warning/Failure) with accurate sub-operation counts
- Added MoveDestinationUnknown (0xA801), SubOperationsCompleteWithFailures (0xB000), UnableToProcess (0xC000) well-known status constants
- C-MOVE and C-GET dispatch wired into HandlePDataAsync replacing Plan 01 stubs

## Task Commits

Each task was committed atomically:

1. **Task 1: HandleCMoveAsync with C-STORE forwarding to destination** - `f48d492` (feat)
2. **Task 2: HandleCGetAsync with same-association C-STORE sub-operations** - `098b3f0` (feat)

## Files Created/Modified
- `src/SharpDicom/Network/DicomServer.cs` - HandleCMoveAsync (separate forwarding), HandleCGetAsync (same-association), SendQRResponseWithProgressAsync, BuildQRResponseCommandWithProgress, BuildCStoreRequestCommand, FindAcceptedContextForSopClass, SendCStoreSubOpRequestAsync, ReadCStoreSubOpResponseAsync, ParseStatusFromCommand helpers
- `src/SharpDicom/Network/DicomStatus.cs` - Added MoveDestinationUnknown, SubOperationsCompleteWithFailures, UnableToProcess well-known status constants

## Decisions Made
- C-MOVE forwarding uses high-level CStoreScu on a new DicomClient (reuses existing SCU infrastructure, clean separation)
- C-GET same-association C-STORE uses low-level PDV building (necessary because SCP must send C-STORE on the association it received C-GET on)
- Match collection capped at 10000 per operation to prevent memory exhaustion
- Forwarding presentation contexts derived from matched SOP Class UIDs rather than hardcoded list
- Failed sub-operations don't terminate the loop - remaining files continue to be sent per DICOM standard

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- CA1822 warning on HandleCGetAsync stub (before full implementation): resolved by accessing _options instance data in the stub, then replaced with full implementation accessing instance members

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- C-MOVE and C-GET SCP handlers are functional, ready for FileSystemDicomStore integration in Plan 03
- All sub-operation tracking infrastructure (SubOperationProgress, progress response helpers) is reusable
- All 2089 existing tests pass without modification

---
*Phase: 24-server-side-dimse*
*Completed: 2026-02-06*
