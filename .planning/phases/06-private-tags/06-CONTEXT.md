---
phase: 06-private-tags
created: 2026-01-27
status: ready-for-research
---

# Phase 6: Private Tags — Context

## Phase Goal

Preserve vendor-specific data with proper creator tracking and configurable handling.

## Core Decisions

### Vendor Dictionary Scope

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Bundled vendors | Comprehensive | All vendors with publicly documented private tags |
| Dictionary structure | Source generated | Generate static code from vendor data at build time |
| Custom registration | Merge with bundled | User dictionaries override/extend bundled |
| Lookup methods | All patterns | Creator+element, creator-only, full-tag all supported |

### Unknown Private Handling

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Unknown VR | UN (Unknown) | Safest for roundtrip preservation |
| Unknown preservation | Configurable | DicomReaderOptions controls retention |
| Orphan elements | Fail strict mode | Error in strict, keep as UN in lenient |
| Warning callback | Unified with element callback | Use existing CallbackFilter system |

### Slot Conflict Resolution

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Duplicate slots | Fail strict mode | Error if duplicate creator slots detected |
| Write validation | Auto-allocate | Automatically assign free slots when adding private tags |
| Slot reuse | Compact on write | Reassign slots to remove gaps when writing |
| Copy behavior | Preserve grouping | Keep creator-element associations intact |

### Stripping Behavior

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Strip scope | All private | Remove all odd-group elements including creators |
| Recursion | All levels | Strip private from nested sequences too |
| Safe whitelist | Bundled | Known non-PHI private tags marked safe |
| Reversibility | Destructive | Removed data is gone, simpler API |

## Success Criteria

From ROADMAP.md:
- [ ] Private creator tracking
- [ ] Siemens/GE/Philips tags recognized
- [ ] Strip-private callback works
- [ ] Roundtrip preserves private data

## Dependencies

**Requires:**
- Phase 1: DicomTag.IsPrivate, PrivateCreatorDictionary
- Phase 3: Sequence parsing for recursive private tags

**Provides:**
- Private tag handling for Phase 7 (File Writing)
- Foundation for de-identification workflows

## Implementation Notes

### PrivateCreatorDictionary Enhancement

```csharp
public sealed class PrivateCreatorDictionary
{
    // Existing: track creators in a dataset
    public void Register(DicomTag creatorTag, string creator);
    public string? GetCreator(DicomTag tag);

    // New: auto-allocate slot
    public DicomTag AllocateSlot(ushort group, string creator);

    // New: compact slots
    public void Compact();
}
```

### VendorDictionary (Source Generated)

```csharp
public static partial class VendorDictionary
{
    // Generated from vendor XML files
    public static PrivateTagInfo? GetInfo(string creator, ushort element);
    public static IEnumerable<PrivateTagInfo> GetAllForCreator(string creator);
    public static bool IsKnownCreator(string creator);
}

public readonly record struct PrivateTagInfo(
    string Creator,
    ushort Element,
    DicomVR VR,
    string Keyword,
    string Name,
    bool IsSafeToRemove  // Non-PHI flag
);
```

### DicomReaderOptions Extensions

```csharp
public class DicomReaderOptions
{
    // Existing options...

    // New private tag options
    public bool RetainUnknownPrivateTags { get; init; } = true;
    public bool FailOnOrphanPrivateElements { get; init; } = false;  // Strict mode sets true
    public bool FailOnDuplicatePrivateSlots { get; init; } = false;  // Strict mode sets true
}
```

### Stripping API

```csharp
public static class DicomDatasetExtensions
{
    public static void StripPrivateTags(this DicomDataset dataset);

    public static void StripPrivateTags(
        this DicomDataset dataset,
        Func<string, bool>? creatorFilter = null);  // Keep if returns true
}
```

---

*Created: 2026-01-27*
*Status: Ready for research and planning*
