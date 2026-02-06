---
phase: 23-cli-tools
plan: 06
subsystem: testing
tags: [nunit, cli-tests, integration-tests, unit-tests, formatters, dicom-fixer]

# Dependency graph
requires:
  - phase: 23-cli-tools
    provides: CLI scaffolding (System.CommandLine, formatters, config, helpers)
  - phase: 23-02
    provides: DumpCommand with TextFormatter, JsonFormatter, XmlFormatter
  - phase: 23-05
    provides: LintCommand, FixCommand, DicomFixer engine, FixAction record
provides:
  - 60 NUnit tests covering all CLI helpers, formatters, lint validation, and DicomFixer engine
  - InternalsVisibleTo configuration enabling test access to internal CLI types
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns: [InternalsVisibleTo for CLI internal type testing, temp directory test fixtures with TearDown cleanup]

key-files:
  created:
    - tests/SharpDicom.Tests/Cli/ConnectionStringParserTests.cs
    - tests/SharpDicom.Tests/Cli/ConfigLoaderTests.cs
    - tests/SharpDicom.Tests/Cli/FileEnumeratorTests.cs
    - tests/SharpDicom.Tests/Cli/DumpCommandTests.cs
    - tests/SharpDicom.Tests/Cli/LintCommandTests.cs
    - tests/SharpDicom.Tests/Cli/FixCommandTests.cs
  modified:
    - src/SharpDicom.Cli/SharpDicom.Cli.csproj
    - tests/SharpDicom.Tests/SharpDicom.Tests.csproj
    - tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj

key-decisions:
  - "Added InternalsVisibleTo to SharpDicom.Cli for test access to internal types"
  - "Split strict/lenient validation tests based on actual profile behavior (strict throws, lenient collects)"
  - "Excluded Cli test files from Polyfills test project to avoid missing reference errors"

patterns-established:
  - "Temp directory with SetUp/TearDown for file-system-dependent CLI tests"
  - "Programmatic DicomDataset construction for formatter and fixer tests"

# Metrics
duration: 18min
completed: 2026-02-05
---

# Phase 23 Plan 06: Integration Tests Summary

**60 NUnit tests covering ConnectionStringParser, ConfigLoader, FileEnumerator, Text/JSON/XML formatters, LintCommand validation profiles, and DicomFixer engine (UID/date/time/encoding/removal fixes)**

## Performance

- **Duration:** 18 min
- **Tasks:** 3 (2 auto + 1 checkpoint)
- **Files created:** 6
- **Files modified:** 3

## Accomplishments
- 10 ConnectionStringParser tests: valid with/without port, IP addresses, empty/null/malformed inputs, port range validation, case-insensitive scheme
- 8 ConfigLoader tests: defaults without config file, TOML parsing, malformed file handling, environment variable overrides for output format/verbosity/color, multiple profile loading
- 7 FileEnumerator tests: .dcm filtering, recursive/non-recursive discovery, non-existent path handling, single file input, allFiles mode
- 12 DumpCommand formatter tests: TextFormatter format and depth indentation, JsonFormatter structure and sequence handling, XmlFormatter well-formedness and attributes
- 7 LintCommand tests: valid file with strict profile, invalid UID detection with lenient profile, strict profile throws on errors, validation result counting, exit code values
- 17 FixCommand/DicomFixer tests: UID replacement, date reformatting, time normalization, encoding detection, element removal, combined fixes, dry-run behavior, FixAction properties

## Task Commits

Each task was committed atomically:

1. **Task 1: Write unit tests for CLI helpers and formatters** - `a608bc0` (test)
2. **Task 2: Write integration tests for lint, fix, and DicomFixer engine** - `7ccff56` (test)
3. **Task 3: Human verification of CLI toolkit** - checkpoint approved

## Files Created/Modified
- `tests/SharpDicom.Tests/Cli/ConnectionStringParserTests.cs` - Tests for pacs://AET@host:port parsing (10 tests)
- `tests/SharpDicom.Tests/Cli/ConfigLoaderTests.cs` - Tests for TOML config loading and env var overrides (8 tests)
- `tests/SharpDicom.Tests/Cli/FileEnumeratorTests.cs` - Tests for recursive .dcm file discovery (7 tests)
- `tests/SharpDicom.Tests/Cli/DumpCommandTests.cs` - Tests for Text/JSON/XML output formatters (12 tests)
- `tests/SharpDicom.Tests/Cli/LintCommandTests.cs` - Tests for lint validation with strict/lenient profiles (7 tests)
- `tests/SharpDicom.Tests/Cli/FixCommandTests.cs` - Tests for DicomFixer engine across 5 fix categories (17 tests)
- `src/SharpDicom.Cli/SharpDicom.Cli.csproj` - Added InternalsVisibleTo for test project access
- `tests/SharpDicom.Tests/SharpDicom.Tests.csproj` - Added ProjectReference to SharpDicom.Cli
- `tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj` - Excluded Cli test files from compilation

## Decisions Made
- Added `InternalsVisibleTo` to SharpDicom.Cli.csproj so tests can access internal types (ConnectionStringParser, ConfigLoader, FileEnumerator, formatters are all internal)
- Split the strict validation test into two separate tests after discovering that Strict profile throws `DicomDataException` on invalid UIDs rather than collecting them as warnings -- Lenient profile is used for collection-based tests, Strict for exception-based tests
- Excluded `Cli\**` from the Polyfills test project's shared source files since it lacks the SharpDicom.Cli project reference and would fail to compile

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed Directory.DeleteDirectory call in ConfigLoaderTests**
- **Found during:** Task 1 (build verification)
- **Issue:** Used non-existent `Directory.DeleteDirectory(_tempDir)` API
- **Fix:** Changed to `Directory.Delete(_tempDir, true)` which is the correct .NET API
- **Files modified:** tests/SharpDicom.Tests/Cli/ConfigLoaderTests.cs
- **Verification:** Build succeeds, tests pass
- **Committed in:** a608bc0 (Task 1 commit)

**2. [Rule 1 - Bug] Added missing using System.Linq in DumpCommandTests**
- **Found during:** Task 1 (build verification)
- **Issue:** `Elements("Item").Count()` failed with CS1501 because the LINQ extension method was not in scope
- **Fix:** Added `using System.Linq;` to the file
- **Files modified:** tests/SharpDicom.Tests/Cli/DumpCommandTests.cs
- **Verification:** Build succeeds, tests pass
- **Committed in:** a608bc0 (Task 1 commit)

**3. [Rule 1 - Bug] Split strict/lenient validation test**
- **Found during:** Task 2 (test execution)
- **Issue:** `InvalidUidFile_StrictProfile_HasValidationIssues` test expected Strict profile to collect issues, but Strict profile throws `DicomDataException` on validation errors
- **Fix:** Split into two tests: `InvalidUidFile_LenientProfile_HasValidationIssues` (uses Lenient, collects warnings) and `InvalidUidFile_StrictProfile_ThrowsOnError` (asserts exception)
- **Files modified:** tests/SharpDicom.Tests/Cli/LintCommandTests.cs
- **Verification:** Both tests pass; behavior matches actual profile semantics
- **Committed in:** 7ccff56 (Task 2 commit)

**4. [Rule 3 - Blocking] Excluded CLI tests from Polyfills project**
- **Found during:** Task 2 (build verification)
- **Issue:** Polyfills test project shares all test source files via wildcard Include but lacks SharpDicom.Cli project reference, causing CS0234 namespace errors
- **Fix:** Added `Cli\**` to the Exclude pattern in the Polyfills csproj
- **Files modified:** tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj
- **Verification:** Full solution builds and all 4085 tests pass (0 failures)
- **Committed in:** 7ccff56 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (3 bugs, 1 blocking)
**Impact on plan:** All fixes were necessary for correct compilation and test behavior. No scope creep.

## Issues Encountered
None beyond the deviations documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 23 CLI Tools is complete (6/6 plans done)
- All 5 CLI subcommands implemented and tested: dump, store, find, lint, fix
- 60 CLI-specific tests covering helpers, formatters, validation, and fix engine
- Full solution: 4085 tests passing, 0 failures, 176 skipped

---
*Phase: 23-cli-tools*
*Completed: 2026-02-05*
