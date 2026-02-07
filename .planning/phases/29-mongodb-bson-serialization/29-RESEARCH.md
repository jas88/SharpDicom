# Phase 29: MongoDB/BSON Serialization - Research

**Researched:** 2026-02-07
**Domain:** BSON binary serialization for DICOM datasets; MongoDB.Driver adapter package
**Confidence:** HIGH

## Summary

This phase adds BSON serialization capability to SharpDicom in two layers: (1) a raw BSON writer inside the core SharpDicom library with zero new dependencies, and (2) an optional adapter package (`SharpDicom.MongoDB`) that references `MongoDB.Bson`/`MongoDB.Driver` for BsonDocument interop, collection helpers, and index recommendations.

The BSON specification (v1.1) is simple and well-documented: documents are length-prefixed sequences of typed key-value elements, all little-endian. SharpDicom already has the exact infrastructure needed -- `IBufferWriter<byte>`, `BinaryPrimitives` for little-endian writes, `DicomStreamWriter` as a pattern to follow, `ArrayBufferWriterPolyfill` for netstandard2.0, and `StreamBufferWriter` for stream targets. The DICOM data model (`IDicomElement`, `DicomStringElement`, `DicomNumericElement`, `DicomBinaryElement`, `DicomSequence`, `DicomPixelDataElement`, `DicomFragmentSequence`) maps cleanly to BSON types with the dual-storage pattern decided in CONTEXT.md.

**Primary recommendation:** Implement a `BsonDicomWriter` class that writes raw BSON bytes to `IBufferWriter<byte>` following the exact pattern established by `DicomStreamWriter`. For deserialization, implement `BsonDicomReader` that reads from `ReadOnlyMemory<byte>`. The MongoDB adapter package converts between raw BSON bytes and `BsonDocument`/`RawBsonDocument` using the driver's existing `BsonBinaryReader`.

## Standard Stack

### Core (Zero Dependencies -- in SharpDicom library)

| Component | Purpose | Why Standard |
|-----------|---------|--------------|
| `System.Buffers.Binary.BinaryPrimitives` | Little-endian int/double writing | Already used throughout SharpDicom; BSON is all little-endian |
| `IBufferWriter<byte>` | Output target abstraction | Already established pattern in DicomStreamWriter, PduWriter |
| `ArrayBufferWriter<byte>` / polyfill | In-memory byte[] target | Already exists (`ArrayBufferWriterPolyfill<T>` for netstandard2.0) |
| `StreamBufferWriter` | Stream target | Already exists and tested |
| `System.Text.Encoding.UTF8` | String encoding for BSON keys/values | BSON spec requires UTF-8 for all strings |

### Supporting (Optional MongoDB Adapter Package)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| MongoDB.Bson | 3.5.0+ | `BsonDocument`, `BsonBinaryReader`, `RawBsonDocument` | When consumers need MongoDB.Bson object model |
| MongoDB.Driver | 3.6.0+ | `MongoClient`, collection operations, bulk writes | When consumers store DICOM in MongoDB |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Raw BSON writer | MongoDB.Bson `BsonBinaryWriter` directly | Would add MongoDB.Bson dependency to core library; context decided against this |
| Custom BSON parser | MongoDB.Bson `BsonBinaryReader` | Reader is only needed in adapter package, so using MongoDB.Bson there is fine |
| System.Text.Json for DICOM-JSON mode | Custom JSON writer | System.Text.Json is already a dependency; use `Utf8JsonWriter` for DICOM-JSON PS3.18 output |

**Installation (adapter package):**
```xml
<PackageReference Include="MongoDB.Bson" Version="3.5.0" />
<PackageReference Include="MongoDB.Driver" Version="3.6.0" />
```

## Architecture Patterns

### Recommended Project Structure

```
src/
├── SharpDicom/
│   └── Serialization/
│       └── Bson/
│           ├── BsonDicomWriter.cs          # Raw BSON serializer (IBufferWriter<byte>)
│           ├── BsonDicomReader.cs          # Raw BSON deserializer (ReadOnlyMemory<byte>)
│           ├── BsonSerializationOptions.cs # Options: tag format, VR inclusion, thresholds
│           ├── BsonTagKeyFormat.cs         # Enum: Hex8, Dotted, Keyword
│           ├── BsonOutputMode.cs           # Enum: MongoNative, DicomJson
│           ├── FlattenProfile.cs           # Sequence flattening configuration
│           ├── BinaryDataReference.cs      # External binary reference model
│           └── DicomDatasetBsonExtensions.cs # Extension methods: ToBson(), FromBson()
├── SharpDicom.MongoDB/                     # Optional adapter package
│   ├── SharpDicom.MongoDB.csproj
│   ├── BsonDocumentAdapter.cs             # Raw bytes <-> BsonDocument conversion
│   ├── DicomCollectionHelper.cs           # MongoDB collection helpers
│   ├── IndexRecommendations.cs            # CreateIndex helpers for common patterns
│   └── BulkImporter.cs                   # Bulk insert helper
```

### Pattern 1: Raw BSON Writer (Core)

**What:** Write BSON bytes directly to `IBufferWriter<byte>`, identical approach to `DicomStreamWriter`.
**When to use:** Always -- this is the core serialization engine.

The BSON document format is:
```
int32    total_size (including this 4-byte field and trailing \0)
element* zero or more elements
\x00     terminal byte
```

Each element is:
```
byte     type_indicator
cstring  field_name (UTF-8 + \0)
value    type-specific data
```

**Implementation approach:**
1. Write a placeholder 4 bytes for document size
2. For each DICOM element, write the BSON element (type byte + key cstring + value)
3. Write terminal \x00
4. Seek back and patch the 4-byte size, or use a two-pass approach (measure then write)

The two-pass approach is preferred for `IBufferWriter<byte>` since seeking is not possible:
- Pass 1: Calculate total document size
- Pass 2: Write bytes with known size prefix

Alternatively, use `ArrayBufferWriter<byte>` internally and write the length at position 0 after completing the document body, then copy to the target writer. This avoids two passes over the dataset at the cost of one buffer copy.

**Best approach for streaming:** Use a "deferred size" pattern where documents are assembled in a temporary `ArrayBufferWriter<byte>`, size is patched at index 0, then the complete bytes are flushed to the target `IBufferWriter<byte>`. This matches how `PduWriter` already handles length-prefixed PDUs in SharpDicom's network layer.

### Pattern 2: VR-to-BSON Type Mapping

**What:** Map each DICOM VR to the appropriate BSON type(s).
**When to use:** During serialization of every element.

| DICOM VR | BSON Type | Notes |
|----------|-----------|-------|
| AE, AS, CS, LO, SH, UC, UR, UT, LT, ST, UI | String (type 0x02) | UTF-8 in BSON |
| IS | Array of Int32/Int64 (type 0x10/0x12) + Raw string | Dual storage per context decision |
| DS | Array of Double (type 0x01) + Raw string | Dual storage per context decision |
| DA | Array of DateTime (type 0x09) + Raw string | UTC milliseconds since epoch |
| TM | Array of DateTime (type 0x09) + Raw string | Map to epoch-relative time |
| DT | Array of DateTime (type 0x09) + Raw string | Full datetime conversion |
| PN | String + parsed component object | Per context decision |
| SS | Int32 (type 0x10) | Upcast from 16-bit |
| US | Int32 (type 0x10) | Upcast from 16-bit |
| SL | Int32 (type 0x10) | Direct |
| UL | Int64 (type 0x12) | Upcast to avoid sign issues |
| FL | Double (type 0x01) | Upcast from 32-bit for BSON compatibility |
| FD | Double (type 0x01) | Direct |
| SV | Int64 (type 0x12) | Direct |
| UV | Int64 (type 0x12) | Note: overflow possible for values > Int64.MaxValue |
| AT | String (type 0x02) | Formatted as 8-char hex |
| SQ | Array (type 0x04) of documents | Recursive serialization |
| OB, OW, OD, OF, OL, OV, UN | Binary (type 0x05) or external ref | Size threshold check |

### Pattern 3: Deferred-Size Document Writing

**What:** Write BSON documents without requiring two passes over the data.
**When to use:** For every document and sub-document in the serialization.

```csharp
// Pseudocode for deferred-size pattern
var docBuffer = new ArrayBufferWriter<byte>(256);

// Reserve 4 bytes for size
docBuffer.GetSpan(4);
docBuffer.Advance(4);

// Write elements...
WriteElement(docBuffer, "00100010", value);

// Write terminal byte
var term = docBuffer.GetSpan(1);
term[0] = 0x00;
docBuffer.Advance(1);

// Patch size at position 0
var sizeBytes = docBuffer.WrittenSpan;
BinaryPrimitives.WriteInt32LittleEndian(
    ((Span<byte>)docBuffer.WrittenMemory.Span).Slice(0, 4),
    docBuffer.WrittenCount);

// Copy to target
target.Write(docBuffer.WrittenSpan);
```

Note: `ArrayBufferWriter<T>.WrittenMemory` returns `ReadOnlyMemory<T>`. For patching the size in-place, we need mutable access. Options:
- Use the internal polyfill's array directly
- Create a specialized `BsonDocumentBuffer` that exposes mutable span at position 0
- Use `MemoryMarshal.AsMemory()` on the `ReadOnlyMemory<byte>` (works but technically unsafe)
- Write to `byte[]` directly with tracked position

The cleanest approach: create a small `BsonDocumentBuffer` helper that wraps `byte[]` with a write position, exposes `GetSpan`/`Advance` for writing, and allows patching the first 4 bytes. This is simpler than fighting `ArrayBufferWriter`'s read-only constraint.

### Pattern 4: Extension Methods for Convenience API

**What:** Provide `dataset.ToBson()` and `DicomDataset.FromBson()` alongside the static serializer.
**When to use:** For users who want a simple one-liner.

```csharp
public static class DicomDatasetBsonExtensions
{
    public static byte[] ToBson(this DicomDataset dataset,
        BsonSerializationOptions? options = null)
    {
        return BsonDicomWriter.Serialize(dataset, options);
    }

    public static DicomDataset FromBson(byte[] bson,
        BsonSerializationOptions? options = null)
    {
        return BsonDicomReader.Deserialize(bson, options);
    }
}
```

### Anti-Patterns to Avoid

- **Using MongoDB.Bson in core library:** The context decision explicitly requires zero external dependencies in the core BSON writer. MongoDB.Bson is only for the adapter package.
- **Full materialization before writing:** Write elements as they are enumerated. Don't build an intermediate object model (no Dictionary, no BsonDocument).
- **Ignoring netstandard2.0:** The core library targets netstandard2.0. Use `BinaryPrimitives`, `Span<T>` via System.Memory polyfill. No `Utf8Formatter` or other net6.0+ APIs without #if guards.
- **Embedded null bytes in BSON keys:** BSON key names are cstrings (null-terminated). Tag hex keys like "00100010" are safe, but keyword-mode keys must be validated.
- **Forgetting VM>1 values:** Even single values go in a BsonArray per context decision. Don't special-case VM=1.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BSON-to-BsonDocument conversion | Custom BsonDocument builder | `RawBsonDocument(byte[])` constructor | MongoDB driver handles raw BSON bytes natively; `RawBsonDocument` wraps `byte[]` without parsing |
| BsonDocument-to-bytes | Custom serializer | `doc.ToBson()` in MongoDB.Bson | Well-tested, handles all edge cases |
| DICOM-JSON output | Custom JSON writer | `System.Text.Json.Utf8JsonWriter` | Already a dependency; fast, zero-allocation |
| Person Name parsing | Custom PN parser | Existing `DicomStringElement.GetStrings()` with `\\` split + `^` split | DICOM PN format is well-defined: `Family^Given^Middle^Prefix^Suffix` with `=` separating component groups |
| Date/time parsing | Custom DICOM date parser | Existing `DicomStringElement.GetDate()`, `.GetTime()`, `.GetDateTime()` | Already implemented and tested |
| UTF-8 string encoding | Manual byte writing | `Encoding.UTF8.GetBytes()` / `Encoding.UTF8.GetByteCount()` | Standard, handles all edge cases |
| MongoDB index creation | Custom index code | `MongoDB.Driver.IndexKeysDefinitionBuilder<T>` | Type-safe index definitions |

**Key insight:** The BSON binary format is simple enough (12 type codes, little-endian, length-prefixed) that a custom writer is straightforward and avoids a large dependency. But the MongoDB BsonDocument object model is complex enough that the adapter package should use the official driver, not reimplement it.

## Common Pitfalls

### Pitfall 1: BSON Document Size Limit (16 MB)

**What goes wrong:** DICOM datasets with large binary data (waveform, overlay, pixel data) easily exceed MongoDB's 16 MB document limit.
**Why it happens:** Naive serialization inlines all binary data.
**How to avoid:** The context decision already addresses this: binary VRs above a configurable threshold (default 16 KB) get external references instead of inline data. Enforce this threshold strictly and make it prominent in the API.
**Warning signs:** Serialization succeeds but MongoDB insert fails with "BSONObjectTooLarge".

### Pitfall 2: BSON Key Name Restrictions

**What goes wrong:** BSON key names are null-terminated cstrings. Keys cannot contain embedded null bytes.
**Why it happens:** Unlikely with hex or keyword tag names, but possible with malformed input.
**How to avoid:** Validate key names during serialization. Hex format ("00100010") and keywords ("PatientName") are always safe. The dotted format ("0010.0010") is also safe. Period characters in BSON keys were historically problematic in MongoDB (couldn't query dotted keys), but this was resolved in MongoDB 5.0+ with `$getField`.
**Warning signs:** BSON parsing errors on read-back.

### Pitfall 3: MongoDB Nested Document Depth Limit (100 levels)

**What goes wrong:** Deeply nested DICOM sequences exceed MongoDB's 100-level nesting limit.
**Why it happens:** Recursive sequences (e.g., Content Sequence in Structured Reports).
**How to avoid:** The context decision includes a configurable depth limit (default 16). Beyond that, serialize as a binary blob. 16 is well within MongoDB's 100-level limit.
**Warning signs:** MongoDB insert fails with "nesting depth" error.

### Pitfall 4: Numeric Precision Loss in IS/DS Dual Storage

**What goes wrong:** DICOM IS (Integer String) allows values up to 12 digits, but BSON Int32 only holds values up to ~2.1 billion. DICOM DS (Decimal String) may have more precision than IEEE 754 double.
**Why it happens:** IS values like "999999999999" overflow Int32. DS values with >15 significant digits lose precision in double.
**How to avoid:** For IS: parse to Int32 first, fall back to Int64 on overflow. For DS: parse to double (acceptable precision loss for queries), always preserve original string in "Raw" for round-trip fidelity. This is exactly what the dual-storage context decision provides.
**Warning signs:** Parsed values differ from original strings on round-trip.

### Pitfall 5: DICOM Date/Time Timezone Handling

**What goes wrong:** BSON DateTime is always UTC milliseconds since epoch. DICOM DA has no timezone. DICOM DT may or may not have a timezone offset.
**Why it happens:** Converting "20240115" (DA) to UTC requires assuming a timezone.
**How to avoid:** For DA: store as UTC midnight (00:00:00Z) -- the "Raw" field preserves the original for round-trip. For DT with offset: convert correctly to UTC. For DT without offset: treat as UTC (document this assumption). For TM: store as milliseconds from midnight UTC.
**Warning signs:** Dates shift by one day depending on timezone.

### Pitfall 6: netstandard2.0 Missing APIs

**What goes wrong:** `BinaryPrimitives.WriteDoubleLittleEndian` does not exist on netstandard2.0. `Span<byte>` indexing limitations.
**Why it happens:** SharpDicom targets netstandard2.0 for maximum compatibility.
**How to avoid:** Use `BitConverter.GetBytes(double)` + manual byte write on netstandard2.0, same pattern as `DicomNumericElement` already does with `#if NETSTANDARD2_0` guards. For double-to-bytes: `BitConverter.DoubleToInt64Bits()` + `BinaryPrimitives.WriteInt64LittleEndian()` works on all targets.
**Warning signs:** Build failures on netstandard2.0 target.

### Pitfall 7: Person Name Component Group Encoding

**What goes wrong:** PN values can have up to three component groups (Alphabetic=Ideographic=Phonetic), each with five components (Family^Given^Middle^Prefix^Suffix). Naive parsing only handles the first group.
**Why it happens:** Most Western DICOM data has only the Alphabetic group, so bugs are not caught until encountering CJK data.
**How to avoid:** Split on `=` first for component groups, then `^` for components. Include all three groups in the BSON output. The existing `DicomStringElement.GetStrings()` handles `\\` splitting but not `=`/`^` parsing -- the serializer needs custom PN parsing.
**Warning signs:** CJK patient names lose Ideographic/Phonetic components.

## Code Examples

### BSON Document Binary Format

```
// BSON document for {"00100010": {"vr": "PN", "Value": ["Doe^John"]}}
//
// Byte layout:
// [4 bytes] int32 total_size = N
// [1 byte]  type 0x03 (embedded document)
// [9 bytes] "00100010\0" (cstring key)
// [4 bytes] int32 sub_doc_size
//   [1 byte]  type 0x02 (string)
//   [3 bytes] "vr\0" (cstring key)
//   [4 bytes] int32 string_length = 3
//   [3 bytes] "PN\0" (string value)
//   [1 byte]  type 0x04 (array)
//   [6 bytes] "Value\0" (cstring key)
//   [4 bytes] int32 array_doc_size
//     [1 byte]  type 0x02 (string)
//     [2 bytes] "0\0" (cstring key -- array index)
//     [4 bytes] int32 string_length = 9
//     [9 bytes] "Doe^John\0" (string value)
//     [1 byte]  0x00 (end of array document)
//   [1 byte]  0x00 (end of sub-document)
// [1 byte]  0x00 (end of document)
```

### BSON Type Indicators (Subset Used)

```csharp
// Source: https://bsonspec.org/spec.html
internal static class BsonType
{
    public const byte Double = 0x01;
    public const byte String = 0x02;
    public const byte Document = 0x03;
    public const byte Array = 0x04;
    public const byte Binary = 0x05;
    public const byte Boolean = 0x08;
    public const byte DateTime = 0x09;
    public const byte Null = 0x0A;
    public const byte Int32 = 0x10;
    public const byte Int64 = 0x12;
}
```

### Writing a BSON String Element

```csharp
// Writing a BSON string to IBufferWriter<byte>
private void WriteBsonString(IBufferWriter<byte> writer, string value)
{
    var byteCount = Encoding.UTF8.GetByteCount(value);
    var totalBytes = 4 + byteCount + 1; // length prefix + UTF-8 bytes + null terminator

    var span = writer.GetSpan(totalBytes);
    BinaryPrimitives.WriteInt32LittleEndian(span, byteCount + 1); // includes null terminator
    Encoding.UTF8.GetBytes(value, span.Slice(4));
    span[4 + byteCount] = 0x00; // null terminator
    writer.Advance(totalBytes);
}
```

### Writing a CString (Key Name)

```csharp
// BSON cstring: UTF-8 bytes + null terminator (no length prefix)
private void WriteCString(IBufferWriter<byte> writer, string value)
{
    var byteCount = Encoding.UTF8.GetByteCount(value);
    var span = writer.GetSpan(byteCount + 1);
    Encoding.UTF8.GetBytes(value, span);
    span[byteCount] = 0x00;
    writer.Advance(byteCount + 1);
}
```

### Element Structure (Per Context Decision)

```csharp
// Each DICOM element becomes a sub-document:
// "00100010": {"Value": ["Doe^John"], "vr": "PN"}
// "00100020": {"Value": ["PAT001"]}
// "00080020": {"Value": [ISODate("2024-01-15T00:00:00Z")], "Raw": "20240115"}
// "00200013": {"Value": [42], "Raw": "42"}
//
// "vr" only present when ambiguous, private, or retired (default mode)
```

### Tag Key Formatting

```csharp
// Hex8 (default): "00100010"
public static string FormatHex8(DicomTag tag)
    => $"{tag.Group:X4}{tag.Element:X4}";

// Dotted: "0010.0010"
public static string FormatDotted(DicomTag tag)
    => $"{tag.Group:X4}.{tag.Element:X4}";

// Keyword: "PatientName" (requires dictionary lookup, falls back to Hex8)
public static string FormatKeyword(DicomTag tag)
{
    var entry = DicomDictionary.Default.GetEntry(tag);
    return entry?.Keyword ?? FormatHex8(tag);
}
```

### Private Tag Grouping

```csharp
// Per context decision, private tags go under "_private" keyed by creator:
// "_private": {
//   "SIEMENS CT": {
//     "00091001": {"Value": [...], "vr": "LO"},
//     "00091002": {"Value": [...], "vr": "DS"}
//   },
//   "AGFA": {
//     "00090010": {"Value": [...], "vr": "SH"}
//   }
// }
```

### MongoDB Adapter: Raw Bytes to BsonDocument

```csharp
// In the adapter package, convert raw BSON bytes to BsonDocument
using MongoDB.Bson;
using MongoDB.Bson.IO;

public static BsonDocument ToBsonDocument(byte[] rawBson)
{
    // RawBsonDocument wraps the byte array without copying
    var raw = new RawBsonDocument(rawBson);
    // Materialize to BsonDocument if mutation is needed
    return raw.ToBsonDocument();
}

// Or use RawBsonDocument directly for read-only/insert operations
// (avoids full materialization, more efficient for bulk inserts)
public static RawBsonDocument ToRawBsonDocument(byte[] rawBson)
{
    return new RawBsonDocument(rawBson);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| BSON keys with dots problematic in MongoDB | `$getField` operator for dotted keys | MongoDB 5.0 (2021) | Dotted tag format is safe for storage but still awkward for queries |
| MongoDB.Bson v2.x with separate Driver.Core | MongoDB.Bson v3.x unified package | 2025 | Simplified package references; v3.x is current |
| `BsonBinaryWriter(Stream)` only | Still Stream-based in v3.x | -- | No IBufferWriter integration in MongoDB.Bson, confirming the choice to write raw BSON ourselves |
| BSON max doc 16MB, GridFS for large docs | Same limit, GridFS still recommended | -- | Stable constraint; drives the binary threshold design |

**Deprecated/outdated:**
- MongoDB.Bson v2.x: Still functional but v3.x is recommended for new projects
- MongoDB dotted-key restrictions: Relaxed in MongoDB 5.0+ but still not intuitive for queries

## Open Questions

1. **Exact representation of UV (unsigned 64-bit) values > Int64.MaxValue**
   - What we know: BSON has only signed Int64. Values 0 to Int64.MaxValue fit. Values above do not.
   - What's unclear: Whether to use Decimal128, string, or accept truncation for extreme values.
   - Recommendation: Use Int64 for values <= Int64.MaxValue, fall back to string representation with a "Raw" field. UV is rare in practice (DICOM 2020+ only). Document the limitation.

2. **TM (Time) to BSON DateTime mapping**
   - What we know: BSON DateTime is milliseconds since Unix epoch. DICOM TM is just a time (no date).
   - What's unclear: What date to use as the epoch-relative base for time-only values.
   - Recommendation: Use Unix epoch date (1970-01-01) + time. The "Raw" field preserves the original DICOM string. Alternatively, store as Int64 milliseconds-from-midnight (not a BSON DateTime). The latter is cleaner but loses MongoDB's date query operators. Recommend: use BSON Int64 (milliseconds from midnight) for TM, not DateTime.

3. **Flatten profile contents**
   - What we know: Context says predefined profiles like "radiology", "pathology" with common sequences pre-selected.
   - What's unclear: Exactly which sequences each profile should flatten.
   - Recommendation: This is in Claude's discretion per context. Start with a "radiology" profile that flattens Referenced Study Sequence, Referenced Series Sequence, Request Attributes Sequence, and Procedure Code Sequence -- the most commonly queried sequences. Document that profiles are extensible.

4. **DICOM-JSON PS3.18 Annex F mode: exact compliance level**
   - What we know: PS3.18 specifies exact JSON format with 8-char hex keys, "vr" always present, "Value"/"BulkDataURI"/"InlineBinary" fields. The context says two modes.
   - What's unclear: Whether full Annex F compliance is needed or just "close enough."
   - Recommendation: Full compliance for the DICOM-JSON mode. The format is simple and well-specified. Use `System.Text.Json.Utf8JsonWriter` (already a dependency). This ensures DICOMweb interoperability.

## Sources

### Primary (HIGH confidence)
- BSON Specification v1.1: https://bsonspec.org/spec.html -- All BSON type codes, binary format, size constraints
- DICOM PS3.18 Annex F (DICOM JSON Model): https://dicom.nema.org/medical/dicom/current/output/chtml/part18/chapter_f.html -- JSON model structure
- DICOM PS3.18 F.2.2 (Object Structure): https://dicom.nema.org/medical/dicom/current/output/chtml/part18/sect_f.2.2.html -- Tag representation, element structure
- DICOM PS3.18 F.2.3 (Value Representation): https://dicom.nema.org/medical/dicom/current/output/chtml/part18/sect_F.2.3.html -- VR-to-JSON type mapping
- MongoDB C# Driver (Context7 /mongodb/mongo-csharp-driver) -- BsonDocument API, BsonBinaryWriter, RawBsonDocument
- SharpDicom codebase (local) -- `DicomStreamWriter`, `StreamBufferWriter`, `ArrayBufferWriterPolyfill`, data model types
- MongoDB limits: https://www.mongodb.com/docs/manual/reference/limits/ -- 16 MB doc size, 100 nesting levels

### Secondary (MEDIUM confidence)
- MongoDB.Bson NuGet v3.5.0: https://www.nuget.org/packages/MongoDB.Bson -- Current version confirmed
- MongoDB.Driver NuGet v3.6.0: https://www.nuget.org/packages/mongodb.driver -- Current version confirmed
- BsonBinaryWriter API: https://mongodb.github.io/mongo-csharp-driver/3.1.0/api/MongoDB.Bson/MongoDB.Bson.IO.BsonBinaryWriter.html -- Stream-based, no IBufferWriter

### Tertiary (LOW confidence)
- DicomTypeTranslation (HicServices/SMI): Confirmed it uses flat records with DicomTypeTranslaterReader for MongoDB. Different approach from SharpDicom's sub-document structure. Not directly reusable.
- SmiServices DICOM-to-MongoDB pattern: Confirmed "metadata in MongoDB, pixels on disk" architecture exists in production. Validated the approach.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- BSON spec is stable and simple; SharpDicom infrastructure already exists; MongoDB.Bson package versions confirmed
- Architecture: HIGH -- Two-layer design (core writer + adapter) confirmed by existing SharpDicom patterns (core + FoDicom compat layers); BSON binary format fully documented
- Pitfalls: HIGH -- All pitfalls derive from BSON spec constraints (16 MB limit, 100 nesting, cstring keys) and DICOM data model realities (large binaries, deep sequences, multi-VR tags); verified against official documentation
- VR mapping: MEDIUM -- Most mappings are straightforward but TM-to-BSON and UV-to-BSON have edge cases documented in Open Questions

**Research date:** 2026-02-07
**Valid until:** 2026-04-07 (90 days -- BSON spec and DICOM standard are stable; MongoDB driver versions may update but API is stable)
