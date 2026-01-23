# Phase 3: Implicit VR & Sequences - Research

**Researched:** 2026-01-27
**Domain:** DICOM encoding (Part 5: Data Structures and Encoding)
**Confidence:** HIGH

## Summary

Phase 3 adds support for Implicit VR Little Endian transfer syntax and DICOM sequences (both defined and undefined length). This is critical functionality as Implicit VR LE (UID 1.2.840.10008.1.2) is the default DICOM transfer syntax that all systems must support.

The implementation builds on Phase 2's DicomStreamReader by adding VR dictionary lookup for implicit encoding and recursive sequence parsing. Key challenges include:

1. **VR lookup from dictionary**: When VR is not in the byte stream, must consult DicomDictionary and default to UN for unknown tags
2. **Context-dependent VR resolution**: Some tags (Pixel Data, US/SS tags) require other elements for correct VR determination
3. **Sequence delimiter parsing**: Three special tags (Item, Item Delimitation, Sequence Delimitation) with group FFFE require special handling
4. **Recursive nesting**: Sequences can contain sequences to arbitrary depth; standard allows recursion with no specified limit

**Primary recommendation:** Extend DicomStreamReader with implicit VR mode and add sequence-aware parsing that handles both explicit and undefined length encodings. Use recursive descent for nested sequences with guard against excessive depth.

## Standard Stack

### Core (Already Available in Phase 1-2)
| Library/Component | Version | Purpose | Why Standard |
|-------------------|---------|---------|--------------|
| DicomDictionary | Generated | VR lookup by tag | Required for implicit VR; populated from NEMA Part 6 |
| DicomStreamReader | Current (ref struct) | Low-level parsing | Zero-copy Span<T> parsing established in Phase 2 |
| DicomDataset | Current | Element storage | Dictionary-based with O(1) lookup |
| TransferSyntax | Current | Encoding metadata | IsExplicitVR property already available |

### Supporting (New for Phase 3)
| Component | Purpose | When to Use |
|-----------|---------|-------------|
| Delimiter tag constants | FFFE,E000 / E00D / E0DD | Sequence and item boundary detection |
| Recursion depth limit | Default 100 levels | Prevent stack overflow on malformed files |
| Deferred VR resolution | Store raw bytes + resolve later | Context-dependent VRs like Pixel Data |

### No External Dependencies
This phase requires no new NuGet packages. All functionality builds on existing .NET primitives and Phase 1-2 infrastructure.

## Architecture Patterns

### Pattern 1: Implicit VR Dictionary Lookup
**What:** When parsing implicit VR, VR field is absent from byte stream; must look up from dictionary
**When to use:** Transfer syntax IsExplicitVR == false
**Example:**
```csharp
// In DicomStreamReader.TryReadElementHeader()
if (_explicitVR)
{
    // Read VR from stream (Phase 2 - already implemented)
    vr = DicomVR.FromBytes(span.Slice(4, 2));
    // ... read length based on VR.Is32BitLength
}
else
{
    // NEW: Implicit VR - look up from dictionary
    var entry = DicomDictionary.Default.GetEntry(tag);
    vr = entry?.DefaultVR ?? DicomVR.UN;

    // Implicit VR always uses 32-bit length field
    length = _littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4))
        : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4));

    _position += 8;  // 4 bytes tag + 4 bytes length
}
```
**Source:** [DICOM Part 5 A.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.2.html)

### Pattern 2: Sequence Delimiter Detection
**What:** Tags with group FFFE are special delimiter tags, not data elements
**When to use:** After reading any tag, check if group == 0xFFFE
**Example:**
```csharp
// Special tags for sequences
public static class DicomDelimiterTags
{
    public static readonly DicomTag Item = new(0xFFFE, 0xE000);
    public static readonly DicomTag ItemDelimitationItem = new(0xFFFE, 0xE00D);
    public static readonly DicomTag SequenceDelimitationItem = new(0xFFFE, 0xE0DD);
}

// In parsing loop
var tag = ReadTag();
if (tag.Group == 0xFFFE)
{
    if (tag == DicomDelimiterTags.SequenceDelimitationItem)
        return; // End of undefined length sequence
    else if (tag == DicomDelimiterTags.ItemDelimitationItem)
        return; // End of undefined length item
    else if (tag == DicomDelimiterTags.Item)
        ParseNextItem(); // Start of new item
}
```
**Source:** [DICOM Part 5 Section 7.5.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html)

### Pattern 3: Recursive Sequence Parsing
**What:** Sequences contain items, items contain datasets, datasets can contain sequences (recursion)
**When to use:** VR is SQ or tag is well-known sequence
**Example:**
```csharp
public DicomSequence ParseSequence(DicomTag tag, uint length, int depth)
{
    if (depth > MaxRecursionDepth)
        throw new DicomDataException($"Sequence nesting exceeds {MaxRecursionDepth}");

    var items = new List<DicomDataset>();

    if (length == 0xFFFFFFFF)
    {
        // Undefined length - parse until delimiter
        while (!IsAtEnd)
        {
            var itemTag = ReadTag();
            if (itemTag == DicomDelimiterTags.SequenceDelimitationItem)
            {
                ReadUInt32(); // Skip 00000000H length
                break;
            }
            if (itemTag == DicomDelimiterTags.Item)
            {
                var itemLength = ReadUInt32();
                items.Add(ParseItem(itemLength, depth + 1));
            }
        }
    }
    else
    {
        // Defined length - parse bytes
        var endPosition = _position + (int)length;
        while (_position < endPosition)
        {
            var itemTag = ReadTag();
            if (itemTag == DicomDelimiterTags.Item)
            {
                var itemLength = ReadUInt32();
                items.Add(ParseItem(itemLength, depth + 1));
            }
        }
    }

    return new DicomSequence(tag, items);
}
```
**Source:** [DICOM Part 5 Section 7.5](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html)

### Pattern 4: Context-Dependent VR Resolution
**What:** Some tags have multiple valid VRs; correct choice depends on other elements
**When to use:** Pixel Data (OB/OW), US/SS tags, LUT Data tags
**Example:**
```csharp
// For implicit VR, some tags need context
public DicomVR ResolveVR(DicomTag tag, DicomDictionaryEntry entry)
{
    // Most tags: use default VR from dictionary
    if (entry.ValueRepresentations.Length == 1)
        return entry.DefaultVR;

    // Multi-VR tags require context
    if (tag == DicomTags.PixelData)
    {
        // Resolution:
        // - If encapsulated (fragment sequence): OB
        // - If BitsAllocated <= 8: OB
        // - If BitsAllocated > 8: OW
        // Since we may not have seen BitsAllocated yet, defer resolution
        return DicomVR.OB; // Default assumption, may need correction later
    }

    if (entry.ValueRepresentations.Contains(DicomVR.US) &&
        entry.ValueRepresentations.Contains(DicomVR.SS))
    {
        // US/SS resolution depends on PixelRepresentation
        // If PixelRepresentation = 0: US (unsigned)
        // If PixelRepresentation = 1: SS (signed)
        // Defer until PixelRepresentation is known
        return DicomVR.US; // Default assumption
    }

    return entry.DefaultVR;
}
```
**Source:** [DICOM Part 3 C.7.6.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_c.7.6.3.html)

### Pattern 5: Undefined Length Handling
**What:** Length field 0xFFFFFFFF indicates delimiter-based termination
**When to use:** Both sequences and individual items can have undefined length
**Example:**
```csharp
public const uint UndefinedLength = 0xFFFFFFFF;

if (length == UndefinedLength)
{
    // Parse until delimiter tag encountered
    while (!IsAtEnd)
    {
        var nextTag = PeekTag(); // Don't advance position

        if (isSequence && nextTag == DicomDelimiterTags.SequenceDelimitationItem)
        {
            ReadTag(); // Consume delimiter
            ReadUInt32(); // Skip 00000000H
            break;
        }

        if (isItem && nextTag == DicomDelimiterTags.ItemDelimitationItem)
        {
            ReadTag(); // Consume delimiter
            ReadUInt32(); // Skip 00000000H
            break;
        }

        // Regular element - parse and add
        ParseElement();
    }
}
```
**Source:** [DICOM Part 5 Section 7.5.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html)

### Anti-Patterns to Avoid

- **Assuming VR is always in stream**: Implicit VR requires dictionary lookup; checking IsExplicitVR is mandatory
- **Hard-coding sequence depth limit**: Some real-world files (RT, SR) nest 5-6 deep; make limit configurable
- **Parsing sequences as byte blobs**: Each item is a full dataset; must recursively parse, not skip
- **Ignoring delimiter tags**: FFFE group tags are structural markers, not data; missing them causes parse errors
- **Allocating sequence lists upfront**: Undefined length means unknown item count; use List<T>, not array
- **Reading past sequence end**: Both defined and undefined length require boundary tracking
- **Treating UN as binary always**: UN may contain structured data; preserve for potential later resolution

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| VR lookup by tag | Custom tag→VR mapping | DicomDictionary.GetEntry() | Already generated from NEMA XML; includes multi-VR tags |
| Delimiter tag detection | Magic number comparisons | DicomTag equality with constants | Type-safe, compiler-verified, self-documenting |
| Sequence recursion | Manual stack tracking | Recursive method with depth parameter | Simpler, leverages call stack, easier to guard |
| Undefined length parsing | Byte counting | Delimiter tag detection loop | DICOM requires delimiter parsing; byte counting fails |
| Context-dependent VR | Complex if/else trees | Strategy pattern or deferred resolution | Maintainable, testable, extensible to new VRs |

**Key insight:** DICOM sequence structure is inherently recursive and delimiter-based. Trying to parse sequences as flat byte streams leads to fragile, bug-prone code. The standard's recursive data model should be reflected in recursive parsing code.

## Common Pitfalls

### Pitfall 1: Missing Data Dictionary Initialization
**What goes wrong:** Parser fails on implicit VR files with "unknown VR" errors or defaults all tags to UN
**Why it happens:** DicomDictionary.Default relies on generated code; if source generator didn't run or output isn't included, dictionary is empty
**How to avoid:**
- Verify GeneratedDictionaryData class exists and contains GetTag methods
- Add unit test that checks dictionary contains common tags (e.g., PatientName)
- Log warning if dictionary lookup fails for standard tags (group < 0x1000, odd groups excluded)

**Warning signs:**
- All implicit VR elements have VR=UN
- Dictionary.Contains() returns false for DicomTags.PatientName
- GetEntry() returns null for well-known tags

**Source:** [DCMTK Forum on Dictionary Loading](https://forum.dcmtk.org/viewtopic.php?t=1757)

### Pitfall 2: Nested Sequence Delimiter Confusion
**What goes wrong:** Parser stops at first SequenceDelimitationItem, leaving parent sequence partially read
**Why it happens:** With nested undefined-length sequences, the first delimiter encountered belongs to the innermost sequence, not the parent
**How to avoid:**
- Track nesting depth with recursion depth parameter
- Each recursion level consumes its own delimiter
- Don't break from parsing loop on ANY delimiter - only the delimiter that matches current nesting level

**Warning signs:**
- Dataset appears truncated after sequence
- Elements following sequence parsed as top-level when they're actually in outer sequence
- Sequence item counts lower than expected

**Example:**
```csharp
// WRONG: breaks on any sequence delimiter
while (!IsAtEnd)
{
    var tag = ReadTag();
    if (tag == DicomDelimiterTags.SequenceDelimitationItem)
        break; // BUG: might be for nested sequence!
}

// CORRECT: recursive calls handle their own delimiters
DicomDataset ParseItem(uint length, int depth)
{
    // Each call consumes its own delimiter
    // Nested sequences make recursive calls
    // Each level returns when ITS delimiter is found
}
```

**Source:** [pydicom Issue #114](https://github.com/pydicom/pydicom/issues/114)

### Pitfall 3: Implicit/Explicit VR Switching Mid-Stream
**What goes wrong:** Parser encounters invalid VR bytes (not A-Z) and incorrectly assumes switch to implicit VR
**Why it happens:** Some malformed files or sequences have corrupt VR fields; parsers try to recover by switching modes
**How to avoid:**
- Transfer syntax determines VR mode for entire dataset; don't auto-switch
- If VR validation fails in explicit mode, treat as invalid data, not mode switch
- Use InvalidVRHandling option: Throw, MapToUN, or Preserve (don't assume implicit)

**Warning signs:**
- VR contains non-alphabetic characters
- Length field value seems wrong (e.g., 0x200A = space + newline)
- Parser reports "unexpected implicit VR in explicit VR file"

**Source:** [pydicom Issue #1847](https://github.com/fo-dicom/fo-dicom/issues/1847)

### Pitfall 4: Private Sequences with Unknown VR
**What goes wrong:** Private sequence elements parsed as binary blob, nested structure lost
**Why it happens:** Private tags not in dictionary default to UN; implicit VR parser can't identify SQ VR
**How to avoid:**
- For implicit VR, if tag is unknown AND length is 0xFFFFFFFF, speculatively try parsing as sequence
- If next tag is Item (FFFE,E000), it's a sequence regardless of VR
- Store as UN if delimiter parsing fails (genuine binary data)

**Warning signs:**
- Private tags with undefined length parsed as single large element
- Item tags (FFFE,E000) appearing in element value data
- Nested structure visible in hex dump but not in parsed dataset

**Source:** [DCMTK Forum on Private Sequences](https://forum.dcmtk.org/viewtopic.php?t=4148)

### Pitfall 5: Context-Dependent VR Ordering
**What goes wrong:** Parser tries to resolve Pixel Data VR before seeing BitsAllocated; uses wrong VR
**Why it happens:** In implicit VR, element order in stream may not provide context elements before multi-VR elements
**How to avoid:**
- For deferred resolution: parse value as raw bytes, mark VR as "tentative"
- After full dataset parsed, resolve context-dependent VRs:
  - Pixel Data: check BitsAllocated
  - US/SS tags: check PixelRepresentation
  - LUT Data: check LUT entry count
- Reparse value bytes with correct VR if needed

**Warning signs:**
- Pixel Data treated as OW when should be OB (or vice versa)
- US tags contain negative values (should be SS)
- Byte swap issues on 16-bit pixel data

**Source:** [DICOM Part 3 C.7.6.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_c.7.6.3.html)

### Pitfall 6: Excessive Recursion Depth
**What goes wrong:** Stack overflow on deeply nested sequences or infinite loop on malformed circular references
**Why it happens:** DICOM allows arbitrary nesting depth; malformed files may have delimiter errors causing infinite recursion
**How to avoid:**
- Guard every recursive call with depth check: `if (depth > MaxDepth) throw`
- Default max depth 100 (real files rarely exceed 10; RT/SR may reach 6-7)
- Make MaxRecursionDepth configurable in DicomReaderOptions

**Warning signs:**
- StackOverflowException on valid-looking files
- Parsing never completes (infinite loop)
- Sequence contains itself (circular reference)

**Source:** [DICOM Part 5 Section 7.5](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html)

## Code Examples

### Example 1: Implicit VR Element Parsing
```csharp
// Source: DICOM Part 5 Section A.2 + Phase 2 DicomStreamReader pattern
public bool TryReadElementHeader(
    out DicomTag tag,
    out DicomVR vr,
    out uint length)
{
    tag = default;
    vr = default;
    length = 0;

    // Need at least 8 bytes for tag + length
    if (Remaining < 8)
        return false;

    var span = _buffer.Slice(_position);

    // Read tag (same for explicit and implicit)
    ushort group = _littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(span)
        : BinaryPrimitives.ReadUInt16BigEndian(span);
    ushort element = _littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2))
        : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2));

    tag = new DicomTag(group, element);

    if (_explicitVR)
    {
        // Explicit VR: VR in stream (Phase 2 - already implemented)
        vr = DicomVR.FromBytes(span.Slice(4, 2));

        if (vr.Is32BitLength)
        {
            if (Remaining < 12) return false;
            length = _littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8))
                : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8));
            _position += 12;
        }
        else
        {
            length = _littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6))
                : BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6));
            _position += 8;
        }
    }
    else
    {
        // NEW: Implicit VR - dictionary lookup
        var entry = DicomDictionary.Default.GetEntry(tag);
        vr = entry?.DefaultVR ?? DicomVR.UN;

        // Implicit VR always has 32-bit length
        length = _littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4))
            : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4));

        _position += 8;
    }

    return true;
}
```

### Example 2: Sequence Detection and Parsing
```csharp
// Source: DICOM Part 5 Section 7.5
public DicomElement? TryReadElement()
{
    if (!TryReadElementHeader(out var tag, out var vr, out var length))
        return null;

    // Check if this is a sequence
    if (vr == DicomVR.SQ || tag.Group == 0xFFFE)
    {
        if (tag == DicomDelimiterTags.Item)
            return ParseItem(length);

        if (tag == DicomDelimiterTags.ItemDelimitationItem ||
            tag == DicomDelimiterTags.SequenceDelimitationItem)
        {
            // Delimiter tag - just consume length field
            // Caller handles sequence/item termination
            return null;
        }

        // Regular sequence element
        return new DicomElement(tag, vr, ParseSequence(tag, length));
    }

    // Regular element
    if (!TryReadValue(length, out var value))
        throw new DicomDataException($"Insufficient data for element {tag}");

    return new DicomElement(tag, vr, value);
}

private DicomSequence ParseSequence(DicomTag tag, uint length, int depth = 0)
{
    if (depth > _options.MaxRecursionDepth)
        throw new DicomDataException(
            $"Sequence nesting exceeds maximum depth {_options.MaxRecursionDepth}");

    var items = new List<DicomDataset>();

    if (length == UndefinedLength)
    {
        // Parse until SequenceDelimitationItem
        while (!IsAtEnd)
        {
            if (!TryReadElementHeader(out var itemTag, out _, out var itemLength))
                throw new DicomDataException("Unexpected end in undefined length sequence");

            if (itemTag == DicomDelimiterTags.SequenceDelimitationItem)
                break; // End of sequence

            if (itemTag == DicomDelimiterTags.Item)
                items.Add(ParseItem(itemLength, depth));
            else
                throw new DicomDataException($"Expected Item tag, got {itemTag}");
        }
    }
    else
    {
        // Defined length - parse until bytes consumed
        var endPosition = _position + (int)length;
        while (_position < endPosition)
        {
            if (!TryReadElementHeader(out var itemTag, out _, out var itemLength))
                throw new DicomDataException("Unexpected end in sequence");

            if (itemTag != DicomDelimiterTags.Item)
                throw new DicomDataException($"Expected Item tag, got {itemTag}");

            items.Add(ParseItem(itemLength, depth));
        }
    }

    return new DicomSequence(tag, items);
}

private DicomDataset ParseItem(uint length, int depth)
{
    var dataset = new DicomDataset();

    if (length == UndefinedLength)
    {
        // Parse until ItemDelimitationItem
        while (!IsAtEnd)
        {
            if (!TryReadElementHeader(out var elemTag, out var elemVR, out var elemLength))
                throw new DicomDataException("Unexpected end in undefined length item");

            if (elemTag == DicomDelimiterTags.ItemDelimitationItem)
                break; // End of item

            var element = ParseElementWithSequences(elemTag, elemVR, elemLength, depth + 1);
            if (element != null)
                dataset.Add(element);
        }
    }
    else
    {
        // Defined length
        var endPosition = _position + (int)length;
        while (_position < endPosition)
        {
            if (!TryReadElementHeader(out var elemTag, out var elemVR, out var elemLength))
                throw new DicomDataException("Unexpected end in item");

            var element = ParseElementWithSequences(elemTag, elemVR, elemLength, depth + 1);
            if (element != null)
                dataset.Add(element);
        }
    }

    return dataset;
}
```

### Example 3: Handling Context-Dependent VRs (Deferred Resolution)
```csharp
// Source: DICOM Part 3 C.7.6.3 + practical implementation pattern
public sealed class DeferredVRElement
{
    public DicomTag Tag { get; }
    public DicomVR TentativeVR { get; }
    public ReadOnlyMemory<byte> RawValue { get; }

    public DicomVR ResolveVR(DicomDataset dataset)
    {
        if (Tag == DicomTags.PixelData)
        {
            var bitsAllocated = dataset.GetInt32(DicomTags.BitsAllocated);
            if (bitsAllocated == null)
                return DicomVR.OB; // Default if unknown

            return bitsAllocated <= 8 ? DicomVR.OB : DicomVR.OW;
        }

        // US/SS tags - check PixelRepresentation
        var entry = DicomDictionary.Default.GetEntry(Tag);
        if (entry != null &&
            entry.ValueRepresentations.Contains(DicomVR.US) &&
            entry.ValueRepresentations.Contains(DicomVR.SS))
        {
            var pixelRep = dataset.GetInt32(DicomTags.PixelRepresentation);
            if (pixelRep == null)
                return DicomVR.US; // Default to unsigned

            return pixelRep == 0 ? DicomVR.US : DicomVR.SS;
        }

        return TentativeVR;
    }
}

// In parser
private DicomElement ParseElementWithDeferredVR(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
{
    var entry = DicomDictionary.Default.GetEntry(tag);
    if (entry != null && entry.ValueRepresentations.Length > 1 && !_explicitVR)
    {
        // Multi-VR tag in implicit VR - defer resolution
        _deferredElements.Add(new DeferredVRElement(tag, vr, value));
        return new DicomElement(tag, vr, value); // Tentative VR
    }

    return new DicomElement(tag, vr, value);
}

// After dataset complete
private void ResolveDeferredVRs(DicomDataset dataset)
{
    foreach (var deferred in _deferredElements)
    {
        var correctVR = deferred.ResolveVR(dataset);
        if (correctVR != deferred.TentativeVR)
        {
            // Update element with correct VR
            var element = dataset[deferred.Tag];
            if (element != null)
            {
                // Reparse with correct VR if needed (e.g., byte swap for OW vs OB)
                var corrected = new DicomElement(element.Tag, correctVR, element.RawValue);
                dataset.AddOrUpdate(corrected);
            }
        }
    }
    _deferredElements.Clear();
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Parse sequences as binary blobs | Recursive dataset parsing | Always required by standard | Proper access to nested structures |
| Hard-coded VR by tag | Dictionary-driven VR lookup | DICOM 2008+ | Support for retired/new tags |
| Fixed recursion depth | Configurable max depth | Best practice | Balance safety vs real-world files |
| Eager VR resolution | Deferred resolution for context-dependent tags | DICOM CP 14 (2000) | Correct Pixel Data VR handling |
| Manual delimiter tracking | Structured delimiter tag handling | Always required | Robust undefined-length parsing |

**Deprecated/outdated:**
- **Manual byte counting for sequences**: DICOM requires delimiter-based parsing for undefined length
- **Assuming SQ is only VR for sequences**: Private sequences in implicit VR may appear as UN
- **Fixed OW for Pixel Data**: Must check BitsAllocated and transfer syntax
- **Single-pass parsing without deferred resolution**: Context elements may appear after dependent elements

## Open Questions

1. **Empty sequences with undefined length**
   - What we know: Spec requires SequenceDelimitationItem even if zero items
   - What's unclear: Should parser warn on empty sequences (unusual but legal)?
   - Recommendation: Accept silently; some modalities generate empty sequences

2. **Private sequences without VR**
   - What we know: Implicit VR + unknown private tag = default to UN
   - What's unclear: Should parser speculatively try SQ if undefined length + Item tag follows?
   - Recommendation: Implement heuristic parsing - if undefined length and next tag is Item, try SQ

3. **Recursion depth for real files**
   - What we know: RT/SR can reach 5-6 levels; max in standard is unspecified
   - What's unclear: What depth limit balances safety vs compatibility?
   - Recommendation: Default 100, configurable in options; log warning at depth 10+

4. **Big Endian with sequences**
   - What we know: Delimiter tags are in native byte order
   - What's unclear: Does Phase 3 need Big Endian support? (Retired in most contexts)
   - Recommendation: Defer Big Endian to later phase; focus on Little Endian (99% of files)

## Sources

### Primary (HIGH confidence)
- [DICOM Part 5 Section 7.5](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html) - Nesting of Data Sets
- [DICOM Part 5 Section 7.5.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html) - Delimitation of The Sequence of Items
- [DICOM Part 5 Section 6.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_6.2.html) - Value Representation (VR)
- [DICOM Part 5 Section 6.2.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_6.2.2.html) - Unknown (UN) Value Representation
- [DICOM Part 5 Section A.2](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.2.html) - Implicit VR Little Endian Transfer Syntax
- [DICOM Part 3 Section C.7.6.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_c.7.6.3.html) - Image Pixel Module (PixelRepresentation)

### Secondary (MEDIUM confidence)
- [PACS Boot Camp: Transfer Syntax](https://pacsbootcamp.com/transfer-syntax/) - Overview verified with NEMA docs
- [pydicom Dataset Basics](https://pydicom.github.io/pydicom/dev/tutorials/dataset_basics.html) - Implementation patterns
- [pydicom Working with Sequences](https://pydicom.github.io/pydicom/stable/auto_examples/metadata_processing/plot_sequences.html) - Sequence access patterns
- [Medical Connections: DICOM Sequences](https://www.medicalconnections.co.uk/kb/DICOM-Sequences/) - Tutorial verified with spec

### Tertiary (LOW confidence - Community discussions, marked for validation)
- [pydicom Issue #114](https://github.com/pydicom/pydicom/issues/114) - Nested private sequence bug (real-world pitfall)
- [fo-dicom Issue #1847](https://github.com/fo-dicom/fo-dicom/issues/1847) - VR parsing with invalid characters
- [DCMTK Forum: Implicit VR](https://forum.dcmtk.org/viewtopic.php?t=1757) - Dictionary initialization requirement
- [DCMTK Forum: Private Sequences](https://forum.dcmtk.org/viewtopic.php?t=4148) - Unknown VR handling
- [vtk-dicom Issue #38](https://github.com/dgobbi/vtk-dicom/issues/38) - Context-dependent VR discussion

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All components from Phase 1-2 or DICOM spec
- Architecture: HIGH - Patterns directly from DICOM Part 5 spec
- Pitfalls: MEDIUM-HIGH - Mix of spec requirements and real-world issues (pydicom/dcmtk)

**Research date:** 2026-01-27
**Valid until:** 2026-04-27 (90 days - DICOM spec stable, implementation patterns mature)

**Notes:**
- DICOM Part 5 specification is stable; no breaking changes expected
- Implementation patterns validated across multiple established libraries (pydicom, DCMTK, fo-dicom)
- Context-dependent VR handling is well-documented in standard and implementations
- Sequence parsing complexity is inherent in DICOM design; no simpler alternative exists
