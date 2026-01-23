# SharpDicom Features Research

This document contains research findings for key DICOM feature areas that SharpDicom must support.

---

## 1. Transfer Syntaxes

### Overview

A Transfer Syntax defines the encoding rules for DICOM data, including byte ordering, VR encoding (explicit/implicit), and compression. There are approximately 35 defined transfer syntaxes in the current DICOM standard, with 14 retired.

### Mandatory Transfer Syntax

The **DICOM Implicit VR Little Endian** (`1.2.840.10008.1.2`) is the default transfer syntax that every conformant DICOM implementation must support. However, modern implementations are strongly encouraged to use Explicit VR Little Endian where possible.

### Complete Transfer Syntax List

| UID | Name | Status | Compression | VR | Endian |
|-----|------|--------|-------------|-----|--------|
| 1.2.840.10008.1.2 | Implicit VR Little Endian | Active | None | Implicit | LE |
| 1.2.840.10008.1.2.1 | Explicit VR Little Endian | Active | None | Explicit | LE |
| 1.2.840.10008.1.2.1.98 | Encapsulated Uncompressed Explicit VR LE | Active | None (encap) | Explicit | LE |
| 1.2.840.10008.1.2.1.99 | Deflated Explicit VR Little Endian | Active | Deflate | Explicit | LE |
| 1.2.840.10008.1.2.2 | Explicit VR Big Endian | **Retired** | None | Explicit | BE |
| 1.2.840.10008.1.2.4.50 | JPEG Baseline (Process 1) | Active | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.51 | JPEG Extended (Process 2 & 4) | Active | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.52 | JPEG Extended (Process 3 & 5) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.53 | JPEG Spectral Selection, Non-Hierarchical (Process 6 & 8) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.54 | JPEG Spectral Selection, Non-Hierarchical (Process 7 & 9) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.55 | JPEG Full Progression, Non-Hierarchical (Process 10 & 12) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.56 | JPEG Full Progression, Non-Hierarchical (Process 11 & 13) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.57 | JPEG Lossless, Non-Hierarchical (Process 14) | Active | JPEG Lossless | Explicit | LE |
| 1.2.840.10008.1.2.4.58 | JPEG Lossless, Non-Hierarchical (Process 15) | **Retired** | JPEG Lossless | Explicit | LE |
| 1.2.840.10008.1.2.4.59 | JPEG Extended, Hierarchical (Process 16 & 18) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.60 | JPEG Extended, Hierarchical (Process 17 & 19) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.61 | JPEG Spectral Selection, Hierarchical (Process 20 & 22) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.62 | JPEG Spectral Selection, Hierarchical (Process 21 & 23) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.63 | JPEG Full Progression, Hierarchical (Process 24 & 26) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.64 | JPEG Full Progression, Hierarchical (Process 25 & 27) | **Retired** | JPEG Lossy | Explicit | LE |
| 1.2.840.10008.1.2.4.65 | JPEG Lossless, Hierarchical (Process 28) | **Retired** | JPEG Lossless | Explicit | LE |
| 1.2.840.10008.1.2.4.66 | JPEG Lossless, Hierarchical (Process 29) | **Retired** | JPEG Lossless | Explicit | LE |
| 1.2.840.10008.1.2.4.70 | JPEG Lossless, Non-Hierarchical, First-Order Prediction | Active | JPEG Lossless | Explicit | LE |
| 1.2.840.10008.1.2.4.80 | JPEG-LS Lossless | Active | JPEG-LS | Explicit | LE |
| 1.2.840.10008.1.2.4.81 | JPEG-LS Lossy (Near-Lossless) | Active | JPEG-LS | Explicit | LE |
| 1.2.840.10008.1.2.4.90 | JPEG 2000 Lossless Only | Active | JPEG2000 | Explicit | LE |
| 1.2.840.10008.1.2.4.91 | JPEG 2000 | Active | JPEG2000 | Explicit | LE |
| 1.2.840.10008.1.2.4.92 | JPEG 2000 Part 2 Multi-component Lossless Only | Active | JPEG2000 | Explicit | LE |
| 1.2.840.10008.1.2.4.93 | JPEG 2000 Part 2 Multi-component | Active | JPEG2000 | Explicit | LE |
| 1.2.840.10008.1.2.4.94 | JPIP Referenced | Active | Reference | Explicit | LE |
| 1.2.840.10008.1.2.4.95 | JPIP Referenced Deflate | Active | Reference | Explicit | LE |
| 1.2.840.10008.1.2.4.100 | MPEG2 Main Profile @ Main Level | Active | MPEG2 | Explicit | LE |
| 1.2.840.10008.1.2.4.101 | MPEG2 Main Profile @ High Level | Active | MPEG2 | Explicit | LE |
| 1.2.840.10008.1.2.4.102 | MPEG-4 AVC/H.264 High Profile / Level 4.1 | Active | H.264 | Explicit | LE |
| 1.2.840.10008.1.2.4.103 | MPEG-4 AVC/H.264 BD-compatible High Profile / Level 4.1 | Active | H.264 | Explicit | LE |
| 1.2.840.10008.1.2.4.104 | MPEG-4 AVC/H.264 High Profile / Level 4.2 2D Video | Active | H.264 | Explicit | LE |
| 1.2.840.10008.1.2.4.105 | MPEG-4 AVC/H.264 High Profile / Level 4.2 3D Video | Active | H.264 | Explicit | LE |
| 1.2.840.10008.1.2.4.106 | MPEG-4 AVC/H.264 Stereo High Profile / Level 4.2 | Active | H.264 | Explicit | LE |
| 1.2.840.10008.1.2.4.107 | HEVC/H.265 Main Profile / Level 5.1 | Active | H.265 | Explicit | LE |
| 1.2.840.10008.1.2.4.108 | HEVC/H.265 Main 10 Profile / Level 5.1 | Active | H.265 | Explicit | LE |
| 1.2.840.10008.1.2.4.201 | High-Throughput JPEG 2000 Lossless Only | Active | HTJ2K | Explicit | LE |
| 1.2.840.10008.1.2.4.202 | High-Throughput JPEG 2000 with RPCL Lossless Only | Active | HTJ2K | Explicit | LE |
| 1.2.840.10008.1.2.4.203 | High-Throughput JPEG 2000 | Active | HTJ2K | Explicit | LE |
| 1.2.840.10008.1.2.4.204 | JPIP HTJ2K Referenced | Active | Reference | Explicit | LE |
| 1.2.840.10008.1.2.4.205 | JPIP HTJ2K Referenced Deflate | Active | Reference | Explicit | LE |
| 1.2.840.10008.1.2.4.206 | JPEG XL Lossless | Active | JPEG-XL | Explicit | LE |
| 1.2.840.10008.1.2.4.207 | JPEG XL JPEG Recompression | Active | JPEG-XL | Explicit | LE |
| 1.2.840.10008.1.2.4.208 | JPEG XL | Active | JPEG-XL | Explicit | LE |
| 1.2.840.10008.1.2.5 | RLE Lossless | Active | RLE | Explicit | LE |

### Encapsulated Pixel Data Handling

For encapsulated (compressed) transfer syntaxes:
- Pixel Data is stored as fragments within a sequence structure
- VR is always OB for encapsulated data
- The Basic Offset Table is required as the first item (may be empty)
- Fragment boundaries may not align with frame boundaries

### Edge Cases

1. **Deflate transfer syntax**: Non-pixel data is deflate-compressed, but this is rare in practice
2. **JPIP Referenced**: Pixel data stored externally, file contains only reference
3. **Video syntaxes (MPEG2/4, HEVC)**: Treat video as whole stream, not individual frames; limited to 4GB due to fragment rules
4. **Retired syntaxes**: Must still be readable for legacy data, but never used for writing

### SharpDicom Recommendations

1. **Core library**: Support Implicit VR LE, Explicit VR LE, and RLE (no external dependencies)
2. **Codec packages**: Separate NuGet packages for JPEG, JPEG2000, JPEG-LS, HEVC
3. **Retired handling**: Read support only, with warnings in strict mode
4. **Unknown syntaxes**: Configurable behavior (throw, assume Explicit LE, or probe data)

### References
- [PS3.5 Transfer Syntaxes](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/chapter_10.html)
- [PS3.5 Annex A - Transfer Syntax Specifications](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/chapter_A.html)
- [PS3.6 UID Registry](https://dicom.nema.org/medical/dicom/current/output/chtml/part06/chapter_a.html)

---

## 2. Character Sets

### Overview

DICOM character encoding is complex due to support for legacy single-byte encodings, multi-byte Asian encodings, and ISO 2022 code extension techniques. The Specific Character Set (0008,0005) attribute controls encoding.

### Character Set Categories

#### Single-Byte Without Code Extensions

| DICOM Term | Description | ISO Standard | .NET Encoding |
|------------|-------------|--------------|---------------|
| (default/empty) | ASCII (ISO-IR 6) | ISO 646 | ASCII (20127) |
| ISO_IR 100 | Latin Alphabet No. 1 | ISO 8859-1 | Latin1 (28591) |
| ISO_IR 101 | Latin Alphabet No. 2 | ISO 8859-2 | 28592 |
| ISO_IR 109 | Latin Alphabet No. 3 | ISO 8859-3 | 28593 |
| ISO_IR 110 | Latin Alphabet No. 4 | ISO 8859-4 | 28594 |
| ISO_IR 144 | Cyrillic | ISO 8859-5 | 28595 |
| ISO_IR 127 | Arabic | ISO 8859-6 | 28596 |
| ISO_IR 126 | Greek | ISO 8859-7 | 28597 |
| ISO_IR 138 | Hebrew | ISO 8859-8 | 28598 |
| ISO_IR 148 | Latin Alphabet No. 5 (Turkish) | ISO 8859-9 | 28599 |
| ISO_IR 166 | Thai | TIS 620-2533 | 874 |
| ISO_IR 13 | Japanese Katakana | JIS X 0201 | 50222 (partial) |

#### Single-Byte With Code Extensions (ISO 2022)

When code extensions are used, the prefix "ISO 2022" is added:
- ISO 2022 IR 100, ISO 2022 IR 101, etc.
- Escape sequences switch between character sets mid-string
- G0 set invoked in GL, G1 set invoked in GR

#### Multi-Byte With Code Extensions

| DICOM Term | Description | Use Case |
|------------|-------------|----------|
| ISO 2022 IR 87 | JIS X 0208 Kanji | Japanese |
| ISO 2022 IR 159 | JIS X 0212 Supplementary Kanji | Japanese |
| ISO 2022 IR 149 | KS X 1001 | Korean |
| ISO 2022 IR 58 | GB 2312 | Simplified Chinese |

#### Multi-Byte Without Code Extensions

| DICOM Term | Description | .NET Encoding | Notes |
|------------|-------------|---------------|-------|
| ISO_IR 192 | UTF-8 | UTF8 (65001) | **Preferred for new data** |
| GB18030 | Chinese GB18030 | 54936 | Full Unicode mapping |
| GBK | Chinese GBK | 936 | Subset of GB18030 |

### ISO 2022 Escape Sequences

When Specific Character Set has multiple values, escape sequences switch character sets:

```
ESC 0x28 0x42  ->  ASCII (G0)
ESC 0x28 0x4A  ->  JIS X 0201 Romaji (G0)
ESC 0x28 0x49  ->  JIS X 0201 Katakana (G0)
ESC 0x24 0x42  ->  JIS X 0208 Kanji (G0)
ESC 0x24 0x28 0x44  ->  JIS X 0212 Supplementary Kanji (G0)
```

### Edge Cases

1. **Backslash in GB18030/GBK**: The ASCII backslash (0x5C) can appear as the second byte of a two-byte character. Must not be parsed as multi-value delimiter in this context.

2. **Mixed encodings forbidden**: UTF-8, GB18030, and GBK prohibit code extension techniques. They may only appear as the first (and only) value in Specific Character Set.

3. **Component-level encoding**: For Person Name (PN) VR, different name components can use different character sets (e.g., alphabetic in Latin, ideographic in Kanji).

4. **Absent Specific Character Set**: Default is ASCII. Many files omit this tag even when using ASCII.

5. **Invalid sequences**: Real-world files often contain encoding errors. Must handle gracefully.

### SharpDicom Recommendations

1. **UTF-8 fast path**: When encoding is UTF-8 or ASCII, enable zero-copy string access
2. **Lazy transcoding**: Transcode to UTF-8 only when string value is accessed
3. **Strict/lenient modes**: Configurable handling of invalid byte sequences
4. **Code extension state machine**: Proper ISO 2022 escape sequence handling
5. **Character set registry**: Extensible for vendor-specific encodings

### References
- [PS3.5 Chapter 6 - Value Encoding](https://dicom.nema.org/dicom/2013/output/chtml/part05/chapter_6.html)
- [PS3.5 Annex J - UTF-8, GB18030, GBK](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/chapter_J.html)
- [Specific Character Set Attribute](https://dicom.innolitics.com/ciods/rt-radiation-set/sop-common/00080005)

---

## 3. Multi-VR Tags

### Overview

A small number of DICOM tags can have multiple possible Value Representations. The correct VR depends on context (other elements in the dataset) or the transfer syntax being used.

### Multi-VR Tag Categories

#### OB or OW Tags

| Tag | Name | Resolution Rule |
|-----|------|-----------------|
| (7FE0,0010) | Pixel Data | OW for Implicit VR LE; OB if BitsAllocated <= 8 in Explicit VR; OB if encapsulated |
| (60xx,3000) | Overlay Data | Same as Pixel Data |
| (0028,1201) | Red Palette Color LUT Data | OW if > 256 entries, US otherwise (see below) |
| (0028,1202) | Green Palette Color LUT Data | Same as Red |
| (0028,1203) | Blue Palette Color LUT Data | Same as Blue |
| (5400,1010) | Waveform Data | OB if Waveform Bits Allocated is 8, OW if 16 |

#### US or SS Tags

These tags depend on Pixel Representation (0028,0103):
- US (unsigned) if Pixel Representation = 0
- SS (signed) if Pixel Representation = 1

| Tag | Name |
|-----|------|
| (0028,0106) | Smallest Image Pixel Value |
| (0028,0107) | Largest Image Pixel Value |
| (0028,0108) | Smallest Pixel Value in Series |
| (0028,0109) | Largest Pixel Value in Series |
| (0028,0110) | Smallest Image Pixel Value in Plane |
| (0028,0111) | Largest Image Pixel Value in Plane |
| (0028,0120) | Pixel Padding Value |
| (0028,0121) | Pixel Padding Range Limit |
| (0028,1101) | Red Palette Color LUT Descriptor |
| (0028,1102) | Green Palette Color LUT Descriptor |
| (0028,1103) | Blue Palette Color LUT Descriptor |

#### US or OW Tags

| Tag | Name | Resolution Rule |
|-----|------|-----------------|
| (0028,3006) | LUT Data | OW if more than 65535 values, US otherwise |

### LUT Descriptor Special Case

LUT Descriptor (0028,3002) has VR of US or SS, but the first and third values are **always interpreted as unsigned**, regardless of the declared VR. This is a historical quirk.

### Implicit VR Deferred Resolution

For Implicit VR Little Endian files, the VR must be determined from the dictionary or context. The problem: context tags may appear **after** the multi-VR element in the stream.

**Streaming solution**:
1. Parse element with deferred VR marker
2. Store raw bytes without interpretation
3. Continue reading, accumulating context tags
4. Resolve VR when context becomes available (or at end of dataset)
5. Parse value on first access

### Edge Cases

1. **Encapsulated always OB**: Even if BitsAllocated > 8, encapsulated pixel data uses OB
2. **Historical LUT confusion**: Older standards specified US/SS for LUT data, newer use OW
3. **Missing context**: If Pixel Representation is absent, default to US (unsigned)
4. **Overlay in high bits**: Overlay data embedded in pixel data (retired approach)

### SharpDicom Recommendations

1. **Dictionary entry design**: Store VR array with default VR first
2. **Context resolver**: Stateful VR resolver that accepts context tags
3. **Deferred parsing**: Element struct holds raw bytes, parses on access
4. **Validation**: Warn if declared VR doesn't match resolved VR

### References
- [PS3.5 Section 6.2 - Value Representation](https://dicom.nema.org/dicom/2013/output/chtml/part05/sect_6.2.html)
- [PS3.5 Chapter 8 - Encoding of Pixel Data](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/chapter_8.html)

---

## 4. Sequences and Nesting

### Overview

Sequences (VR = SQ) allow nested datasets within a DICOM file. Each sequence contains zero or more Items, and each Item is itself a complete dataset that can contain further sequences.

### Sequence Structure

```
Sequence Element (VR=SQ)
├── Item 1 (FFFE,E000)
│   └── Dataset (nested elements, may include sequences)
├── Item 2 (FFFE,E000)
│   └── Dataset
└── Sequence Delimitation Item (FFFE,E0DD) [if undefined length]
```

### Length Encoding

#### Defined Length
- Sequence has explicit byte length
- Items have explicit byte lengths
- No delimiter tags needed
- Faster to skip (seek past entire sequence)

#### Undefined Length
- Sequence length = 0xFFFFFFFF
- Items may have defined or undefined length
- Item Delimitation Tag (FFFE,E00D) marks end of undefined-length items
- Sequence Delimitation Tag (FFFE,E0DD) marks end of sequence
- Must parse entire sequence to find end

### Delimiter Tags

| Tag | Name | Length Field | Purpose |
|-----|------|--------------|---------|
| (FFFE,E000) | Item | uint32 | Marks start of item |
| (FFFE,E00D) | Item Delimitation Item | 0x00000000 | Marks end of undefined-length item |
| (FFFE,E0DD) | Sequence Delimitation Item | 0x00000000 | Marks end of undefined-length sequence |

### Nesting Depth

**Standard specification**: No defined maximum. "Data Sets can be nested recursively."

**Practical depths observed**:
- **Typical IODs**: 2-3 levels
- **Structured Reports**: Up to 5-6 levels
- **Radiotherapy IODs**: Up to 5-6 levels

**Implementation guidance**: Support at least 10-20 levels; use iterative parsing with explicit stack rather than recursion to avoid stack overflow.

### Edge Cases

1. **Empty sequences**: Valid to have sequence with zero items
2. **Empty items**: Valid to have item with zero elements
3. **Mixed length encoding**: Sequence may be defined length, but items undefined (or vice versa)
4. **Private sequences**: Private tags can have VR=SQ, follow same rules
5. **Nested private creator scope**: Private creator reservations are scoped to the item, not inherited

### Parsing Considerations

1. **Streaming**: Cannot skip undefined-length sequences without parsing
2. **Memory**: Deep nesting can cause memory pressure if fully materialized
3. **Validation**: Check for orphan delimiters, mismatched lengths
4. **Encapsulated pixel data**: Uses sequence-like structure but is NOT a true sequence

### SharpDicom Recommendations

1. **Iterative parsing**: Use explicit stack, not recursion
2. **Configurable depth limit**: Default to 50, allow override
3. **Lazy loading option**: Load sequence items on demand
4. **Length validation**: Cross-check defined lengths against actual bytes read

### References
- [PS3.5 Section 7.5 - Nesting of Data Sets](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html)
- [PS3.5 Section 7.5.2 - Delimitation of Sequences](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html)

---

## 5. Private Tags

### Overview

Private tags use odd group numbers and allow vendors to embed proprietary data. A reservation mechanism prevents collisions between different vendors using the same group.

### Private Tag Structure

```
Group: Odd number (e.g., 0019, 0021, 0033)
Private Creator: (gggg,00xx) where xx = 0x10 to 0xFF
Private Data: (gggg,xxyy) where xx = creator slot, yy = 0x00 to 0xFF
```

### Reservation Mechanism

1. Encoder writes Private Creator element at (gggg,00xx) with LO value identifying the vendor
2. This reserves the block (gggg,xx00) through (gggg,xxFF) for that creator
3. Private data elements use tags within the reserved block

**Example**:
```
(0019,0010) LO "SIEMENS CT VA0 CINE"    # Reserves block 0019,10xx
(0019,1002) DS "123.45"                  # Private data in reserved block
(0019,1003) LO "Some value"              # Another private data element
(0019,0011) LO "SIEMENS CT VA0 ACQU"    # Reserves block 0019,11xx
(0019,1102) US 42                        # Private data in second block
```

### Common Vendor Private Groups

| Vendor | Groups | Example Creator |
|--------|--------|-----------------|
| Siemens | 0019, 0021, 0029, 0051 | SIEMENS CT VA0 CINE |
| GE | 0025, 0027, 0043, 0045 | GEMS_PARM_01 |
| Philips | 2001, 2005, 7053 | Philips Imaging DD 001 |
| Canon/Toshiba | 7005 | TOSHIBA_MEC_CT3 |

### Scope Rules

- Private creator reservations are scoped to the **current dataset**
- Items within sequences have their own scope
- Reservations do NOT inherit from parent datasets

### Private Dictionaries

fo-dicom and DCMTK maintain private dictionaries for known vendors:
- Enable proper VR assignment for known private tags
- Provide human-readable names
- Format: XML or text-based dictionary files

### Edge Cases

1. **Missing private creator**: Some broken files have private data without creator element
2. **Duplicate creators**: Same creator string at multiple slots (technically allowed)
3. **Creator slot overflow**: Creator at 0x00FF, can't reserve more in that group
4. **Private sequences**: Private tags can be sequences, follow same nesting rules
5. **Implicit VR private tags**: VR must come from private dictionary or default to UN

### Handling Options

1. **Strip all private**: Remove private tags during de-identification
2. **Preserve unknown**: Keep raw bytes with VR=UN
3. **Dictionary lookup**: Use private dictionaries for known vendors
4. **Custom callback**: Let caller decide per-tag

### Clinical Considerations

Not all private tags should be removed:
- Dose parameters
- Reconstruction kernels
- Scanner-specific calibration data
- Quality metrics

### SharpDicom Recommendations

1. **PrivateCreatorDictionary class**: Track creator registrations per dataset
2. **Bundled private dictionaries**: Include XML files for major vendors
3. **Extensible registry**: Allow loading additional vendor dictionaries
4. **Configurable handling**: Strip/preserve/callback options
5. **Validation**: Warn on missing creators, unknown private tags

### References
- [PS3.5 Section 7.8 - Private Data Elements](https://dicom.nema.org/dicom/2013/output/chtml/part05/sect_7.8.html)
- [fo-dicom Private Tags Documentation](https://fo-dicom.github.io/stable/v5/usage/add_private_tags.html)
- [DCMTK Private Dictionary](https://github.com/InsightSoftwareConsortium/DCMTK/blob/master/dcmdata/data/private.dic)

---

## 6. Pixel Data

### Overview

Pixel Data (7FE0,0010) is the most complex DICOM element. It can be uncompressed (native) or compressed (encapsulated), and handling differs significantly between the two.

### Native (Uncompressed) Format

**Structure**: Contiguous byte array containing all frames sequentially.

**Size calculation**:
```
Size = Rows * Columns * (BitsAllocated/8) * SamplesPerPixel * NumberOfFrames
```

**VR Rules**:
- Implicit VR Little Endian: Always OW
- Explicit VR, BitsAllocated <= 8: May be OB or OW
- Explicit VR, BitsAllocated > 8: OW

**Byte ordering**:
- OW: Affected by transfer syntax endianness
- OB: Byte string, unaffected by endianness

### Encapsulated (Compressed) Format

**Structure**: Sequence of fragments using Item tags.

```
Pixel Data (7FE0,0010) VR=OB, Length=Undefined
├── Basic Offset Table Item (FFFE,E000)
│   └── [0 or N*4 bytes of 32-bit offsets]
├── Fragment 1 (FFFE,E000)
│   └── [compressed frame data]
├── Fragment 2 (FFFE,E000)
│   └── [compressed frame data]
├── ... more fragments ...
└── Sequence Delimiter (FFFE,E0DD)
```

### Basic Offset Table

The first item in encapsulated pixel data is the Basic Offset Table:
- **Empty (length=0)**: Valid, offsets must be computed by scanning
- **Populated**: Contains N 32-bit unsigned integers (one per frame)
- **Offset meaning**: Byte offset from end of BOT to first byte of frame's first fragment

### Extended Offset Table

For very large datasets (> 4GB), extended attributes provide 64-bit offsets:
- (7FE0,0001) Extended Offset Table - byte offsets
- (7FE0,0002) Extended Offset Table Lengths - byte lengths per frame

### Frame/Fragment Relationship

**Key rule**: A fragment may contain at most one frame's data, but a frame may span multiple fragments.

**Single fragment per frame**: Number of fragments equals number of frames (common case)

**Multiple fragments per frame**: Used for streaming/buffering during compression
- No length per fragment exceeds implementation limits
- Must reassemble fragments before decoding

**Detection**: Compare fragment count vs frame count. If fragments > frames, reassembly is needed.

### Multi-Frame Considerations

1. **Frame seeking**: With populated BOT, can seek to specific frame
2. **Without BOT**: Must decode all preceding frames to find Nth frame
3. **Per-frame compression**: Each frame compressed independently (usually)
4. **Video syntaxes**: Entire video as single logical "frame" (MPEG, HEVC)

### Edge Cases

1. **Empty BOT with multi-frame**: Common, especially from older systems
2. **Odd-length fragments**: Must be padded to even bytes
3. **Empty frames**: Valid in some IODs (e.g., SC with 0x0 frames)
4. **Truncated pixel data**: File may be incomplete
5. **Fragmented single frame**: One frame split across multiple items

### Streaming Considerations

For streaming pixel data (network or large files):
1. Read BOT first to determine frame offsets
2. If BOT empty, must read all fragments sequentially
3. Stream-to-disk for large studies (avoid memory pressure)
4. Consider lazy loading (keep file handle, read on access)

### SharpDicom Recommendations

1. **PixelDataHandling enum**: LoadInMemory, LazyLoad, Skip, Callback
2. **DicomFragmentSequence class**: Track offset table and fragments
3. **Frame accessor**: GetFrame(int index) with lazy decode
4. **Streaming support**: IAsyncEnumerable<Frame> for large studies
5. **Codec abstraction**: IPixelDataCodec interface for decompression

### References
- [PS3.5 Section 8 - Encoding of Pixel Data](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/chapter_8.html)
- [PS3.5 Section A.4 - Encapsulated Transfer Syntaxes](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.4.html)
- [Extended Offset Table](https://dicom.innolitics.com/ciods/cr-image/image-pixel/7fe00002)

---

## Summary Recommendations

### Priority Order for Implementation

1. **Transfer Syntaxes**: Core reading/writing capability
   - Implicit VR LE, Explicit VR LE, Explicit VR BE (read only)
   - RLE codec (no external dependencies)
   - Codec plugin system for JPEG/JPEG2000/etc.

2. **Sequences**: Required for any useful DICOM parsing
   - Iterative parser with configurable depth limit
   - Defined and undefined length support

3. **Pixel Data**: Primary use case for imaging data
   - Native and encapsulated format support
   - Streaming/lazy loading options
   - BOT parsing and generation

4. **Character Sets**: Required for metadata correctness
   - UTF-8 fast path for modern data
   - ISO 2022 state machine for legacy
   - Graceful error handling

5. **Multi-VR Tags**: Edge cases in real data
   - Context-aware VR resolver
   - Deferred parsing for streaming

6. **Private Tags**: Vendor-specific data preservation
   - Creator tracking
   - Optional vendor dictionaries
   - Strip/preserve options

### General Principles

1. **Streaming first**: Design for large files from the start
2. **Zero-copy where possible**: UTF-8 strings, Memory<byte> slicing
3. **Configurable strictness**: Strict/lenient/permissive presets
4. **Extensible**: Plugin points for codecs, vendor dictionaries
5. **Testable**: Round-trip tests, reference file validation
