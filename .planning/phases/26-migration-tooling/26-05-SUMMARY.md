---
phase: 26-migration-tooling
plan: 05
subsystem: compatibility
tags: [fo-dicom, migration, compat, dicom-4x, namespace-shim]

# Dependency graph
requires:
  - phase: 26-01
    provides: FoDicom5.Compat core types as template for FoDicom4 variant
provides:
  - SharpDicom.FoDicom4.Compat package with Dicom namespace
  - fo-dicom 4.x Get<T> API surface
  - 25 tests verifying 4.x compatibility
affects: [26-06, future SmiServices migration, future RdmpDicom migration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "FoDicom4.Compat mirrors FoDicom5.Compat with Dicom namespace and Get<T> primary API"

key-files:
  created:
    - src/SharpDicom.FoDicom4.Compat/SharpDicom.FoDicom4.Compat.csproj
    - src/SharpDicom.FoDicom4.Compat/DicomDataset.cs
    - src/SharpDicom.FoDicom4.Compat/DicomFile.cs
    - src/SharpDicom.FoDicom4.Compat/DicomTag.cs
    - src/SharpDicom.FoDicom4.Compat/DicomVR.cs
    - src/SharpDicom.FoDicom4.Compat/DicomUID.cs
    - src/SharpDicom.FoDicom4.Compat/DicomItem.cs
    - src/SharpDicom.FoDicom4.Compat/DicomElement.cs
    - src/SharpDicom.FoDicom4.Compat/DicomStringElement.cs
    - src/SharpDicom.FoDicom4.Compat/DicomAttributeTag.cs
    - src/SharpDicom.FoDicom4.Compat/DicomOtherElement.cs
    - src/SharpDicom.FoDicom4.Compat/DicomSequence.cs
    - src/SharpDicom.FoDicom4.Compat/DicomDictionaryEntry.cs
    - src/SharpDicom.FoDicom4.Compat/Exceptions/DicomDataException.cs
    - src/SharpDicom.FoDicom4.Compat/Compatibility.cs
    - tests/SharpDicom.FoDicom4.Compat.Tests/SharpDicom.FoDicom4.Compat.Tests.csproj
    - tests/SharpDicom.FoDicom4.Compat.Tests/FoDicom4CompatTests.cs
  modified:
    - Directory.Build.props
    - SharpDicom.sln

key-decisions:
  - "Get<T>(tag, index) is primary API; GetSingleValue<T> retained as alias for late 4.x compat"
  - "Get<T>(tag, defaultValue) overload added for missing-tag safety (common fo-dicom 4.x pattern)"
  - "No network types in FoDicom4 - fo-dicom 4.x had different network API, not needed yet"
  - "Shared GetValueInternal<T> method avoids duplication between Get<T> and GetSingleValue<T>"

patterns-established:
  - "FoDicom4 Dicom namespace: all types use namespace Dicom (not FellowOakDicom)"
  - "Dual compat approach: same SharpDicom core backing both 4.x and 5.x API surfaces"

# Metrics
duration: 6min
completed: 2026-02-06
---

# Phase 26 Plan 05: FoDicom4 Compat Summary

**fo-dicom 4.x compatibility layer with Dicom namespace and Get<T> primary API backed by SharpDicom core**

## Performance

- **Duration:** 6 min
- **Started:** 2026-02-06T17:43:12Z
- **Completed:** 2026-02-06T17:49:22Z
- **Tasks:** 2
- **Files modified:** 19

## Accomplishments
- Created SharpDicom.FoDicom4.Compat with all core types using `Dicom` namespace
- Added fo-dicom 4.x primary API: `Get<T>(tag, index)` and `Get<T>(tag, defaultValue)`
- Retained `GetSingleValue<T>` for late 4.x backward compatibility
- 25 tests verify namespace correctness and API surface
- Full solution: 4584 tests pass (4404 succeeded, 180 skipped, 0 failed)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create FoDicom4.Compat project with Dicom namespace and 4.x API** - `1e55926` (feat)
2. **Task 2: Create FoDicom4 compat tests** - `63b5b1f` (test)

## Files Created/Modified
- `src/SharpDicom.FoDicom4.Compat/SharpDicom.FoDicom4.Compat.csproj` - Project file with Dicom root namespace
- `src/SharpDicom.FoDicom4.Compat/DicomDataset.cs` - Dataset with Get<T>, GetSingleValue<T>, AddOrUpdate
- `src/SharpDicom.FoDicom4.Compat/DicomFile.cs` - File I/O (Open, Save, OpenAsync, SaveAsync)
- `src/SharpDicom.FoDicom4.Compat/DicomTag.cs` - Tag class with DictionaryEntry, well-known tags
- `src/SharpDicom.FoDicom4.Compat/DicomVR.cs` - VR singleton instances (LO, CS, SQ, etc.)
- `src/SharpDicom.FoDicom4.Compat/DicomUID.cs` - UID type with well-known UIDs
- `src/SharpDicom.FoDicom4.Compat/DicomItem.cs` - Base item class with Wrap factory
- `src/SharpDicom.FoDicom4.Compat/DicomElement.cs` - Abstract element with Get<T>
- `src/SharpDicom.FoDicom4.Compat/DicomStringElement.cs` - String VR element wrapper
- `src/SharpDicom.FoDicom4.Compat/DicomAttributeTag.cs` - AT VR element wrapper
- `src/SharpDicom.FoDicom4.Compat/DicomOtherElement.cs` - Binary/numeric element wrapper
- `src/SharpDicom.FoDicom4.Compat/DicomSequence.cs` - Sequence with Items property
- `src/SharpDicom.FoDicom4.Compat/DicomDictionaryEntry.cs` - Dictionary entry wrapper
- `src/SharpDicom.FoDicom4.Compat/Exceptions/DicomDataException.cs` - Exception type
- `src/SharpDicom.FoDicom4.Compat/Compatibility.cs` - Unwrap extension methods
- `tests/SharpDicom.FoDicom4.Compat.Tests/FoDicom4CompatTests.cs` - 25 tests for 4.x API
- `tests/SharpDicom.FoDicom4.Compat.Tests/SharpDicom.FoDicom4.Compat.Tests.csproj` - Test project
- `Directory.Build.props` - TFM configuration for FoDicom4 projects
- `SharpDicom.sln` - Solution updated with new projects

## Decisions Made
- **Get<T> as primary, GetSingleValue<T> as alias:** fo-dicom 4.x uses `Get<T>(tag)` as the primary accessor while late 4.x also supports `GetSingleValue<T>`. Both backed by shared `GetValueInternal<T>`.
- **Get<T>(tag, defaultValue) overload:** Common fo-dicom 4.x pattern for safe missing-tag access. Returns default when tag absent.
- **No network types:** fo-dicom 4.x used direct constructor `new DicomClient(host, port, ...)` which differs significantly from 5.x factory pattern. Network compat not needed for current migration targets.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Both fo-dicom 4.x and 5.x compat layers now available
- Ready for migration analyzer (26-06) or direct project migrations
- SmiServices (uses fo-dicom 4.x Dicom namespace) can now use FoDicom4.Compat
- RdmpDicom can use either compat layer depending on version

---
*Phase: 26-migration-tooling*
*Completed: 2026-02-06*
