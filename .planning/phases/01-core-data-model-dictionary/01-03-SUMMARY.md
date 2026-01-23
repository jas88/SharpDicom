---
phase: 01-core-data-model-dictionary
plan: 03
subsystem: data-model
tags: [uid, transfer-syntax, dictionary, inline-storage, zero-allocation]

# Dependency graph
requires:
  - phase: 01-01
    provides: Core primitive types (DicomTag, DicomVR, DicomVRInfo, ValueMultiplicity)
provides:
  - DicomUID struct with 64-byte inline storage
  - TransferSyntax with encoding properties
  - DicomDictionaryEntry for multi-VR tag metadata
  - DicomDictionary runtime lookup class
affects: [01-04-generator, 02-file-reading, 03-sequences]

# Tech tracking
tech-stack:
  added: [System.Numerics.BigInteger, System.Security.Cryptography.SHA256]
  patterns: [inline-storage, multi-tfm-polyfills, zero-allocation-uid]

key-files:
  created:
    - src/SharpDicom/Data/DicomUID.cs
    - src/SharpDicom/Data/TransferSyntax.cs
    - src/SharpDicom/Data/CompressionType.cs
    - src/SharpDicom/Data/DicomDictionaryEntry.cs
    - src/SharpDicom/Data/DicomDictionary.cs
    - src/SharpDicom/Internal/HashCode.cs
    - tests/SharpDicom.Tests/Data/DicomUIDTests.cs
    - tests/SharpDicom.Tests/Data/TransferSyntaxTests.cs

key-decisions:
  - "DicomUID uses 64-byte inline storage (8 longs) for zero-allocation"
  - "HashCode polyfill for netstandard2.0 using xxHash-inspired algorithm"
  - "TransferSyntax as record struct for value semantics"
  - "DicomDictionaryEntry.ValueRepresentations array supports multi-VR tags"
  - "Unknown transfer syntaxes default to Explicit VR Little Endian"

patterns-established:
  - "Inline storage pattern: fixed-size structs with long fields for byte arrays"
  - "Multi-TFM compatibility: conditional compilation for netstandard2.0"
  - "Polyfills in Internal namespace with EditorBrowsable(Never)"
  - "Generator augmentation via partial classes/structs"

# Metrics
duration: 5min
completed: 2026-01-27
---

# Phase 01 Plan 03: DicomUID, TransferSyntax, and Dictionary Entry Summary

**Zero-allocation DicomUID with 64-byte inline storage, TransferSyntax record struct with encoding properties, and DicomDictionaryEntry for multi-VR tag metadata**

## Performance

- **Duration:** 5 min
- **Started:** 2026-01-27T01:11:25Z
- **Completed:** 2026-01-27T01:16:44Z
- **Tasks:** 2
- **Files modified:** 8
- **Tests added:** 41 (29 DicomUID + 12 TransferSyntax)
- **Total test count:** 153 passing

## Accomplishments

- DicomUID stores UIDs up to 64 characters inline without heap allocation
- Three UID generation strategies: UUID-based (2.25.x), timestamp+random, and deterministic hash
- TransferSyntax provides encoding properties for six well-known syntaxes
- DicomDictionaryEntry supports multi-VR tags with DefaultVR property
- DicomDictionary ready for source generator population
- Full netstandard2.0 compatibility with polyfills

## Task Commits

Each task was committed atomically:

1. **Task 1: Implement DicomUID with inline storage** - `79b88ea` (feat)
   - DicomUID readonly struct with 8 long fields for 64-byte storage
   - IsValid property validates UID format (digits/periods, no leading zeros)
   - Generate() creates UUID-based UIDs (2.25.{uuid-as-decimal})
   - Generate(root) creates timestamp+random UIDs
   - GenerateFromName(root, name) creates deterministic hash-based UIDs
   - HashCode polyfill for netstandard2.0 (xxHash-inspired)
   - 29 passing tests

2. **Task 2: Implement TransferSyntax and DicomDictionaryEntry** - `db4df3c` (feat)
   - TransferSyntax record struct with IsExplicitVR, IsLittleEndian, IsEncapsulated, IsLossy
   - CompressionType enum for pixel data compression types
   - Six well-known transfer syntaxes hardcoded
   - FromUID() with unknown handling (IsKnown=false, default to Explicit VR LE)
   - DicomDictionaryEntry with ValueRepresentations array and DefaultVR/HasMultipleVRs properties
   - DicomDictionary partial class with tag/keyword lookups
   - 12 passing tests

## Files Created/Modified

**Created:**
- `src/SharpDicom/Data/DicomUID.cs` - Zero-allocation UID storage with inline 64-byte buffer
- `src/SharpDicom/Data/TransferSyntax.cs` - Transfer syntax encoding properties
- `src/SharpDicom/Data/CompressionType.cs` - Compression type enumeration
- `src/SharpDicom/Data/DicomDictionaryEntry.cs` - Dictionary entry metadata with multi-VR support
- `src/SharpDicom/Data/DicomDictionary.cs` - Runtime dictionary lookup class (partial for generator)
- `src/SharpDicom/Internal/HashCode.cs` - netstandard2.0 polyfill for HashCode struct
- `tests/SharpDicom.Tests/Data/DicomUIDTests.cs` - 29 tests for DicomUID
- `tests/SharpDicom.Tests/Data/TransferSyntaxTests.cs` - 12 tests for TransferSyntax

## Decisions Made

**1. DicomUID inline storage layout**
- **Decision:** Use 8 long fields (_p0 through _p7) for 64-byte storage
- **Rationale:** Sequential layout ensures predictable memory, unsafe code enables span access, struct fits in cache line

**2. HashCode polyfill algorithm**
- **Decision:** xxHash-inspired algorithm with Prime1-5 constants, unchecked arithmetic
- **Rationale:** Fast, good distribution, matches .NET Core behavior for consistent hashing across frameworks

**3. TransferSyntax as record struct**
- **Decision:** record struct instead of class
- **Rationale:** Value semantics, structural equality, immutability, zero-allocation for well-known instances

**4. Unknown transfer syntax handling**
- **Decision:** FromUID() returns IsKnown=false with Explicit VR LE defaults
- **Rationale:** Lenient parsing - most modern DICOM uses Explicit VR LE, allows reading of files with unrecognized syntaxes

**5. DicomDictionary partial class**
- **Decision:** Partial class with internal AddEntry() method
- **Rationale:** Generator can augment class with static initialization, internal method prevents external tampering

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added netstandard2.0 polyfills**
- **Found during:** Task 1 (DicomUID compilation)
- **Issue:** HashCode, Random.Shared, SHA256.HashData, Encoding.GetBytes(span) not available in netstandard2.0
- **Fix:**
  - Created HashCode polyfill in Internal namespace with xxHash-inspired algorithm
  - Used conditional compilation for Random.Shared → new Random()
  - SHA256.HashData → SHA256.Create().ComputeHash()
  - Encoding.GetBytes(span) → unsafe char* → byte* pointer conversion
  - Encoding.GetString(span) → GetString(span.ToArray())
- **Files modified:**
  - src/SharpDicom/Data/DicomUID.cs (conditional compilation blocks)
  - src/SharpDicom/Internal/HashCode.cs (new polyfill)
- **Verification:** Build succeeds on all target frameworks (netstandard2.0, net6.0, net8.0, net9.0)
- **Committed in:** 79b88ea (Task 1 commit)

**2. [Rule 1 - Bug] Fixed HashCode overflow in checked mode**
- **Found during:** Task 1 (netstandard2.0 compilation)
- **Issue:** `v4 = 0u - Prime1` caused CS0220 overflow error in checked arithmetic mode
- **Fix:** Wrapped initialization and Round methods in unchecked blocks
- **Files modified:** src/SharpDicom/Internal/HashCode.cs
- **Verification:** Build succeeds in Release configuration (checked mode)
- **Committed in:** 79b88ea (Task 1 commit)

**3. [Rule 1 - Bug] Fixed sign extension in random generation**
- **Found during:** Task 1 (netstandard2.0 compilation)
- **Issue:** `((ulong)rng.Next() << 32) | (ulong)rng.Next()` caused CS0675 bitwise-or on sign-extended operand
- **Fix:** Cast to uint before ulong: `(((ulong)(uint)rng.Next()) << 32) | (ulong)(uint)rng.Next()`
- **Files modified:** src/SharpDicom/Data/DicomUID.cs
- **Verification:** Build succeeds without warnings
- **Committed in:** 79b88ea (Task 1 commit)

---

**Total deviations:** 3 auto-fixed (1 blocking, 2 bugs)
**Impact on plan:** All deviations necessary for netstandard2.0 compatibility and correct compilation. No scope creep.

## Issues Encountered

None - all multi-TFM compatibility issues resolved via polyfills and conditional compilation.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**Ready:**
- Core data model types complete (DicomTag, DicomVR, DicomUID, TransferSyntax)
- Dictionary entry structure defined for generator population
- DicomDictionary class ready for generated initialization code
- All types support netstandard2.0 through net9.0

**For 01-04 (Source Generator):**
- DicomUID.cs is partial - generator can add static UID members
- TransferSyntax.cs is partial - generator can add more well-known syntaxes
- DicomDictionary.cs is partial - generator will populate _tagIndex and _keywordIndex
- DicomDictionaryEntry includes multi-VR support for tags like Pixel Data (OB/OW)

**Technical context:**
- Inline storage pattern established for large fixed-size data
- Polyfill strategy proven for netstandard2.0 compatibility
- Record structs provide value semantics without heap allocation

---
*Phase: 01-core-data-model-dictionary*
*Completed: 2026-01-27*
