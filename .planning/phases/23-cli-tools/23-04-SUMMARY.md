---
phase: 23-cli-tools
plan: 04
subsystem: cli
tags: [dicom, c-find, cfind, query, pacs, spectre-console, csv, json, system-commandline]

# Dependency graph
requires:
  - phase: 23-01
    provides: CLI scaffolding (project, global options, formatters, config, helpers)
  - phase: 11-03
    provides: CFindScu, DicomQuery fluent builder, CFindOptions
  - phase: 10-05
    provides: DicomClient, DicomClientOptions, PresentationContext
provides:
  - "sharpdcm find subcommand for C-FIND queries"
  - "PacsConnectionResolver shared helper for PACS connection resolution"
  - "Tag resolution by keyword or hex string"
  - "Text table, JSON array, and RFC 4180 CSV output formatters for query results"
affects: [23-06, future-move-get-echo-commands]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "PacsConnectionResolver for shared PACS connection resolution across network commands"
    - "Tag resolution helper supporting DicomDictionary keyword lookup and hex string parsing"
    - "Private static readonly DicomTag fields for missing well-known constants"
    - "AddStringFilter helper for raw string filters on DicomQuery dataset"

key-files:
  created:
    - src/SharpDicom.Cli/Commands/FindCommand.cs
    - src/SharpDicom.Cli/Helpers/PacsConnectionResolver.cs
  modified:
    - src/SharpDicom.Cli/Program.cs

key-decisions:
  - "Defined missing DicomTag constants (StudyDescription, SeriesNumber, SeriesDescription, NumberOfSeriesRelatedInstances, InstanceNumber) as private static readonly fields rather than modifying source generator"
  - "Used DicomQuery.ForImages() for instance-level queries (API has ForImages not ForInstances)"
  - "Created AddStringFilter helper to bypass DicomQuery type-safe methods for raw string filters like study date ranges"
  - "PacsConnectionResolver extracted as shared static helper in Helpers namespace for reuse across network commands"

patterns-established:
  - "PacsConnectionResolver: flags > connection string > named profile > default profile priority"
  - "ResolveTag: keyword lookup via DicomDictionary.GetEntryByKeyword, hex via DicomTag.TryParse"
  - "Three output formatters: Spectre.Console Table (text), Utf8JsonWriter (json), RFC 4180 (csv)"

# Metrics
duration: 12min
completed: 2026-02-06
---

# Phase 23 Plan 04: Find Command Summary

**C-FIND query command with patient/study/series/instance levels, wildcard filters, configurable return fields, and text/JSON/CSV output via CFindScu**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-02-05T19:36:00Z
- **Completed:** 2026-02-06T01:50:00Z
- **Tasks:** 2
- **Files created:** 2
- **Files modified:** 1

## Accomplishments

- Full C-FIND query at patient, study, series, and instance levels via CFindScu
- 17 CLI options: level selection, 6 query filters (patient-name, patient-id, accession, modality, study-date, study-description), return field customization, output format, result limit, and 7 PACS connection options
- Three output formatters: Spectre.Console Table for text, Utf8JsonWriter for JSON arrays, RFC 4180 CSV with proper escaping
- PacsConnectionResolver shared helper with priority: explicit flags > connection string > named profile > default profile
- Tag resolution from keyword (e.g., PatientName) or hex string (GGGGEEEE or GGGG,EEEE formats)
- Default return fields per query level (patient, study, series, instance)

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement FindCommand with query building and result formatting** - `4c096d1` (feat)
2. **Task 2: Wire FindCommand into Program.cs and extract shared PACS connection resolver** - Included in `9dba910` (parallel plan 23-05 committed Program.cs with all remaining stubs replaced)

## Files Created/Modified

- `src/SharpDicom.Cli/Commands/FindCommand.cs` (561 lines) - Complete find subcommand: query building, PACS connection, result formatting (text/JSON/CSV), tag resolution
- `src/SharpDicom.Cli/Helpers/PacsConnectionResolver.cs` (79 lines) - Shared PACS connection resolution: flags > connection string > named profile > default profile
- `src/SharpDicom.Cli/Program.cs` (modified) - Added `FindCommand.Create()` to subcommands list

## Decisions Made

1. **Private static readonly DicomTag fields for missing constants** - DicomTag.WellKnown lacks StudyDescription (0008,1030), SeriesNumber (0020,0011), SeriesDescription (0008,103E), NumberOfSeriesRelatedInstances (0020,1209), InstanceNumber (0020,0013). Defined locally in FindCommand rather than modifying the source generator, keeping changes scoped to this plan.

2. **ForImages() instead of ForInstances()** - DicomQuery API has `ForImages()` not `ForInstances()`. Accepted both "instance" and "image" as valid level strings for the `--level` option.

3. **AddStringFilter helper for raw dataset manipulation** - DicomQuery.WithStudyDate takes DateTime, but CLI needs raw DICOM date strings and ranges (YYYYMMDD-YYYYMMDD). Created helper that writes directly to the query's underlying dataset via `query.ToDataset()`.

4. **PacsConnectionResolver as separate shared class** - Extracted to `Helpers/PacsConnectionResolver.cs` for reuse by store, find, and future network commands (move, get, echo).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing DicomTag well-known constants**
- **Found during:** Task 1 (FindCommand implementation)
- **Issue:** DicomTag.StudyDescription, SeriesNumber, SeriesDescription, NumberOfSeriesRelatedInstances, InstanceNumber not present in generated well-known constants
- **Fix:** Defined as `private static readonly DicomTag` fields with correct hex values in FindCommand.cs
- **Files modified:** src/SharpDicom.Cli/Commands/FindCommand.cs
- **Verification:** Build succeeds with 0 warnings, 0 errors
- **Committed in:** 4c096d1 (Task 1 commit)

**2. [Rule 3 - Blocking] DicomQuery API mismatch (ForInstances vs ForImages)**
- **Found during:** Task 1 (query building)
- **Issue:** Plan specified `DicomQuery.ForInstances()` but actual API is `ForImages()`
- **Fix:** Used `ForImages()` and accepted both "instance" and "image" as level strings
- **Files modified:** src/SharpDicom.Cli/Commands/FindCommand.cs
- **Verification:** All four query levels build and resolve correctly
- **Committed in:** 4c096d1 (Task 1 commit)

**3. [Rule 3 - Blocking] DicomQuery.WithStudyDate type mismatch**
- **Found during:** Task 1 (filter building)
- **Issue:** `WithStudyDate(DateTime)` takes DateTime, but CLI passes raw DICOM date strings including ranges
- **Fix:** Created `AddStringFilter()` helper that writes raw string filters directly to query dataset
- **Files modified:** src/SharpDicom.Cli/Commands/FindCommand.cs
- **Verification:** Study date, study description, and accession filters all work as raw string values
- **Committed in:** 4c096d1 (Task 1 commit)

---

**Total deviations:** 3 auto-fixed (3 blocking)
**Impact on plan:** All fixes necessary to compile against actual API surface. No scope creep.

## Issues Encountered

- **Parallel plan file contention:** Program.cs was modified by multiple parallel plans (23-02 through 23-05). The find stub replacement was committed as part of plan 23-05's batch commit (`9dba910`) which replaced all remaining stubs simultaneously. This is expected behavior in parallel wave execution.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Find command fully functional and wired into CLI
- PacsConnectionResolver available for future network commands (move, get, echo)
- Ready for integration tests in plan 23-06
- All 5 subcommands (dump, store, find, lint, fix) wired and building

---
*Phase: 23-cli-tools*
*Completed: 2026-02-06*
