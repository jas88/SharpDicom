---
phase: 01-core-data-model-dictionary
plan: 02
subsystem: source-generation
tags: [roslyn, code-generation, dicom-dictionary, xml-parsing]
requires: []
provides:
  - nema-xml-cache
  - source-generator-infrastructure
  - xml-parsing-stubs
  - code-emission-stubs
affects:
  - 01-04-parsing-implementation
  - 01-05-code-emission
tech-stack:
  added:
    - Microsoft.CodeAnalysis.CSharp@4.8.0
    - Microsoft.CodeAnalysis.Analyzers@3.3.4
  patterns:
    - Incremental Source Generators
    - Central Package Management
key-files:
  created:
    - data/dicom-standard/part06.xml
    - data/dicom-standard/part07.xml
    - data/dicom-standard/VERSION
    - src/SharpDicom.Generators/SharpDicom.Generators.csproj
    - src/SharpDicom.Generators/DicomDictionaryGenerator.cs
    - src/SharpDicom.Generators/Parsing/TagDefinition.cs
    - src/SharpDicom.Generators/Parsing/UidDefinition.cs
    - src/SharpDicom.Generators/Parsing/Part6Parser.cs
    - src/SharpDicom.Generators/Parsing/Part7Parser.cs
    - src/SharpDicom.Generators/Emitters/TagEmitter.cs
    - src/SharpDicom.Generators/Emitters/UidEmitter.cs
  modified:
    - Directory.Packages.props
    - SharpDicom.sln
    - src/SharpDicom/SharpDicom.csproj
    - src/SharpDicom/Data/DicomMaskedTag.cs
decisions:
  - decision: Cache NEMA XML files in repository
    rationale: Enables reproducible builds and offline compilation
    alternatives: [Download at build time, Embed as resources]
    trade-offs: 10MB repository size increase vs build reliability
  - decision: Use Roslyn incremental source generators
    rationale: Compile-time code generation with incremental build support
    alternatives: [T4 templates, Manual code, Runtime reflection]
    trade-offs: netstandard2.0 target required for analyzers
  - decision: Stub parsing/emission for Plan 04
    rationale: Infrastructure first, implementation later enables parallel work
    alternatives: [Implement immediately]
    trade-offs: Two-phase approach adds coordination overhead
  - decision: netstandard2.0 compatibility workarounds
    rationale: Generator must target netstandard2.0 for Roslyn compatibility
    alternatives: [Drop netstandard2.0 support]
    trade-offs: Manual HashCode implementation required
metrics:
  duration: 288s
  tasks-completed: 3
  commits: 3
  files-changed: 18
  lines-added: 176533
  lines-removed: 3
completed: 2026-01-26
---

# Phase 01 Plan 02: Source Generator Infrastructure Summary

NEMA XML cached, Roslyn source generator skeleton created with parsing/emission stubs ready for Plan 04 implementation

## Objective Achieved

Downloaded and cached NEMA DICOM standard XML files (Part 6 and Part 7), created SharpDicom.Generators project with IIncrementalGenerator implementation, and established XML parsing and code emission infrastructure as stubs.

## Tasks Completed

### Task 1: Download and cache NEMA XML files
**Status**: Complete
**Commit**: 32b9909

Downloaded official DICOM standard XML files from NEMA:
- Part 6 (Data Dictionary): 9.2MB, 161,545 lines
- Part 7 (Message Exchange - command fields): 778KB, 14,545 lines
- VERSION file documenting source and download date

Files verified as valid XML with DocBook namespace structure.

**Verification**: Files exist and contain valid XML content

### Task 2: Create source generator project
**Status**: Complete
**Commit**: 92c7fe0

Created SharpDicom.Generators project:
- Targets netstandard2.0 (required for Roslyn analyzers)
- Added Microsoft.CodeAnalysis.CSharp@4.8.0 and Analyzers@3.3.4 to central package management
- Implemented IIncrementalGenerator with [Generator] attribute
- Uses AdditionalTextsProvider to filter for part06.xml and part07.xml
- Fixed hint name collision by using Collect() to emit once for all files
- Referenced from main library as analyzer with OutputItemType="Analyzer"
- Updated solution file to include generator project
- Added XML documentation for public API

**Verification**: Generator project compiles as valid Roslyn analyzer

### Task 3: Create XML parsing and code emission infrastructure
**Status**: Complete
**Commit**: 829e446

Created parsing and emission infrastructure:

**Parsing Layer**:
- TagDefinition struct (readonly struct with constructor for netstandard2.0 compatibility)
- UidDefinition struct (readonly struct with constructor)
- Part6Parser static class with ParseTags() and ParseUids() methods (stubs)
- Part7Parser static class with ParseCommandTags() method (stub)

**Emission Layer**:
- TagEmitter static class with Emit() method (stub generating placeholder class)
- UidEmitter static class with Emit() method (stub generating placeholder class)

**Compatibility Fixes**:
- Fixed DicomMaskedTag.GetHashCode() for netstandard2.0 (manual hash calculation instead of HashCode.Combine)

All stubs compile cleanly and return empty/placeholder results. Actual parsing and emission logic will be implemented in Plan 04.

**Verification**: Generator and main library build successfully across all target frameworks

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Central Package Management version conflict**
- **Found during**: Task 2
- **Issue**: Generator project specified package versions directly, but project uses central package management
- **Fix**: Added Microsoft.CodeAnalysis.* packages to Directory.Packages.props, removed version attributes from project file
- **Files modified**: Directory.Packages.props, SharpDicom.Generators.csproj
- **Commit**: 92c7fe0

**2. [Rule 1 - Bug] Missing XML documentation**
- **Found during**: Task 2
- **Issue**: Public Initialize method lacked XML doc comment, causing CS1591 error
- **Fix**: Added XML summary and param documentation
- **Files modified**: DicomDictionaryGenerator.cs
- **Commit**: 92c7fe0

**3. [Rule 3 - Blocking] Record struct IsExternalInit not available in netstandard2.0**
- **Found during**: Task 3
- **Issue**: C# 9 record structs require IsExternalInit type which doesn't exist in netstandard2.0
- **Fix**: Converted TagDefinition and UidDefinition from record structs to regular readonly structs with constructors and properties
- **Files modified**: TagDefinition.cs, UidDefinition.cs
- **Commit**: 829e446

**4. [Rule 3 - Blocking] Generator hint name collision**
- **Found during**: Task 3
- **Issue**: Generator emitted same hint name for each XML file, causing ArgumentException
- **Fix**: Used Collect() on AdditionalTextsProvider to combine files and emit once
- **Files modified**: DicomDictionaryGenerator.cs
- **Commit**: 829e446

**5. [Rule 3 - Blocking] HashCode.Combine not available in netstandard2.0**
- **Found during**: Task 3
- **Issue**: DicomMaskedTag.GetHashCode() used System.HashCode which doesn't exist in netstandard2.0
- **Fix**: Added conditional compilation to use manual hash calculation for netstandard2.0
- **Files modified**: DicomMaskedTag.cs
- **Commit**: 829e446

## Architecture Decisions

### Source Generator Design

**IIncrementalGenerator pipeline**:
1. AdditionalTextsProvider filters for part06.xml and part07.xml
2. Collect() combines files to emit once (avoids hint name collision)
3. RegisterSourceOutput generates placeholder comment (implementation in Plan 04)

**Benefits**:
- Incremental builds: Only regenerates when XML files change
- IDE integration: IntelliSense sees generated code
- No runtime overhead: All work done at compile time

### Data Flow

```
NEMA XML Files (cached in repo)
    ↓
AdditionalTextsProvider (filter .xml)
    ↓
Collect() (combine all files)
    ↓
Parse (Part6Parser, Part7Parser) → TagDefinition[], UidDefinition[]
    ↓
Emit (TagEmitter, UidEmitter) → C# source code
    ↓
AddSource (DicomTag.Generated.cs, DicomUID.Generated.cs)
```

### Compatibility Strategy

**netstandard2.0 challenges**:
- No record structs (IsExternalInit missing) → Use regular structs with constructors
- No HashCode struct → Manual hash calculation with conditional compilation
- No DateOnly/TimeOnly → Polyfills in main library (from Plan 01-01)

**Solution**: Conditional compilation and manual implementations where needed, while maintaining modern C# syntax for newer targets.

## Next Phase Readiness

**Ready for Plan 04**:
- ✅ XML files cached and accessible
- ✅ Generator project compiles as analyzer
- ✅ Parsing infrastructure in place (TagDefinition, UidDefinition, Part*Parser)
- ✅ Emission infrastructure in place (TagEmitter, UidEmitter)
- ✅ Main library references generator correctly
- ✅ Full solution builds across all target frameworks

**Blockers**: None

**Risks**: None identified

**Open Questions**: None

## Lessons Learned

### What Went Well
1. Central Package Management caught version conflicts early
2. Incremental generator pattern with Collect() prevents hint name collisions
3. netstandard2.0 compatibility issues caught and fixed immediately
4. Stub-first approach validates architecture before implementation

### What Could Be Improved
1. Could have anticipated netstandard2.0 compatibility issues (IsExternalInit, HashCode)
2. Initial generator emitted per-file instead of combined - caught early

### Technical Debt
None introduced. All compatibility issues resolved immediately.

## Testing Notes

**Build verification**:
- ✅ Generator project compiles as netstandard2.0 analyzer
- ✅ Main library compiles for netstandard2.0, net6.0, net8.0, net9.0
- ✅ Test project compiles (net9.0)
- ✅ Full solution build succeeds with zero warnings

**Manual testing**:
- Generator runs and produces placeholder output
- No generated code errors (placeholder only)
- XML files are valid and parseable

**Actual parsing/emission testing** deferred to Plan 04 when implementation is complete.

## Files Changed Summary

**Created** (11 files):
- data/dicom-standard/* (3 files): NEMA XML cache
- src/SharpDicom.Generators/* (8 files): Generator infrastructure

**Modified** (4 files):
- Directory.Packages.props: Added CodeAnalysis packages
- SharpDicom.sln: Added generator project
- src/SharpDicom/SharpDicom.csproj: Referenced generator as analyzer
- src/SharpDicom/Data/DicomMaskedTag.cs: Fixed netstandard2.0 hash code

**Total impact**: +176,533 lines (mostly XML data), -3 lines

## Commit History

| Commit | Message | Files | +Lines | -Lines |
|--------|---------|-------|--------|--------|
| 32b9909 | feat(01-02): cache NEMA DICOM standard XML files | 3 | 176,093 | 0 |
| 92c7fe0 | feat(01-02): create Roslyn source generator project | 5 | 77 | 0 |
| 829e446 | feat(01-02): add XML parsing and code emission infrastructure | 7 | 218 | 3 |

**Total**: 3 commits, 18 files changed, 176,533 insertions, 3 deletions
