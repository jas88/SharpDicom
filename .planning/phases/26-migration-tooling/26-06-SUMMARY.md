---
phase: 26-migration-tooling
plan: 06
subsystem: tooling
tags: [roslyn, analyzer, code-fix, migration, fo-dicom, netstandard2.0]

# Dependency graph
requires:
  - phase: 26-02
    provides: FoDicom5.Compat package with compat namespace structure
  - phase: 26-05
    provides: FoDicom4.Compat package with Dicom namespace structure
provides:
  - SharpDicom.Analyzers Roslyn analyzer project
  - FoDicomUsageAnalyzer (SD0001-SD0003) detecting fo-dicom 4.x and 5.x usage
  - CompatUsageAnalyzer (SD0010-SD0011) detecting compat layer usage
  - FoDicomToCompatFix code fix provider for step 1 migration
  - CompatToNativeFix code fix provider for step 2 migration
affects: [future analyzer tests, NuGet packaging, migration documentation]

# Tech tracking
tech-stack:
  added: [Microsoft.CodeAnalysis.CSharp.Workspaces 5.0.0]
  patterns: [DiagnosticAnalyzer with semantic analysis, CodeFixProvider with BatchFixer, analyzer release tracking]

key-files:
  created:
    - src/SharpDicom.Analyzers/SharpDicom.Analyzers.csproj
    - src/SharpDicom.Analyzers/DiagnosticIds.cs
    - src/SharpDicom.Analyzers/Analyzers/FoDicomUsageAnalyzer.cs
    - src/SharpDicom.Analyzers/Analyzers/CompatUsageAnalyzer.cs
    - src/SharpDicom.Analyzers/CodeFixes/FoDicomToCompatFix.cs
    - src/SharpDicom.Analyzers/CodeFixes/CompatToNativeFix.cs
    - src/SharpDicom.Analyzers/AnalyzerReleases.Shipped.md
    - src/SharpDicom.Analyzers/AnalyzerReleases.Unshipped.md
  modified:
    - Directory.Build.props
    - Directory.Packages.props
    - SharpDicom.sln

key-decisions:
  - "Suppressed RS1038 to keep analyzers and code fixes in one assembly (standard pattern)"
  - "Added analyzer release tracking files (AnalyzerReleases.Shipped.md/Unshipped.md) for RS2008 compliance"
  - "Used semantic analysis for reliable fo-dicom type detection to avoid false positives"

patterns-established:
  - "Roslyn analyzer project structure: netstandard2.0 with EnforceExtendedAnalyzerRules"
  - "Diagnostic ID scheme: SD0001-SD0003 for fo-dicom, SD0010-SD0011 for compat"
  - "Two-step migration fix: fo-dicom -> compat -> native SharpDicom"

# Metrics
duration: 4min
completed: 2026-02-06
---

# Phase 26 Plan 06: SharpDicom.Analyzers Summary

**Roslyn analyzer and code fix providers for two-step fo-dicom migration: SD0001-SD0003 detect fo-dicom usage, SD0010-SD0011 detect compat layer, with FoDicomToCompatFix and CompatToNativeFix code fix providers**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-06T17:52:55Z
- **Completed:** 2026-02-06T17:57:49Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- SharpDicom.Analyzers project targeting netstandard2.0 with full Roslyn analyzer infrastructure
- FoDicomUsageAnalyzer detects `using FellowOakDicom` (5.x) and `using Dicom` (4.x) via semantic analysis
- CompatUsageAnalyzer detects compat layer usage with Info severity for step 2 migration
- FoDicomToCompatFix rewrites fo-dicom using directives to compat layer namespaces
- CompatToNativeFix rewrites compat layer usings to native SharpDicom namespaces
- NuGet packaging ready with analyzer DLL in `analyzers/dotnet/cs` path
- Fix All support via WellKnownFixAllProviders.BatchFixer on both code fix providers

## Task Commits

Each task was committed atomically:

1. **Task 1: Create analyzer project with FoDicomUsageAnalyzer** - `9db6c3f` (feat)
2. **Task 2: Create code fix providers** - `f63d213` (feat)

## Files Created/Modified
- `src/SharpDicom.Analyzers/SharpDicom.Analyzers.csproj` - Analyzer project targeting netstandard2.0 with NuGet packaging
- `src/SharpDicom.Analyzers/DiagnosticIds.cs` - SD0001-SD0003 and SD0010-SD0011 constants
- `src/SharpDicom.Analyzers/Analyzers/FoDicomUsageAnalyzer.cs` - Detects fo-dicom 4.x/5.x usage patterns
- `src/SharpDicom.Analyzers/Analyzers/CompatUsageAnalyzer.cs` - Detects compat layer usage for step 2
- `src/SharpDicom.Analyzers/CodeFixes/FoDicomToCompatFix.cs` - Rewrites fo-dicom usings to compat namespaces
- `src/SharpDicom.Analyzers/CodeFixes/CompatToNativeFix.cs` - Rewrites compat usings to native SharpDicom
- `src/SharpDicom.Analyzers/AnalyzerReleases.Shipped.md` - Release tracking (empty, pre-release)
- `src/SharpDicom.Analyzers/AnalyzerReleases.Unshipped.md` - Release tracking for new diagnostics
- `Directory.Build.props` - Added Analyzers TFM configuration
- `Directory.Packages.props` - Added Microsoft.CodeAnalysis.CSharp.Workspaces 5.0.0
- `SharpDicom.sln` - Added SharpDicom.Analyzers to solution

## Decisions Made
- **RS1038 suppression:** Suppressed RS1038 (analyzer + workspaces in same assembly) because this is the standard pattern for NuGet packages containing both analyzers and code fixes. The alternative (separate assemblies) adds unnecessary complexity for no benefit.
- **Analyzer release tracking:** Added AnalyzerReleases.Shipped.md and Unshipped.md files to comply with RS2008. All five diagnostics listed as unshipped (pre-release).
- **Semantic analysis for type detection:** Used semantic analysis (not just syntax) for FoDicomUsageAnalyzer to reliably identify fo-dicom types vs. user types with similar names.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed RS1038 build error from analyzer + workspaces in same assembly**
- **Found during:** Task 1 (initial build)
- **Issue:** EnforceExtendedAnalyzerRules + Microsoft.CodeAnalysis.Workspaces reference causes RS1038 error
- **Fix:** Added NoWarn for RS1038 with explanation comment (standard pattern for analyzer+codefix packages)
- **Files modified:** src/SharpDicom.Analyzers/SharpDicom.Analyzers.csproj
- **Verification:** Build succeeds with zero errors
- **Committed in:** 9db6c3f (Task 1 commit)

**2. [Rule 3 - Blocking] Added analyzer release tracking files for RS2008 compliance**
- **Found during:** Task 1 (initial build)
- **Issue:** RS2008 requires AnalyzerReleases.Shipped.md and AnalyzerReleases.Unshipped.md
- **Fix:** Created both files with diagnostic tracking, added as AdditionalFiles in csproj
- **Files modified:** AnalyzerReleases.Shipped.md, AnalyzerReleases.Unshipped.md, csproj
- **Verification:** Build succeeds with zero errors
- **Committed in:** 9db6c3f (Task 1 commit)

**3. [Rule 1 - Bug] Fixed nullable reference type error in code fix providers**
- **Found during:** Task 2 (code fix build)
- **Issue:** CS8600 from FirstAncestorOrSelf returning nullable UsingDirectiveSyntax? when assigned to non-nullable pattern variable
- **Fix:** Refactored to use `as` cast with null-coalescing operator instead of `is not` pattern
- **Files modified:** FoDicomToCompatFix.cs, CompatToNativeFix.cs
- **Verification:** Build succeeds with zero errors
- **Committed in:** f63d213 (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (1 bug, 2 blocking)
**Impact on plan:** All auto-fixes necessary for compilation. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- SharpDicom.Analyzers project complete with analyzers and code fixes
- Phase 26 is now complete (all 4 plans executed)
- Ready for phase transition

---
*Phase: 26-migration-tooling*
*Completed: 2026-02-06*
