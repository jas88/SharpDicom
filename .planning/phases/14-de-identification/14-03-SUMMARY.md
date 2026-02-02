---
phase: 14-de-identification
plan: 03
subsystem: deidentification
status: complete
completed: 2026-02-02
duration: ~18min
tags: [de-identification, date-shifting, fluent-api, ps3.15]
tech-stack:
  patterns: [fluent-builder, vr-aware-processing, strategy-pattern]
dependency-graph:
  requires: ["14-01", "14-02"]
  provides: ["DicomDeidentifier", "DateShifter", "DicomDeidentifierBuilder"]
  affects: ["14-04", "14-05", "14-06", "14-07"]
key-files:
  created:
    - src/SharpDicom/Deidentification/DateShifter.cs
    - src/SharpDicom/Deidentification/DicomDeidentifier.cs
    - src/SharpDicom/Deidentification/DicomDeidentifierBuilder.cs
  modified:
    - src/SharpDicom/Deidentification/PixelCleaner/HeuristicPhiDetector.cs
    - src/SharpDicom/Deidentification/PixelCleaner/BurnedInPhiRegions.cs
decisions:
  - id: d1
    title: "Direct DicomTag constant for PatientAge"
    context: "Generated DicomTag constants not available at compile time for some TFMs"
    decision: "Use private static readonly DicomTag with hex values"
    rationale: "Avoids source generator timing issues across multi-TFM builds"
  - id: d2
    title: "Explicit Func<string,bool> for StripPrivateTags"
    context: ".NET Standard 2.0 doesn't support target-typed conditionals"
    decision: "Create explicit filter variable before calling StripPrivateTags"
    rationale: "Cross-TFM compatibility"
  - id: d3
    title: "VR comparison via DicomVR equality"
    context: "DicomVR.Code returns ushort, not string"
    decision: "Compare vr == DicomVR.DA instead of vr.Code == 'DA'"
    rationale: "Proper type-safe VR comparison"
metrics:
  tasks-completed: 3
  commits: 3
  files-created: 3
  files-modified: 2
  tests-passing: 1650
  tests-skipped: 25
---

# Phase 14 Plan 03: DicomDeidentifier Engine Summary

DicomDeidentifier core engine with fluent builder API for PS3.15-compliant de-identification

## One-liner

DicomDeidentifier engine with date shifting, UID remapping, action lookup, and fluent builder API

## Completed Tasks

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | DateShifter for VR-aware date handling | 4927a2b | DateShifter.cs, HeuristicPhiDetector.cs, BurnedInPhiRegions.cs |
| 2 | DicomDeidentifier core engine | 56a745c | DicomDeidentifier.cs |
| 3 | DicomDeidentifierBuilder fluent API | b159205 | DicomDeidentifierBuilder.cs |

## Key Implementations

### DateShifter (Task 1)

VR-aware date/time shifting for DA, TM, DT value representations:

```csharp
// Shift a date element
var shifted = DateShifter.Shift(element, TimeSpan.FromDays(-180), zeroTime: true);

// Calculate PatientAge from shifted dates
var age = DateShifter.CalculateAge(birthDate, studyDate); // Returns "042Y"

// Parse DICOM date
var date = DateShifter.ParseDate("20240115"); // Returns DateOnly(2024,1,15)
```

- **ShiftDate**: Shifts DA VR (YYYYMMDD format)
- **ShiftDateTime**: Shifts DT VR with optional time zeroing
- **ZeroTime**: Replaces TM with 000000
- **CalculateAge**: Computes AS format (nnnY) from birth/study dates
- **ParseDate**: Parses YYYYMMDD to DateOnly/DateTime (TFM-aware)

### DicomDeidentifier (Task 2)

Main de-identification engine orchestrating all operations:

```csharp
var options = new DeidentificationOptions
{
    Profile = DeidentificationProfile.Basic,
    DateShiftStrategy = DateShiftStrategy.PerPatient,
    DateShiftRange = (-365, 365)
};
var deidentifier = new DicomDeidentifier(options);
await deidentifier.ApplyAsync(dataset);
```

**Key Links Verified:**
- `DeidentificationActionTable.GetAction(tag, profile)` - Profile-based action lookup
- `_context.GetDateOffset(patientId)` - PerPatient date offset
- `_context.GetStudyDateOffset(studyUid)` - PerStudy date offset
- `_context.RemapUID(originalUid)` - Consistent UID remapping

**Actions Supported:**
- **Remove**: Delete element entirely
- **Zero**: Replace with zero-length value
- **Dummy**: Type-1 safe dummy values per VR
- **UidRemap**: Consistent UID remapping via context
- **Clean**: Date shifting for DA/TM/DT, dummy for text
- **Keep**: Preserve element, process nested sequences

### DicomDeidentifierBuilder (Task 3)

Fluent API for discoverable configuration:

```csharp
var deidentifier = DicomDeidentifier.Create()
    .WithProfile(DeidentificationProfile.Basic)
    .WithOption(DeidentificationProfile.RetainLongitudinalModifiedDates)
    .WithDateShift(-365, 365)
    .WithDateStrategy(DateShiftStrategy.PerPatient)
    .WithZeroTime()
    .WithRecalculateAge()
    .WithRemovePrivateTags()
    .WithSafePrivateCreator("SIEMENS MR HEADER")
    .Build();
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed pre-existing PixelCleaner compilation errors**
- **Found during:** Task 1
- **Issue:** HeuristicPhiDetector.cs used `ValueTask.FromResult` (not available in netstandard2.0), BurnedInPhiRegions.cs had null dereference
- **Fix:** Added conditional `#if NETSTANDARD2_0` for ValueTask constructor, added null-forgiving operator
- **Files:** HeuristicPhiDetector.cs, BurnedInPhiRegions.cs
- **Commit:** 4927a2b

**2. [Rule 1 - Bug] DicomTag.PatientAge not available in netstandard2.0**
- **Found during:** Task 2
- **Issue:** Source-generated DicomTag constants not available at compile time for netstandard2.0 TFM
- **Fix:** Created private static readonly DicomTag with explicit hex values
- **Files:** DicomDeidentifier.cs
- **Commit:** 56a745c

**3. [Rule 1 - Bug] Lambda expression type inference for StripPrivateTags**
- **Found during:** Task 2
- **Issue:** Target-typed conditional expression not supported in netstandard2.0
- **Fix:** Created explicit Func<string, bool> variable before calling StripPrivateTags
- **Files:** DicomDeidentifier.cs
- **Commit:** 56a745c

## Verification Results

```
Build: Succeeded (0 warnings, 0 errors)
Tests: 1650 passed, 0 failed, 25 skipped
```

Key links verified:
- `_context.GetDateOffset` pattern found at line 106
- `_context.GetStudyDateOffset` pattern found at line 109
- `DeidentificationActionTable.GetAction` pattern found at line 138
- `_context.RemapUID` pattern found at line 255

## Next Phase Readiness

**Blockers:** None

**Ready for:**
- 14-04: Burned-in PHI detection (pixel cleaning)
- 14-05: De-identification tests
- 14-06: DicomFile.Anonymize extension
- 14-07: Integration tests

**Context provides:**
- DicomDeidentifier class for applying de-identification
- DateShifter for VR-aware date handling
- DicomDeidentifierBuilder for fluent configuration
- Recursive sequence processing
- PatientAge recalculation
- Custom rule support via IDeidentificationRule
