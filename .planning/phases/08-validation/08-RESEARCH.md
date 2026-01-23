# Phase 8: Validation & Strictness - Research

**Researched:** 2026-01-27
**Domain:** DICOM validation, conformance checking, .NET validation patterns
**Confidence:** HIGH

## Summary

This research covers DICOM validation requirements from PS3.5 and PS3.3, VR-specific validation rules, IOD attribute type requirements, common real-world conformance issues, recovery strategies for malformed data, and .NET validation framework design patterns.

DICOM validation operates at multiple levels: structural (element encoding), value (VR-specific format and character constraints), and semantic (IOD conformance with Type 1/1C/2/2C/3 requirements). Real-world DICOM files frequently contain conformance violations that must be handled gracefully in lenient parsing modes while being strictly rejected in conformance-checking contexts.

**Primary recommendation:** Implement a three-tier validation architecture: (1) structural validation during parsing, (2) VR-specific value validation on element access, and (3) optional IOD-level validation via post-parse analysis. Use callback-based issue collection with unique error codes for programmatic handling.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| None | - | Custom validation logic | DICOM validation is domain-specific; general libraries don't understand VR constraints |

### Design Patterns to Follow
| Pattern | From | Purpose | Adaptation |
|---------|------|---------|------------|
| Specification pattern | Validot | Define validation rules declaratively | IValidationRule interface |
| Result objects | FluentValidation/Validot | Aggregate issues without exceptions | ValidationResult class |
| Error codes | dciodvfy | Programmatic error categorization | DICOM-XXX unique codes |
| Severity levels | dciodvfy | Error/Warning/Info distinction | ValidationSeverity enum |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom validation | FluentValidation | FluentValidation doesn't understand DICOM VRs; custom rules would duplicate our work |
| Data Annotations | Custom IValidationRule | Data Annotations tied to models; DICOM needs context-aware validation |

## Architecture Patterns

### Validation Layer Structure
```
src/SharpDicom/
├── Validation/
│   ├── ValidationIssue.cs          # Issue record with context
│   ├── ValidationResult.cs         # Collection of issues
│   ├── ValidationSeverity.cs       # Error/Warning/Info enum
│   ├── ValidationCode.cs           # Unique code constants
│   ├── IValidationRule.cs          # Rule interface
│   ├── ValidationProfile.cs        # Rule collection with presets
│   ├── ValidationContext.cs        # Context passed to rules
│   ├── Rules/
│   │   ├── VR/                     # VR-specific validators
│   │   │   ├── DateValidator.cs    # DA format: YYYYMMDD
│   │   │   ├── TimeValidator.cs    # TM format: HHMMSS.FFFFFF
│   │   │   ├── DateTimeValidator.cs
│   │   │   ├── AgeStringValidator.cs
│   │   │   ├── UidValidator.cs
│   │   │   ├── PersonNameValidator.cs
│   │   │   ├── CodeStringValidator.cs
│   │   │   └── StringLengthValidator.cs
│   │   ├── Structure/              # Structural validators
│   │   │   ├── VRMismatchRule.cs
│   │   │   ├── ValueLengthRule.cs
│   │   │   ├── OddLengthRule.cs
│   │   │   ├── PaddingByteRule.cs
│   │   │   └── ValueMultiplicityRule.cs
│   │   └── IOD/                    # IOD-level validators (future)
│   │       └── AttributeTypeRule.cs
│   └── StandardProfiles.cs         # Strict/Lenient/Permissive presets
```

### Pattern 1: Validation Rule Interface
**What:** Interface for pluggable validation rules
**When to use:** All validation logic
**Example:**
```csharp
// Source: Derived from Validot/FluentValidation patterns + DICOM requirements
public interface IValidationRule
{
    /// <summary>Unique identifier for this rule.</summary>
    string RuleId { get; }

    /// <summary>Human-readable description of what this rule checks.</summary>
    string Description { get; }

    /// <summary>Validates an element in context.</summary>
    /// <param name="context">Element and dataset context.</param>
    /// <returns>Issue if validation fails; null if valid.</returns>
    ValidationIssue? Validate(in ElementValidationContext context);
}

public readonly struct ElementValidationContext
{
    public DicomTag Tag { get; init; }
    public DicomVR DeclaredVR { get; init; }
    public DicomVR? ExpectedVR { get; init; }  // From dictionary
    public ReadOnlyMemory<byte> RawValue { get; init; }
    public DicomDataset Dataset { get; init; }  // For conditional checks
    public DicomEncoding Encoding { get; init; }
    public long StreamPosition { get; init; }
    public bool IsPrivate { get; init; }
    public string? PrivateCreator { get; init; }
}
```

### Pattern 2: Validation Issue with Full Context
**What:** Immutable issue record with all diagnostic information
**When to use:** Reporting any validation failure or warning
**Example:**
```csharp
// Source: Derived from dciodvfy error format + CONTEXT.md requirements
public readonly record struct ValidationIssue(
    string Code,                    // DICOM-001, DICOM-002, etc.
    ValidationSeverity Severity,    // Error, Warning, Info
    DicomTag? Tag,                  // Associated tag if element-level
    DicomVR? DeclaredVR,           // VR in file
    DicomVR? ExpectedVR,           // VR from dictionary
    long? Position,                // Stream position for debugging
    string Message,                // Human-readable description
    string? SuggestedFix,          // How to remediate
    ReadOnlyMemory<byte> RawValue) // Actual bytes for inspection
{
    /// <summary>Creates an error-level issue.</summary>
    public static ValidationIssue Error(string code, DicomTag tag, string message)
        => new(code, ValidationSeverity.Error, tag, null, null, null, message, null, default);

    /// <summary>Creates a warning-level issue.</summary>
    public static ValidationIssue Warning(string code, DicomTag tag, string message)
        => new(code, ValidationSeverity.Warning, tag, null, null, null, message, null, default);
}

public enum ValidationSeverity
{
    /// <summary>Informational only (cosmetic issues).</summary>
    Info = 0,

    /// <summary>Recoverable issue (may indicate data quality problems).</summary>
    Warning = 1,

    /// <summary>Fatal issue (structural corruption, cannot proceed).</summary>
    Error = 2
}
```

### Pattern 3: Validation Result Aggregation
**What:** Collection of all issues with convenience properties
**When to use:** Final validation output
**Example:**
```csharp
// Source: Derived from FluentValidation ValidationResult pattern
public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues;

    public ValidationResult() => _issues = new List<ValidationIssue>();

    public bool IsValid => !_issues.Any(i => i.Severity == ValidationSeverity.Error);
    public bool HasWarnings => _issues.Any(i => i.Severity == ValidationSeverity.Warning);

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public IEnumerable<ValidationIssue> Errors =>
        _issues.Where(i => i.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings =>
        _issues.Where(i => i.Severity == ValidationSeverity.Warning);

    public IEnumerable<ValidationIssue> Infos =>
        _issues.Where(i => i.Severity == ValidationSeverity.Info);

    internal void Add(ValidationIssue issue) => _issues.Add(issue);

    internal void AddRange(IEnumerable<ValidationIssue> issues) => _issues.AddRange(issues);
}
```

### Anti-Patterns to Avoid
- **Throwing exceptions for validation failures:** Validation failures are expected; use result objects, not exceptions
- **Global validation state:** fo-dicom's global `DicomValidation` property causes unexpected behavior; use per-operation options
- **Mixing structural and semantic validation:** Keep parsing separate from IOD conformance checking
- **Allocating on valid path:** Use static issue instances or pooling for common patterns

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Date parsing | Custom YYYYMMDD parser | Span-based parse with format validation | Edge cases: leap years, month boundaries, optional components |
| UID validation | Simple regex | Full UID validation per DICOM | Leading zeros, component length, max 64 chars |
| Person Name parsing | Split by ^ | Full 5-component + 3-group parser | ISO 2022 escape handling, trailing delimiters |
| Character repertoire | ASCII check | Per-VR character set validation | Different VRs allow different characters |

**Key insight:** DICOM validation rules are deceptively complex. What looks like "just check it's 8 digits" (DA) actually requires validating the date is real (no Feb 30), handling optional components (YYYY, YYYYMM, YYYYMMDD all valid historically), and reporting why validation failed.

## Common Pitfalls

### Pitfall 1: VR-Specific Character Repertoire Confusion
**What goes wrong:** Assuming all string VRs accept the same characters
**Why it happens:** Most VRs look similar but have strict character restrictions
**How to avoid:** Implement per-VR character validators referencing PS3.5 Section 6.2
**Warning signs:** "Invalid character" errors on text that displays fine elsewhere

**VR Character Repertoires (from PS3.5 2025e):**

| VR | Allowed Characters | Notes |
|----|-------------------|-------|
| AE | Default repertoire excluding `\` and control chars | Leading/trailing space-only values invalid |
| AS | `0-9`, `D`, `W`, `M`, `Y` | Fixed 4 chars: nnnD/W/M/Y |
| CS | `A-Z`, `0-9`, `SPACE`, `_` | Uppercase only, 16 chars max |
| DA | `0-9` | 8 chars: YYYYMMDD |
| DS | `0-9`, `+`, `-`, `E`, `e`, `.`, `SPACE` | 16 chars max, no embedded spaces |
| DT | `0-9`, `+`, `-`, `.`, `SPACE` | 26 chars max |
| IS | `0-9`, `+`, `-`, `SPACE` | 12 chars max, range -2^31 to 2^31-1 |
| TM | `0-9`, `.`, `SPACE` | 14 chars max: HHMMSS.FFFFFF |
| UI | `0-9`, `.` | 64 chars max, null-padded |
| UR | RFC3986 subset + `SPACE` | No leading spaces |

### Pitfall 2: Treating Type 1C/2C Conditions as Simple
**What goes wrong:** Hard-coding conditions that depend on real-world context
**Why it happens:** Conditions reference values that may not be in the dataset
**How to avoid:** Evaluate conditions lazily; report "cannot evaluate" when context missing
**Warning signs:** False positive violations when condition can't be evaluated

### Pitfall 3: Strict Validation Breaking Real-World Files
**What goes wrong:** Rejecting files that other tools accept
**Why it happens:** Real DICOM files often have minor violations
**How to avoid:** Default to lenient mode; strict mode is opt-in for conformance testing
**Warning signs:** Support tickets about "valid" files being rejected

**Common real-world violations (from DCMTK/dciodvfy reports):**
- Odd-length values (should be padded)
- Wrong padding byte (null vs space)
- VR mismatch between explicit encoding and dictionary
- Missing Type 2 attributes (empty allowed, but attribute required)
- Non-standard defined terms (vendor extensions)
- Group 0002 in wrong transfer syntax

### Pitfall 4: Performance Impact of Full Validation
**What goes wrong:** Validation slowing down bulk processing
**Why it happens:** Checking every element on every file
**How to avoid:** Offer validation levels: None, InvalidOnly, All
**Warning signs:** Processing time increases 10x with validation enabled

### Pitfall 5: Cryptic Error Messages
**What goes wrong:** Users can't fix issues without understanding what failed
**Why it happens:** Generic "validation failed" without context
**How to avoid:** Include tag, position, actual value, expected format in every issue
**Warning signs:** Support questions asking "what does DICOM-003 mean?"

## Code Examples

### VR Format Validation: Date (DA)
```csharp
// Source: PS3.5 Section 6.2, DA definition
public sealed class DateValidator : IValidationRule
{
    public string RuleId => "VR-DA-FORMAT";
    public string Description => "Validates DA (Date) format: YYYYMMDD";

    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.DA)
            return null;

        var value = context.RawValue.Span;

        // Empty is valid for Type 2
        if (value.Length == 0)
            return null;

        // Trim trailing padding (space for string VRs)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Must be 8 characters (YYYYMMDD) or valid shorter form
        if (value.Length != 8 && value.Length != 6 && value.Length != 4)
        {
            return ValidationIssue.Warning(
                ValidationCodes.InvalidDateFormat,
                context.Tag,
                $"DA value must be YYYYMMDD, YYYYMM, or YYYY; got {value.Length} characters");
        }

        // Validate all characters are digits
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    $"DA value contains invalid character '{(char)value[i]}' at position {i}");
            }
        }

        // Parse and validate date components
        if (value.Length >= 4)
        {
            int year = ParseDigits(value.Slice(0, 4));
            if (year < 1 || year > 9999)
                return ValidationIssue.Warning(ValidationCodes.InvalidDateValue, context.Tag,
                    $"Year {year} out of valid range");

            if (value.Length >= 6)
            {
                int month = ParseDigits(value.Slice(4, 2));
                if (month < 1 || month > 12)
                    return ValidationIssue.Warning(ValidationCodes.InvalidDateValue, context.Tag,
                        $"Month {month} out of valid range 1-12");

                if (value.Length == 8)
                {
                    int day = ParseDigits(value.Slice(6, 2));
                    int maxDay = DateTime.DaysInMonth(year, month);
                    if (day < 1 || day > maxDay)
                        return ValidationIssue.Warning(ValidationCodes.InvalidDateValue, context.Tag,
                            $"Day {day} invalid for {year}-{month:D2} (max {maxDay})");
                }
            }
        }

        return null;
    }

    private static int ParseDigits(ReadOnlySpan<byte> digits)
    {
        int result = 0;
        for (int i = 0; i < digits.Length; i++)
            result = result * 10 + (digits[i] - '0');
        return result;
    }
}
```

### UID Validation
```csharp
// Source: PS3.5 Section 6.2, UI definition
public sealed class UidValidator : IValidationRule
{
    public string RuleId => "VR-UI-FORMAT";
    public string Description => "Validates UI (Unique Identifier) format";

    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.UI)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing null padding
        while (value.Length > 0 && value[value.Length - 1] == 0x00)
            value = value.Slice(0, value.Length - 1);

        if (value.Length == 0)
            return null; // Empty valid for Type 2

        if (value.Length > 64)
        {
            return ValidationIssue.Warning(
                ValidationCodes.ValueTooLong,
                context.Tag,
                $"UID exceeds maximum 64 characters: {value.Length}");
        }

        // Validate: digits and dots only, no leading zeros in components,
        // components separated by single dots, no leading/trailing dots
        int componentStart = 0;
        int componentLength = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '.')
            {
                // End of component
                if (componentLength == 0)
                {
                    return ValidationIssue.Warning(
                        ValidationCodes.InvalidUidFormat,
                        context.Tag,
                        "UID contains empty component");
                }

                // Check for leading zero (only "0" itself is allowed)
                if (componentLength > 1 && value[componentStart] == '0')
                {
                    return ValidationIssue.Warning(
                        ValidationCodes.InvalidUidFormat,
                        context.Tag,
                        "UID component has leading zero");
                }

                componentStart = i + 1;
                componentLength = 0;
            }
            else if (value[i] >= '0' && value[i] <= '9')
            {
                componentLength++;
            }
            else
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    $"UID contains invalid character '{(char)value[i]}'");
            }
        }

        return null;
    }
}
```

### Validation Profile with Presets
```csharp
// Source: CONTEXT.md requirements + DCMTK/dciodvfy severity patterns
public sealed class ValidationProfile
{
    public string Name { get; init; } = "Custom";
    public IReadOnlyList<IValidationRule> Rules { get; init; } = Array.Empty<IValidationRule>();
    public ValidationBehavior DefaultBehavior { get; init; } = ValidationBehavior.Validate;
    public IReadOnlyDictionary<DicomTag, ValidationBehavior>? TagOverrides { get; init; }

    /// <summary>Strict: all rules, all errors reported, any error aborts.</summary>
    public static ValidationProfile Strict { get; } = new()
    {
        Name = "Strict",
        Rules = StandardRules.All,
        DefaultBehavior = ValidationBehavior.Validate
    };

    /// <summary>Lenient: all rules, warnings collected but don't abort.</summary>
    public static ValidationProfile Lenient { get; } = new()
    {
        Name = "Lenient",
        Rules = StandardRules.All,
        DefaultBehavior = ValidationBehavior.Warn
    };

    /// <summary>Permissive: structural rules only, continue on errors.</summary>
    public static ValidationProfile Permissive { get; } = new()
    {
        Name = "Permissive",
        Rules = StandardRules.StructuralOnly,
        DefaultBehavior = ValidationBehavior.Skip
    };

    /// <summary>None: skip all validation for maximum performance.</summary>
    public static ValidationProfile None { get; } = new()
    {
        Name = "None",
        Rules = Array.Empty<IValidationRule>(),
        DefaultBehavior = ValidationBehavior.Skip
    };
}

public enum ValidationBehavior
{
    /// <summary>Run validation rules, report all issues.</summary>
    Validate,

    /// <summary>Run validation, log warnings but don't fail.</summary>
    Warn,

    /// <summary>Skip validation entirely.</summary>
    Skip
}
```

### DicomReaderOptions Validation Integration
```csharp
// Source: Existing DicomReaderOptions + CONTEXT.md requirements
public sealed class DicomReaderOptions
{
    // ... existing properties ...

    /// <summary>
    /// Gets or sets the validation profile to use during parsing.
    /// </summary>
    /// <remarks>
    /// Validation runs during element parsing when CallbackFilter includes InvalidOnly or All.
    /// Issues are collected in ValidationResult and passed to ValidationCallback if set.
    /// </remarks>
    public ValidationProfile? ValidationProfile { get; init; }

    /// <summary>
    /// Gets or sets a callback invoked for each validation issue.
    /// </summary>
    /// <remarks>
    /// Return false to abort parsing (strict mode), true to continue (lenient mode).
    /// If not set, behavior depends on ValidationProfile.DefaultBehavior.
    /// </remarks>
    public Func<ValidationIssue, bool>? ValidationCallback { get; init; }

    /// <summary>
    /// Gets or sets whether to collect all validation issues in result.
    /// </summary>
    /// <remarks>
    /// When true, issues are accumulated in DicomFile.ValidationResult.
    /// When false, only callback is invoked (saves memory for streaming).
    /// </remarks>
    public bool CollectValidationIssues { get; init; } = true;

    // Updated presets
    public static DicomReaderOptions Strict { get; } = new()
    {
        Preamble = FilePreambleHandling.Require,
        FileMetaInfo = FileMetaInfoHandling.Require,
        InvalidVR = InvalidVRHandling.Throw,
        ValidationProfile = ValidationProfile.Strict,
        CollectValidationIssues = true
    };

    public static DicomReaderOptions Lenient { get; } = new()
    {
        Preamble = FilePreambleHandling.Optional,
        FileMetaInfo = FileMetaInfoHandling.Optional,
        InvalidVR = InvalidVRHandling.MapToUN,
        ValidationProfile = ValidationProfile.Lenient,
        CollectValidationIssues = true
    };

    public static DicomReaderOptions Permissive { get; } = new()
    {
        Preamble = FilePreambleHandling.Ignore,
        FileMetaInfo = FileMetaInfoHandling.Ignore,
        InvalidVR = InvalidVRHandling.Preserve,
        ValidationProfile = ValidationProfile.Permissive,
        CollectValidationIssues = false  // Performance
    };
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Global validation flag (fo-dicom) | Per-operation options | SharpDicom design | Predictable behavior |
| Throw on invalid | Collect issues, callback | Modern validation libs | Graceful degradation |
| Binary valid/invalid | Error/Warning/Info severity | dciodvfy pattern | Better triage |

**Deprecated/outdated:**
- Global validation settings (fo-dicom pattern) cause unexpected behavior across library boundaries

## DICOM Attribute Type Requirements

From PS3.5 Section 7.4 (verified via official docs):

| Type | Required | Can Be Empty | Protocol Violation If |
|------|----------|--------------|----------------------|
| Type 1 | Always | No | Missing or empty |
| Type 1C | When condition met | No | Missing when condition met; present when condition not met |
| Type 2 | Always | Yes | Missing |
| Type 2C | When condition met | Yes | Missing when condition met; present when condition not met |
| Type 3 | Never | Yes | Never (optional) |

## Validation Error Codes

Proposed codes based on dciodvfy categories and CONTEXT.md:

| Code | Severity | Category | Description |
|------|----------|----------|-------------|
| DICOM-001 | Error | Structure | Invalid tag format |
| DICOM-002 | Error | Structure | Invalid VR format (unknown VR) |
| DICOM-003 | Warning | Structure | VR mismatch (declared vs dictionary) |
| DICOM-004 | Warning | Length | Value length exceeds VR maximum |
| DICOM-005 | Warning | VM | Invalid value multiplicity |
| DICOM-006 | Info | Padding | Incorrect padding byte |
| DICOM-007 | Warning | Format | Invalid date format (DA) |
| DICOM-008 | Warning | Format | Invalid time format (TM) |
| DICOM-009 | Warning | Format | Invalid UID format (UI) |
| DICOM-010 | Warning | Format | Invalid person name format (PN) |
| DICOM-011 | Error | Structure | Truncated element (length > available) |
| DICOM-012 | Error | Structure | Invalid sequence structure |
| DICOM-013 | Warning | Character | Invalid character for VR |
| DICOM-014 | Info | Length | Odd value length (not padded) |
| DICOM-015 | Error | IOD | Missing required tag (Type 1) |
| DICOM-016 | Warning | IOD | Missing conditional tag (Type 1C) |
| DICOM-017 | Info | IOD | Empty required tag (Type 2) |
| DICOM-018 | Warning | IOD | Present when condition not met (1C/2C) |
| DICOM-019 | Warning | Format | Invalid decimal string (DS) |
| DICOM-020 | Warning | Format | Invalid integer string (IS) |
| DICOM-021 | Warning | Format | Invalid age string (AS) |
| DICOM-022 | Warning | Format | Invalid code string (CS) |
| DICOM-023 | Warning | Value | Value out of valid range |
| DICOM-024 | Warning | Character | Non-standard defined term |
| DICOM-025 | Info | Deprecated | Retired attribute present |

## Open Questions

1. **IOD Validation Scope**
   - What we know: IOD validation requires SOP class knowledge and module definitions
   - What's unclear: Should IOD validation be part of v1.0 or deferred?
   - Recommendation: Implement structural + VR validation now; defer full IOD validation to post-v1.0

2. **Private Tag Validation**
   - What we know: Private tags have unknown VRs; vendor dictionaries can provide info
   - What's unclear: How strict to validate private tags without vendor dict?
   - Recommendation: Skip VR-specific validation for unknown private tags; structure-only validation

3. **Condition Evaluation for Type 1C/2C**
   - What we know: Conditions depend on other attribute values
   - What's unclear: What to report when condition references missing attribute?
   - Recommendation: Report "condition cannot be evaluated" at Info level, not Error

## Sources

### Primary (HIGH confidence)
- [PS3.5 Section 6.2 - VR Definitions](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_6.2.html) - Complete VR validation rules
- [PS3.5 Section 7.4 - Data Element Type](https://dicom.nema.org/dicom/2013/output/chtml/part05/sect_7.4.html) - Type 1/1C/2/2C/3 requirements
- [dciodvfy Documentation](https://dclunie.com/dicom3tools/dciodvfy.html) - Validation categories and error patterns

### Secondary (MEDIUM confidence)
- [DCMTK Troubleshooting](https://www.laurelbridge.com/pdf/DICOM-Troubleshooting-Issues.pdf) - Real-world malformed file patterns
- [fo-dicom Validation Issues](https://github.com/fo-dicom/fo-dicom/issues?q=validation) - Lessons from existing implementation
- [Validot GitHub](https://github.com/bartoszlenar/Validot) - Performance-first validation patterns

### Tertiary (LOW confidence)
- Web searches for DICOM conformance issues - Community experiences

## Metadata

**Confidence breakdown:**
- VR validation rules: HIGH - Direct from PS3.5 official documentation
- IOD type requirements: HIGH - Direct from PS3.5 Section 7.4
- Recovery strategies: MEDIUM - Derived from DCMTK patterns and community practices
- Validation framework patterns: HIGH - Established .NET patterns from Validot/FluentValidation
- Common real-world issues: MEDIUM - Community reports, may not be exhaustive

**Research date:** 2026-01-27
**Valid until:** 60 days (DICOM standard updates infrequently)
