# Data Model

Core DICOM data types and their implementations.

## DicomTag

**Single uint representation** - compact, 4 bytes total:

```csharp
public readonly struct DicomTag : IEquatable<DicomTag>, IComparable<DicomTag>
{
    private readonly uint _value;

    public DicomTag(ushort group, ushort element)
        => _value = ((uint)group << 16) | element;

    public DicomTag(uint value)
        => _value = value;

    // Core properties
    public ushort Group => (ushort)(_value >> 16);
    public ushort Element => (ushort)_value;
    public uint Value => _value;

    // Private tag detection
    public bool IsPrivate => (Group & 1) == 1;
    public bool IsPrivateCreator => IsPrivate && Element > 0x0000 && Element <= 0x00FF;

    // Private creator slot (0x10-0xFF range for data elements)
    public byte PrivateCreatorSlot => IsPrivate && Element > 0x00FF
        ? (byte)(Element >> 8)
        : (byte)0;

    // Key for looking up creator in PrivateCreatorDictionary
    public uint PrivateCreatorKey => IsPrivate && Element > 0x00FF
        ? ((uint)Group << 16) | (uint)(Element >> 8)
        : 0;

    // Standard implementations
    public bool Equals(DicomTag other) => _value == other._value;
    public int CompareTo(DicomTag other) => _value.CompareTo(other._value);
    public override int GetHashCode() => (int)_value;
    public override string ToString() => $"({Group:X4},{Element:X4})";
}
```

### Private Creator Lookup

Private creator lookup via external dictionary (not embedded in tag):

```csharp
public sealed class PrivateCreatorDictionary
{
    private readonly Dictionary<uint, string> _creators = new();

    // Register creator element value during parsing
    public void Register(DicomTag creatorTag, string creator)
    {
        if (!creatorTag.IsPrivateCreator)
            throw new ArgumentException("Not a private creator tag");

        var key = ((uint)creatorTag.Group << 16) | creatorTag.Element;
        _creators[key] = creator;
    }

    // Lookup creator for a private data element
    public string? GetCreator(DicomTag tag)
    {
        if (tag.PrivateCreatorKey == 0) return null;
        return _creators.TryGetValue(tag.PrivateCreatorKey, out var creator)
            ? creator
            : null;
    }
}
```

### Masked Tags

Masked tags for dictionary pattern matching (e.g., `(50xx,0010)`):

```csharp
public readonly struct DicomMaskedTag
{
    public uint Mask { get; }   // Bits that must match (0xFFFF for fixed, 0xFF00 for xx)
    public uint Card { get; }   // Expected value after masking

    public bool Matches(DicomTag tag)
        => (tag.Value & Mask) == Card;
}
```

**Design rationale**:
- Single uint = trivial equality, comparison, hashing
- Private creator separate = tag remains fixed size (4 bytes)
- Dictionary lookup = O(1), shared across dataset
- Masked tags separate = rarely used, don't bloat main struct

---

## DicomVR

**Compact 2-byte struct** storing packed ASCII code:

```csharp
public readonly struct DicomVR : IEquatable<DicomVR>
{
    private readonly ushort _code;  // 'A'<<8 | 'E' = 0x4145 for AE

    // Accepts ANY two bytes - validation is separate concern
    public DicomVR(byte char1, byte char2)
        => _code = (ushort)((char1 << 8) | char2);

    // Convenience for source code readability
    public DicomVR(string code) : this((byte)code[0], (byte)code[1]) { }

    public static DicomVR FromBytes(ReadOnlySpan<byte> bytes)
        => new(bytes[0], bytes[1]);

    public byte Char1 => (byte)(_code >> 8);
    public byte Char2 => (byte)_code;
    public ushort Code => _code;

    // Check if standard VR
    public bool IsKnown => VRInfoLookup.ContainsKey(_code);

    // Equality via single comparison
    public bool Equals(DicomVR other) => _code == other._code;
    public override int GetHashCode() => _code;
    public override string ToString() => $"{(char)Char1}{(char)Char2}";

    // Static instances for all 31 standard VRs
    public static readonly DicomVR AE = new("AE");
    public static readonly DicomVR AS = new("AS");
    // ... all 31 VRs
    public static readonly DicomVR UN = new("UN");
}
```

### VR Metadata

Metadata via separate lookup (keeps VR at 2 bytes):

```csharp
public readonly record struct DicomVRInfo(
    DicomVR VR,
    string Name,                    // "Application Entity"
    byte PaddingByte,               // 0x20 (space) or 0x00 (null)
    uint MaxLength,                 // 16, 64, uint.MaxValue, etc.
    bool IsStringVR,                // True for text-based VRs
    bool Is16BitLength,             // True = 16-bit length in explicit VR
    bool CanHaveUndefinedLength,    // SQ, UN, OB, OW, etc.
    char? MultiValueDelimiter       // '\\' for most strings, null for binary
);

public static DicomVRInfo GetInfo(DicomVR vr)
{
    if (VRInfoLookup.TryGetValue(vr.Code, out var info))
        return info;

    // Fallback for unknown VRs - treat like UN
    return new DicomVRInfo(
        VR: vr, Name: "Unknown", PaddingByte: 0x00,
        MaxLength: uint.MaxValue, IsStringVR: false,
        Is16BitLength: false, CanHaveUndefinedLength: true,
        MultiValueDelimiter: null
    );
}
```

### Invalid VR Handling

```csharp
public enum InvalidVRHandling
{
    Throw,      // Strict: throw DicomDataException
    MapToUN,    // Lenient: treat as UN, continue
    Preserve    // Permissive: keep original bytes, best-effort
}
```

**Implicit VR files**: VR looked up from dictionary by tag. Unknown tags default to UN.

---

## DicomElement

**Hybrid approach**: Struct for regular elements, classes for sequences.

```csharp
public readonly struct DicomElement
{
    public DicomTag Tag { get; }                    // 4 bytes
    public DicomVR VR { get; }                      // 2 bytes
    public ReadOnlyMemory<byte> RawValue { get; }   // 16 bytes
    private readonly DicomSequence? _sequence;      // 8 bytes (null for non-SQ)

    // ~30 bytes total, no heap allocation for regular elements

    public bool IsSequence => _sequence is not null;
    public DicomSequence? Sequence => _sequence;

    // Typed accessors (stateless, parse on call)
    public string? GetString(DicomEncoding? encoding = null);
    public string GetStringOrThrow(DicomEncoding? encoding = null);

    public int? GetInt32();
    public int GetInt32OrThrow();

    public DateOnly? GetDate();
    public DateOnly GetDateOrThrow();

    public TimeOnly? GetTime();
    public TimeOnly GetTimeOrThrow();

    public DicomUID? GetUID();
    public DicomUID GetUIDOrThrow();
}
```

### Sequence Classes

```csharp
public class DicomSequence
{
    public DicomTag Tag { get; }
    public IReadOnlyList<DicomDataset> Items { get; }
}

public class DicomFragmentSequence : DicomSequence
{
    public ReadOnlyMemory<byte> OffsetTable { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> Fragments { get; }
}
```

**Benefits**:
- Regular elements (99%): struct, no heap allocation
- Sequences: proper class with `Items` collection
- Fragments: inherits sequence, adds offset table + fragment list
- `RawValue` empty for sequences (data is in `_sequence.Items`)

---

## DicomDataset

**Dictionary with cached sorted enumeration**:

```csharp
public sealed class DicomDataset : IEnumerable<DicomElement>
{
    private readonly Dictionary<DicomTag, DicomElement> _elements = new();
    private readonly PrivateCreatorDictionary _privateCreators = new();
    private DicomElement[]? _sortedCache;
    private bool _isDirty = true;

    // O(1) access
    public DicomElement? this[DicomTag tag]
        => _elements.TryGetValue(tag, out var e) ? e : null;

    public bool Contains(DicomTag tag) => _elements.ContainsKey(tag);

    // O(1) mutation, invalidates cache
    public void Add(DicomElement element)
    {
        _elements[element.Tag] = element;
        _isDirty = true;
    }

    // Default enumeration: sorted by tag, cached until modified
    public IEnumerator<DicomElement> GetEnumerator()
    {
        if (_isDirty)
        {
            _sortedCache = _elements.Values.OrderBy(e => e.Tag).ToArray();
            _isDirty = false;
        }
        return ((IEnumerable<DicomElement>)_sortedCache).GetEnumerator();
    }

    // Typed convenience (delegates to element accessors)
    public string? GetString(DicomTag tag, DicomEncoding? encoding = null)
        => this[tag]?.GetString(encoding);

    public int? GetInt32(DicomTag tag) => this[tag]?.GetInt32();
}
```

**Design rationale**:
- `Dictionary<DicomTag, DicomElement>`: O(1) lookup and insert
- Sorted cache: O(n log n) once per modification batch, O(1) reuse
- Default `GetEnumerator()` returns sorted order (DICOM requirement)
- Cache invalidated on any mutation
- `PrivateCreatorDictionary` tracks private tag creators

---

## DicomUID

**Zero-allocation inline storage** (max 64 chars):

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct DicomUID : IEquatable<DicomUID>
{
    // 64 bytes inline storage (max UID length)
    private readonly long _p0, _p1, _p2, _p3, _p4, _p5, _p6, _p7;
    private readonly byte _length;

    public DicomUID(string value)
    {
        if (value.Length > 64)
            throw new ArgumentException("UID exceeds 64 characters");
        _length = (byte)value.Length;
        // Copy ASCII bytes into inline buffer
    }

    public int Length => _length;
    public ReadOnlySpan<byte> AsSpan();
    public override string ToString() => Encoding.ASCII.GetString(AsSpan());

    // Equality via span comparison
    public bool Equals(DicomUID other)
        => _length == other._length && AsSpan().SequenceEqual(other.AsSpan());

    // Validation
    public bool IsValid => ValidateFormat(AsSpan());
}
```

### UID Generation

```csharp
public static partial class DicomUID
{
    // UUID-based: 2.25.{uuid-as-decimal}
    public static DicomUID Generate()
    {
        var uuid = Guid.NewGuid();
        var decimal128 = UuidToDecimal(uuid);
        return new($"2.25.{decimal128}");
    }

    // Organization root + timestamp/random
    public static DicomUID Generate(string root)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = Random.Shared.NextInt64();
        return new($"{root}.{timestamp}.{random}");
    }

    // Hash-based (deterministic)
    public static DicomUID GenerateFromName(string root, string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var numeric = HashToDecimal(hash[..16]);
        return new($"{root}.{numeric}");
    }
}
```

---

## DICOM Dictionary

**Source**: Roslyn incremental source generator consuming official NEMA DocBook XML.

**Files cached in repository** (`data/dicom-standard/`):
- `part06.xml` - Main data dictionary, UIDs
- `part07.xml` - Command fields (group 0000)
- `part15.xml` - Confidentiality profile (for de-identification)
- `part16.xml` - Context groups

**Update strategy**: GitHub Action runs weekly, fetches latest XML from NEMA, creates PR if changed.

**Generated output**:
- `DicomTag.Generated.cs` - Static tag members (~4000 tags)
- `DicomUID.Generated.cs` - Static UID members
- `FrozenDictionary` lookups on .NET 8+, `Dictionary` on older TFMs

### Multi-VR Tags

Some DICOM tags allow multiple Value Representations (e.g., Pixel Data can be OB or OW).

```csharp
public readonly record struct DicomDictionaryEntry(
    DicomTag Tag,
    string Keyword,
    string Name,
    DicomVR[] ValueRepresentations,  // Ordered - first is default
    ValueMultiplicity VM,
    bool IsRetired)
{
    public DicomVR DefaultVR => ValueRepresentations[0];
    public bool HasMultipleVRs => ValueRepresentations.Length > 1;
}
```

**Context-dependent VR resolution**:

| Tag | VRs | Resolution Rule |
|-----|-----|-----------------|
| Pixel Data (7FE0,0010) | OB/OW | OW if BitsAllocated > 8, OB if ≤ 8, always OB if encapsulated |
| US/SS tags | US/SS | US if PixelRepresentation = 0, SS if = 1 |
| LUT Data | US/OW | OW if LUT entries > 256 |

---

## Date/Time Types

**VR to .NET type mapping**:

| VR | Format | .NET Type |
|----|--------|-----------|
| DA | YYYYMMDD | `DateOnly` (net6+) / `DateTime` (netstandard2.0) |
| TM | HHMMSS.FFFFFF | `TimeOnly` (net6+) / `TimeSpan` (netstandard2.0) |
| DT | YYYYMMDDHHMMSS.FFFFFF±ZZXX | `DateTime` + `TimeSpan?` offset |
| AS | nnnW/M/Y/D | Custom `Age` struct |

**Design choices**:
- **Stateless**: No caching, parse on each access (caller caches if needed)
- **Nullable + throwing accessors**: `Value` returns null if invalid, `GetValueOrThrow()` throws
- **Raw access**: Always expose `RawBytes` and `RawString` for invalid data inspection

---

## Exception Hierarchy

```
DicomException (base)
├── DicomDataException (parsing/validation)
│   ├── DicomTagException
│   ├── DicomVRException
│   ├── DicomValueException
│   └── DicomSequenceException
├── DicomFileException (file I/O)
│   ├── DicomPreambleException
│   └── DicomMetaInfoException
├── DicomNetworkException (networking)
│   ├── DicomAssociationException
│   ├── DicomAbortException
│   └── DicomDimseException
└── DicomCodecException (compression)
```

**Design rationale**:
- Context via `init` properties - set at throw site
- `byte[]` not `ReadOnlyMemory<byte>` - exceptions copy data, safe to store/log
- Copying is acceptable since exceptions are rare (error path)
