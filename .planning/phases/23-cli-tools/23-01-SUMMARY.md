---
phase: 23-cli-tools
plan: 01
subsystem: cli
tags: [system-commandline, spectre-console, tomlyn, toml, cli, dicom-toolkit]

# Dependency graph
requires:
  - phase: 01-core-data-model
    provides: DicomTag, DicomVR, DicomDataset, DicomDictionary, IDicomElement, DicomSequence, DicomStringElement
  - phase: 06-private-tags
    provides: VendorDictionary for private tag name lookup
provides:
  - SharpDicom.Cli project with System.CommandLine root command and 5 subcommand stubs
  - IOutputFormatter abstraction with Text, JSON, XML implementations
  - ConfigLoader with TOML file + env var layered configuration
  - FileEnumerator for recursive .dcm discovery
  - ProgressReporter with TTY-aware Spectre.Console progress
  - ConnectionStringParser for pacs://AET@host:port
  - ExitCodes constants (0/1/2/3)
affects: [23-02, 23-03, 23-04, 23-05, 23-06]

# Tech tracking
tech-stack:
  added: [System.CommandLine 2.0.2, Spectre.Console 0.54.0, Tomlyn 0.20.0]
  patterns: [top-level-statements CLI entry, IOutputFormatter abstraction, layered config precedence]

key-files:
  created:
    - src/SharpDicom.Cli/SharpDicom.Cli.csproj
    - src/SharpDicom.Cli/Program.cs
    - src/SharpDicom.Cli/Helpers/ExitCodes.cs
    - src/SharpDicom.Cli/Helpers/FileEnumerator.cs
    - src/SharpDicom.Cli/Helpers/ProgressReporter.cs
    - src/SharpDicom.Cli/Helpers/ConnectionStringParser.cs
    - src/SharpDicom.Cli/Output/IOutputFormatter.cs
    - src/SharpDicom.Cli/Output/TextFormatter.cs
    - src/SharpDicom.Cli/Output/JsonFormatter.cs
    - src/SharpDicom.Cli/Output/XmlFormatter.cs
    - src/SharpDicom.Cli/Configuration/CliConfig.cs
    - src/SharpDicom.Cli/Configuration/ConfigLoader.cs
    - src/SharpDicom.Cli/Configuration/PacsProfile.cs
  modified:
    - Directory.Build.props
    - Directory.Packages.props
    - SharpDicom.sln

key-decisions:
  - "System.CommandLine 2.0.2 stable API: property-based Option construction, SetAction with ParseResult+CancellationToken"
  - "RootCommand auto-includes VersionOption; no manual VersionOption add needed"
  - "TextFormatter uses reflection on DicomUIDs for UID name reverse-lookup"
  - "Progress output always goes to stderr to avoid corrupting piped structured output"
  - "Config errors produce warnings to stderr, never prevent command execution"

patterns-established:
  - "Top-level statements with System.CommandLine for CLI entry point"
  - "IOutputFormatter interface for format-agnostic DICOM element rendering"
  - "Layered config: TOML file < env vars < CLI flags"
  - "TTY detection via AnsiConsole.Profile.Capabilities for adaptive output"

# Metrics
duration: 7min
completed: 2026-02-06
---

# Phase 23 Plan 01: CLI Scaffolding Summary

**SharpDicom.Cli project with System.CommandLine 2.0.2, Spectre.Console progress, TOML config, and Text/JSON/XML output formatters**

## Performance

- **Duration:** 7 min
- **Started:** 2026-02-06T01:29:08Z
- **Completed:** 2026-02-06T01:37:02Z
- **Tasks:** 2
- **Files modified:** 16

## Accomplishments
- Created new SharpDicom.Cli console project targeting net10.0 with sharpdcm assembly name
- Root command with 7 global options (--format, --verbose, --quiet, --debug, --no-color, --config, --continue-on-error) and 5 stub subcommands (dump, store, find, lint, fix)
- Complete shared infrastructure: output formatters (Text/JSON/XML), config system (TOML + env vars), file enumeration, progress reporting, PACS connection parsing, exit codes

## Task Commits

Each task was committed atomically:

1. **Task 1: Create CLI project with dependencies and solution integration** - `5c2ce96` (chore)
2. **Task 2: Create shared infrastructure (helpers, formatters, config, Program.cs)** - `5a9e6c4` (feat)

## Files Created/Modified
- `src/SharpDicom.Cli/SharpDicom.Cli.csproj` - Console app project referencing SharpDicom, System.CommandLine, Spectre.Console, Tomlyn
- `src/SharpDicom.Cli/Program.cs` - Root command with global options and 5 stub subcommands
- `src/SharpDicom.Cli/Helpers/ExitCodes.cs` - Exit code constants (0=Success, 1=Usage, 2=Runtime, 3=Validation)
- `src/SharpDicom.Cli/Helpers/FileEnumerator.cs` - Lazy recursive .dcm file discovery
- `src/SharpDicom.Cli/Helpers/ProgressReporter.cs` - Spectre.Console progress bar (TTY) / line logging (pipe)
- `src/SharpDicom.Cli/Helpers/ConnectionStringParser.cs` - Parse pacs://AET@host:port
- `src/SharpDicom.Cli/Output/IOutputFormatter.cs` - Format-agnostic output interface
- `src/SharpDicom.Cli/Output/TextFormatter.cs` - dcmdump-style text with colour support and UID name lookup
- `src/SharpDicom.Cli/Output/JsonFormatter.cs` - Utf8JsonWriter-based structured JSON output
- `src/SharpDicom.Cli/Output/XmlFormatter.cs` - XmlWriter-based XML with proper escaping
- `src/SharpDicom.Cli/Configuration/CliConfig.cs` - Configuration model record
- `src/SharpDicom.Cli/Configuration/ConfigLoader.cs` - TOML loading with env var override
- `src/SharpDicom.Cli/Configuration/PacsProfile.cs` - Named PACS connection profile record
- `Directory.Build.props` - Added CLI project config (net10.0, Exe, sharpdcm)
- `Directory.Packages.props` - Added System.CommandLine 2.0.2, Spectre.Console 0.54.0, Tomlyn 0.20.0
- `SharpDicom.sln` - Added SharpDicom.Cli project under src folder

## Decisions Made
- Used System.CommandLine 2.0.2 stable API (property-based `Option` construction, `DefaultValueFactory`, `SetAction` with `ParseResult`+`CancellationToken`) rather than beta-era named parameter constructors
- `RootCommand` auto-includes `VersionOption` in 2.0.2 -- manual add causes duplicate `--version` in help
- TextFormatter uses reflection on generated `DicomUIDs` class for UID name reverse-lookup (can be optimised with compiled lookup if profiling shows need)
- All progress output goes to stderr to keep stdout clean for structured output piping
- Config file parse errors produce warnings to stderr but never block command execution

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] System.CommandLine 2.0.2 API differs from research**
- **Found during:** Task 2 (Program.cs creation)
- **Issue:** Research documented named parameters (`aliases:`, `description:`, `getDefaultValue:`) which were beta-era API. Stable 2.0.2 uses positional constructor + property initializers (`Description`, `DefaultValueFactory`)
- **Fix:** Rewrote all Option declarations to use property-based initialization pattern
- **Files modified:** src/SharpDicom.Cli/Program.cs
- **Verification:** Build succeeds with 0 warnings, --help output correct
- **Committed in:** 5a9e6c4 (Task 2 commit)

**2. [Rule 1 - Bug] Duplicate --version option in help output**
- **Found during:** Task 2 (verification)
- **Issue:** Explicit `rootCommand.Options.Add(new VersionOption())` caused duplicate since `RootCommand` auto-includes it in 2.0.2
- **Fix:** Removed explicit VersionOption add
- **Files modified:** src/SharpDicom.Cli/Program.cs
- **Verification:** --help shows single --version entry
- **Committed in:** 5a9e6c4 (Task 2 commit)

**3. [Rule 1 - Bug] CA1305 locale-sensitive int.ToString() in XmlFormatter**
- **Found during:** Task 2 (build verification)
- **Issue:** `element.Length.ToString()` flagged as locale-sensitive by analyzer (TreatWarningsAsErrors)
- **Fix:** Changed to `element.Length.ToString(CultureInfo.InvariantCulture)`
- **Files modified:** src/SharpDicom.Cli/Output/XmlFormatter.cs
- **Verification:** Build succeeds with 0 warnings
- **Committed in:** 5a9e6c4 (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 blocking)
**Impact on plan:** All auto-fixes necessary for correctness. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All shared infrastructure compiles and is ready for subcommand implementation
- Wave 2 plans (23-02 through 23-05) can now be built independently using the formatters, config, helpers, and stub subcommands
- Ready for 23-02-PLAN.md (sharpdcm dump command)

---
*Phase: 23-cli-tools*
*Completed: 2026-02-06*
