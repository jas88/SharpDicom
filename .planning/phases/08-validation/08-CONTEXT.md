---
phase: 08-validation
created: 2026-01-27
status: ready-for-research
---

# Phase 8: Validation & Strictness — Context

## Phase Goal

Configurable parsing behavior with comprehensive validation options.

## Core Decisions

### Validation Scope

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Parse validation | Full IOD validation | Complete SOP class conformance |
| VR format | All VRs | DA/TM/DT format, PN structure, UID chars |
| VM validation | Yes | Check value count matches dictionary VM |
| Character repertoire | Yes | Validate allowed characters per VR |

### Error Reporting

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Reporting mechanism | Callback + collection | Real-time callback plus aggregate collection |
| Issue detail | Full context | Tag, VR, value, position, severity, message, suggested fix |
| Severity levels | 3 levels | Error (fatal), Warning (recoverable), Info (cosmetic) |
| Issue codes | Unique codes | Each issue type has code like DICOM-001 |

### Recovery Strategies

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Invalid VR handling | Map to UN | Treat as Unknown, preserve bytes |
| Truncated elements | Pad to expected length | Add zeros/spaces to reach declared length |
| Invalid dates | Keep raw, flag invalid | Preserve string, parsing returns null |
| Structure recovery | Best effort | Try to resync after corrupted sequences |

### Custom Rules

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Custom rules | Both callback and interface | Callback for simple, IValidationRule for complex |
| Composition | Rule chains | Chain multiple rules, first failure wins |
| Profiles | Multiple | Strict, Lenient, Permissive, IOD-specific |
| Overrides | Per-tag | Disable/change validation for specific tags |

## Success Criteria

From ROADMAP.md:
- [ ] Strict mode rejects bad files
- [ ] Lenient mode recovers gracefully
- [ ] Validation callback invoked
- [ ] Issues reported with context

## Dependencies

**Requires:**
- Phase 7: File writing for roundtrip validation
- All previous phases: Complete parsing infrastructure

**Provides:**
- Quality assurance for production use
- Conformance testing support

## Implementation Notes

### ValidationIssue

```csharp
public readonly record struct ValidationIssue(
    string Code,           // DICOM-001
    ValidationSeverity Severity,
    DicomTag? Tag,
    DicomVR? DeclaredVR,
    DicomVR? ExpectedVR,
    long? Position,
    string Message,
    string? SuggestedFix,
    ReadOnlyMemory<byte> RawValue
);

public enum ValidationSeverity
{
    Info,      // Cosmetic issues (trailing spaces, etc.)
    Warning,   // Recoverable (VR mismatch, format issues)
    Error      // Fatal (structural corruption, invalid length)
}
```

### ValidationResult

```csharp
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public IEnumerable<ValidationIssue> Errors =>
        Issues.Where(i => i.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == ValidationSeverity.Warning);

    public IEnumerable<ValidationIssue> Infos =>
        Issues.Where(i => i.Severity == ValidationSeverity.Info);
}
```

### IValidationRule Interface

```csharp
public interface IValidationRule
{
    string RuleId { get; }
    string Description { get; }

    ValidationIssue? Validate(in ElementValidationContext context);
}

public readonly struct ElementValidationContext
{
    public DicomTag Tag { get; init; }
    public DicomVR DeclaredVR { get; init; }
    public DicomVR? ExpectedVR { get; init; }
    public ReadOnlyMemory<byte> RawValue { get; init; }
    public DicomDataset Dataset { get; init; }
    public long Position { get; init; }
}
```

### ValidationProfile

```csharp
public class ValidationProfile
{
    public string Name { get; init; }
    public IReadOnlyList<IValidationRule> Rules { get; init; }
    public ValidationBehavior DefaultBehavior { get; init; }
    public IReadOnlyDictionary<DicomTag, ValidationBehavior>? TagOverrides { get; init; }

    public static readonly ValidationProfile Strict;
    public static readonly ValidationProfile Lenient;
    public static readonly ValidationProfile Permissive;

    // IOD-specific profiles
    public static ValidationProfile ForSopClass(DicomUID sopClassUid);
}

public enum ValidationBehavior
{
    Validate,   // Run validation rules
    Warn,       // Log warning, don't fail
    Skip        // No validation
}
```

### DicomReaderOptions Integration

```csharp
public class DicomReaderOptions
{
    // Existing options...

    // Validation
    public ValidationProfile? ValidationProfile { get; init; }
    public Func<ValidationIssue, bool>? ValidationCallback { get; init; }  // Return false to abort
    public bool CollectValidationIssues { get; init; } = true;
}
```

### Built-in Validation Codes

| Code | Severity | Description |
|------|----------|-------------|
| DICOM-001 | Error | Invalid tag format |
| DICOM-002 | Error | Invalid VR format |
| DICOM-003 | Warning | VR mismatch (declared vs expected) |
| DICOM-004 | Warning | Value length exceeds maximum |
| DICOM-005 | Warning | Invalid value multiplicity |
| DICOM-006 | Info | Incorrect padding byte |
| DICOM-007 | Warning | Invalid date format |
| DICOM-008 | Warning | Invalid time format |
| DICOM-009 | Warning | Invalid UID format |
| DICOM-010 | Warning | Invalid person name format |
| DICOM-011 | Error | Truncated element |
| DICOM-012 | Error | Invalid sequence structure |
| DICOM-013 | Warning | Invalid character for VR |
| DICOM-014 | Info | Odd value length |
| DICOM-015 | Error | Missing required tag (Type 1) |
| DICOM-016 | Warning | Missing conditional tag (Type 1C) |
| DICOM-017 | Info | Empty required tag (Type 2) |
| ... | ... | ... |

---

*Created: 2026-01-27*
*Status: Ready for research and planning*
