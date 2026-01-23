---
phase: 01-core-data-model-dictionary
plan: 01
subsystem: core
tags: [dicom, primitives, readonly-structs, multi-targeting, nunit]

# Dependency graph
requires: []
provides:
  - DicomTag struct (4-byte, group/element, private tag detection)
  - DicomVR struct (2-byte, 31 static instances)
  - DicomVRInfo (metadata for each VR)
  - DicomMaskedTag (pattern matching for tag wildcards)
  - ValueMultiplicity (VM validation)
  - Exception hierarchy (DicomException base)
affects: [02-dictionary-generator, 03-dicom-element, 04-dicom-dataset]

# Tech tracking
tech-stack:
  added: [NUnit 4.2.2, System.Memory for netstandard2.0]
  patterns:
    - Multi-targeting (netstandard2.0, net6.0, net8.0, net9.0)
    - Readonly structs for compact primitives
    - Static instances for well-known values
    - Polyfills for netstandard2.0 (IsExternalInit, HashCode)
    - Warnings as errors
    - XML documentation required

key-files:
  created:
    - src/SharpDicom/Data/DicomTag.cs
    - src/SharpDicom/Data/DicomVR.cs
    - src/SharpDicom/Data/DicomVRInfo.cs
    - src/SharpDicom/Data/DicomMaskedTag.cs
    - src/SharpDicom/Data/ValueMultiplicity.cs
    - src/SharpDicom/Data/Exceptions/DicomException.cs
    - tests/SharpDicom.Tests/Data/DicomTagTests.cs
    - tests/SharpDicom.Tests/Data/DicomVRTests.cs

key-decisions:
  - "DicomTag as single uint (4 bytes) with computed properties"
  - "DicomVR as ushort (2 bytes) storing packed ASCII"
  - "Static VR instances for all 31 standard VRs"
  - "Separate DicomVRInfo for metadata (keeps VR compact)"
  - "IsExternalInit polyfill for init properties in netstandard2.0"
  - "HashCode polyfill for netstandard2.0 using (x * 397) ^ y pattern"

patterns-established:
  - "Compact readonly structs for core primitives"
  - "IEquatable<T> and IComparable<T> for value types"
  - "Static Parse/TryParse methods for string conversion"
  - "Multi-TFM polyfills with #if directives"
  - "Comprehensive XML documentation on all public members"

# Metrics
duration: 7min
completed: 2026-01-27
---

# Phase 01 Plan 01: Core Data Model & Dictionary Setup Summary

**Foundation types established: 4-byte DicomTag, 2-byte DicomVR with 31 static instances, pattern matching, and multi-targeted library**

## Performance

- **Duration:** 7 min
- **Started:** 2026-01-27T00:26:00Z
- **Completed:** 2026-01-27T00:33:04Z
- **Tasks:** 3
- **Files modified:** 21

## Accomplishments
- Multi-targeted solution (netstandard2.0, net6.0, net8.0, net9.0) with Central Package Management
- DicomTag readonly struct (4 bytes) with private tag detection and creator slot extraction
- DicomVR readonly struct (2 bytes) with all 31 standard DICOM VRs as static instances
- DicomMaskedTag for pattern matching tags like (50xx,0010) with wildcard support
- Comprehensive test suite with 112 passing tests (40 DicomTag, 45 DicomVR, 15 DicomMaskedTag, 12 ValueMultiplicity)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create solution and project structure** - `433311d` (chore)
2. **Task 2: Implement DicomTag and DicomVR primitives** - `9c21beb` (feat)
3. **Task 3: Write unit tests for primitives** - `ea16c69` (test)

## Files Created/Modified

**Core primitives:**
- `src/SharpDicom/Data/DicomTag.cs` - 4-byte tag struct with group/element, private tag detection
- `src/SharpDicom/Data/DicomVR.cs` - 2-byte VR struct with 31 static instances (AE, DA, UI, etc.)
- `src/SharpDicom/Data/DicomVRInfo.cs` - VR metadata (padding, max length, string/binary)
- `src/SharpDicom/Data/DicomMaskedTag.cs` - Pattern matching for tag wildcards
- `src/SharpDicom/Data/ValueMultiplicity.cs` - VM validation (1, 1-n, 2-2n, etc.)

**Exception hierarchy:**
- `src/SharpDicom/Data/Exceptions/DicomException.cs` - Base exception
- `src/SharpDicom/Data/Exceptions/DicomDataException.cs` - Data parsing exceptions
- `src/SharpDicom/Data/Exceptions/DicomTagException.cs` - Tag-specific exceptions
- `src/SharpDicom/Data/Exceptions/DicomVRException.cs` - VR-specific exceptions

**Test suite:**
- `tests/SharpDicom.Tests/Data/DicomTagTests.cs` - 40 tests
- `tests/SharpDicom.Tests/Data/DicomVRTests.cs` - 45 tests
- `tests/SharpDicom.Tests/Data/DicomMaskedTagTests.cs` - 15 tests
- `tests/SharpDicom.Tests/Data/ValueMultiplicityTests.cs` - 12 tests

**Infrastructure:**
- `global.json` - SDK version pinning to 9.0.x
- `Directory.Build.props` - Nullable, warnings as errors, XML docs
- `Directory.Packages.props` - Central Package Management with NUnit 4.x
- `src/SharpDicom/Internal/IsExternalInit.cs` - Polyfill for init properties

## Decisions Made

**1. Single uint representation for DicomTag (4 bytes)**
- Group in high 16 bits, element in low 16 bits
- All properties computed on demand (Group, Element, IsPrivate, etc.)
- Private creator key computed for lookup: (group << 16) | (element >> 8)
- Rationale: Compact, trivial equality/comparison, no heap allocation

**2. Packed ushort for DicomVR (2 bytes)**
- First char in high byte, second char in low byte: ('A' << 8) | 'E' = 0x4145
- Metadata in separate DicomVRInfo struct via static lookup
- All 31 standard VRs as static readonly instances
- Rationale: Minimal memory, fast equality, VR objects stay compact

**3. Multi-targeting with polyfills**
- netstandard2.0 for maximum compatibility (Unity, Xamarin, .NET Framework 4.6.1+)
- IsExternalInit polyfill for init properties in record structs
- HashCode.Combine polyfill using (x * 397) ^ y pattern
- Rationale: Broad compatibility without compromising modern C# features

**4. Test project excludes XML documentation**
- Added `<GenerateDocumentationFile>false</GenerateDocumentationFile>` to test project
- Rationale: Test methods don't need XML docs, reduces warning noise

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added IsExternalInit polyfill**
- **Found during:** Task 2 (Building primitives)
- **Issue:** Init properties in record structs require IsExternalInit type, not available in netstandard2.0
- **Fix:** Created `src/SharpDicom/Internal/IsExternalInit.cs` with #if NETSTANDARD2_0 guard
- **Files modified:** src/SharpDicom/Internal/IsExternalInit.cs
- **Verification:** Build succeeds on all target frameworks
- **Committed in:** 9c21beb (Task 2 commit)

**2. [Rule 3 - Blocking] Added HashCode polyfill for netstandard2.0**
- **Found during:** Task 2 (Building primitives)
- **Issue:** HashCode.Combine not available in netstandard2.0
- **Fix:** Used `(x * 397) ^ y` pattern for hash combining in netstandard2.0
- **Files modified:** src/SharpDicom/Data/ValueMultiplicity.cs, src/SharpDicom/Data/DicomMaskedTag.cs
- **Verification:** Build succeeds on netstandard2.0
- **Committed in:** 9c21beb (Task 2 commit)

**3. [Rule 3 - Blocking] Disabled XML docs for test project**
- **Found during:** Task 3 (Running tests)
- **Issue:** Test project inheriting GenerateDocumentationFile from Directory.Build.props
- **Fix:** Added `<GenerateDocumentationFile>false</GenerateDocumentationFile>` to test csproj
- **Files modified:** tests/SharpDicom.Tests/SharpDicom.Tests.csproj
- **Verification:** Tests compile and run
- **Committed in:** ea16c69 (Task 3 commit)

**4. [Rule 1 - Bug] Fixed TestCase parameter types**
- **Found during:** Task 3 (Running tests)
- **Issue:** NUnit TestCase attributes passing int literals for uint parameters
- **Fix:** Added 'u' suffix to numeric literals (16u instead of 16)
- **Files modified:** tests/SharpDicom.Tests/Data/DicomVRTests.cs
- **Verification:** All 112 tests pass
- **Committed in:** ea16c69 (Task 3 commit)

**5. [Rule 1 - Bug] Fixed stackalloc in lambda**
- **Found during:** Task 3 (Compiling tests)
- **Issue:** ReadOnlySpan<byte> from stackalloc cannot be captured in lambda (CS8175)
- **Fix:** Changed to heap-allocated byte array for exception test
- **Files modified:** tests/SharpDicom.Tests/Data/DicomVRTests.cs
- **Verification:** Test compiles and passes
- **Committed in:** ea16c69 (Task 3 commit)

---

**Total deviations:** 5 auto-fixed (3 blocking, 2 bugs)
**Impact on plan:** All auto-fixes required for multi-targeting support and test correctness. No scope creep.

## Issues Encountered

**Build errors on netstandard2.0**
- IsExternalInit not available: Fixed with polyfill
- HashCode.Combine not available: Fixed with FNV-1a hash pattern
- Both are expected for netstandard2.0 targeting and were resolved via standard polyfills

**Test compilation errors**
- Stackalloc in lambda: C# language limitation, resolved by using heap allocation for that test
- TestCase type mismatch: Fixed by adding 'u' suffix for uint literals

All issues were straightforward compatibility fixes with no design impact.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**Ready for next phases:**
- Core primitive types (DicomTag, DicomVR) available for dictionary generation
- Exception hierarchy established for error handling
- Test infrastructure in place for future test development
- Multi-targeting proven to work across all frameworks

**Foundation complete for:**
- Phase 01-02: Dictionary source generator (will consume DicomTag, DicomVR, DicomVRInfo)
- Phase 01-03: DicomElement implementation (will use DicomTag, DicomVR as fields)
- Phase 01-04: DicomDataset implementation (will store Dictionary<DicomTag, DicomElement>)

**No blockers or concerns.**

---
*Phase: 01-core-data-model-dictionary*
*Completed: 2026-01-27*
