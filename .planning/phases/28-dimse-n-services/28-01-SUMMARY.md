---
phase: 28-dimse-n-services
plan: 01
subsystem: network
tags: [dicom, dimse, n-service, mpps, storage-commitment, command]

# Dependency graph
requires:
  - phase: 24-dimse-messaging
    provides: DicomCommand C-Service factories, CommandField constants, DicomStatus, DicomTag well-known tags
provides:
  - 12 N-Service factory methods on DicomCommand (6 request + 6 response)
  - N-Service convenience properties (IsNCreateRequest, IsNSetRequest, etc.)
  - RequestedSOPInstanceUID, EventTypeID, ActionTypeID command properties
  - 8 N-Service status codes (InvalidAttributeValue, NoSuchAttribute, etc.)
  - MPPS and Storage Commitment well-known UIDs
affects: [28-02, 28-03, 28-04, 28-05]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "N-CREATE/N-EVENT-REPORT use Affected SOP UIDs; N-SET/N-GET/N-DELETE/N-ACTION use Requested SOP UIDs"
    - "N-Service factory methods follow same AddUidElement/AddUInt16Element pattern as C-Service factories"

key-files:
  modified:
    - src/SharpDicom/Network/Dimse/DicomCommand.cs
    - src/SharpDicom/Network/DicomStatus.cs
    - src/SharpDicom/Data/DicomUID.WellKnown.cs

key-decisions:
  - "N-GET request uses NoDataSetPresent (attribute identifier list is a separate mechanism)"
  - "N-CREATE request has optional affectedSopInstanceUid to allow SCP-assigned instance UIDs"
  - "N-DELETE response uses NoDataSetPresent per PS3.7"

patterns-established:
  - "N-Service request factories: requests that modify use Requested UIDs, responses always use Affected UIDs"
  - "Optional struct parameters use DicomUID? nullable value type"

# Metrics
duration: 5min
completed: 2026-02-07
---

# Phase 28 Plan 01: N-Service Command Foundation Summary

**12 N-Service DIMSE factory methods on DicomCommand with correct Affected/Requested UID selection, 8 N-Service status codes, and MPPS/Storage Commitment well-known UIDs**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-07T16:39:04Z
- **Completed:** 2026-02-07T16:44:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- 12 N-Service factory methods covering all 6 DIMSE-N services (N-EVENT-REPORT, N-GET, N-SET, N-ACTION, N-CREATE, N-DELETE) with both request and response variants
- IsNXxx convenience properties for all 12 N-Service command types
- RequestedSOPInstanceUID, EventTypeID, and ActionTypeID command properties
- 8 N-Service-specific status codes (NoSuchAttribute, InvalidAttributeValue, AttributeListError, NoSuchEventType, InvalidArgumentValue, AttributeValueOutOfRange, ClassInstanceConflict, NoSuchActionType)
- 3 well-known UIDs: ModalityPerformedProcedureStep, StorageCommitmentPushModel, StorageCommitmentPushModelInstance

## Task Commits

Each task was committed atomically:

1. **Task 1: Add N-Service factory methods and properties to DicomCommand** - `59aac84` (feat)
2. **Task 2: Add N-Service status codes and well-known UIDs** - `22ece45` (feat)

## Files Created/Modified
- `src/SharpDicom/Network/Dimse/DicomCommand.cs` - 12 N-Service factory methods, 12 IsNXxx convenience properties, 3 N-Service command properties (RequestedSOPInstanceUID, EventTypeID, ActionTypeID)
- `src/SharpDicom/Network/DicomStatus.cs` - 8 N-Service status codes in dedicated region
- `src/SharpDicom/Data/DicomUID.WellKnown.cs` - MPPS SOP Class, Storage Commitment SOP Class, and Storage Commitment well-known Instance UIDs

## Decisions Made
- N-GET request uses NoDataSetPresent because the attribute identifier list is part of the command dataset mechanism, not a separate data set
- N-CREATE request accepts optional affectedSopInstanceUid (DicomUID?) to allow SCP to assign instance UIDs when the SCU does not specify one
- N-DELETE response always uses NoDataSetPresent per PS3.7 specification

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All N-Service factory methods are ready for use by handler interfaces (plan 28-02)
- Status codes are ready for MPPS state machine (plan 28-03) and Storage Commitment (plan 28-04)
- Well-known UIDs are ready for SOP Class negotiation in plans 28-03 through 28-05
- Zero test regressions: 2313 pass, 55 skipped, 0 failed

---
*Phase: 28-dimse-n-services*
*Completed: 2026-02-07*
