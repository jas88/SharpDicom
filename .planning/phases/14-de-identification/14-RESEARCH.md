# Phase 14: De-identification Research

**Completed:** 2026-01-29
**Status:** Ready for planning

---

## PS3.15 Basic Application Level Confidentiality Profile

### Overview

The Basic Application Level Confidentiality Profile (PS3.15 Annex E) provides a standards-compliant method for removing Individually Identifiable Information from DICOM datasets. The profile is designed for:

- Teaching files and publications
- Research and clinical trials
- Registry submissions
- Any scenario requiring patient identity protection

### Source Data: part15.xml

The NEMA part15.xml DocBook document (3.5MB, available at `https://dicom.nema.org/medical/dicom/current/source/docbook/part15/part15.xml`) contains two key tables:

**Table E.1-1a: De-identification Action Codes**
- Defines the meaning of each action code
- Simple XML structure: `<tr>` rows with code and description cells

**Table E.1-1: Application Level Confidentiality Profile Attributes**
- ~600+ attributes with actions per profile/option
- 15 columns: Attribute Name, Tag, Retired, In Std IOD, Basic Profile, and 10 option columns
- XML structure follows same pattern as part06.xml (DocBook namespace)

### XML Structure for Source Generator

```xml
<table frame="box" label="E.1-1" rules="all" xml:id="table_E.1-1">
  <caption>Application Level Confidentiality Profile Attributes</caption>
  <thead>...</thead>
  <tbody>
    <tr valign="top">
      <td align="left"><para>Accession Number</para></td>
      <td align="center"><para>(0008,0050)</para></td>
      <td align="center"><para>N</para></td>  <!-- Retired -->
      <td align="center"><para>Y</para></td>  <!-- In Std IOD -->
      <td align="center"><para>Z</para></td>  <!-- Basic Profile -->
      <td align="center"/>                     <!-- Rtn. Safe Priv. -->
      <td align="center"/>                     <!-- Rtn. UIDs -->
      <!-- ... more option columns ... -->
    </tr>
  </tbody>
</table>
```

---

## De-identification Action Codes

### Primary Action Codes

| Code | Action | Implementation |
|------|--------|----------------|
| **D** | Replace with non-zero dummy value consistent with VR | Generate VR-appropriate placeholder |
| **Z** | Replace with zero-length or dummy value | Empty string or VR-appropriate placeholder |
| **X** | Remove attribute (and sequence contents) | Delete from dataset |
| **K** | Keep unchanged (clean sequences recursively) | Preserve value, recurse into sequences |
| **C** | Clean - replace with safe values of similar meaning | Context-aware replacement |
| **U** | Replace with internally consistent UID | UID remapping with referential integrity |

### Compound Action Codes

| Code | Meaning | Resolution |
|------|---------|------------|
| **Z/D** | Z unless D required for Type 2 vs Type 1 | Check IOD type, prefer Z |
| **X/Z** | X unless Z required for Type 3 vs Type 2 | Check IOD type, prefer X |
| **X/D** | X unless D required for Type 3 vs Type 1 | Check IOD type, prefer X |
| **X/Z/D** | X unless Z or D required for Type 3/2/1 | Check IOD type, prefer most restrictive |
| **X/Z/U*** | X unless Z or U required for sequences with UIDs | Check IOD type, handle contained UIDs |

### Action Resolution Strategy

For compound actions, resolve based on attribute type in IOD:
1. Type 1 (Required): Use D (dummy value required)
2. Type 2 (Required, may be empty): Use Z (empty allowed)
3. Type 3 (Optional): Use X (removal allowed)

When IOD type is unknown, use most restrictive (safest) option.

---

## Profile Options

### Retention Options (Override Basic Profile to KEEP)

| Option | Purpose | Effect |
|--------|---------|--------|
| **Retain Safe Private** | Preserve known-safe private tags | Changes X to K for specified private creators |
| **Retain UIDs** | Keep original UIDs | Changes U to K for all UID attributes |
| **Retain Device Identity** | Keep device serial numbers | Changes X to K for device-related tags |
| **Retain Institution Identity** | Keep institution names | Changes X to K for institution tags |
| **Retain Patient Characteristics** | Keep non-identifying patient data | Changes X to K for age, sex, etc. |
| **Retain Longitudinal Full Dates** | Keep all dates unchanged | Changes X/Z/D to K for date/time tags |
| **Retain Longitudinal Modified Dates** | Keep dates with offset | Changes X/Z/D to C (shifted dates) |

### Cleaning Options (Override Basic Profile to modify/remove more)

| Option | Purpose | Effect |
|--------|---------|--------|
| **Clean Descriptors** | Remove free-text descriptions | Changes K to X for description fields |
| **Clean Structured Content** | Clean SR content items | Recursive SR de-identification |
| **Clean Graphics** | Remove graphic overlays | Changes K to X for graphic annotations |

### Action Precedence

When combining profiles/options:
1. Options override Basic Profile
2. Most restrictive action wins when rules conflict
3. K (keep) is least restrictive, X (remove) is most restrictive

---

## UID Remapping Strategy

### Requirements

1. **Referential Integrity**: All references to a UID must use the same remapped value
2. **Internal Consistency**: Within a study/batch, UIDs must be consistently remapped
3. **Standard UIDs Preserved**: Transfer Syntax, SOP Class, Coding Scheme UIDs never remapped

### UID Categories

**Must Remap (U action):**
- SOP Instance UID (0008,0018)
- Study Instance UID (0020,000D)
- Series Instance UID (0020,000E)
- Referenced SOP Instance UID (0008,1155)
- All UID references in sequences

**Never Remap:**
- Transfer Syntax UID (0002,0010)
- SOP Class UID (0008,0016)
- Coding Scheme UIDs
- Context Group UIDs
- Any DICOM-defined UID (root 1.2.840.10008.*)

### Generation Strategy: UUID-Derived UIDs (2.25.xxx)

The 2.25 arc is the ISO-registered UUID namespace:
- Format: `2.25.{uuid-as-decimal}`
- UUID (128-bit) converted to decimal integer
- Example: `2.25.329800735698586629295641978511506172918`

**Advantages:**
- Globally unique without registration
- No organization root required
- Maximum length: 64 chars (UUID decimal is ~39 digits + prefix)

**Implementation:**
```csharp
public static string GenerateUid()
{
    var uuid = Guid.NewGuid();
    var bytes = uuid.ToByteArray();
    // Convert to big-endian for consistent decimal
    Array.Reverse(bytes);
    var value = new BigInteger(bytes, isUnsigned: true);
    return $"2.25.{value}";
}
```

### UID Reference Discovery

UIDs can appear in:
1. **UI VR elements**: Standard UID attributes
2. **Sequences**: Referenced SOP Instance UID in nested items
3. **Text fields**: UIDs embedded in comments, descriptions (scan with regex)
4. **Private tags**: Vendor-specific UID storage

**Pattern for text scanning:**
```regex
\b[0-9]+(\.[0-9]+){2,}\b
```

### Persistence Strategy: SQLite

For large batch de-identification, UID mappings require persistent storage:

```sql
CREATE TABLE uid_mappings (
    original_uid TEXT PRIMARY KEY,
    remapped_uid TEXT NOT NULL UNIQUE,
    scope TEXT NOT NULL,         -- 'study', 'batch', 'global'
    created_at TEXT NOT NULL,
    expires_at TEXT              -- NULL for permanent
);

CREATE INDEX idx_remapped ON uid_mappings(remapped_uid);
CREATE INDEX idx_scope ON uid_mappings(scope, created_at);
```

**Features:**
- Bidirectional lookup (original -> new, new -> original)
- Scoped mappings (study, batch, or global consistency)
- Transactional batch operations
- Optional TTL for temporary mappings
- Thread-safe with WAL mode

---

## Date/Time Shifting

### Strategies

**1. Fixed Offset (Recommended for most use cases)**
- Same offset applied to all dates within a patient/study
- Preserves temporal relationships (study before follow-up)
- Offset stored in mapping file for reversibility

**2. Random Offset per Patient**
- Random offset within configurable range (e.g., -365 to +365 days)
- Same offset for all dates within patient
- More privacy but harder to audit

**3. Remove Time Component**
- Keep date, remove time portion
- Reduces temporal precision
- May be required for some regulations

### Implementation

```csharp
public readonly struct DateShiftConfig
{
    public DateShiftStrategy Strategy { get; init; }
    public TimeSpan FixedOffset { get; init; }      // For fixed strategy
    public TimeSpan MinOffset { get; init; }        // For random strategy
    public TimeSpan MaxOffset { get; init; }        // For random strategy
    public bool PreserveTimeOfDay { get; init; }    // Keep HH:MM:SS
    public bool ShiftAcrossMidnight { get; init; }  // Allow date change from time shift
}
```

### VR-Specific Handling

| VR | Format | Shift Behavior |
|----|--------|----------------|
| DA | YYYYMMDD | Shift date |
| TM | HHMMSS.FFFFFF | Optionally shift (usually preserve) |
| DT | YYYYMMDDHHMMSS.FFFFFF&ZZXX | Shift datetime, preserve timezone |

### Temporal Relationship Preservation

Within a study, ensure:
1. Acquisition times maintain relative ordering
2. Study date <= series dates <= instance dates
3. Report dates follow acquisition dates

---

## Required Output Attributes

### De-identification Confirmation Attributes

After de-identification, add/update:

| Tag | Attribute | Value |
|-----|-----------|-------|
| (0012,0062) | Patient Identity Removed | "YES" (if removed) |
| (0012,0063) | De-identification Method | Text description |
| (0012,0064) | De-identification Method Code Sequence | Coded terms |
| (0028,0303) | Longitudinal Temporal Information Modified | "MODIFIED" or "REMOVED" |

### De-identification Method Code Sequence Structure

```
(0012,0064) De-identification Method Code Sequence
  > (0008,0100) Code Value: "113100" (Basic Profile)
  > (0008,0102) Coding Scheme Designator: "DCM"
  > (0008,0104) Code Meaning: "Basic Application Confidentiality Profile"
  > (Additional items for each option applied)
```

### Standard Code Values (DCM)

| Code | Meaning |
|------|---------|
| 113100 | Basic Application Confidentiality Profile |
| 113101 | Clean Pixel Data Option |
| 113102 | Clean Recognizable Visual Features Option |
| 113103 | Clean Graphics Option |
| 113104 | Clean Structured Content Option |
| 113105 | Clean Descriptors Option |
| 113106 | Retain Longitudinal Temporal Information Modified Dates Option |
| 113107 | Retain Longitudinal Temporal Information Full Dates Option |
| 113108 | Retain Patient Characteristics Option |
| 113109 | Retain Device Identity Option |
| 113110 | Retain UIDs |
| 113111 | Retain Safe Private Option |
| 113112 | Retain Institution Identity Option |

---

## Source Generator Design

### Part15Parser

Similar to Part6Parser, parse table E.1-1:

```csharp
public record DeidentificationActionDefinition(
    DicomTag Tag,
    string AttributeName,
    bool IsRetired,
    bool InStandardIOD,
    string BasicProfileAction,
    string? RetainSafePrivateAction,
    string? RetainUidsAction,
    string? RetainDeviceIdentityAction,
    string? RetainInstitutionIdentityAction,
    string? RetainPatientCharsAction,
    string? RetainLongFullDatesAction,
    string? RetainLongModifiedDatesAction,
    string? CleanDescriptorsAction,
    string? CleanStructuredContentAction,
    string? CleanGraphicsAction
);
```

### Generated Output

```csharp
public static partial class DeidentificationProfiles
{
    public static IReadOnlyDictionary<DicomTag, DeidentificationAction> BasicProfile { get; }

    public static DeidentificationAction GetAction(
        DicomTag tag,
        DeidentificationOptions options);
}

public enum DeidentificationAction
{
    D,      // Replace with dummy
    Z,      // Replace with zero/dummy
    X,      // Remove
    K,      // Keep
    C,      // Clean
    U,      // UID remap
    ZD,     // Z unless D required
    XZ,     // X unless Z required
    XD,     // X unless D required
    XZD,    // X unless Z or D required
    XZU     // X unless Z or U required
}
```

---

## API Design Patterns

### Fluent Builder Pattern

```csharp
var deidentifier = new DicomDeidentifier()
    .WithBasicProfile()
    .WithOption(DeidentificationOption.RetainPatientCharacteristics)
    .WithOption(DeidentificationOption.CleanDescriptors)
    .WithDateShift(TimeSpan.FromDays(-100))
    .WithUidMapping(uidMapper)
    .WithOverride(DicomTag.InstitutionName, DeidentificationAction.K)
    .Build();

var deidentified = deidentifier.Deidentify(dataset);
```

### Element Callback Integration

```csharp
// Use as element callback during reading
var options = new DicomReaderOptions
{
    ElementCallback = deidentifier.AsElementCallback()
};

// Or use as standalone processor
var result = deidentifier.Process(dataset);
```

### Batch Processing

```csharp
await using var context = new DeidentificationContext(sqlitePath);

await foreach (var file in Directory.EnumerateFiles(inputDir, "*.dcm"))
{
    var dataset = await DicomFile.OpenAsync(file);
    var deidentified = await deidentifier.DeidentifyAsync(dataset, context);
    await deidentified.SaveAsync(Path.Combine(outputDir, Path.GetFileName(file)));
}

// Export mappings for reversibility
await context.ExportMappingsAsync("mappings.json");
```

---

## Clean Pixel Data Option

### Region-Based Redaction

For burned-in annotations, support manual region specification:

```csharp
public readonly struct RedactionRegion
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int? Frame { get; init; }  // null = all frames
}

deidentifier.WithPixelRedaction(new[]
{
    new RedactionRegion { X = 0, Y = 0, Width = 100, Height = 50 },   // Top-left corner
    new RedactionRegion { X = 0, Y = 480, Width = 640, Height = 40 } // Bottom bar
});
```

### High-Risk Modality Detection

Warn for modalities with high burned-in PHI risk:

| Modality | Risk | Common Locations |
|----------|------|------------------|
| US | High | Corners, top bar, bottom bar |
| ES | High | Patient info overlay |
| SC | High | Entire image may be screenshot |
| XA | Medium | Corners, annotation bars |
| RF | Medium | Fluoroscopy annotations |

---

## JSON Configuration Format

### Config Schema

```json
{
  "$schema": "https://sharpdicom.io/schemas/deidentification-config.json",
  "$extends": "basic-profile",

  "options": [
    "RetainPatientCharacteristics",
    "CleanDescriptors"
  ],

  "dateShift": {
    "strategy": "fixed",
    "offsetDays": -100
  },

  "uidMapping": {
    "scope": "study",
    "persistence": "sqlite",
    "dbPath": "./uid-mappings.db"
  },

  "overrides": {
    "(0008,0080)": "K",
    "(0008,1030)": "C"
  },

  "privateTagDefaults": "remove",

  "safePrivateCreators": [
    "SIEMENS CSA HEADER",
    "GEMS_PARM_01"
  ]
}
```

### Inheritance with $extends

```json
{
  "$extends": "clinical-trial-base",
  "overrides": {
    "(0010,0030)": "K"  // Keep birth date for age-stratified analysis
  }
}
```

---

## Implementation Notes

### Threading Considerations

- DeidentificationContext must be thread-safe for parallel batch processing
- SQLite with WAL mode supports concurrent readers
- Use connection pooling for high-throughput scenarios

### Error Handling

- Fail fast: Validate all rules before processing
- Continue on warning: Log but continue for non-critical issues
- Strict mode: Any validation failure aborts

### Audit Logging

Summary-only logging (counts by action type):
```
De-identification complete:
  - D (dummy): 15 attributes
  - Z (zero): 8 attributes
  - X (removed): 42 attributes
  - U (UID remap): 12 UIDs
  - Dates shifted: 6 attributes
  - Regions redacted: 2
```

---

## Success Criteria

1. Source generator parses part15.xml and generates ~600 action definitions
2. Basic Profile removes all required tags per PS3.15
3. UID remapping maintains referential integrity across study
4. Date shifting preserves temporal relationships
5. All profile options implementable via action overrides
6. De-identified files validate with DICOM validator
7. Callback integration works with existing validation framework
8. JSON config enables profile customization without code changes
9. SQLite persistence supports large batch operations
10. Bidirectional UID lookup enables re-identification with mapping file

---

*Research completed: 2026-01-29*
