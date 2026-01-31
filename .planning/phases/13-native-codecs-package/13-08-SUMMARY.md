---
phase: 13-native-codecs-package
plan: 08
subsystem: packaging
tags: [nuget, msbuild, native, cross-platform, release, ci-cd]

# Dependency graph
requires:
  - phase: 13-07
    provides: Native codec wrappers and P/Invoke infrastructure
provides:
  - NuGet package structure for cross-platform native distribution
  - MSBuild targets for native library handling
  - Runtime packages for 6 platforms (win/linux/osx x64/arm64)
  - Release workflow for automated package publishing
  - Third-party license documentation
affects: [13-09, release-automation, nuget-consumers]

# Tech tracking
tech-stack:
  added: [nuget-runtime-packages, msbuild-targets]
  patterns: [rid-conditional-packaging, buildtransitive-targets]

key-files:
  created:
    - src/SharpDicom.Codecs/build/SharpDicom.Codecs.targets
    - src/SharpDicom.Codecs/buildTransitive/SharpDicom.Codecs.targets
    - nuget/SharpDicom.Codecs.runtime.*.nuspec (6 files)
    - nuget/README.md
    - nuget/THIRD_PARTY_LICENSES.txt
    - .github/workflows/release.yml
  modified:
    - src/SharpDicom.Codecs/SharpDicom.Codecs.csproj

key-decisions:
  - "Warning-only when runtime package unavailable (managed fallback exists)"
  - "GPL-3.0-or-later license for all packages (matches project)"
  - "Skip duplicate on NuGet push to handle re-runs gracefully"

patterns-established:
  - "RID-conditional PackageReference for runtime packages"
  - "buildTransitive targets for transitive consumers"
  - "Prerelease detection from version suffix"

# Metrics
duration: 8min
completed: 2026-01-31
---

# Phase 13 Plan 08: NuGet Package Structure Summary

**MSBuild targets and runtime package specs for cross-platform native codec distribution via NuGet**

## Performance

- **Duration:** 8 min
- **Started:** 2026-01-31T20:57:41Z
- **Completed:** 2026-01-31T21:05:00Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments

- MSBuild targets for native library copy-to-output with RID awareness
- Runtime package specifications for all 6 supported platforms
- Comprehensive third-party license documentation
- Automated release workflow with NuGet and GitHub Release publishing

## Task Commits

Each task was committed atomically:

1. **Task 1: MSBuild targets and csproj updates** - `f7068e6` (feat)
2. **Task 2: Runtime packages and release workflow** - `8aaee24` (feat)

## Files Created/Modified

- `src/SharpDicom.Codecs/build/SharpDicom.Codecs.targets` - Native library copy for direct consumers
- `src/SharpDicom.Codecs/buildTransitive/SharpDicom.Codecs.targets` - Native library copy for transitive consumers
- `src/SharpDicom.Codecs/SharpDicom.Codecs.csproj` - Added targets and RID-conditional runtime refs
- `nuget/SharpDicom.Codecs.runtime.win-x64.nuspec` - Windows x64 runtime package
- `nuget/SharpDicom.Codecs.runtime.win-arm64.nuspec` - Windows ARM64 runtime package
- `nuget/SharpDicom.Codecs.runtime.linux-x64.nuspec` - Linux x64 runtime package
- `nuget/SharpDicom.Codecs.runtime.linux-arm64.nuspec` - Linux ARM64 runtime package
- `nuget/SharpDicom.Codecs.runtime.osx-x64.nuspec` - macOS x64 runtime package
- `nuget/SharpDicom.Codecs.runtime.osx-arm64.nuspec` - macOS ARM64 runtime package
- `nuget/README.md` - Package documentation
- `nuget/THIRD_PARTY_LICENSES.txt` - License texts for bundled libraries
- `.github/workflows/release.yml` - Automated release and publish workflow

## Decisions Made

1. **Warning-only for missing natives** - Emit warning rather than error when runtime package not installed, since managed fallback codecs exist
2. **GPL-3.0-or-later for all packages** - Consistent with main project license and bundled FFmpeg
3. **Skip duplicate on NuGet push** - Handle workflow re-runs gracefully without failing on existing versions
4. **Prerelease detection** - Release workflow marks packages as prerelease based on version suffix

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - all tasks completed successfully.

## User Setup Required

None - no external service configuration required for packaging infrastructure. NUGET_API_KEY secret needed for publishing but that's standard CI/CD configuration.

## Next Phase Readiness

- Package infrastructure complete, ready for Phase 13-09 (final verification)
- Release workflow will work once native builds produce actual libraries (Phase 13b)
- All 3240 tests passing, 0 failures

---
*Phase: 13-native-codecs-package*
*Completed: 2026-01-31*
