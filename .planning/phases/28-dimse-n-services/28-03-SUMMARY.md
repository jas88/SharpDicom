---
phase: 28-dimse-n-services
plan: 03
subsystem: network
tags: [dimse-n, n-create, n-set, n-get, n-delete, n-action, n-event-report, scu, scp, dicom-server]

# Dependency graph
requires:
  - phase: 28-01
    provides: "12 N-Service factory methods on DicomCommand, N-Service status codes"
provides:
  - "6 N-Service handler interfaces (INCreateHandler through INEventReportHandler)"
  - "6 typed request context classes with service-specific properties"
  - "NServiceResponse with Status, Dataset, and AffectedSOPInstanceUID"
  - "NServiceScu with 6 async methods for client-side N-Service operations"
  - "DicomServer N-Service command dispatch to registered handlers"
  - "DicomServerOptions with 6 handler registration properties"
affects: [28-04-mpps-sop-class, 28-05-storage-commitment]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "N-Service handler interface pattern (INXxxHandler returning ValueTask<NServiceResponse>)"
    - "N-Service SCU pattern (NServiceScu wrapping DicomClient internal methods)"
    - "N-Service server dispatch pattern (ParseNServiceCommand + HandleNXxxAsync)"

key-files:
  created:
    - src/SharpDicom/Network/Dimse/Services/NServiceRequest.cs
    - src/SharpDicom/Network/Dimse/Services/NServiceResponse.cs
    - src/SharpDicom/Network/Dimse/Services/NServiceScu.cs
    - src/SharpDicom/Network/Dimse/Services/INCreateHandler.cs
    - src/SharpDicom/Network/Dimse/Services/INSetHandler.cs
    - src/SharpDicom/Network/Dimse/Services/INGetHandler.cs
    - src/SharpDicom/Network/Dimse/Services/INDeleteHandler.cs
    - src/SharpDicom/Network/Dimse/Services/INActionHandler.cs
    - src/SharpDicom/Network/Dimse/Services/INEventReportHandler.cs
  modified:
    - src/SharpDicom/Network/DicomServerOptions.cs
    - src/SharpDicom/Network/DicomServer.cs

key-decisions:
  - "NServiceRequestContext uses abstract base class (not interface) for common fields, matching CStoreRequestContext pattern"
  - "NServiceResponse is a unified type for all 6 N-Services rather than per-service response types"
  - "Server-side dispatch uses NServiceCommandInfo struct that extracts both Affected and Requested SOP UIDs"
  - "ParseNServiceCommand handles both Affected (N-CREATE, N-EVENT-REPORT) and Requested (N-SET, N-GET, N-ACTION, N-DELETE) SOP Class/Instance UIDs"
  - "Handler-absent responses return ProcessingFailure (0x0110) consistent with handler pattern"

patterns-established:
  - "N-Service handler interface: single method, typed context + optional DicomDataset + CancellationToken, returns ValueTask<NServiceResponse>"
  - "N-Service SCU: GetRequiredContext + CreateNXxxRequest + SendDimseRequestAsync + ReceiveDimseResponseAsync pattern"
  - "N-Service server dispatch: ParseNServiceCommand extracts all fields, HandleNXxxAsync reads dataset, invokes handler, sends typed response"
  - "BuildNServiceResponseCommand includes AffectedSOPClassUID and AffectedSOPInstanceUID in all N-Service responses per DICOM PS3.7"

# Metrics
duration: 8min
completed: 2026-02-07
---

# Phase 28 Plan 03: N-Service Infrastructure Summary

**Complete N-Service handler interfaces, NServiceScu, and DicomServer dispatch for all 6 DIMSE-N operations (N-CREATE through N-EVENT-REPORT)**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-07T16:47:47Z
- **Completed:** 2026-02-07T16:55:54Z
- **Tasks:** 2
- **Files modified:** 11 (9 created, 2 modified)

## Accomplishments
- 6 handler interfaces following ICStoreHandler pattern for server-side N-Service handling
- NServiceScu with 6 async methods wrapping DicomClient for client-side N-Service invocation
- DicomServer fully dispatches all 6 N-Service command fields to registered handler interfaces
- All N-Service responses include AffectedSOPClassUID and AffectedSOPInstanceUID per DICOM PS3.7
- Zero test regressions (2313 pass, 55 skipped, 0 failed)

## Task Commits

Each task was committed atomically:

1. **Task 1: N-Service request/response classes and handler interfaces** - `6bba74e` (feat)
2. **Task 2: NServiceScu and server-side N-Service dispatch** - `2340bc2` (feat)

## Files Created/Modified
- `src/SharpDicom/Network/Dimse/Services/NServiceRequest.cs` - NServiceRequestContext base + 6 concrete context types
- `src/SharpDicom/Network/Dimse/Services/NServiceResponse.cs` - Unified N-Service response with Status, Dataset, AffectedSOPInstanceUID
- `src/SharpDicom/Network/Dimse/Services/NServiceScu.cs` - Generic N-Service SCU with 6 async methods
- `src/SharpDicom/Network/Dimse/Services/INCreateHandler.cs` - Server-side N-CREATE handler interface
- `src/SharpDicom/Network/Dimse/Services/INSetHandler.cs` - Server-side N-SET handler interface
- `src/SharpDicom/Network/Dimse/Services/INGetHandler.cs` - Server-side N-GET handler interface
- `src/SharpDicom/Network/Dimse/Services/INDeleteHandler.cs` - Server-side N-DELETE handler interface
- `src/SharpDicom/Network/Dimse/Services/INActionHandler.cs` - Server-side N-ACTION handler interface
- `src/SharpDicom/Network/Dimse/Services/INEventReportHandler.cs` - Server-side N-EVENT-REPORT handler interface
- `src/SharpDicom/Network/DicomServerOptions.cs` - Added 6 N-Service handler registration properties
- `src/SharpDicom/Network/DicomServer.cs` - N-Service command parsing, dispatch, and response sending

## Decisions Made
- Used abstract base class NServiceRequestContext (not interface) for common fields, matching existing CStoreRequestContext sealed class pattern with PresentationContextId
- Created a single NServiceResponse type shared across all 6 N-Services, with optional AffectedSOPInstanceUID for N-CREATE SCP-assigned UIDs
- ParseNServiceCommand handles both Affected (tags 0002/1000) and Requested (tags 0003/1001) SOP Class/Instance UIDs with fallback logic
- Handler-absent N-Service requests return DicomStatus.ProcessingFailure (0x0110) rather than NoSuchSOPClass (0xA900) to be consistent with the handler pattern
- BuildNServiceResponseCommand always includes AffectedSOPInstanceUID in responses per DICOM PS3.7

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Missing `using SharpDicom.Network.Items` in NServiceScu for `PresentationContext` type - fixed immediately

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- N-Service infrastructure complete: Plans 04 (MPPS) and 05 (Storage Commitment) can now implement concrete SOP Classes by implementing the handler interfaces
- NServiceScu provides the client-side mechanism for MPPS N-CREATE/N-SET and Storage Commitment N-ACTION/N-EVENT-REPORT
- DicomServer dispatches all N-Service commands, so MPPS and StorageCommitment handlers just need to be registered in DicomServerOptions

---
*Phase: 28-dimse-n-services*
*Completed: 2026-02-07*
