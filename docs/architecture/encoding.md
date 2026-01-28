# Character Encoding

DICOM character set handling and the DicomEncoding system.

## DICOM Character Sets

| DICOM Term | Description | .NET Encoding | UTF-8 Compatible |
|------------|-------------|---------------|------------------|
| (default) | ISO-IR 6 | ASCII | ✓ (subset) |
| ISO_IR 100 | Latin1 | ISO-8859-1 | ✗ |
| ISO_IR 101 | Latin2 | ISO-8859-2 | ✗ |
| ISO_IR 144 | Cyrillic | ISO-8859-5 | ✗ |
| ISO_IR 127 | Arabic | ISO-8859-6 | ✗ |
| ISO_IR 126 | Greek | ISO-8859-7 | ✗ |
| ISO_IR 138 | Hebrew | ISO-8859-8 | ✗ |
| ISO_IR 148 | Turkish | ISO-8859-9 | ✗ |
| ISO 2022 IR 13 | Japanese | JIS X 0201 | ✗ |
| ISO_IR 166 | Thai | TIS-620 | ✗ |
| ISO_IR 192 | UTF-8 | UTF-8 | ✓ |
| GB18030 | Chinese | GB18030 | ✗ |
| GBK | Chinese | GBK | ✗ |

## Design Principle

UTF-8 as far as possible, avoid string allocations.

- Default: ISO-IR 6 (ASCII subset)
- Specific Character Set (0008,0005) specifies encoding
- Modern DICOM: ISO-IR 192 (UTF-8) preferred
- Legacy support: ISO 8859-x, JIS, GB18030, etc.

## DicomEncoding

```csharp
public sealed class DicomEncoding
{
    public Encoding Primary { get; }
    public IReadOnlyList<Encoding>? Extensions { get; }  // ISO 2022 code extensions

    // ASCII is strict subset of UTF-8 - zero-copy for both
    public bool IsUtf8Compatible => Primary.CodePage is 65001 or 20127;

    public bool HasExtensions => Extensions is { Count: > 0 };

    // Well-known instances
    public static readonly DicomEncoding Default = new(Encoding.ASCII);  // UTF-8 compatible
    public static readonly DicomEncoding Utf8 = new(Encoding.UTF8);      // UTF-8 compatible
    public static readonly DicomEncoding Latin1 = new(Encoding.Latin1);  // Needs transcode

    // Parse from Specific Character Set (0008,0005)
    public static DicomEncoding FromSpecificCharacterSet(string? value);
    public static DicomEncoding FromSpecificCharacterSet(string[]? values);

    // Create from a .NET Encoding
    public static DicomEncoding FromEncoding(Encoding encoding);
}
```

## Zero-Copy String Access

```csharp
public sealed class DicomEncoding
{
    // Zero-copy if UTF-8 compatible (ASCII or UTF-8)
    public bool TryGetUtf8(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> utf8)
    {
        if (IsUtf8Compatible)
        {
            utf8 = bytes;
            return true;
        }
        utf8 = default;
        return false;
    }

    // Decode bytes to string using this encoding
    public string GetString(ReadOnlySpan<byte> bytes);
}
```

For ISO 2022 encodings with extensions, .NET's ISO2022Encoding class automatically handles escape sequences during decoding. No custom escape sequence parsing is needed - the .NET Encoding classes (code pages 50220-50227) handle this internally.

## DicomStringValue

Stateless string value - parses on each access, caller caches if needed:

```csharp
public readonly struct DicomStringValue
{
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly DicomEncoding _encoding;

    public ReadOnlyMemory<byte> RawBytes { get; }
    public bool IsUtf8 { get; }  // True = zero-copy possible

    // Zero-copy if UTF-8, otherwise transcodes to caller's buffer
    public ReadOnlySpan<byte> AsUtf8(Span<byte> buffer);

    // Try zero-copy, returns false if transcoding needed
    public bool TryGetUtf8(out ReadOnlySpan<byte> utf8);

    // Allocates string
    public string AsString();
}
```

## Character Set Registry

```csharp
public static class DicomCharacterSets
{
    public static Encoding? GetEncoding(string dicomTerm);
    public static string? GetDicomTerm(Encoding encoding);
    public static void Register(string dicomTerm, Encoding encoding);  // Vendor-specific
}
```

## Error Handling

Unknown character set terms throw `ArgumentException` from `FromSpecificCharacterSet()`. UTF-8, GB18030, and GBK prohibit code extensions and must be single-valued - passing them in a multi-valued array throws `ArgumentException`.

## Integration with DicomDataset

The `DicomDataset` class tracks its encoding based on the Specific Character Set (0008,0005) element:

```csharp
public sealed class DicomDataset
{
    // Encoding is determined from Specific Character Set
    public DicomEncoding Encoding { get; }

    // String access uses dataset encoding
    public string? GetString(DicomTag tag);
}
```

When a dataset is parsed, the encoding is automatically set from the Specific Character Set element if present, otherwise defaults to ASCII.
