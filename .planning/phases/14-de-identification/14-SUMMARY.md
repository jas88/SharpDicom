# Phase 14: De-identification Summary

## One-liner

PS3.15 Basic Profile de-identification with UUID-derived UID remapping, date shifting, and fluent builder API.

## Objectives Achieved

### 14-01: PS3.15 Source Generator

- Created `Part15Parser` to extract de-identification action table from NEMA part15.xml
- Created `DeidentificationEmitter` to generate C# lookup code
- Integrated with existing `DicomDictionaryGenerator` pipeline
- Added minimal `part15.xml` stub with common tags for testing

### 14-02: Core Types

- `DeidentificationAction` enum - PS3.15 action codes (K, X, Z, D, C, U, compound)
- `DeidentificationProfileOption` flags - retain/clean options
- `ResolvedAction` enum - concrete actions after resolution
- `ActionResolver` - resolves compound actions (X/Z/D, etc.) based on IOD type
- `DummyValueGenerator` - VR-appropriate dummy values
- `DeidentificationOptions` - profile configuration with presets
- `DeidentificationResult` - statistics and UID mapping log

### 14-03: UID Remapping

- `IUidStore` interface for persistence abstraction
- `InMemoryUidStore` for single-session use
- `UidGenerator` for UUID-derived 2.25.xxx format UIDs
- `UidRemapper` for consistent UID mapping across datasets
- Support for deterministic UIDs via seed

### 14-04: Date Shifting

- `DateShifter` for temporal data modification
- Fixed offset strategy for simple cases
- Random-per-patient strategy with consistent offsets
- Handles DA, TM, DT VR formats
- Preserves temporal relationships within patient data

### 14-05: DicomDeidentifier

- Main de-identification engine with PS3.15 profile application
- Fluent `DicomDeidentifierBuilder` for configuration
- Support for tag-specific overrides
- Private tag handling with safe-list
- Automatic de-identification marker insertion

## Test Coverage

82 tests covering:
- Action resolution for all compound types
- Dummy value generation for all string VRs
- UID generation uniqueness and validity
- Date shifting with various strategies
- Full de-identification workflow

## API Examples

```csharp
// Basic de-identification
using var deid = new DicomDeidentifier();
var result = deid.Deidentify(dataset);

// With configuration
using var deid = new DicomDeidentifierBuilder()
    .WithBasicProfile()
    .RetainPatientCharacteristics()
    .RetainDeviceIdentity()
    .CleanDescriptors()
    .WithDateShift(TimeSpan.FromDays(-365))
    .WithSafePrivateCreators("SIEMENS CSA HEADER")
    .Build();

// Consistent UID mapping across files
using var remapper = new UidRemapper();
using var deid = new DicomDeidentifierBuilder()
    .WithUidRemapper(remapper)
    .Build();

foreach (var file in files)
{
    deid.Deidentify(file.Dataset);
}
```

## Not Implemented (Future Work)

### 14-06: JSON Config (Deferred)

- Configuration file format with `$extends` inheritance
- Would enable config-driven de-identification pipelines

### 14-07: Pixel Redaction (Deferred)

- Burned-in annotation detection
- OCR-based text region detection
- Pixel region blackout

### 14-08: Integration Tests (Partial)

- Full end-to-end tests with real DICOM files
- Roundtrip verification

## Technical Decisions

1. **UUID-derived UIDs (2.25.xxx)**: No registered root needed, globally unique
2. **In-memory store default**: Simple use case; SQLite store can be added
3. **Date shifting before de-identification**: Preserves dates for shift, then removes/cleans
4. **Profile options as flags**: Combinable for complex scenarios
5. **Marker insertion**: Automatic PatientIdentityRemoved and DeidentificationMethod

## Files Created

**Source Generator (14-01):**
- `src/SharpDicom.Generators/Parsing/Part15Parser.cs`
- `src/SharpDicom.Generators/Parsing/DeidentificationActionDefinition.cs`
- `src/SharpDicom.Generators/Emitters/DeidentificationEmitter.cs`
- `data/dicom-standard/part15.xml` (stub)

**Core Types (14-02):**
- `src/SharpDicom/Deidentification/ResolvedAction.cs`
- `src/SharpDicom/Deidentification/ActionResolver.cs`
- `src/SharpDicom/Deidentification/DummyValueGenerator.cs`
- `src/SharpDicom/Deidentification/DeidentificationOptions.cs`
- `src/SharpDicom/Deidentification/DeidentificationResult.cs`
- `src/SharpDicom/Deidentification/DeidentificationProfiles.cs`

**UID Remapping (14-03):**
- `src/SharpDicom/Deidentification/IUidStore.cs`
- `src/SharpDicom/Deidentification/InMemoryUidStore.cs`
- `src/SharpDicom/Deidentification/UidGenerator.cs`
- `src/SharpDicom/Deidentification/UidRemapper.cs`

**Date Shifting (14-04):**
- `src/SharpDicom/Deidentification/DateShifter.cs`

**DicomDeidentifier (14-05):**
- `src/SharpDicom/Deidentification/DicomDeidentifier.cs`
- `src/SharpDicom/Deidentification/DicomDeidentifierBuilder.cs`

**Tests:**
- `tests/SharpDicom.Tests/Deidentification/ActionResolverTests.cs`
- `tests/SharpDicom.Tests/Deidentification/DummyValueGeneratorTests.cs`
- `tests/SharpDicom.Tests/Deidentification/UidRemapperTests.cs`
- `tests/SharpDicom.Tests/Deidentification/DateShifterTests.cs`
- `tests/SharpDicom.Tests/Deidentification/DicomDeidentifierTests.cs`

## Commits

1. `0d4c62c` feat(14-01): add PS3.15 de-identification source generator
2. `a38c9f9` feat(14-02): add de-identification core types
3. `6ea44b6` feat(14-03): add UID remapping infrastructure
4. `e15cc59` feat(14-04): add date shifting module
5. `00c39d7` feat(14-05): add DicomDeidentifier with fluent builder
6. `9f1d708` test(14): add comprehensive de-identification test suite
