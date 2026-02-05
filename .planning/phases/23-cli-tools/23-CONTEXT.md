# Phase 23: CLI Tools (sharpdcm) - Context

**Gathered:** 2026-02-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Command-line toolkit for DICOM operations - single binary (`sharpdcm`) with subcommands for common workflows: dump (display file contents), store (send to PACS), find (query PACS), lint (validate files), fix (repair issues).

This phase delivers the CLI infrastructure and core subcommands. Additional subcommands (move, get, echo) and advanced features are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Command Structure
- **Subcommand pattern:** Single verbs (Docker-style: `sharpdcm dump`, `sharpdcm store`, `sharpdcm find`)
- **Flag support:** Both short and long forms (Unix tradition: `-v`/`--verbose`, `-o`/`--output`, `-h`/`--help`)
- **File paths:** Hybrid approach - positional for input files, flags for options (`sharpdcm dump file.dcm --output json`)
- **Directory processing:** Recursive by default - `sharpdcm dump directory/` processes all `.dcm` files automatically

### Output Formats & Verbosity
- **Supported formats:** Human-readable text (default), JSON (`--json` or `--format json`), XML (`--format xml`)
- **Color usage:** Automatic TTY detection - colorize when outputting to terminal, plain text when piped
- **Progress reporting:** Configurable based on output destination
  - TTY: Progress bar with statistics (`[=====>    ] 50/100 files (5 errors)`)
  - Non-TTY: Line-per-file logging (`foo.dcm ... OK`)
- **Verbosity levels:** Four-level system via flags
  - `-q`/`--quiet`: Errors only
  - Normal (default): Standard output
  - `-v`/`--verbose`: Detailed information
  - `-vv`/`--debug`: Debug-level output

### Error Handling & Exit Codes
- **Exit code strategy:** Categorized codes
  - `0`: Success
  - `1`: Usage error (invalid arguments, missing flags)
  - `2`: Runtime error (file not found, network failure)
  - `3`: Validation error (invalid DICOM, constraint violations)
- **Error message format:** Optional verbosity - three styles available
  - Terse (default): `file.dcm: invalid tag 0010,0010`
  - Verbose (`-v`): `ERROR: [file.dcm] Tag (0010,0010) invalid`
  - Compiler-style (`-vv`): Multi-line with context and suggestions (like Rust errors)
- **Invalid file handling:** Configurable via `--continue-on-error` flag
  - Default: Fail fast (stop on first error)
  - With flag: Continue processing, report errors at end
- **Network retry:** Configurable via `--retry N` flag
  - Default: No automatic retries (fail immediately with clear error)
  - With flag: Retry N times before failing

### User Experience & Defaults
- **File modification safety:**
  - Write to new files by default (e.g., `file.dcm` → `file.fixed.dcm`)
  - Only prompt for confirmation when overwriting existing files
  - Require `-f`/`--force` flag to skip confirmation and overwrite
- **Help system:** Tiered documentation
  - `-h`: Brief usage summary
  - `--help`: Full documentation with examples and all flags
  - Man pages: Comprehensive reference documentation
- **Configuration:** Both config file and environment variables
  - Config file: `~/.sharpdcm/config` for persistent defaults (output format, verbosity, PACS endpoints)
  - Environment variables: `SHARPDCM_*` prefix (e.g., `SHARPDCM_OUTPUT_FORMAT`, `SHARPDCM_VERBOSITY`)
  - Precedence: Flags override env vars override config file
- **PACS connection:** Flexible specification
  - Connection string format: `pacs://MY_AET@host:port`
  - Individual flags: `--host`, `--port`, `--aet`
  - Named profiles: `--profile production` (loads from config file)
  - Default behavior: Use default profile if present and nothing specified, otherwise require explicit connection details

### Claude's Discretion
- Specific confirmation prompt wording
- Color scheme and formatting details
- Progress bar implementation library
- Config file format (TOML, YAML, JSON)
- Man page generation approach

</decisions>

<specifics>
## Specific Ideas

- Error handling should feel like modern CLI tools (Rust compiler, ripgrep) - helpful and actionable
- Progress reporting should adapt intelligently to the context (TTY vs pipe)
- Configuration should be discoverable but not required - tool should work well out-of-box
- PACS connection profiles should make common workflows (store to production PACS) simple

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope

</deferred>

---

*Phase: 23-cli-tools*
*Context gathered: 2026-02-05*
