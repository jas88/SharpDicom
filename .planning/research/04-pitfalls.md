# DICOM Pitfalls Research

This document catalogs common pitfalls, edge cases, and non-conformance issues encountered when parsing real-world DICOM files. It provides detection strategies and recommended handling approaches for SharpDicom.

## Table of Contents

1. [Non-Conformant File Structure](#1-non-conformant-file-structure)
2. [Value Representation (VR) Issues](#2-value-representation-vr-issues)
3. [Implicit VR Quirks](#3-implicit-vr-quirks)
4. [Length Field Issues](#4-length-field-issues)
5. [Character Encoding Problems](#5-character-encoding-problems)
6. [Sequence and Delimiter Issues](#6-sequence-and-delimiter-issues)
7. [Pixel Data Issues](#7-pixel-data-issues)
8. [Vendor-Specific Issues](#8-vendor-specific-issues)
9. [fo-dicom Known Issues](#9-fo-dicom-known-issues)
10. [Test Data Sources](#10-test-data-sources)

---

## 1. Non-Conformant File Structure

### 1.1 Missing Preamble and DICM Prefix

**Severity**: High (blocks parsing)

**Description**: Many older DICOM files lack the 128-byte preamble and/or the "DICM" magic bytes at offset 132.

**Detection**:
```csharp
// Check for DICM at offset 132
bool hasDicmPrefix = buffer[132..136].SequenceEqual("DICM"u8);

// If no DICM, check if file starts with a valid DICOM tag
bool startsWithValidTag = IsValidDicomTag(buffer[0..4]);
```

**Handling Strategies**:

| Mode | Behavior |
|------|----------|
| Strict | Reject file with `DicomPreambleException` |
| Lenient | Skip preamble detection, assume raw dataset |
| Permissive | Try multiple detection heuristics |

**Heuristics for Detection**:
1. Check for DICM at offset 132
2. Check if bytes 0-3 form a valid group/element pair
3. Check if bytes 4-5 form a valid VR (for explicit VR)
4. Check for common first tags: (0002,0000), (0008,0005), (0008,0008)

**Reference**: [DICOM Part 10, Chapter 7](https://dicom.nema.org/dicom/2013/output/chtml/part10/chapter_7.html)

---

### 1.2 Missing File Meta Information

**Severity**: High (affects Transfer Syntax detection)

**Description**: File Meta Information (Group 0002) may be missing entirely. Without it, Transfer Syntax is unknown.

**Detection**:
```csharp
// After preamble, first tag should be (0002,0000) or (0002,0001)
bool hasFileMetaInfo = firstTag.Group == 0x0002;
```

**Handling Strategies**:

| Mode | Behavior |
|------|----------|
| Strict | Reject file with `DicomMetaInfoException` |
| Lenient | Assume Implicit VR Little Endian |
| Permissive | Use heuristics to detect Transfer Syntax |

**Transfer Syntax Heuristics** (when File Meta missing):
1. **Default assumption**: Implicit VR Little Endian (1.2.840.10008.1.2)
2. **Explicit VR detection**: Check if bytes 4-5 after first tag are valid VR
3. **Endianness detection**: Check if group/element values make sense in LE vs BE
4. **Reference**: [dclunie.com FAQ 2.2.3](http://www.dclunie.com/medical-image-faq/html/part2.html#DeterminingTransferSyntax)

---

### 1.3 Incorrect Transfer Syntax Declaration

**Severity**: Medium (may cause parsing errors)

**Description**: File Meta Information declares one Transfer Syntax but dataset uses another.

**Detection**:
- Parsing failures after File Meta Information
- VR length field mismatches
- Invalid VR codes encountered

**Handling**: Implement auto-detection fallback when parsing fails with declared syntax.

---

## 2. Value Representation (VR) Issues

### 2.1 Invalid VR Codes

**Severity**: Medium

**Description**: Non-standard VR codes encountered (not one of the 31 standard VRs).

**fo-dicom Issue**: [#1847 - Unable to retrieve correct VR when DICOM element's length is a blank character](https://github.com/fo-dicom/fo-dicom/issues/1847)

**Common Invalid VRs**:
- `0x2020` (two spaces)
- `0x200A` (line separator)
- Null bytes `0x0000`
- Random garbage bytes

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Throw `DicomVRException` |
| Lenient | Map to UN, log warning |
| Permissive | Preserve original bytes, mark as invalid |

---

### 2.2 VR Mismatch with Dictionary

**Severity**: Low to Medium

**Description**: Explicit VR in file doesn't match dictionary definition.

**Common Cases**:
- US vs SS interchanged
- IS vs DS interchanged
- LO vs SH vs CS interchanged
- OB vs OW for Pixel Data

**Detection**:
```csharp
var declaredVR = ReadVRFromStream();
var expectedVR = Dictionary.GetVR(tag);
if (declaredVR != expectedVR)
{
    // Log warning but use declared VR for parsing
}
```

**Handling**: Use declared VR for parsing (it's what's actually in the file), but log mismatch for diagnostics.

---

### 2.3 Multi-VR Tags (Context-Dependent VR)

**Severity**: Medium (especially in Implicit VR files)

**Description**: Some tags allow multiple VRs depending on context:

| Tag | VRs | Resolution Rule |
|-----|-----|-----------------|
| Pixel Data (7FE0,0010) | OB/OW | OB if BitsAllocated <= 8, OW if > 8; always OB if encapsulated |
| LUT Data (0028,3006) | US/OW | OW if entries > 256 (US limited to 16-bit length) |
| US/SS tags (various) | US/SS | US if PixelRepresentation = 0, SS if = 1 |
| Palette Descriptors | SS/US | Always interpret 1st and 3rd values as unsigned |

**Reference**: [DICOM Part 5, Section 8](https://dicom.nema.org/dicom/2013/output/chtml/part05/chapter_8.html)

**Deferred Resolution Strategy**:
```csharp
public sealed class DeferredVRElement
{
    public DicomTag Tag { get; }
    public ReadOnlyMemory<byte> RawValue { get; }
    public DicomVR[] PossibleVRs { get; }

    public DicomVR ResolveVR(DicomDataset context)
    {
        // Use BitsAllocated, PixelRepresentation, etc. to determine
    }
}
```

---

## 3. Implicit VR Quirks

### 3.1 Unknown Tags in Implicit VR

**Severity**: High (blocks correct parsing)

**Description**: Without explicit VR, unknown tags cannot be parsed correctly. The VR determines the length field format.

**Reference**: [DCMTK Forum - Read private tags from implicit VR file](https://forum.dcmtk.org/viewtopic.php?t=3941)

**Detection**: Tag not in dictionary when parsing Implicit VR file.

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Throw `DicomTagException` |
| Lenient | Assume UN, try to continue |
| Permissive | Store raw bytes, attempt best-effort parsing |

**Key Insight**: UN with undefined length should be parsed as SQ because SQ is the only VR (besides items) that can have undefined length.

---

### 3.2 Private Tags in Implicit VR

**Severity**: High

**Description**: Private tags require VR from private dictionary. Without it, parsing is guesswork.

**Solution**: Bundle known private dictionaries for major vendors (Siemens, GE, Philips).

**Fallback Heuristics**:
1. If length is 0xFFFFFFFF, assume SQ
2. If length is short (< 64), try string VRs
3. If length matches common patterns (2, 4, 8 bytes), try numeric VRs

---

### 3.3 Dictionary Initialization Failure

**Severity**: Critical

**Description**: If dictionary fails to load, Implicit VR parsing completely breaks.

**fo-dicom Reference**: [DCMTK Forum - Reading Implicit VR](https://forum.dcmtk.org/viewtopic.php?t=1757)

**Prevention**:
- Source-generated dictionary (compile-time, cannot fail)
- Fallback embedded dictionary
- Clear error message if dictionary unavailable

---

## 4. Length Field Issues

### 4.1 Odd Length Values

**Severity**: Low to Medium

**Description**: DICOM requires even-length values. Some files have odd lengths.

**Reference**: [DCMTK Forum - Odd Fragment Length](https://forum.dcmtk.org/viewtopic.php?t=2895)

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Throw `DicomValueException` |
| Lenient | Assume real length is length + 1, log warning |
| Permissive | Read exact bytes, don't assume padding |

---

### 4.2 Length Exceeds Remaining Bytes

**Severity**: High (causes read past EOF)

**Description**: Element declares length greater than remaining file size.

**fo-dicom Issue**: [#64 - Problem to parse a specific DICOM file](https://github.com/fo-dicom/fo-dicom/issues/64)

**Detection**:
```csharp
if (declaredLength > stream.Length - stream.Position)
{
    // Truncated file or corrupted length
}
```

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Throw `DicomFileException` with truncation info |
| Lenient | Read remaining bytes, mark element as truncated |
| Permissive | Best-effort recovery, continue parsing |

---

### 4.3 Undefined Length Without Delimiter

**Severity**: High (infinite loop risk)

**Description**: Element has 0xFFFFFFFF length but no proper delimitation item.

**fo-dicom Reference**: Added support for parsing DICOM files where pixel data is not properly closed with SequenceDelimitationItem.

**Detection**: Reaching EOF while searching for delimiter.

**Handling**: Treat EOF as implicit delimiter, log warning.

---

### 4.4 VR-Dependent Length Field Size

**Severity**: Critical (parsing offset corruption)

**Description**: In Explicit VR, some VRs use 16-bit length, others use 32-bit:

| 16-bit Length | 32-bit Length |
|---------------|---------------|
| AE, AS, AT, CS, DA, DS, DT, FL, FD, IS, LO, LT, PN, SH, SL, SS, ST, TM, UC, UI, UL, UR, US, UV | OB, OD, OF, OL, OV, OW, SQ, SV, UC, UN, UR, UT, UV |

**Critical Bug Pattern**: Invalid VR interpreted as known VR causes wrong length field size, corrupting all subsequent parsing.

---

## 5. Character Encoding Problems

### 5.1 Missing Specific Character Set

**Severity**: Medium

**Description**: SpecificCharacterSet (0008,0005) tag missing. Encoding is unknown.

**fo-dicom Behavior**: Added FallbackEncoding option, but it wasn't working when tag was missing ([fixed in recent versions](https://github.com/fo-dicom/fo-dicom/releases)).

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Assume ASCII (ISO-IR 6), reject non-ASCII bytes |
| Lenient | Assume UTF-8 (modern DICOM practice) |
| Permissive | Try UTF-8, fall back to Latin-1, use replacement chars |

---

### 5.2 Invalid Character Set Value

**Severity**: Medium

**Description**: SpecificCharacterSet contains unrecognized value.

**Common Misspellings** (fo-dicom tolerates these):
- `ISO IR 100` instead of `ISO_IR 100`
- `ISO-IR 100` instead of `ISO_IR 100`
- Trailing whitespace

**Handling**:
```csharp
// Normalize common variants
charset = charset.Trim()
    .Replace("ISO IR", "ISO_IR")
    .Replace("ISO-IR", "ISO_IR");
```

---

### 5.3 ISO 2022 Escape Sequence Issues

**Severity**: High (data corruption risk)

**Description**: Multi-byte encodings use escape sequences that may be mishandled.

**Reference**: [fo-dicom Issue #1789 - Support proper encoding of strings with multi-valued encodings](https://github.com/fo-dicom/fo-dicom/issues/1789)

**Key Issues**:
1. **Backslash (0x5C) ambiguity**: In GB18030/GBK, multi-byte sequences may contain 0x5C which is also the DICOM value delimiter
2. **Escape sequence parsing**: Some libraries (ICU) handle escape sequences internally, others (libiconv) require manual parsing
3. **Encoding reset**: Delimiters (CR, LF, TAB, FF, ^, =) reset encoding to first in list

**Language-Specific Requirements**:
- **Korean (ISO 2022 IR 149)**: Requires ESC $)C at beginning of every line
- **Chinese (ISO 2022 IR 58)**: Requires ESC $)A at beginning of every line

---

### 5.4 Mixed Encodings Within Dataset

**Severity**: Medium

**Description**: Different elements use different encodings within same dataset.

**DICOM Rule**: Each text element should switch back to default after delimiter, but not all implementations follow this.

**Handling**: Track encoding state, reset appropriately on delimiters.

---

## 6. Sequence and Delimiter Issues

### 6.1 Missing Sequence Delimitation Item

**Severity**: High

**Description**: Sequence with undefined length lacks (FFFE,E0DD) delimiter.

**Reference**: [cornerstone dicomParser Issue #143](https://github.com/cornerstonejs/dicomParser/issues/143)

**Detection**: EOF reached while parsing sequence.

**Handling**: Treat EOF as implicit sequence end.

---

### 6.2 UN with Undefined Length

**Severity**: Medium

**Description**: UN VR with 0xFFFFFFFF length is ambiguous.

**Reference**: [DICOM Part 5, Section 6.2.2](https://dicom.nema.org/medical/dicom/2017c/output/chtml/part05/sect_6.2.2.html)

**DICOM Standard Rule**: UN with undefined length should be parsed as if it were SQ, using Implicit VR encoding for contents.

**Implementation**:
```csharp
if (vr == DicomVR.UN && length == UndefinedLength)
{
    // Parse contents as Implicit VR sequence
    return ParseAsImplicitVRSequence(stream);
}
```

---

### 6.3 Embedded Sequence Delimiter in Pixel Data

**Severity**: High (premature termination)

**Description**: Compressed pixel data may contain bytes FE FF DD E0 (sequence delimiter tag) by coincidence.

**Reference**: [pydicom Issue #1140](https://github.com/pydicom/pydicom/issues/1140)

**Detection**: Finding "delimiter" mid-fragment when total bytes don't match expected.

**Handling**: Track fragment boundaries from offset table, don't scan for delimiters within fragments.

---

### 6.4 Nested Undefined Length Sequences

**Severity**: High

**Description**: Private sequences with undefined length containing nested undefined length sequences.

**Reference**: [pydicom Issue #114](https://github.com/pydicom/pydicom/issues/114)

**Issue**: Parser may lose track of nesting level, treating nested delimiter as parent delimiter.

**Handling**: Maintain explicit nesting stack with expected delimiters.

---

### 6.5 Empty Sequences

**Severity**: Low

**Description**: Sequence with zero items but undefined length.

**Reference**: [cornerstone dicomParser Issue #18](https://github.com/cornerstonejs/dicomParser/issues/18)

**Valid Structure**:
```
SQ tag | 0xFFFFFFFF | (FFFE,E0DD) | 0x00000000
```

**Handling**: Recognize immediate sequence delimiter as valid empty sequence.

---

## 7. Pixel Data Issues

### 7.1 Unencapsulated Pixel Data with Undefined Length

**Severity**: Critical (invalid DICOM)

**Description**: Pixel Data has undefined length but is not encapsulated.

**Reference**: [pydicom Issue #1942](https://github.com/pydicom/pydicom/issues/1942)

**DICOM Rule**: Undefined length pixel data MUST be encapsulated (with fragments).

**Handling**:

| Mode | Behavior |
|------|----------|
| Strict | Reject as invalid |
| Lenient | Try to find sequence delimiter, read as raw |
| Permissive | Read until EOF |

---

### 7.2 Missing Offset Table

**Severity**: Low to Medium

**Description**: Encapsulated pixel data may have empty Basic Offset Table.

**DICOM Rule**: First fragment is BOT, may be empty (length 0).

**Handling**: If BOT empty, scan fragments to build offset table on demand.

---

### 7.3 Photometric Interpretation Changes During Decompression

**Severity**: Medium

**Description**: Compressed data may have different photometric interpretation than declared.

**fo-dicom Fix**: Fixed rendering of compressed data where photometric interpretation changed while decompressing.

**Example**: JPEG may convert YBR_FULL to RGB during decompression.

**Handling**: Update PhotometricInterpretation after decompression if codec indicates change.

---

### 7.4 Large Pixel Data (> 2GB)

**Severity**: Medium

**Description**: Files with pixel data exceeding 2GB cannot use 32-bit length fields.

**fo-dicom Fix**: Fixed issue where reading DICOM file with large pixel data (> 2 GB) did not work.

**Handling**: Use 64-bit positions internally, handle fragmented/streaming reads.

---

## 8. Vendor-Specific Issues

### 8.1 Siemens

**Private Tag Groups**: 0019, 0021, 0051

**CSA Headers** (0029,1010 and 0029,1020):
- Binary format containing extensive acquisition parameters
- Two versions: CSA1 and CSA2 (CSA2 starts with "SV10")
- Always little-endian
- Uses ISO-8859-1 encoding

**Known Issues**:
- Duplicate "### ASCCONV END ###" pattern can break parsing
- Number of tags should be 1-128
- Item length may exceed remaining bytes

**Reference**: [nibabel Siemens CSA documentation](https://nipy.org/nibabel/dicom/siemens_csa.html)

**Private Dictionary Entries** (partial):
```csharp
// Example Siemens private tags
(0019,100A) "NumberOfImagesInMosaic" US
(0019,100B) "SliceMeasurementDuration" DS
(0019,100C) "B_value" IS
(0029,1010) "CSA Image Header Info" OB
(0029,1020) "CSA Series Header Info" OB
(0051,100F) "SlicePositionPCS" FD
```

---

### 8.2 GE

**Private Tag Groups**: 0027, 0043

**Known Issues**:
- b_value in tag (0043,1039) may be masked (e.g., 1000001500 instead of 1500)
- Use modulus 100000 to extract actual value

**Reference**: [NAMIC Wiki DTI DICOM](https://www.na-mic.org/wiki/NAMIC_Wiki:DTI:DICOM_for_DWI_and_DTI)

**Private Dictionary Entries** (partial):
```csharp
(0043,1039) "B_value" IS  // May need modulus extraction
(0027,1010) "ImageType" CS
(0043,102C) "ImagePositionPatientPrivate" DS
```

---

### 8.3 Philips

**Private Tag Groups**: 2001, 2005

**Known Issues**:
- Stores derived images (isotropic) in same series as raw data
- Tag (2001,1004) values: P, M, S, O, I (Isotropic uses "I" but so do raw B=0 images)
- May store conflicting values in private tags vs public tags

**Reference**: [NAMIC Wiki DTI DICOM](https://www.na-mic.org/wiki/NAMIC_Wiki:DTI:DICOM_for_DWI_and_DTI)

**Private Dictionary Entries** (partial):
```csharp
(2001,1003) "DiffusionDirection" FD
(2001,1004) "DiffusionDirectionType" CS
(2005,100D) "PixelSpacingPrivate" DS
(2005,100E) "SliceThicknessPrivate" DS
```

---

### 8.4 Common Vendor Issues

**All Vendors**:
- Private tags with VR=UN may actually have known VR in vendor dictionary
- Casting UN to LO can corrupt binary data
- Non-ASCII data in UN tags may be silently dropped

**Reference**: [nextgenhealthcare Issue #1567](https://github.com/nextgenhealthcare/connect/issues/1567)

---

## 9. fo-dicom Known Issues

### 9.1 Parsing Issues

| Issue | Description | SharpDicom Approach |
|-------|-------------|---------------------|
| [#1847](https://github.com/fo-dicom/fo-dicom/issues/1847) | VR detection fails when length bytes are whitespace (0x2020, 0x200A) | Check for common non-VR byte patterns before VR lookup |
| [#64](https://github.com/fo-dicom/fo-dicom/issues/64) | Infinite loop parsing certain tags | Implement loop detection with position tracking |
| [#1146](https://github.com/fo-dicom/fo-dicom/issues/1146) | Private UN tag incorrectly cast to LO | Preserve original VR/bytes until explicit conversion requested |
| [#328](https://github.com/fo-dicom/fo-dicom/issues/328) | DateTime parsing format issues with timezone | Support all DICOM DT format variants including optional timezone |

### 9.2 Culture-Sensitive Issues

| Issue | Description | SharpDicom Approach |
|-------|-------------|---------------------|
| [#1320](https://github.com/fo-dicom/fo-dicom/issues/1320) | Float parsing fails in EU cultures (comma decimal separator) | Always use InvariantCulture for DS/IS parsing |

### 9.3 Encoding Issues

| Issue | Description | SharpDicom Approach |
|-------|-------------|---------------------|
| [#1789](https://github.com/fo-dicom/fo-dicom/issues/1789) | Multi-valued encodings only use first encoding when writing | Track required encoding per string fragment |
| FallbackEncoding | Didn't work when SpecificCharacterSet missing | Apply fallback during dataset parse, not just per-element |

### 9.4 Anonymizer Issues

| Issue | Description | SharpDicom Approach |
|-------|-------------|---------------------|
| [#1202](https://github.com/fo-dicom/fo-dicom/issues/1202) | Doesn't process items in sequences | Recursive processing via callback system |

---

## 10. Test Data Sources

### 10.1 Official DICOM Test Datasets

| Source | URL | Contents |
|--------|-----|----------|
| NEMA Official | ftp://medical.nema.org/medical/Dicom/DataSets/ | Reference datasets |
| NEMA Multiframe CT | ftp://medical.nema.org/medical/Dicom/Multiframe/CT/ | Enhanced CT |
| NEMA Multiframe MR | ftp://medical.nema.org/medical/Dicom/Multiframe/MR/ | Enhanced MR |
| GDCM Conformance | https://sourceforge.net/projects/gdcm/files/gdcmConformanceTests/ | Edge case files |

### 10.2 Public Collections

| Source | URL | Description |
|--------|-----|-------------|
| Aliza Medical | https://www.aliza-dicom-viewer.com/download/datasets | Multi-vendor test files |
| Rubo Medical | https://www.rubomedical.com/dicom_files/ | Various compressions |
| OsiriX Library | https://www.osirix-viewer.com/resources/dicom-image-library/ | JPEG2000 samples |
| Medimodel | https://medimodel.com/sample-dicom-files/ | Anonymized CT/MRI |
| 3DICOM Library | https://3dicomviewer.com/dicom-library/ | Research datasets |

### 10.3 GitHub Test Collections

| Repository | URL | Focus |
|------------|-----|-------|
| robyoung/dicom-test-files | https://github.com/robyoung/dicom-test-files | Multi-library testing |
| SlicerRt/SlicerRtData | https://github.com/SlicerRt/SlicerRtData | DICOM-RT datasets |

### 10.4 Validation Tools

| Tool | URL | Purpose |
|------|-----|---------|
| DVTk | https://www.dvtk.org/ | DICOM validation toolkit |
| dciodvfy | https://dclunie.com/dicom3tools/dciodvfy.html | DICOM validator |
| dicom-validator | https://pydicom.github.io/dicom-validator/ | Python-based validator |

---

## Recommended Implementation Strategy

### Parsing Mode Presets

```csharp
public static class DicomReaderOptions
{
    public static readonly DicomReaderOptions Strict = new()
    {
        InvalidVR = InvalidVRHandling.Throw,
        UnknownTransferSyntax = UnknownTransferSyntaxHandling.Throw,
        Preamble = FilePreambleHandling.Require,
        FileMetaInfo = FileMetaInfoHandling.Require,
        OddLength = OddLengthHandling.Throw,
        TruncatedElement = TruncatedElementHandling.Throw,
        UnknownCharacterSet = InvalidCharacterSetHandling.Throw
    };

    public static readonly DicomReaderOptions Lenient = new()
    {
        InvalidVR = InvalidVRHandling.MapToUN,
        UnknownTransferSyntax = UnknownTransferSyntaxHandling.AssumeExplicitLE,
        Preamble = FilePreambleHandling.Optional,
        FileMetaInfo = FileMetaInfoHandling.Optional,
        OddLength = OddLengthHandling.AssumeEven,
        TruncatedElement = TruncatedElementHandling.ReadAvailable,
        UnknownCharacterSet = InvalidCharacterSetHandling.AssumeUtf8
    };

    public static readonly DicomReaderOptions Permissive = new()
    {
        InvalidVR = InvalidVRHandling.Preserve,
        UnknownTransferSyntax = UnknownTransferSyntaxHandling.TryParse,
        Preamble = FilePreambleHandling.Ignore,
        FileMetaInfo = FileMetaInfoHandling.Ignore,
        OddLength = OddLengthHandling.Preserve,
        TruncatedElement = TruncatedElementHandling.ReadAvailable,
        UnknownCharacterSet = InvalidCharacterSetHandling.AssumeUtf8
    };
}
```

### Error Reporting

All parsing issues should be reported via the callback system, allowing callers to:
1. Collect all issues without stopping
2. Make per-element decisions
3. Log for diagnostics

```csharp
public readonly struct ParsingIssue
{
    public DicomTag? Tag { get; init; }
    public long StreamPosition { get; init; }
    public ParsingIssueType Type { get; init; }
    public string Message { get; init; }
    public ParsingIssueSeverity Severity { get; init; }
}

public enum ParsingIssueSeverity
{
    Info,       // Non-conformance that doesn't affect parsing
    Warning,    // Issue that was auto-corrected
    Error,      // Issue that may affect data integrity
    Critical    // Issue that prevents further parsing
}
```

---

## Summary

Real-world DICOM files frequently violate the standard in predictable ways. SharpDicom should:

1. **Default to lenient parsing** - Most users want files to open
2. **Provide strict mode** - For validation and conformance testing
3. **Report all issues** - Via callback system for logging/analysis
4. **Never lose data** - Preserve raw bytes when interpretation fails
5. **Bundle vendor dictionaries** - Enable proper private tag handling
6. **Track stream position** - For debugging and error messages

The callback-based validation system described in CLAUDE.md is well-suited to handle these requirements, allowing flexible per-element decisions while maintaining performance for conformant files.
