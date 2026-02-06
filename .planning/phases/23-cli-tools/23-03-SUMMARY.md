---
phase: 23-cli-tools
plan: 03
subsystem: cli
tags: [dicom, c-store, networking, cli, system-commandline, spectre-console]

# Dependency graph
requires:
  - phase: 23-01
    provides: CLI scaffolding, shared helpers (FileEnumerator, ConnectionStringParser, ProgressReporter, ConfigLoader, ExitCodes)
  - phase: 10-11
    provides: DicomClient, CStoreScu, PresentationContext, association negotiation
provides:
  - "sharpdcm store subcommand for sending DICOM files to PACS via C-STORE"
  - "PACS connection resolution (flags, connection string, named profiles)"
  - "Two-pass SOP Class collection and presentation context negotiation"
affects: [23-06]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Two-pass file scanning: header scan for SOP classes, then bulk send"
    - "Connection resolution precedence: flags > connection string > profile"
    - "StoreCommand static class with Create() factory for System.CommandLine"

key-files:
  created:
    - src/SharpDicom.Cli/Commands/StoreCommand.cs
  modified:
    - src/SharpDicom.Cli/Program.cs

key-decisions:
  - "Two-pass approach for efficiency: scan all files for SOP Class UIDs first, then connect with all needed presentation contexts in a single association"
  - "Accept warnings (IsSuccessOrWarning) as successful sends, not just exact success"
  - "allFiles=true in FileEnumerator to accept any file extension, not just .dcm"
  - "Default CalledAE of ANY-SCP when --host specified without --called-ae"

patterns-established:
  - "Command class pattern: static class with Create() returning Command, private ExecuteAsync handler"
  - "Connection resolution: TryResolveConnection with out params for host/port/ae/tls"

# Metrics
duration: 3min
completed: 2026-02-06
---

# Phase 23 Plan 03: Store Command Summary

**C-STORE SCU CLI command sending DICOM files to PACS with connection profiles, retry, and TTY-aware progress**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-06T01:42:28Z
- **Completed:** 2026-02-06T01:46:19Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Complete `sharpdcm store` subcommand with all connection options (host/port/ae flags, connection string, named profiles)
- Two-pass file processing: scan headers for SOP Class UIDs, then single association with all needed presentation contexts
- Retry logic with configurable count via `--retry` flag
- TTY-aware progress reporting (Spectre.Console progress bar on interactive terminals, line-per-file on pipes)
- Graceful association cleanup via `await using` DicomClient pattern
- Error reporting with DICOM status codes and `--continue-on-error` support

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement StoreCommand** - `918fac4` (feat)
2. **Task 2: Wire into Program.cs** - `e153b34` (feat)

## Files Created/Modified
- `src/SharpDicom.Cli/Commands/StoreCommand.cs` - Complete store subcommand (413 lines): connection resolution, file scanning, C-STORE send loop, progress, retry
- `src/SharpDicom.Cli/Program.cs` - Replaced store stub with StoreCommand.Create()

## Decisions Made
- **Two-pass approach**: Scan all file headers first to collect unique SOP Class UIDs, then connect once with presentation contexts for all SOP classes. More efficient than reconnecting per-file or per-SOP-class.
- **IsSuccessOrWarning for success counting**: DICOM C-STORE warnings (0xB000 range) indicate data was stored with modifications - this counts as success for the purpose of progress reporting.
- **allFiles=true**: Accept any file extension when enumerating, not just .dcm. Users may have DICOM files without the .dcm extension.
- **ANY-SCP default CalledAE**: When --host is specified without --called-ae, default to "ANY-SCP" rather than requiring it.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Parallel plans 23-04 (FindCommand) and 23-05 (DicomFixer) have compilation errors in their files, but these do not affect StoreCommand.cs or Program.cs. Build verification confirmed zero errors/warnings in the files modified by this plan.
- System.CommandLine 2.0.2 uses property-based Argument construction (not constructor params for description). Fixed to use `Description` property.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Store command ready for integration testing in plan 23-06
- All connection resolution patterns established for reuse by find command

---
*Phase: 23-cli-tools*
*Completed: 2026-02-06*
