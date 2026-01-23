# Phase 6: Private Tags - Research

**Researched:** 2026-01-27
**Domain:** DICOM Private Data Elements (PS3.5 Section 7.8)
**Confidence:** HIGH

## Summary

DICOM private tags provide vendor-specific extensions using odd group numbers with a structured slot allocation mechanism. Each private block is reserved by a Private Creator element at (gggg,00xx) that controls elements (gggg,xx00-xxFF). The private creator string identifies the vendor/implementer and must be LO VR with VM=1 using only the Default Character Repertoire.

SharpDicom already has the foundation: `DicomTag.IsPrivate`, `DicomTag.IsPrivateCreator`, `DicomTag.PrivateCreatorSlot`, `DicomTag.PrivateCreatorKey`, and `PrivateCreatorDictionary`. Phase 6 extends this with vendor dictionary lookup, auto-slot allocation, and configurable handling options.

**Primary recommendation:** Use source generation to compile vendor dictionaries from malaterre/dicom-private-dicts XML files into static lookup tables, with runtime extension support for user-defined dictionaries.

## Standard Stack

### Core (Already Implemented)
| Component | Purpose | Status |
|-----------|---------|--------|
| DicomTag.IsPrivate | Detect odd group numbers | Complete |
| DicomTag.IsPrivateCreator | Detect (gggg,00xx) elements | Complete |
| DicomTag.PrivateCreatorSlot | Extract xx from (gggg,xxyy) | Complete |
| DicomTag.PrivateCreatorKey | Create lookup key for creator | Complete |
| PrivateCreatorDictionary | Track creators per dataset | Complete |

### New Components
| Component | Purpose | Implementation |
|-----------|---------|----------------|
| VendorDictionary | Lookup private tag metadata by creator | Source generated |
| PrivateTagInfo | Struct holding VR, name, keyword, safe flag | New record struct |
| PrivateCreatorInfo | Metadata about known creators | New record struct |
| DicomReaderOptions extensions | Configurable private tag handling | Extend existing |

### Data Sources
| Source | URL | Content |
|--------|-----|---------|
| dicom-private-dicts | https://github.com/malaterre/dicom-private-dicts | XML dictionaries for all major vendors |
| DCMTK private.dic | https://github.com/InsightSoftwareConsortium/DCMTK/blob/master/dcmdata/data/private.dic | Text format dictionary |
| fo-dicom PrivateDictionary.xml | https://github.com/fo-dicom/fo-dicom | Gzipped XML dictionary |
| PS3.15 Table E.3.10-1 | https://dicom.nema.org/medical/dicom/current/output/chtml/part15/sect_E.3.10.html | Safe private attributes |

## Architecture Patterns

### Private Tag Structure (DICOM PS3.5 Section 7.8)

```
Private Group Structure (odd group number):
+------------------+--------------------------------+
| (gggg,0010)      | Private Creator 1 = "VENDOR1"  |
| (gggg,0011)      | Private Creator 2 = "VENDOR2"  |
| ...              | (up to 0x00FF)                 |
+------------------+--------------------------------+
| (gggg,1000-10FF) | Data elements for VENDOR1      |
| (gggg,1100-11FF) | Data elements for VENDOR2      |
| ...              |                                |
+------------------+--------------------------------+
```

**Private Creator Element Constraints:**
- Location: (gggg,0010) through (gggg,00FF) where gggg is odd
- VR: LO (Long String)
- VM: 1
- Character Set: Default Character Repertoire only (ISO-IR 6 subset, no bytes 05/12 or 07/14)
- Maximum length: 64 characters

**Private Data Element Structure:**
- Location: (gggg,xx00) through (gggg,xxFF) where xx matches creator slot
- VR: Defined by vendor (or UN if unknown)
- Uniqueness: Creator string + element offset determines meaning

### Vendor Dictionary Model

```csharp
// Source: Official DICOM standard + vendor documentation
public readonly record struct PrivateTagInfo(
    string Creator,           // "SIEMENS CT VA0 CINE"
    ushort ElementOffset,     // 0x00-0xFF (lower byte of full element)
    DicomVR VR,               // Expected VR for this tag
    string Keyword,           // "NumberOfImagesInMosaic"
    string Name,              // "Number of Images in Mosaic"
    bool IsSafeToRetain       // Non-PHI per PS3.15 E.3.10
);

public readonly record struct PrivateCreatorInfo(
    string Creator,           // "SIEMENS CT VA0 CINE"
    string Vendor,            // "Siemens"
    string Description,       // "CT cinematic parameters"
    int TagCount              // Number of defined tags
);
```

### Element Offset Notation

The `xx` notation in vendor dictionaries represents the variable portion:

| Dictionary Entry | Meaning |
|------------------|---------|
| element="xx00" | Offset 0x00 in the allocated block |
| element="xx1A" | Offset 0x1A (26) in the allocated block |
| element="1000" | Fixed at element 0x1000 (implies slot 0x10) |

**Resolution formula:**
```csharp
// Full element = (slot << 8) | offset
// Tag (0019,100A) with creator at (0019,0010):
//   slot = 0x10 (from creator position 0x0010)
//   offset = 0x0A (from element 0x100A & 0xFF)
//   lookup key = (creator: "SIEMENS CT VA0 CINE", offset: 0x0A)
```

### Lookup Strategy

```csharp
public static partial class VendorDictionary
{
    // Primary lookup: by creator string and element offset
    public static PrivateTagInfo? GetInfo(string creator, byte elementOffset);

    // Secondary: by creator string and full element (extracts offset)
    public static PrivateTagInfo? GetInfo(string creator, ushort element)
        => GetInfo(creator, (byte)(element & 0xFF));

    // All tags for a creator
    public static IEnumerable<PrivateTagInfo> GetAllForCreator(string creator);

    // Creator metadata
    public static PrivateCreatorInfo? GetCreatorInfo(string creator);

    // Check if known
    public static bool IsKnownCreator(string creator);
}
```

### Slot Allocation for Writing

```csharp
public sealed class PrivateCreatorDictionary
{
    // Existing
    public void Register(DicomTag creatorTag, string creator);
    public string? GetCreator(DicomTag tag);

    // New: allocate next available slot in group
    public DicomTag AllocateSlot(ushort group, string creator)
    {
        // Check if creator already has a slot in this group
        foreach (var (tag, c) in GetAll())
        {
            if (tag.Group == group && c == creator)
                return tag;  // Reuse existing slot
        }

        // Find first unused slot (0x10-0xFF)
        var usedSlots = GetAll()
            .Where(x => x.Tag.Group == group)
            .Select(x => (byte)x.Tag.Element)
            .ToHashSet();

        for (byte slot = 0x10; slot <= 0xFF; slot++)
        {
            if (!usedSlots.Contains(slot))
            {
                var newTag = new DicomTag(group, slot);
                Register(newTag, creator);
                return newTag;
            }
        }

        throw new DicomDataException($"No available private slots in group {group:X4}");
    }

    // New: compact slots to remove gaps (for clean output)
    public void Compact(ushort group);
}
```

### Sequence Scope Handling

Per DICOM standard, private creator reservations are scoped to each dataset independently:

```csharp
// Each sequence item is a self-contained dataset
// Private creator at (0019,0010) in parent does NOT apply to items
// Each item needing private tags must have its own creator elements

public void ParseSequenceItem(DicomDataset item)
{
    // Item gets its own PrivateCreatorDictionary
    // NOT inherited from parent dataset
    var itemCreators = new PrivateCreatorDictionary();
    // ... populate from item's own (gggg,00xx) elements
}
```

### Anti-Patterns to Avoid

- **Global creator inheritance:** Never assume parent dataset creators apply to sequence items
- **Slot reuse across creators:** Different creators in same group must have different slots
- **Hardcoded element numbers:** Always use (creator + offset) for lookup, not full element
- **Ignoring orphan elements:** Private data without creator is invalid (handle per strictness)

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Vendor dictionaries | Parse conformance statements manually | malaterre/dicom-private-dicts | Comprehensive, maintained, multiple vendors |
| Safe attribute list | Hardcode list | PS3.15 Table E.3.10-1 | Authoritative, versioned |
| Creator string matching | Simple equality | Case-insensitive + whitespace normalization | Real-world files have variations |
| Slot allocation | Sequential from 0x10 | Check existing allocations first | Avoid collisions on merge |

## Common Pitfalls

### Pitfall 1: Orphan Private Elements

**What goes wrong:** Private data elements (gggg,xxyy) exist without corresponding Private Creator element (gggg,00xx).

**Why it happens:**
- Incomplete de-identification that removes creators but not data
- File corruption or improper editing
- Non-conformant DICOM generators

**How to avoid:**
- Strict mode: Reject file with DicomDataException
- Lenient mode: Keep as UN VR with warning via callback
- Track orphan count in parsing statistics

**Warning signs:** Element in range (gggg,1000-FFFF) with no creator at (gggg,00{slot})

### Pitfall 2: Duplicate Creator Slots

**What goes wrong:** Same slot (gggg,00xx) assigned to different creator strings in merged datasets.

**Why it happens:**
- Copying/merging datasets from different sources
- Manual editing without understanding slot allocation

**How to avoid:**
- Strict mode: Reject with error on duplicate
- Writing: Auto-reallocate one creator to different slot
- Always preserve creator-element associations when copying

**Warning signs:** Multiple creators at same (group, slot) position

### Pitfall 3: Creator String Variations

**What goes wrong:** Same vendor uses slightly different creator strings:
- "SIEMENS CT VA0 CINE" vs "SIEMENS CT VA0  CINE" (extra space)
- "GEMS_ACQU_01" vs "gems_acqu_01" (case)

**Why it happens:**
- Different software versions
- Human error in conformance statements
- Trailing whitespace not trimmed

**How to avoid:**
- Normalize for lookup: trim, collapse whitespace, case-insensitive
- Store original for roundtrip fidelity
- Log when normalization changes value

### Pitfall 4: Implicit VR Private Tag VR Lookup

**What goes wrong:** Private tag VR unknown when reading implicit VR file without vendor dictionary.

**Why it happens:**
- Implicit VR doesn't encode VR in file
- Dictionary lookup requires creator string match
- Unknown creators default to UN

**How to avoid:**
- Load vendor dictionaries before parsing
- Fall back to UN for unknown
- Allow callback to provide VR hint

### Pitfall 5: Private Tags in Nested Sequences

**What goes wrong:** Private creators from parent dataset assumed to apply in sequence items.

**Why it happens:** Misunderstanding of DICOM spec scope rules.

**How to avoid:**
- Create fresh PrivateCreatorDictionary for each sequence item
- Document this behavior clearly
- Validate each item's creators independently

## Code Examples

### Parsing Private Tags with Creator Tracking

```csharp
// Source: DICOM PS3.5 Section 7.8.1
public void ParseElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
{
    if (tag.IsPrivateCreator)
    {
        // Register creator for this block
        var creator = Encoding.ASCII.GetString(value.Span).TrimEnd(' ', '\0');
        _privateCreators.Register(tag, creator);

        // Store element
        _dataset.Add(new DicomElement(tag, DicomVR.LO, value));
    }
    else if (tag.IsPrivate)
    {
        // Look up creator for this element
        var creator = _privateCreators.GetCreator(tag);

        if (creator == null && _options.FailOnOrphanPrivateElements)
        {
            throw new DicomDataException($"Orphan private element {tag} has no creator");
        }

        // Look up VR from vendor dictionary if not explicit
        if (vr == DicomVR.UN && creator != null)
        {
            var info = VendorDictionary.GetInfo(creator, tag.Element);
            if (info.HasValue)
                vr = info.Value.VR;
        }

        _dataset.Add(new DicomElement(tag, vr, value));
    }
}
```

### Stripping Private Tags

```csharp
// Source: DICOM PS3.15 Section E.1
public static void StripPrivateTags(this DicomDataset dataset)
{
    // Remove all private elements (odd groups)
    var toRemove = dataset
        .Where(e => e.Tag.IsPrivate)
        .Select(e => e.Tag)
        .ToList();

    foreach (var tag in toRemove)
        dataset.Remove(tag);

    // Recursively process sequences
    foreach (var element in dataset)
    {
        if (element.IsSequence && element.Sequence != null)
        {
            foreach (var item in element.Sequence.Items)
            {
                item.StripPrivateTags();
            }
        }
    }
}
```

### Adding Private Tags with Auto-Slot

```csharp
// Source: SharpDicom design
public void AddPrivateElement(
    DicomDataset dataset,
    ushort group,
    string creator,
    byte elementOffset,
    DicomVR vr,
    ReadOnlyMemory<byte> value)
{
    // Validate group is odd
    if ((group & 1) == 0)
        throw new ArgumentException("Private tags require odd group number");

    // Allocate or reuse slot
    var creatorTag = dataset.PrivateCreators.AllocateSlot(group, creator);
    var slot = (byte)creatorTag.Element;

    // Build full element number
    var fullElement = (ushort)((slot << 8) | elementOffset);
    var dataTag = new DicomTag(group, fullElement);

    // Add creator element if not already present
    if (!dataset.Contains(creatorTag))
    {
        var creatorBytes = Encoding.ASCII.GetBytes(creator.PadRight(creator.Length + (creator.Length & 1)));
        dataset.Add(new DicomElement(creatorTag, DicomVR.LO, creatorBytes));
    }

    // Add data element
    dataset.Add(new DicomElement(dataTag, vr, value));
}
```

### Retaining Safe Private Attributes

```csharp
// Source: DICOM PS3.15 Section E.3.10
public static void StripUnsafePrivateTags(
    this DicomDataset dataset,
    Func<string, byte, bool>? additionalSafeFilter = null)
{
    var toRemove = new List<DicomTag>();

    foreach (var element in dataset)
    {
        if (!element.Tag.IsPrivate)
            continue;

        if (element.Tag.IsPrivateCreator)
        {
            // Keep creator if any of its elements are safe
            // (will remove orphan creators in cleanup pass)
            continue;
        }

        var creator = dataset.PrivateCreators.GetCreator(element.Tag);
        if (creator == null)
        {
            toRemove.Add(element.Tag);
            continue;
        }

        var offset = (byte)(element.Tag.Element & 0xFF);
        var info = VendorDictionary.GetInfo(creator, offset);

        // Check if safe per vendor dictionary
        bool isSafe = info?.IsSafeToRetain == true;

        // Check additional filter
        if (!isSafe && additionalSafeFilter != null)
            isSafe = additionalSafeFilter(creator, offset);

        if (!isSafe)
            toRemove.Add(element.Tag);
    }

    foreach (var tag in toRemove)
        dataset.Remove(tag);

    // Cleanup: remove creators with no remaining data elements
    CleanupOrphanCreators(dataset);
}
```

## Vendor Dictionary Sources

### Primary Source: malaterre/dicom-private-dicts

**URL:** https://github.com/malaterre/dicom-private-dicts

**Format:** XML with structure:
```xml
<entry owner="SIEMENS MED DISPLAY" group="0029" element="xx04"
       vr="CS" vm="1" name="Photometric Interpretation"/>
```

**Available dictionaries:**
| File | Vendor | Private Creators |
|------|--------|------------------|
| siemens.xml | Siemens | SIEMENS MED DISPLAY, SIEMENS CM VA0 CMS, SIEMENS ISI, SIEMENS RA GEN, etc. |
| gems.xml | GE | GEMS_ACQU_01, GEMS_GENIE_1, GEMS_IMAG_01, GEIIS, etc. (~30 creators) |
| pms.xml | Philips | SPI-P-Private_ICS Release 1;*, PHILIPS MR/LAST, etc. |
| toshiba.xml | Toshiba/Canon | Various Toshiba-specific creators |
| fuji.xml | Fujifilm | Fujifilm-specific creators |
| hitachi.xml | Hitachi | Hitachi-specific creators |
| agfa.xml | Agfa | Agfa-specific creators |
| other.xml | Miscellaneous | DCMTK_ANONYMIZER, ELSCINT1, PAPYRUS, etc. |

### Secondary Source: DCMTK private.dic

**URL:** https://github.com/InsightSoftwareConsortium/DCMTK/blob/master/dcmdata/data/private.dic

**Format:** Tab-separated text:
```
(0019,"GEMS_ACQU_01",02)	SL	NumberOfCellsInDetector	1	PrivateTag
```

### Safe Private Attributes: PS3.15 Table E.3.10-1

Categories of safe attributes (non-PHI):
- Philips imaging parameters (2001,xx** range): chemical shift, diffusion, cardiac sync
- GE/Siemens acquisition: table speed, rotation, pitch, detector specs
- ELSCINT1: dosimetry (DLP), phantom type
- Specialized vendors: HOLOGIC compression, NeuroQuant segmentation

**Note:** PS3.15 states vendors do not guarantee these are safe in all software versions.

## Source Generation Strategy

### Build-Time Processing

1. **Download XML files** from malaterre/dicom-private-dicts (cache in repo)
2. **Parse XML** to extract entries
3. **Generate C# source** with static lookup tables

### Generated Code Structure

```csharp
// Generated: VendorDictionary.Generated.cs
public static partial class VendorDictionary
{
    private static readonly FrozenDictionary<(string Creator, byte Offset), PrivateTagInfo> _tagLookup =
        new Dictionary<(string, byte), PrivateTagInfo>
        {
            // Siemens
            [("SIEMENS MED DISPLAY", 0x04)] = new("SIEMENS MED DISPLAY", 0x04,
                DicomVR.CS, "PhotometricInterpretation", "Photometric Interpretation", false),
            // ... thousands more
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, PrivateCreatorInfo> _creatorLookup =
        new Dictionary<string, PrivateCreatorInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["SIEMENS MED DISPLAY"] = new("SIEMENS MED DISPLAY", "Siemens", "Medical display parameters", 42),
            // ...
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
```

### Runtime Extension

```csharp
public static partial class VendorDictionary
{
    private static readonly ConcurrentDictionary<(string, byte), PrivateTagInfo> _userDictionary = new();

    public static void Register(PrivateTagInfo info)
    {
        _userDictionary[(info.Creator, info.ElementOffset)] = info;
    }

    public static PrivateTagInfo? GetInfo(string creator, byte offset)
    {
        // User dictionary takes precedence
        if (_userDictionary.TryGetValue((creator, offset), out var userInfo))
            return userInfo;

        // Fall back to generated
        if (_tagLookup.TryGetValue((creator, offset), out var info))
            return info;

        return null;
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Hardcoded vendor lists | Source-generated from XML | 2020+ | Easier maintenance |
| Runtime XML parsing | Compile-time generation | fo-dicom 5.0 | Faster startup |
| Global creator scope | Per-dataset scope | Always per spec | Correct behavior |
| Remove all private | Retain safe per PS3.15 | 2015 (Sup 142) | Better de-identification |

**Current best practices:**
- FrozenDictionary for read-mostly lookups (.NET 8+)
- Case-insensitive creator matching with original preservation
- Callback-based handling for unknown/orphan elements
- Separate safe attribute tracking for de-identification

## Open Questions

1. **Dictionary update frequency**
   - What we know: malaterre/dicom-private-dicts is community-maintained
   - What's unclear: Update frequency, PR acceptance policy
   - Recommendation: Cache XML in repo, update periodically via GitHub Action

2. **Safe attribute source**
   - What we know: PS3.15 Table E.3.10-1 exists
   - What's unclear: Machine-readable format availability
   - Recommendation: Parse from Part 15 DocBook XML or maintain manually

3. **Creator string normalization**
   - What we know: Variations exist in real files
   - What's unclear: Exact normalization rules that preserve compatibility
   - Recommendation: Document normalization, log when applied, keep original for write

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.5 Section 7.8](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.8.html) - Private data element specification
- [DICOM PS3.15 Section E.3.10](https://dicom.nema.org/medical/dicom/current/output/chtml/part15/sect_E.3.10.html) - Retain Safe Private Option
- [malaterre/dicom-private-dicts](https://github.com/malaterre/dicom-private-dicts) - Vendor dictionary source

### Secondary (MEDIUM confidence)
- [DCMTK private.dic](https://github.com/InsightSoftwareConsortium/DCMTK/blob/master/dcmdata/data/private.dic) - Reference private dictionary
- [fo-dicom private tags documentation](https://fo-dicom.github.io/stable/v5/usage/add_private_tags.html) - XML format reference
- [pydicom private elements guide](https://pydicom.github.io/pydicom/stable/guides/user/private_data_elements.html) - Implementation patterns

### Tertiary (LOW confidence)
- Various DCMTK forum posts on orphan handling - community practices

## Metadata

**Confidence breakdown:**
- Private tag structure: HIGH - Official DICOM specification
- Vendor dictionaries: HIGH - Well-maintained open source
- Safe attributes: MEDIUM - Standard exists but machine-readable unclear
- Orphan handling: MEDIUM - Based on DCMTK/fo-dicom patterns

**Research date:** 2026-01-27
**Valid until:** 90 days (stable domain, infrequent spec changes)
