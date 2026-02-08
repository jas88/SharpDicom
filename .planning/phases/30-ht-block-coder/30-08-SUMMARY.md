---
phase: 30
plan: 8
subsystem: cli
tags: [cli, convert, transcoding, transfer-syntax, htj2k, batch-processing]
dependency-graph:
  requires: [30-06]
  provides: [sharpdcm-convert-command, transfer-syntax-resolution, batch-transcoding]
  affects: [30-09, 30-10]
tech-stack:
  added: []
  patterns: [cli-command-factory, parallel-file-processing, codec-pipeline]
key-files:
  created:
    - src/SharpDicom.Cli/Commands/ConvertCommand.cs
    - tests/SharpDicom.Tests/Cli/ConvertCommandTests.cs
  modified:
    - src/SharpDicom.Cli/Program.cs
decisions:
  - id: ts-alias-map
    description: Transfer syntax short names use kebab-case (htj2k-lossless, jpeg-baseline, etc.)
    rationale: Consistent with CLI conventions; case-insensitive matching for usability
  - id: default-suffix
    description: Default output uses .converted.dcm suffix when no --output or --force specified
    rationale: Follows existing FixCommand pattern (.fixed.dcm) for safe non-destructive defaults
  - id: parallel-with-semaphore
    description: Parallel mode uses SemaphoreSlim-gated Task.Run for configurable concurrency
    rationale: Allows CPU-bound codec work to saturate cores while respecting user-specified limits
metrics:
  duration: 6m
  completed: 2026-02-08
  tests-added: 41
  tests-total: 2896
---

# Phase 30 Plan 8: `sharpdcm convert` CLI Command Summary

Batch transfer syntax transcoding CLI command with HTJ2K as primary use case, supporting 10 transfer syntax targets and 5 quality presets.

## What Was Built

### ConvertCommand (`src/SharpDicom.Cli/Commands/ConvertCommand.cs`)

Full CLI command following the existing `Command.Create()` factory pattern:

- **Transfer syntax resolution**: 10 short names (htj2k-lossless, htj2k-lossy, jpeg-baseline, jpeg-lossless, j2k-lossless, j2k-lossy, jpeg-ls-lossless, rle, explicit-le, htj2k-lossless-rpcl) plus direct UID input
- **Preset system**: Maps quality names (diagnostic, archive, review, fast, lossless) to HtEncoderOptions from Phase 30-06
- **Batch processing**: Recursive directory enumeration via FileEnumerator, configurable parallelism via SemaphoreSlim
- **Dry-run mode**: Lists conversions without writing files
- **Codec pipeline**: Source decode -> target encode via CodecRegistry, handles compressed-to-compressed, compressed-to-uncompressed, and uncompressed-to-compressed paths
- **Output flexibility**: --output directory, --force in-place overwrite, default .converted.dcm suffix
- **Progress reporting**: TTY-aware via ProgressReporter (Spectre.Console progress bar)
- **Error handling**: --skip-errors for batch resilience, per-file error messages

### Test Coverage (`tests/SharpDicom.Tests/Cli/ConvertCommandTests.cs`)

41 tests covering:
- All 10 transfer syntax short name resolutions with expected UIDs
- Case-insensitivity for both names and presets
- UID-based resolution (direct UID strings)
- Invalid input handling (unknown names, unknown UIDs)
- All 5 preset mappings with property assertions (PSNR, pass counts, lossless flag)
- Output path determination logic (suffix, force, output directory)
- Command structure verification (name, arguments, required options, all option names)
- Non-pixel-data file handling
- Alias and preset map completeness assertions

## Deviations from Plan

None - plan executed exactly as written.

## Integration Points

- Registered in `Program.cs` alongside dump, store, find, lint, fix commands
- Uses existing `FileEnumerator`, `ProgressReporter`, `ExitCodes` helpers
- Leverages `CodecRegistry` for codec lookup and `HtEncoderOptions` presets from Phase 30-06
- `InternalsVisibleTo` already configured in CLI csproj for test access

## Next Phase Readiness

- No blockers identified
- Convert command ready for integration testing once codec implementations mature
- Phase 30-09 (benchmarks) and 30-10 (conformance) can proceed
