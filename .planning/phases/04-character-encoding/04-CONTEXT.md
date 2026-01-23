# Phase 4: Character Encoding - Context

**Gathered:** 2026-01-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Correct text decoding for international DICOM data. Handle Specific Character Set (0008,0005) to decode string VRs correctly across all DICOM-defined encodings including Latin, Asian, RTL, and UTF-8. File writing encoding is Phase 7.

</domain>

<decisions>
## Implementation Decisions

### Encoding Coverage

- **Comprehensive support**: All DICOM standard character sets including retired ones
- **UTF-8 (ISO-IR 192)**: Recommended for modern DICOM, preferred when writing
- **Full Latin family**: ISO-8859-1 through ISO-8859-15 (Latin-1 through Latin-9)
- **Asian encodings**:
  - Japanese: JIS X 0201 (ISO-IR 13), JIS X 0208 (ISO-IR 87), JIS X 0212 (ISO-IR 159), Shift_JIS, EUC-JP
  - Chinese: GB2312 (ISO-IR 58), GB18030, GBK, Big5
  - Korean: KS X 1001 (ISO-IR 149), EUC-KR
- **RTL languages**: Hebrew (ISO-IR 138), Arabic (ISO-IR 127) - encoding only, BiDi layout separate
- **Other scripts**: Thai (ISO-IR 166/TIS-620), Greek (ISO-IR 126), Turkish (ISO-IR 148), Cyrillic (ISO-IR 144 + Windows-1251), Baltic (ISO-IR 110)
- **Indian scripts**: ISCII support
- **Vietnamese**: TCVN 5712 if requested
- **ISO 2022 escape sequences**: Full support for multi-encoding strings per DICOM PS3.5
- **Multi-valued Specific Character Set**: First value = default, additional values for ISO 2022 extensions
- **Person Name (PN) VR**: Full component group support with different encoding per group (alphabetic/ideographic/phonetic)
- **64-bit VRs**: Already handled in Phase 3
- **Retired character sets**: Full legacy support (ISO-IR 13, ISO-IR 14, etc.)
- **Vendor extensions**: Extensible registry via DicomCharacterSets.Register(term, encoding)
- **Major vendor priority**: Siemens/GE/Philips character set quirks prioritized
- **netstandard2.0**: Use System.Text.Encoding.CodePages package for full encoding support
- **ISO-IR 6 (ASCII)**: Support as explicit character set declaration
- **Mixed Latin/Cyrillic**: ISO-IR 111 (ECMA-113) support
- **Full Unicode**: Support characters beyond BMP (supplementary planes via UTF-8/surrogate pairs)
- **Unicode normalization**: Preserve as-is (no NFC/NFD normalization)
- **BOM handling**: Strip UTF-8 BOM on read
- **Expose underlying Encoding**: DicomEncoding.Primary exposes System.Text.Encoding for advanced use
- **Encoding diagnostics**: DicomDataset.Encoding, HasNonAsciiStrings, UsesExtensions properties exposed
- **ISO 2022 validation**: Mode-dependent - strict rejects invalid escape sequences, lenient best-effort

### Fallback Behavior

- **Missing Specific Character Set**: Assume UTF-8 (backward compatible with ASCII)
- **Unrecognized character set term**: Mode-dependent - Strict throws, Lenient uses UTF-8, Permissive tries best-effort
- **Encoding detection failure mid-string**: Mode-dependent - Strict throws, Lenient replaces with U+FFFD, Permissive preserves raw
- **Auto-detection**: Permissive mode tries byte pattern heuristics as fallback
- **FMI encoding**: Honor dataset's Specific Character Set (relaxed from strict ASCII requirement)
- **Sequence item encoding inheritance**: Inherit from parent if no local Specific Character Set
- **Sequence item local charset**: Overrides parent when present
- **Empty string values**: Return empty string, not null
- **Padding handling**: Trim padding bytes, return empty if all padding
- **PN missing components**: Empty string for missing component, not null
- **ASCII-only VRs (DA, TM, DT, UI, AE, CS)**: Mode-dependent for non-ASCII violations
- **String length limits (LO, SH)**: Strict throws if >limit bytes, Lenient truncates, Permissive keeps
- **Multi-value encoding consistency**: Strict throws on inconsistent encoding, Lenient normalizes to UTF-8, Permissive preserves
- **Lossy encoding conversion on write**: Mode-dependent - Strict throws, Lenient replaces with ?, Permissive skips char
- **Conflicting extension terms**: Mode-dependent - Strict fails, Lenient uses first valid

### Invalid Character Handling

- **Invalid byte sequences**: Mode-dependent - Strict throws, Lenient replaces with U+FFFD, Permissive preserves raw
- **Raw bytes access**: Always available via RawBytes property on string elements
- **Encoding issue tracking**: DicomDataset.EncodingIssues collection of affected tags
- **Replacement counting**: Track number of replaced bytes per element
- **Replacement granularity**: Single U+FFFD per invalid byte sequence (not per byte)
- **Custom replacement character**: Configurable via DicomReaderOptions.ReplacementCharacter (default U+FFFD)
- **Best-effort multi-encoding**: Permissive mode tries alternative encodings before giving up
- **Write validation**: Mode-dependent - Strict validates characters valid for declared encoding
- **Private tag strings**: Use dataset encoding (same as public tags)
- **Control characters (0x00-0x1F)**: Strip except allowed (CR, LF, FF, ESC), configurable in permissive mode

### Zero-Copy Optimization

- **UTF-8/ASCII zero-copy priority**: Return ReadOnlySpan<byte> directly from buffer when possible
- **TryGetUtf8 method**: TryGetUtf8(out ReadOnlySpan<byte>) for zero-allocation access
- **AsUtf8 method**: AsUtf8(Span<byte> buffer) for transcoding to caller's buffer
- **IsUtf8Compatible property**: Exposed on DicomEncoding for fast-path detection
- **TranscodeRequired property**: Exposed to let callers know if allocation will happen
- **Transcoding buffers**: stackalloc for <256 bytes, ArrayPool<byte>.Shared for larger
- **No string caching**: Stateless design - decode on each access, caller caches if needed
- **Memory types**: Both ReadOnlySpan<byte> and ReadOnlyMemory<byte> for sync/async scenarios
- **PN component spans**: GetAlphabeticSpan(), GetIdeographicSpan(), GetPhoneticSpan()
- **Multi-value enumeration**: EnumerateValues() yields ReadOnlySpan<byte> per value
- **DicomStringValue**: ref struct to prevent escaping stack
- **Date/time parsing**: Direct UTF-8 parsing via Utf8Parser on .NET 6+, string fallback on netstandard2.0
- **Hot path optimization**: Aggressive optimization (inline, branch hints) for UTF-8 string access

### Claude's Discretion

- Exact stackalloc threshold (suggested 256 bytes)
- SIMD optimization for UTF-8 validation if beneficial
- Specific heuristics for auto-detection in permissive mode
- Internal buffer sizing for ISO 2022 state machine

</decisions>

<specifics>
## Specific Ideas

- Follow DICOM PS3.5 specification for character set handling
- Consistent mode-dependent behavior: "strict fails, lenient recovers, permissive preserves"
- Zero-copy is priority for performance-critical UTF-8 path
- ref struct prevents accidental allocations
- System.Text.Encoding.CodePages enables full encoding support across all TFMs

</specifics>

<deferred>
## Deferred Ideas

- dcmlint tool: Parse files in strict mode, report all conformance issues — future utility
- dcmfix tool: Parse in lenient mode, write strict-conformant output — future utility

</deferred>

---

*Phase: 04-character-encoding*
*Context gathered: 2026-01-26*
