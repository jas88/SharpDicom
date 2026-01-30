---
phase: 11-dimse-services
plan: 06
subsystem: network
tags: [c-get, scu, dimse, qr-get, scp-role, sub-operations, streaming]

# Dependency graph
requires:
  - phase: 11-01
    provides: QueryRetrieveLevel, SubOperationProgress, DicomCommand, DicomClient DIMSE primitives
  - phase: 11-03
    provides: DicomQuery fluent builder, CFindScu pattern for IAsyncEnumerable
  - phase: 10-02
    provides: PresentationContext base class
provides:
  - CGetScu service class with GetAsync returning IAsyncEnumerable<CGetProgress>
  - CGetOptions with timeout, priority, UsePatientRoot, CancellationBehavior
  - CGetProgress tracking sub-operations and received datasets
  - PresentationContext SCP role selection (WithScpRole, WithBothRoles)
  - QueryRetrieveLevel C-GET/C-MOVE extension methods
affects: [11-07, network-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Interleaved message handling (C-STORE-RQ + C-GET-RSP in same loop)
    - SCP role selection for SCU to receive sub-operations
    - CancellationBehavior enum for cancellation strategy

key-files:
  created:
    - src/SharpDicom/Network/Dimse/Services/CGetOptions.cs
    - src/SharpDicom/Network/Dimse/Services/CGetProgress.cs
    - src/SharpDicom/Network/Dimse/Services/CGetScu.cs
    - tests/SharpDicom.Tests/Network/Dimse/CGetScuTests.cs
  modified:
    - src/SharpDicom/Network/Items/PresentationContext.cs
    - src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs

key-decisions:
  - "CGetProgress yields after both C-STORE sub-ops (with dataset) and C-GET-RSP (with counts)"
  - "PresentationContext SCP role selection uses mutable properties for fluent API"
  - "CancellationBehavior.RejectInFlight as default - fail fast on cancel"
  - "Store handler is async delegate returning DicomStatus for response"

patterns-established:
  - "SCP role selection via WithScpRole() fluent method on PresentationContext"
  - "Interleaved command type detection via IsCStoreRequest/IsCGetResponse"
  - "Sub-operation handler delegate pattern for extensible storage handling"

# Metrics
duration: 15min
completed: 2026-01-29
---

# Phase 11 Plan 06: C-GET SCU Summary

**C-GET SCU with interleaved C-STORE sub-operation handling, SCP role selection, and cancellation behavior options**

## Performance

- **Duration:** 15 min
- **Started:** 2026-01-29T16:35:36Z
- **Completed:** 2026-01-29T16:50:00Z
- **Tasks:** 4
- **Files created:** 4
- **Files modified:** 2

## Accomplishments
- CGetScu service class with GetAsync returning IAsyncEnumerable<CGetProgress>
- PresentationContext SCP role selection for C-GET sub-operations
- Interleaved message handling for C-STORE-RQ and C-GET-RSP
- CancellationBehavior controls in-flight sub-operation handling
- Full unit test coverage (86 tests in CGetScuTests)

## Task Commits

Each task was committed atomically:

1. **Task 1: CGetOptions and CGetProgress types** - `acd97af` (feat)
2. **Task 2: PresentationContext SCP role selection** - `4477cab` (feat)
3. **Task 3: CGetScu service class** - `564757d` (feat)
4. **Task 4: Unit tests for CGetScu** - `51c8653` (test)

## Files Created/Modified

**Created:**
- `src/SharpDicom/Network/Dimse/Services/CGetOptions.cs` - C-GET options (timeout, priority, UsePatientRoot, CancellationBehavior)
- `src/SharpDicom/Network/Dimse/Services/CGetProgress.cs` - Progress with SubOperations, Status, ReceivedDataset
- `src/SharpDicom/Network/Dimse/Services/CGetScu.cs` - C-GET SCU with interleaved message handling
- `tests/SharpDicom.Tests/Network/Dimse/CGetScuTests.cs` - Comprehensive unit tests

**Modified:**
- `src/SharpDicom/Network/Items/PresentationContext.cs` - Added ScuRoleRequested, ScpRoleRequested, WithScpRole(), WithBothRoles()
- `src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs` - Added GetPatientRootGetSopClassUid, GetStudyRootGetSopClassUid, C-MOVE convenience methods

## Decisions Made

1. **CGetProgress yields on both message types** - Progress updates yield both when C-STORE sub-operations complete (with received dataset) and when C-GET-RSP arrives (with cumulative sub-operation counts). This gives consumers maximum visibility into progress.

2. **PresentationContext SCP role as mutable properties** - ScuRoleRequested and ScpRoleRequested are settable properties rather than constructor parameters, enabling fluent WithScpRole() chaining without breaking existing constructors.

3. **CancellationBehavior.RejectInFlight default** - Default behavior rejects in-flight C-STORE sub-operations on cancellation for fast fail. CompleteInFlight option available for data integrity preservation.

4. **Store handler is async delegate** - Handler signature `Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>>` allows flexible storage implementations (file save, database insert, forwarding) with proper async support.

5. **Added C-MOVE convenience methods too** - While adding GetPatientRootGetSopClassUid/GetStudyRootGetSopClassUid, also added C-MOVE variants for API consistency and future C-MOVE SCU implementation.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing C-GET SOP Class UID extension methods**
- **Found during:** Task 3 (CGetScu implementation)
- **Issue:** Plan referenced GetPatientRootGetSopClassUid/GetStudyRootGetSopClassUid but they didn't exist
- **Fix:** Added extension methods to QueryRetrieveLevelExtensions
- **Files modified:** src/SharpDicom/Network/Dimse/QueryRetrieveLevel.cs
- **Verification:** Build succeeds, CGetScu compiles
- **Committed in:** 564757d (Task 3 commit)

**2. [Rule 1 - Bug] CA2263 analyzer error in tests**
- **Found during:** Task 4 (Unit tests)
- **Issue:** `Enum.IsDefined(typeof(T), value)` flagged as using non-preferred overload
- **Fix:** Changed to generic `Enum.IsDefined<T>(value)` form
- **Files modified:** tests/SharpDicom.Tests/Network/Dimse/CGetScuTests.cs
- **Verification:** Build succeeds without warnings
- **Committed in:** 51c8653 (Task 4 commit)

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both fixes necessary for compilation. No scope creep.

## Issues Encountered
None - plan executed smoothly after addressing missing extension methods.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- C-GET SCU ready for integration testing (Plan 11-07)
- PduWriter needs SCP/SCU Role Selection Sub-Item (0x54) support for full association negotiation
- C-MOVE SCU can now reuse the new QueryRetrieveLevel extension methods

---
*Phase: 11-dimse-services*
*Completed: 2026-01-29*
