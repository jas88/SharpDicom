# Phase 14: De-identification - Research

**Researched:** 2026-02-02
**Domain:** DICOM De-identification (PS3.15), OCR, UID Generation
**Confidence:** MEDIUM

## Summary

DICOM de-identification is a standards-driven process defined in DICOM PS3.15 Annex E. The standard defines action codes (D, Z, X, K, C, U) that specify how each attribute should be handled during de-identification. SharpDicom already has a source generator infrastructure parsing NEMA XML files, which can be extended to parse part15.xml for generating the de-identification action table.

The implementation requires four major components: (1) a source-generated action table from part15.xml, (2) a UID remapping system with study-level consistency, (3) date/time shifting with VR-aware handling, and (4) burned-in PHI detection for pixel data. SharpDicom already has a `DicomUID.Generate()` method using the 2.25 (UUID-based) prefix which satisfies the random UID requirement.

For burned-in PHI detection, the Tesseract OCR wrapper (TesseractOCR 5.5.1 on NuGet) provides the best balance of capability and licensing for .NET. Heuristic region detection should leverage modality-specific patterns, especially for ultrasound images which have 100% burned-in text rate according to research.

**Primary recommendation:** Extend the existing DicomDictionaryGenerator to parse part15.xml and generate a `DeidentificationActionTable` class, then build a `DicomDeidentifier` class that uses this table with configurable profiles, integrating with the existing validation callback pattern.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| DICOM PS3.15 | 2025e | De-identification profile definitions | NEMA standard, authoritative source |
| part15.xml | Latest | Machine-readable action table | Official NEMA source, same pattern as part06/07 |
| TesseractOCR | 5.5.1 | OCR for burned-in PHI detection | Open source, actively maintained, .NET wrapper |
| System.Numerics.BigInteger | Built-in | UUID-to-decimal conversion for UIDs | Already used in DicomUID.Generate() |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| tessdata-best | Latest | Tesseract trained data (English) | Required for OCR accuracy |
| System.IO.MemoryMappedFiles | Built-in | Large image processing | For efficient pixel data access |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| TesseractOCR | IronOCR | Commercial ($749+), better accuracy claims but license cost |
| TesseractOCR | Tesseract.Net.SDK | Similar capability, less actively maintained |
| Random UIDs | Deterministic UIDs | Deterministic allows correlation - privacy risk |

**Installation:**
```bash
dotnet add package TesseractOCR --version 5.5.1
# Download tessdata-best for English from tessdata repository
```

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/
├── Deidentification/
│   ├── DeidentificationAction.cs           # Enum: D, Z, X, K, C, U
│   ├── DeidentificationProfile.cs          # Enum: Basic, RetainUIDs, etc.
│   ├── DeidentificationOptions.cs          # Configuration options
│   ├── DeidentificationContext.cs          # Study-level UID/date mapping
│   ├── DicomDeidentifier.cs               # Main API with fluent builder
│   ├── IDeidentificationRule.cs           # Interface for custom rules
│   └── PixelCleaner/
│       ├── IBurnedInPhiDetector.cs        # Interface for detection
│       ├── TesseractPhiDetector.cs        # OCR-based implementation
│       ├── HeuristicPhiDetector.cs        # Region-based detection
│       └── OverlayPlaneProcessor.cs       # 60xx group handling
├── Generators/
│   └── Parsing/
│       └── Part15Parser.cs                # New: Parse confidentiality table
```

### Pattern 1: Source-Generated Action Table
**What:** Parse part15.xml Table E.1-1 at compile time to generate a lookup table mapping (tag, profile) to action code.
**When to use:** Always - this is the core of PS3.15 compliance.
**Example:**
```csharp
// Source: DICOM PS3.15 Table E.1-1
// Generated code structure
public static partial class DeidentificationActionTable
{
    // Generated from part15.xml
    private static readonly Dictionary<uint, ActionEntry> _actions = new()
    {
        // PatientName (0010,0010): Basic=Z, RetainPatientChars=K
        [0x00100010] = new ActionEntry(
            Basic: DeidentificationAction.Z,
            RetainPatientCharacteristics: DeidentificationAction.K,
            RetainDeviceIdentity: DeidentificationAction.Z,
            // ... other profiles
        ),
        // PatientBirthDate (0010,0030): Basic=Z
        [0x00100030] = new ActionEntry(Basic: DeidentificationAction.Z),
        // StudyInstanceUID (0020,000D): Basic=U
        [0x0020000D] = new ActionEntry(Basic: DeidentificationAction.U),
    };

    public static DeidentificationAction GetAction(DicomTag tag, DeidentificationProfile profile)
        => _actions.TryGetValue(tag.ToUInt32(), out var entry)
            ? entry.GetAction(profile)
            : DeidentificationAction.X; // Default: remove unknown
}
```

### Pattern 2: DeidentificationContext for Study Consistency
**What:** Object that tracks UID mappings and date offsets within a study/batch.
**When to use:** Multi-file processing, maintaining referential integrity.
**Example:**
```csharp
// Source: CONTEXT.md decision - study-level consistency
public sealed class DeidentificationContext : IDisposable
{
    private readonly Dictionary<DicomUID, DicomUID> _uidMap = new();
    private readonly Dictionary<string, TimeSpan> _dateOffsets = new();
    private readonly string _uidPrefix;

    public DeidentificationContext(string uidPrefix = "2.25")
    {
        _uidPrefix = uidPrefix;
    }

    public DicomUID RemapUID(DicomUID original)
    {
        if (_uidMap.TryGetValue(original, out var mapped))
            return mapped;

        var newUid = DicomUID.Generate(); // Uses 2.25 prefix
        _uidMap[original] = newUid;
        return newUid;
    }

    public TimeSpan GetDateOffset(string patientId)
    {
        if (_dateOffsets.TryGetValue(patientId, out var offset))
            return offset;

        offset = TimeSpan.FromDays(Random.Shared.Next(-365, 366));
        _dateOffsets[patientId] = offset;
        return offset;
    }

    // Serialization for persisting context between sessions
    public void SaveTo(Stream stream) { /* JSON serialization */ }
    public static DeidentificationContext LoadFrom(Stream stream) { /* ... */ }
}
```

### Pattern 3: Fluent Builder with Options Fallback
**What:** Primary fluent API with options object for advanced scenarios.
**When to use:** API design for DicomDeidentifier.
**Example:**
```csharp
// Fluent API (simple cases)
var deidentifier = DicomDeidentifier.Create()
    .WithProfile(DeidentificationProfile.Basic)
    .WithOption(DeidentificationProfile.RetainPatientCharacteristics)
    .WithDateShift(-365, 365)
    .WithContext(context)
    .Build();

await deidentifier.ApplyAsync(dataset);

// Options object (advanced cases)
var options = new DeidentificationOptions
{
    Profile = DeidentificationProfile.Basic,
    EnabledOptions = new[] { DeidentificationProfile.RetainDeviceIdentity },
    DateShiftStrategy = DateShiftStrategy.PerPatient,
    DateShiftRange = (-365, 365),
    ZeroTimeComponents = true,
    CustomRules = new[] { new MyCustomRule() },
    PixelCleaning = new PixelCleaningOptions
    {
        Enabled = true,
        ReplacementValue = PixelReplacementValue.Black,
        DetectOverlayPlanes = true
    }
};

var deidentifier = new DicomDeidentifier(options);
```

### Pattern 4: Integration with ElementCallback
**What:** De-identification as a callback in the parsing pipeline.
**When to use:** When processing files and applying de-id in single pass.
**Example:**
```csharp
// From DicomReaderOptions callback pattern
var readerOptions = new DicomReaderOptions
{
    ValidationCallback = issue => true, // Continue on issues
};

// De-id can compose with existing callbacks
var deidentifier = DicomDeidentifier.Create()
    .WithProfile(DeidentificationProfile.Basic)
    .Build();

// Apply during traversal
foreach (var element in dataset)
{
    var action = deidentifier.GetAction(element.Tag);
    // Process based on action...
}
```

### Anti-Patterns to Avoid
- **Hardcoding action table:** Action table must come from part15.xml to stay current with standard updates.
- **Deterministic UID generation:** Allows correlation between de-identified datasets - use random UIDs.
- **Ignoring sequences:** Must traverse all sequences for UID remapping (ReferencedSOPInstanceUID, etc.).
- **Modifying original dataset:** Always work on copy or use explicit modification tracking.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OCR text detection | Custom neural network | TesseractOCR | Years of training, 100+ languages |
| UID generation | Custom algorithm | DicomUID.Generate() | Already implements 2.25 spec correctly |
| Date parsing | Custom regex | DicomStringElement.GetDate() | Already handles DA/TM/DT VRs |
| Action table | Hardcoded table | Generated from part15.xml | Standard updates 3x/year |
| Overlay extraction | Custom parser | DicomTag.OverlayData (60xx) | Standard VR handling |

**Key insight:** The DICOM standard evolves frequently (3 releases per year). Source generation from official XML ensures the action table stays current without manual maintenance.

## Common Pitfalls

### Pitfall 1: Missing UID References in Sequences
**What goes wrong:** De-identified study has broken references because UIDs inside sequences weren't remapped.
**Why it happens:** ReferencedSOPInstanceUID, ReferencedStudyInstanceUID appear in many sequences (GSPS, SR, RT).
**How to avoid:** Always traverse all sequences recursively. Use existing DicomSequence enumeration.
**Warning signs:** Viewers cannot resolve references, hanging protocol failures.

### Pitfall 2: Date/Time Inconsistency
**What goes wrong:** Shifted dates create impossible timelines (study before birth, age negative).
**Why it happens:** Shifting StudyDate but not PatientBirthDate, or inconsistent per-element shifts.
**How to avoid:** Use per-patient or per-study shift strategy consistently. Recalculate PatientAge from shifted dates.
**Warning signs:** PatientAge doesn't match difference between birth date and study date.

### Pitfall 3: Private Tags Contain PHI
**What goes wrong:** Private tags leak patient names, referring physician, etc.
**Why it happens:** PS3.15 Basic Profile removes private tags, but "Retain Safe Private" option preserves some.
**How to avoid:** Default to removing all private tags unless explicitly configured with whitelist.
**Warning signs:** Private creator blocks with known vendors that embed identifiers.

### Pitfall 4: Burned-in PHI Not Detected
**What goes wrong:** Text in image corners contains patient name, visible after de-identification.
**Why it happens:** Not all modalities set BurnedInAnnotation (0028,0301), especially Secondary Capture.
**How to avoid:** Apply OCR/heuristic detection regardless of attribute. Ultrasound = 100% risk.
**Warning signs:** Secondary Capture SOP Class, Ultrasound modality, missing BurnedInAnnotation tag.

### Pitfall 5: Type 1/2 Conformance Violations
**What goes wrong:** De-identified file fails validation because required Type 1 element is empty.
**Why it happens:** Applying "Z" (zero length) to Type 1 attributes that require non-empty value.
**How to avoid:** Use Z/D code logic - apply D (dummy value) for Type 1, Z for Type 2.
**Warning signs:** Validation errors on PatientName, PatientID after de-identification.

### Pitfall 6: Overlay Planes Ignored
**What goes wrong:** 60xx group overlay planes contain text annotations with PHI.
**Why it happens:** Overlay planes are separate from pixel data, easy to miss.
**How to avoid:** Process all 6000-601E groups with same pixel cleaning logic.
**Warning signs:** Group 60xx tags present in dataset.

## Code Examples

### Action Code Application
```csharp
// Source: DICOM PS3.15 Section E.1
public async ValueTask ApplyActionAsync(
    DicomDataset dataset,
    DicomTag tag,
    DeidentificationAction action,
    DeidentificationContext context)
{
    switch (action)
    {
        case DeidentificationAction.D:
            // Replace with dummy value consistent with VR
            dataset.AddOrUpdate(CreateDummyElement(tag));
            break;

        case DeidentificationAction.Z:
            // Replace with zero-length value
            dataset.AddOrUpdate(CreateEmptyElement(tag));
            break;

        case DeidentificationAction.X:
            // Remove entirely
            dataset.Remove(tag);
            break;

        case DeidentificationAction.K:
            // Keep unchanged (but clean if sequence)
            if (dataset[tag] is DicomSequence seq)
            {
                foreach (var item in seq.Items)
                    await ApplyToDatasetAsync(item, context);
            }
            break;

        case DeidentificationAction.C:
            // Clean - replace with non-identifying value
            // Requires VR-specific logic
            dataset.AddOrUpdate(CleanElement(dataset[tag]!));
            break;

        case DeidentificationAction.U:
            // Replace UID with consistent remapped UID
            var originalUid = dataset.GetUID(tag);
            if (originalUid != null)
            {
                var newUid = context.RemapUID(originalUid);
                dataset.AddOrUpdate(DicomElement.Create(tag, newUid.ToString()));
            }
            break;
    }
}
```

### Date Shifting
```csharp
// Source: CONTEXT.md decision - zero time, recalculate age
public DicomStringElement ShiftDate(DicomTag tag, DicomStringElement original, TimeSpan offset)
{
    var date = original.GetDate();
    if (date == null)
        return original;

    var shifted = date.Value.Add(offset);

    // Format based on VR
    return tag.VR switch
    {
        DicomVR.DA => DicomElement.Create(tag, shifted.ToString("yyyyMMdd")),
        DicomVR.DT => DicomElement.Create(tag, shifted.ToString("yyyyMMdd") + "000000"), // Zero time
        DicomVR.TM => DicomElement.Create(tag, "000000"), // Always zero for privacy
        _ => original
    };
}

public string RecalculatePatientAge(DateOnly birthDate, DateOnly studyDate)
{
    var years = studyDate.Year - birthDate.Year;
    if (studyDate.DayOfYear < birthDate.DayOfYear)
        years--;

    return years >= 0 ? $"{years:D3}Y" : "000Y"; // AS VR format: nnnD, nnnW, nnnM, or nnnY
}
```

### Burned-in PHI Detection Regions
```csharp
// Source: Research findings on modality-specific regions
public static class BurnedInPhiRegions
{
    // Ultrasound: corners and header regions typically contain PHI
    public static readonly (int X, int Y, int Width, int Height)[] UltrasoundRegions = new[]
    {
        (0, 0, -1, 80),       // Top banner (full width)
        (0, -60, -1, 60),     // Bottom banner (full width)
        (0, 0, 100, 120),     // Top-left corner
        (-100, 0, 100, 120),  // Top-right corner
    };

    // CT/MR: typically corners only if Secondary Capture
    public static readonly (int X, int Y, int Width, int Height)[] CtMrRegions = new[]
    {
        (0, 0, 512, 80),      // Top region
        (0, 0, 120, 120),     // Top-left corner
    };

    public static (int, int, int, int)[] GetRegions(string modality, int width, int height)
    {
        var templates = modality switch
        {
            "US" => UltrasoundRegions,
            "CT" or "MR" => CtMrRegions,
            "SC" => UltrasoundRegions, // Secondary Capture: assume worst case
            _ => Array.Empty<(int, int, int, int)>()
        };

        // Convert relative (-1 = full, negative = from edge) to absolute
        return templates.Select(r => NormalizeRegion(r, width, height)).ToArray();
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual action table | Source-generated from part15.xml | PS3.15 standardization | Automatic updates with DICOM releases |
| Hash-based UIDs | Random UUID-based UIDs (2.25) | DICOM PS3.5 B.2 | No correlation risk |
| Pixel blackout | OCR + heuristic detection | ~2020 | Higher accuracy, fewer false negatives |
| Single-file processing | Study-context aware | Common practice | Maintains referential integrity |

**Deprecated/outdated:**
- DICOM Supplement 55 (older de-id guidance): Superseded by PS3.15 Annex E
- Deterministic UID generation: Privacy risk, discouraged by HIPAA Safe Harbor

## Open Questions

1. **part15.xml Table Parsing Complexity**
   - What we know: part15.xml is 3.5MB DocBook XML with Table E.1-1 containing action codes
   - What's unclear: Exact XML structure differs from part06.xml; may need custom parser
   - Recommendation: Download and inspect part15.xml structure before parser implementation

2. **Tesseract Thread Safety**
   - What we know: TesseractEngine should be reused, not created per-image
   - What's unclear: Thread safety guarantees for concurrent image processing
   - Recommendation: Use object pooling or per-thread instances

3. **GSPS/SR Text Annotation Parsing**
   - What we know: Clean Graphics profile requires scanning GSPS/SR for PHI
   - What's unclear: Complexity of parsing all annotation content
   - Recommendation: Start with basic text content, iterate based on real-world data

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.15 Chapter E](https://dicom.nema.org/medical/dicom/current/output/chtml/part15/chapter_e.html) - De-identification profiles and action codes
- [DICOM PS3.15 Section E.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part15/sect_E.2.html) - Basic Application Level Confidentiality Profile
- [DICOM PS3.5 Section B.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_b.2.html) - UUID Derived UID specification
- [part15.xml](https://dicom.nema.org/medical/dicom/current/source/docbook/part15/) - Official NEMA source (3.5MB)

### Secondary (MEDIUM confidence)
- [TesseractOCR NuGet](https://www.nuget.org/packages/TesseractOCR) - Version 5.5.1, wraps Tesseract 5.4.1
- [charlesw/tesseract GitHub](https://github.com/charlesw/tesseract) - Original .NET wrapper
- [Burned-in PHI Detection Research](https://pmc.ncbi.nlm.nih.gov/articles/PMC11522224/) - Method for efficient de-identification
- [EDRN De-identification Process](https://edrn.cancer.gov/data-and-resources/informatics/labcas-help/edrn-dicom-de-identification-process/) - Best practices guidance

### Tertiary (LOW confidence)
- [John Snow Labs Visual-NLP](https://medium.com/john-snow-labs/de-identifying-dicom-files-a-step-by-step-guide-with-john-snow-labs-visual-nlp-2c21b60f92a8) - Date shifting approaches (commercial tool, patterns applicable)
- [Deid pydicom](https://pydicom.github.io/deid/getting-started/dicom-pixels/) - Pixel cleaning rules (Python, pattern reference)

## Metadata

**Confidence breakdown:**
- Standard stack: MEDIUM - PS3.15 is authoritative; Tesseract choice is Claude's discretion per CONTEXT.md
- Architecture: MEDIUM - Patterns derived from existing SharpDicom codebase and standard practices
- Pitfalls: HIGH - Well-documented in DICOM community and research papers

**Research date:** 2026-02-02
**Valid until:** 30 days (DICOM standard stable, library versions may update)
