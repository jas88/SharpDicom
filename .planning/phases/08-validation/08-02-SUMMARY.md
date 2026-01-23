---
phase: 08-validation
plan: 02
subsystem: validation
tags: [validation, VR, format, character-repertoire]
dependency_graph:
  requires: ["08-01"]
  provides: ["built-in validators", "StandardRules collection"]
  affects: ["08-03", "validation-integration"]
tech_stack:
  added: []
  patterns:
    - "IValidationRule implementation"
    - "Span-based character validation"
    - "Per-VR format enforcement"
key_files:
  created:
    - src/SharpDicom/Validation/Rules/DateValidator.cs
    - src/SharpDicom/Validation/Rules/TimeValidator.cs
    - src/SharpDicom/Validation/Rules/DateTimeValidator.cs
    - src/SharpDicom/Validation/Rules/AgeStringValidator.cs
    - src/SharpDicom/Validation/Rules/UidValidator.cs
    - src/SharpDicom/Validation/Rules/PersonNameValidator.cs
    - src/SharpDicom/Validation/Rules/CodeStringValidator.cs
    - src/SharpDicom/Validation/Rules/StringLengthValidator.cs
    - src/SharpDicom/Validation/Rules/CharacterRepertoireValidator.cs
    - src/SharpDicom/Validation/StandardRules.cs
    - tests/SharpDicom.Tests/Validation/Rules/DateValidatorTests.cs
    - tests/SharpDicom.Tests/Validation/Rules/UidValidatorTests.cs
    - tests/SharpDicom.Tests/Validation/Rules/CharacterValidatorTests.cs
  modified: []
decisions:
  - id: "08-02-01"
    choice: "Pre-trimming space-only AE detection"
    reason: "Space-only AE values must be detected before padding is trimmed"
  - id: "08-02-02"
    choice: "Warnings for CS/PN violations"
    reason: "Real-world files frequently violate these constraints; use warnings to allow processing"
  - id: "08-02-03"
    choice: "Error for date/time/UID format violations"
    reason: "These are structural issues that prevent correct interpretation"
metrics:
  duration: "~15 minutes"
  completed: "2026-01-27"
---

# Phase 08 Plan 02: Built-in VR Validators Summary

9 VR validators implementing DICOM PS3.5 Section 6.2 format requirements, plus StandardRules collection.

## What Was Built

### Date/Time Validators (Task 1)

**DateValidator** (`VR-DA-FORMAT`):
- Validates YYYYMMDD format with partial forms (YYYY, YYYYMM)
- Real date validation using DateTime.DaysInMonth for leap years
- Detects invalid months (1-12), days (1-max for month)

**TimeValidator** (`VR-TM-FORMAT`):
- Validates HHMMSS.FFFFFF with partial forms
- Hour 0-23, minute 0-59, second 0-59 bounds checking
- Fractional seconds up to 6 digits

**DateTimeValidator** (`VR-DT-FORMAT`):
- Combined DA + TM validation with timezone
- Timezone offset validation (+/-HHMM)
- Maximum 26 characters

**AgeStringValidator** (`VR-AS-FORMAT`):
- Fixed 4-character format (nnnD/W/M/Y)
- Case-sensitive unit validation

### Identifier/Format Validators (Task 2)

**UidValidator** (`VR-UI-FORMAT`):
- Max 64 characters after null trimming
- Digits and dots only
- No leading zeros in components (except "0" itself)
- No empty components (consecutive dots)

**PersonNameValidator** (`VR-PN-FORMAT`):
- Up to 3 component groups (=)
- Up to 5 components per group (^)
- 64 character limits per component

**CodeStringValidator** (`VR-CS-FORMAT`):
- A-Z, 0-9, space, underscore only
- Warns on lowercase (common real-world issue)
- 16 character limit per value

**StringLengthValidator** (`VR-LENGTH`):
- Uses DicomVRInfo.MaxLength for per-VR limits
- Handles multi-valued strings with backslash delimiter

**CharacterRepertoireValidator** (`VR-CHARS`):
- AE: No backslash, no control chars, no space-only
- DS: 0-9, +, -, E, e, ., space
- IS: 0-9, +, -, space

### StandardRules Collection

```csharp
StandardRules.All              // 9 validators
StandardRules.StructuralOnly   // StringLengthValidator only
StandardRules.DateTimeRules    // DA, TM, DT, AS
StandardRules.IdentifierRules  // UI, CS
StandardRules.CharacterRules   // CharacterRepertoireValidator
```

## Implementation Details

### Validation Patterns

All validators follow the same pattern:
1. Check if VR matches (return null if not applicable)
2. Trim appropriate padding (space for strings, null for UI)
3. Accept empty values (Type 2 support)
4. Validate format and return issue or null

### Error vs Warning Severity

- **Error**: Format prevents correct interpretation (date format, UID structure)
- **Warning**: Common real-world violation (lowercase CS, PN structure issues)

### Character Validation

Span-based byte iteration for zero-allocation validation:
```csharp
for (int i = 0; i < value.Length; i++)
{
    byte c = value[i];
    if (c < '0' || c > '9') return error;
}
```

## Test Coverage

| Test File | Tests | Coverage |
|-----------|-------|----------|
| DateValidatorTests | 22 | Leap years, month bounds, partial dates |
| UidValidatorTests | 19 | Leading zeros, empty components, length |
| CharacterValidatorTests | 63 | CS, DS, IS, AE, TM, AS edge cases |
| **Total** | **104** | Exceeds 30 test minimum |

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 06aeba7 | feat | Add date/time validators (DA, TM, DT, AS) |
| 9c2cbce | feat | Add UID, PN, CS, and character validators |
| 88a680d | test | Add validator test coverage |

## Deviations from Plan

### Pre-trimming Space-Only AE Detection

**Found during**: Task 3 testing
**Issue**: AE values containing only spaces should warn, but trimming occurred first
**Fix**: Added space-only check before trimming in CharacterRepertoireValidator
**Files modified**: CharacterRepertoireValidator.cs

## Verification

- [x] `dotnet build` succeeds with no new warnings
- [x] All 972 tests pass (104 new validator tests)
- [x] DateValidator rejects "20240230" (Feb 30)
- [x] UidValidator rejects "1.02.3" (leading zero)
- [x] CodeStringValidator warns on "abc" (lowercase)
- [x] StandardRules.All contains exactly 9 validators

## Next Phase Readiness

Plan 08-02 complete. Ready for:
- **08-03**: Validation integration with DicomReaderOptions
- Integration of validators with file reading pipeline
