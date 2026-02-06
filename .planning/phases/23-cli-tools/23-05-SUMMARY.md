---
phase: 23-cli-tools
plan: 05
subsystem: cli
tags: [validation, lint, fix, dicom-repair, ci-integration, json-output]

# Dependency graph
requires:
  - phase: 23-cli-tools
    provides: CLI scaffolding (System.CommandLine, ExitCodes, FileEnumerator, formatters)
  - phase: 08-validation
    provides: ValidationProfile (Strict/Lenient/Permissive), ValidationResult, ValidationIssue, ValidationCodes
  - phase: 14-deidentification
    provides: UidGenerator for generating replacement UIDs
provides:
  - LintCommand for validating DICOM files with configurable strictness and machine-readable output
  - FixCommand for automated DICOM file repair (UIDs, dates, times, encoding, invalid elements)
  - DicomFixer engine with 5 fix categories and FixAction record
affects: [23-06]

# Tech tracking
tech-stack:
  added: []
  patterns: [static Command.Create() factory for subcommands, DicomFixer static fix engine pattern, FixAction readonly record struct]

key-files:
  created:
    - src/SharpDicom.Cli/Commands/LintCommand.cs
    - src/SharpDicom.Cli/Commands/FixCommand.cs
    - src/SharpDicom.Cli/Diagnostics/DicomFixer.cs
    - src/SharpDicom.Cli/Diagnostics/FixAction.cs
  modified:
    - src/SharpDicom.Cli/Program.cs

key-decisions:
  - "Lint uses colored ANSI output when TTY, plain text when piped"
  - "Lint JSON includes per-file issues and aggregate summary"
  - "Fix writes to .fixed.dcm by default; --force for overwrite"
  - "DicomFixer.RemoveInvalidElements is opt-in (destructive)"
  - "--fix-dates flag covers both DA and TM VR elements"

patterns-established:
  - "Static Command.Create() factory returning fully configured Command"
  - "DicomFixer static engine: Fix(dataset, options) returns List<FixAction>"
  - "FixAction record struct for describing repair actions with old/new values"

# Metrics
duration: 6min
completed: 2026-02-06
---

# Phase 23 Plan 05: Lint and Fix Commands Summary

**Lint validates DICOM files with configurable strictness (strict/lenient/permissive) outputting text or JSON; Fix repairs invalid UIDs, dates, times, and encoding with dry-run and safe-output defaults**

## Performance

- **Duration:** 6 min
- **Started:** 2026-02-06T01:43:32Z
- **Completed:** 2026-02-06T01:50:08Z
- **Tasks:** 3
- **Files modified:** 5

## Accomplishments
- LintCommand validates DICOM files against Strict/Lenient/Permissive profiles with text and JSON output formats
- FixCommand applies automated repairs with dry-run mode, safe file output (.fixed.dcm by default), and force overwrite option
- DicomFixer engine with 5 fix categories: invalid UIDs, malformed dates, non-standard times, missing character encoding, invalid elements
- Both commands integrated into CLI with proper help, exit codes (0/2/3), and continue-on-error support

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement LintCommand with validation profiles and machine-readable output** - `ff7c9f6` (feat)
2. **Task 2: Implement FixCommand with DicomFixer engine and safe file output** - `4d770c0` (feat)
3. **Task 3: Wire LintCommand and FixCommand into Program.cs** - `9dba910` (feat)

## Files Created/Modified
- `src/SharpDicom.Cli/Commands/LintCommand.cs` - Lint subcommand with profile selection, severity filtering, text/JSON output
- `src/SharpDicom.Cli/Commands/FixCommand.cs` - Fix subcommand with dry-run, force, output-dir, per-category fix flags
- `src/SharpDicom.Cli/Diagnostics/DicomFixer.cs` - Fix engine with UID repair, date cleanup, time cleanup, encoding fix, element removal
- `src/SharpDicom.Cli/Diagnostics/FixAction.cs` - Readonly record struct describing a single repair action
- `src/SharpDicom.Cli/Program.cs` - Replaced lint/fix stubs with LintCommand.Create() and FixCommand.Create()

## Decisions Made
- Lint text output uses ANSI colour codes when writing to a TTY (red ERROR, yellow WARN, blue INFO) and plain text when piped
- Lint JSON includes both per-file issue arrays and an aggregate summary object for easy CI consumption
- Fix writes repaired files to `{name}.fixed.dcm` by default for safety; `--force` overwrites originals
- DicomFixer.RemoveInvalidElements defaults to off because it is destructive; must be explicitly opted into with `--remove-invalid`
- The `--fix-dates` flag controls both DA (date) and TM (time) VR fixes since they are closely related

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Removed unused DicomReaderOptions reference in DicomFixer**
- **Found during:** Task 2 (build verification)
- **Issue:** DicomFixer.RemoveInvalidElements initially declared an unused `DicomReaderOptions` variable, causing CS0246 due to missing `using SharpDicom.IO`
- **Fix:** Removed the unused variable and simplified the method to use direct element inspection instead of reader-based validation
- **Files modified:** src/SharpDicom.Cli/Diagnostics/DicomFixer.cs
- **Verification:** Build succeeds with 0 warnings
- **Committed in:** 4d770c0 (Task 2 commit)

**2. [Rule 3 - Blocking] Parallel plan contamination in build**
- **Found during:** Task 2 (build verification)
- **Issue:** Parallel plans 23-02, 23-03, 23-04 wrote uncommitted files (DumpCommand.cs, StoreCommand.cs, FindCommand.cs, PacsConnectionResolver.cs) to the working directory. FindCommand.cs initially referenced a type that did not yet exist, causing build failure. This resolved itself when the parallel plan completed its work.
- **Fix:** No action needed; parallel plan completed and the files compiled correctly
- **Files modified:** None (parallel plan issue)
- **Verification:** Build succeeds with 0 warnings after parallel plan completion

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Minor build issues from parallel execution and an unused variable. No scope creep.

## Issues Encountered
None - all issues were handled as deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All 5 CLI subcommands (dump, store, find, lint, fix) are now functional
- Ready for 23-06-PLAN.md (integration tests)

---
*Phase: 23-cli-tools*
*Completed: 2026-02-06*
