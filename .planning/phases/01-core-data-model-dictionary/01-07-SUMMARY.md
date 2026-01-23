---
phase: 01
plan: 07
subsystem: testing
tags: [source-generator, verify, snapshot-testing, integration-tests, phase-1-completion]

dependency-graph:
  requires: ["01-04", "01-06"]
  provides: ["generator-tests", "phase-1-verification", "integration-tests"]
  affects: []

tech-stack:
  added: [Verify.SourceGenerators, Verify.NUnit]
  patterns: [snapshot-testing, module-initializer]

key-files:
  created:
    - tests/SharpDicom.Tests/ModuleInitializer.cs
    - tests/SharpDicom.Tests/Generators/InMemoryAdditionalText.cs
    - tests/SharpDicom.Tests/Generators/DicomDictionaryGeneratorTests.cs
    - tests/SharpDicom.Tests/Data/IntegrationTests.cs
    - tests/SharpDicom.Tests/Generators/*.verified.cs
  modified:
    - Directory.Packages.props
    - tests/SharpDicom.Tests/SharpDicom.Tests.csproj
    - src/SharpDicom.Generators/Emitters/DictionaryEmitter.cs
    - src/SharpDicom.Generators/Parsing/Part6Parser.cs
    - src/SharpDicom/Data/DicomDictionary.cs
    - src/SharpDicom/Data/DicomStringElement.cs
    - src/SharpDicom/Data/ValueMultiplicity.cs

decisions:
  - id: use-verify-for-generator-testing
    context: Need to test source generator output
    choice: Use Verify.SourceGenerators for snapshot testing
    rationale: Industry standard for generator testing, provides diff-friendly output
    alternatives: [manual-string-comparison, roslyn-analyzers]

metrics:
  duration: ~45min
  completed: 2026-01-26
---

# Phase 1 Plan 07: Generator Tests and Phase 1 Verification Summary

Generator tests with Verify.SourceGenerators, integration tests, and full Phase 1 verification.

## What Was Done

### Task 1: Add Verify packages and setup
- Added Verify.SourceGenerators (2.5.0) and Verify.NUnit (28.13.0) packages
- Updated NUnit to 4.3.2 for Verify compatibility
- Created ModuleInitializer.cs for Verify setup
- Created InMemoryAdditionalText.cs helper for generator tests
- Added ProjectReference to SharpDicom.Generators for direct testing

Commit: `5e37533` - feat(01-07): add Verify.SourceGenerators testing infrastructure

### Task 2: Write generator snapshot tests
- Created DicomDictionaryGeneratorTests.cs with 8 snapshot tests
- Tests cover: minimal XML, multi-VR tags, retired tags, UIDs, masked tags, empty XML, invalid XML, combined tags/UIDs
- All tests produce verified snapshot files

Commit: `e5b59ae` - feat(01-07): add generator snapshot tests using Verify.SourceGenerators

### Task 3: Run full Phase 1 test suite
- Created IntegrationTests.cs with 12 comprehensive tests
- Tests cover: dictionary lookups, generated UIDs, TransferSyntax, DicomDataset usage, performance, keyword lookup
- Fixed case-insensitive keyword lookup in GeneratedDictionaryData
- All 226 tests pass

Commit: `08b2ed4` - feat(01-07): add Phase 1 integration tests and fix case-insensitive keyword lookup

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed date/time parsing using wrong format**
- **Found during:** Pre-task verification
- **Issue:** DateOnly.TryParse and TimeOnly.TryParse don't recognize DICOM compact formats (YYYYMMDD, HHMMSS)
- **Fix:** Changed to use format-specific parsing with exact DICOM formats
- **Files modified:** src/SharpDicom/Data/DicomStringElement.cs
- **Commit:** e51e72c

**2. [Rule 1 - Bug] Fixed GetFloat64/GetInt32 for multi-value strings**
- **Found during:** Pre-task verification
- **Issue:** Methods tried to parse entire backslash-separated string instead of first value
- **Fix:** Split on backslash and parse first value only
- **Files modified:** src/SharpDicom/Data/DicomStringElement.cs
- **Commit:** e51e72c

**3. [Rule 3 - Blocking] Fixed Part6Parser not finding tables**
- **Found during:** Pre-task verification
- **Issue:** Parser looked for `<title>` element but XML uses `<caption>` for table titles
- **Fix:** Updated parser to check both `<title>` and `<caption>` elements
- **Files modified:** src/SharpDicom.Generators/Parsing/Part6Parser.cs
- **Commit:** e51e72c

**4. [Rule 1 - Bug] Fixed duplicate key error in generated dictionary**
- **Found during:** Pre-task verification
- **Issue:** XML contains duplicate tags, causing duplicate key exception in FrozenDictionary
- **Fix:** Added deduplication in DictionaryEmitter (prefer non-retired over retired)
- **Files modified:** src/SharpDicom.Generators/Emitters/DictionaryEmitter.cs
- **Commit:** e51e72c

**5. [Rule 2 - Missing Critical] Added ValueMultiplicity.Parse method**
- **Found during:** Pre-task verification
- **Issue:** DicomDictionary needed to parse VM strings but method didn't exist
- **Fix:** Added ValueMultiplicity.Parse() to handle VM string formats from standard
- **Files modified:** src/SharpDicom/Data/ValueMultiplicity.cs
- **Commit:** e51e72c

**6. [Rule 3 - Blocking] Updated DicomDictionary to use generated data**
- **Found during:** Pre-task verification
- **Issue:** DicomDictionary class didn't use GeneratedDictionaryData lookups
- **Fix:** Rewrote DicomDictionary to delegate to GeneratedDictionaryData
- **Files modified:** src/SharpDicom/Data/DicomDictionary.cs
- **Commit:** e51e72c

**7. [Rule 1 - Bug] Fixed test expectation for little-endian byte order**
- **Found during:** Pre-task verification
- **Issue:** Test expected 0xFF01 but little-endian byte order gives 0x00FF
- **Fix:** Corrected test expectation with comments explaining little-endian format
- **Files modified:** tests/SharpDicom.Tests/Data/DicomElementTests.cs
- **Commit:** e51e72c

**8. [Rule 1 - Bug] Fixed case-insensitive keyword lookup**
- **Found during:** Task 3
- **Issue:** GeneratedDictionaryData keyword lookup was case-sensitive
- **Fix:** Added StringComparer.OrdinalIgnoreCase to dictionary creation
- **Files modified:** src/SharpDicom.Generators/Emitters/DictionaryEmitter.cs
- **Commit:** 08b2ed4

**9. [Rule 3 - Blocking] Fixed netstandard2.0 compatibility for date/time parsing**
- **Found during:** Post-task verification
- **Issue:** DateOnly/TimeOnly polyfills don't have TryParseExact method
- **Fix:** Use DateTime.TryParseExact then convert to DateOnly/TimeOnly
- **Files modified:** src/SharpDicom/Data/DicomStringElement.cs
- **Commit:** e4a52a6

## Test Results

### Generator Snapshot Tests (8 tests)
- GeneratesTagsFromMinimalXml - PASS
- GeneratesMultiVRTag - PASS
- GeneratesRetiredTag - PASS
- GeneratesUIDs - PASS
- HandlesMaskedTag - PASS
- HandlesEmptyXml - PASS
- ReportsDiagnosticForInvalidXml - PASS
- GeneratesTagsAndUIDsCombined - PASS

### Integration Tests (12 tests)
- GeneratedDictionaryContainsExpectedTags - PASS
- GeneratedUIDsAreAccessible - PASS
- TransferSyntaxFromGeneratedUID - PASS
- DatasetWithGeneratedTags - PASS
- DictionaryLookupPerformance - PASS (< 500ms for 100K lookups)
- DictionaryContainsMultiVRTag - PASS
- DictionaryKeywordLookupCaseInsensitive - PASS
- DicomTagStaticMembersAreAccessible - PASS
- DicomUIDsContainsExpectedUIDs - PASS
- ValueMultiplicityParseVariants - PASS
- CompleteDatasetRoundtrip - PASS

### Full Test Suite
- **Total Tests:** 226
- **Passed:** 226
- **Failed:** 0
- **Duration:** ~500ms

## Phase 1 Verification

### Success Criteria Met
- [x] DicomTag equality/comparison/hashing works
- [x] Source generator produces tag definitions from NEMA Part 6 XML
- [x] Dictionary lookup is O(1) via FrozenDictionary (net8+) or Dictionary
- [x] All unit tests pass
- [x] Generator snapshot tests verify correct code generation
- [x] Integration tests demonstrate end-to-end functionality

### Build Verification
- [x] Full solution builds in Release mode
- [x] All target frameworks compile (netstandard2.0, net6.0, net8.0, net9.0)
- [x] No warnings

## Generated Code Statistics

From Part 6 XML (2025e edition):
- **Tags generated:** ~5000+ (deduplicated)
- **UIDs generated:** ~1000+ (deduplicated)
- **Transfer syntaxes defined:** 30+

## Files Changed Summary

| File | Changes |
|------|---------|
| Directory.Packages.props | Added Verify packages, updated NUnit |
| tests/SharpDicom.Tests.csproj | Added packages and generator reference |
| ModuleInitializer.cs | Created - Verify initialization |
| InMemoryAdditionalText.cs | Created - Test helper |
| DicomDictionaryGeneratorTests.cs | Created - 8 snapshot tests |
| IntegrationTests.cs | Created - 12 integration tests |
| *.verified.cs | Created - 9 snapshot files |
| DictionaryEmitter.cs | Case-insensitive lookup, deduplication |
| Part6Parser.cs | Check caption element |
| DicomDictionary.cs | Use GeneratedDictionaryData |
| DicomStringElement.cs | Fix date/time/value parsing |
| ValueMultiplicity.cs | Add Parse method |
| DicomElementTests.cs | Fix test expectation |

## Phase 1 Complete

Phase 1 (Core Data Model and Dictionary) is now complete with:
- All 7 plans executed
- 226 tests passing
- Full multi-targeting support
- Source generator producing dictionary from standard XML
- Complete data model with DicomTag, DicomVR, DicomUID, DicomElement hierarchy
- DicomDataset with O(1) lookup and sorted enumeration
- TransferSyntax support
- Comprehensive test coverage including generator snapshots and integration tests
