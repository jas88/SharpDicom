---
phase: 14
plan: 01
subsystem: de-identification
tags: [source-generator, ps3.15, confidentiality, xml-parsing]

dependency-graph:
  requires: [phase-1-plan-04]  # DicomDictionaryGenerator infrastructure
  provides: [deidentification-action-table]
  affects: [phase-14-plan-02, phase-14-plan-03]

tech-stack:
  added: []
  patterns: [incremental-source-generation, frozen-dictionary, xml-parsing]

key-files:
  created:
    - data/dicom-standard/part15.xml
    - src/SharpDicom.Generators/Parsing/Part15Parser.cs
    - src/SharpDicom.Generators/Parsing/ConfidentialityActionDefinition.cs
    - src/SharpDicom.Generators/Emitters/DeidentificationTableEmitter.cs
  modified:
    - src/SharpDicom.Generators/DicomDictionaryGenerator.cs
    - src/SharpDicom/Deidentification/DeidentificationProfile.cs

decisions:
  - id: compound-action-codes
    choice: "Extract primary action from compound codes (X/Z -> X)"
    rationale: "Simplifies runtime logic; most restrictive action first"
  - id: frozen-dictionary
    choice: "FrozenDictionary on NET8+, Dictionary fallback"
    rationale: "Matches existing DicomDictionary pattern"
  - id: fully-qualified-enums
    choice: "Generate DeidentificationAction.X not bare X"
    rationale: "Compilation without 'using static' directive"

metrics:
  duration: ~15 minutes
  completed: 2026-02-02
---

# Phase 14 Plan 01: De-identification Action Table Generator Summary

**One-liner:** Source-generated 654-entry de-identification action table from PS3.15 Table E.1-1 with FrozenDictionary lookup.

## What Was Built

### 1. Part 15 XML Cache (data/dicom-standard/part15.xml)
- Downloaded NEMA PS3.15 2025e XML (~3.5MB)
- Contains Table E.1-1 "Application Level Confidentiality Profile Attributes"
- Defines action codes for 654 DICOM attributes across 11 profiles

### 2. Part15Parser (src/SharpDicom.Generators/Parsing/)
- `ConfidentialityActionDefinition`: Readonly struct for parsed table rows
- `Part15Parser.ParseConfidentialityActions`: DocBook XML parser
- Handles masked tags (e.g., 0008,00xx)
- Extracts action codes: D, Z, X, K, C, U, and compounds (X/Z, Z/D, etc.)

### 3. DeidentificationTableEmitter (src/SharpDicom.Generators/Emitters/)
- Generates `DeidentificationActionTable.Generated.cs`
- FrozenDictionary on NET8+ with Dictionary fallback
- ActionEntry struct with GetAction(profile) method
- All enum values fully qualified (e.g., `DeidentificationAction.Remove`)

### 4. Generator Integration
- Added part15.xml to AdditionalTextsProvider filter
- Parses and emits during incremental build
- 654 tag entries generated (exceeds 400+ target)

## API Generated

```csharp
namespace SharpDicom.Deidentification;

public static partial class DeidentificationActionTable
{
    // Main lookup method
    public static DeidentificationAction GetAction(DicomTag tag, DeidentificationProfile profile);

    // Convenience for Basic profile only
    public static DeidentificationAction GetBasicAction(DicomTag tag);

    // Check if tag is in de-identification table
    public static bool TryGetEntry(DicomTag tag, out ActionEntry entry);
}

public readonly record struct ActionEntry(
    DeidentificationAction Basic,
    DeidentificationAction RetainSafePrivate,
    DeidentificationAction RetainUIDs,
    DeidentificationAction RetainDeviceIdentity,
    DeidentificationAction RetainInstitutionIdentity,
    DeidentificationAction RetainPatientCharacteristics,
    DeidentificationAction RetainLongFullDates,
    DeidentificationAction RetainLongModifDates,
    DeidentificationAction CleanDescriptors,
    DeidentificationAction CleanStructuredContent,
    DeidentificationAction CleanGraphics)
{
    public DeidentificationAction GetAction(DeidentificationProfile profile);
}
```

## Profile Handling

The generated `GetAction` method handles profile option flags with proper precedence:
1. Clean options checked first (more restrictive)
2. Retain options checked second (less restrictive)
3. Falls back to Basic profile action

## Deviations from Plan

**1. [Rule 2 - Missing Critical] Added RetainInstitutionIdentity to DeidentificationProfile**
- Found during: Task 3
- Issue: PS3.15 Table E.1-1 has "Retain Institution Identity" column not in existing enum
- Fix: Added `RetainInstitutionIdentity = 1 << 11` to DeidentificationProfile enum
- Files modified: DeidentificationProfile.cs
- Commit: b576471

## Commits

| Hash | Message | Files |
|------|---------|-------|
| 5ea2761 | feat(14-01): download and cache PS3.15 XML for de-identification | part15.xml |
| ace5f5e | feat(14-01): add Part15Parser and ConfidentialityActionDefinition | Part15Parser.cs, ConfidentialityActionDefinition.cs |
| b576471 | feat(14-01): add DeidentificationTableEmitter and generator integration | DeidentificationTableEmitter.cs, DicomDictionaryGenerator.cs, DeidentificationProfile.cs |

## Test Results

- **All 1650 tests pass** (25 skipped - optional DCMTK integration)
- Build succeeds on all target frameworks: netstandard2.0, net8.0, net9.0, net10.0
- Generated file verified with correct entry count and enum qualification

## Next Phase Readiness

This plan provides the foundation for:
- **14-02**: DicomDeidentifier can use GetAction for attribute processing
- **14-03**: Date shifting can leverage action table for DA/DT/TM attributes
- **14-05**: Pixel cleaning knows which attributes need burned-in PHI detection

No blockers or known issues.
