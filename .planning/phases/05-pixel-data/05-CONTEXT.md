---
phase: 05-pixel-data
created: 2026-01-27
status: ready-for-research
---

# Phase 5: Pixel Data & Lazy Loading — Context

## Phase Goal

Handle large elements efficiently with configurable loading strategies.

## Core Decisions

### Loading Strategies

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Default behavior | Configurable: skip/lazy(seekable)/memory | Flexibility for different use cases |
| Stream disposed access | Throw exception | Fail-fast prevents silent data loss |
| Callback context | Image dimensions (rows, cols, bits, frames) | Enables informed loading decisions |
| Size threshold | Via callback, no built-in option | User determines what's "large" |
| Post-parse loading | Auto on access + explicit LoadPixelDataAsync | Convenience + control |
| Decompression timing | Never - store raw bytes | Phase 9+ handles codecs |
| Skip marker | Element with VR/length, empty RawValue | Metadata preserved for later loading |
| ToOwned behavior | Force load pixel data | Owned copy must be self-contained |
| Async loading | Yes, with CancellationToken | Required for large data |
| Progress reporting | IProgress<long> for bytes read | User feedback on large loads |

### Element Type Design

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Type hierarchy | DicomPixelDataElement special type | Distinct from regular binary elements |
| Access API | Property on DicomFile/DicomDataset | Consistent with other elements |
| Return type | Wrapper with RawBytes + metadata | Frame-aware access |
| Stream copy | CopyToAsync(Stream) method | Direct streaming without buffering |
| Span access | GetFrameSpan(int frameIndex) | Zero-copy frame access |
| Typed accessors | Generic GetFrame<T>(int) | Type-safe pixel access |
| Thread safety | Yes, synchronization on load | Multi-threaded access safe |
| Disposal | Owned by DicomFile | Consistent lifetime management |

### Multi-Frame Support

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Partial loading | Frame range support | Load frames 10-20 without loading all |
| Per-frame metadata | Support functional groups | Enhanced multi-frame IOD |
| Frame indexing | Zero-based | C# convention |

### Encapsulated Pixel Data

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Fragment storage | DicomFragmentSequence | Distinct from regular sequences |
| Basic Offset Table | Parse and expose | Required for random frame access |
| Extended Offset Table | Support in v1 | Modern DICOM feature |
| Pixel VR determination | Use BitsAllocated from dataset | Context-dependent VR resolution |

### Stream Handling

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Non-seekable streams | Buffer to temp file | Enable lazy loading |
| Temp file config | DicomReaderOptions.TempDirectory | User controls location |
| Temp cleanup | On Dispose | No orphaned files |
| Memory-mapped files | Optional via options | Large file optimization |

### Data Format Support

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Big endian | Auto byte-swap to native | Transparent to caller |
| Planar configuration | Configurable (interleaved/planar) | Support both formats |
| Float/Double pixels | Full support (OD, OF VRs) | Scientific imaging |
| Signed/unsigned | Typed access based on PixelRepresentation | Correct value interpretation |
| High bit validation | Validate matches BitsStored | Catch common errors |
| Bit alignment | Auto-handle odd BitsAllocated | Robust parsing |

### Metadata Access

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Dimension properties | Rows, Columns, BitsAllocated, etc. | Convenient access |
| Geometry metadata | PixelSpacing, SliceThickness | Spatial interpretation |
| Orientation | ImageOrientationPatient | 3D positioning |

### Additional Pixel Data Types (v1)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Icon image (0088,0200) | Include in v1 | Common thumbnail element |
| Overlay data (60xx,3000) | Include in v1 | Legacy but still used |
| Waveform data | Include in v1 | ECG/EEG support |
| Spectroscopy data | Include in v1 | MR spectroscopy |

## Deferred to Future Phases

The following features were discussed but are outside v1.0 scope:

### v2+ Features (Separate Phases)

| Feature | Reason for Deferral |
|---------|---------------------|
| Codec integration (JPEG, J2K, etc.) | Phase 9 handles RLE; others need separate packages |
| GPU acceleration (CUDA/OpenCL) | Significant complexity, separate package |
| SkiaSharp integration | Separate SharpDicom.Imaging package |
| 3D rendering (MIP, MPR, VRT) | Separate SharpDicom.Visualization package |
| ML tensor export | Separate SharpDicom.ML package |
| Image processing (rotate, flip, crop) | Separate package |
| Histogram/statistics | Could be v1 utility, but not core |
| Windowing helpers | Separate imaging package |
| Color conversion | Separate imaging package |
| Palette color expansion | Separate imaging package |
| Modality-specific features | Separate modality packages |
| Video codecs | Separate codec package |
| DICOMweb integration | Separate networking package |
| Encryption/watermarking | Separate security package |
| Clinical trial support | Separate compliance package |

## Success Criteria

From ROADMAP.md:
- [ ] Load uncompressed pixel data
- [ ] Parse encapsulated fragments
- [ ] Lazy loading skips pixel data
- [ ] Multi-frame datasets work

## Dependencies

**Requires:**
- Phase 3: Sequence parsing for fragment sequences
- Phase 3: Parent property for context inheritance
- Phase 4: Encoding for string metadata

**Provides:**
- Pixel data infrastructure for Phase 9 (RLE Codec)
- Foundation for future imaging packages

## Implementation Notes

### DicomPixelDataElement

```csharp
public sealed class DicomPixelDataElement : IDicomElement
{
    public DicomTag Tag => DicomTag.PixelData;
    public DicomVR VR { get; }  // OB or OW based on BitsAllocated

    // Metadata
    public ushort Rows { get; }
    public ushort Columns { get; }
    public ushort BitsAllocated { get; }
    public int NumberOfFrames { get; }
    public bool IsEncapsulated { get; }

    // Access
    public ReadOnlyMemory<byte> RawBytes { get; }
    public ReadOnlySpan<byte> GetFrameSpan(int frameIndex);
    public T[] GetFrame<T>(int frameIndex) where T : unmanaged;

    // Streaming
    public ValueTask CopyToAsync(Stream destination, CancellationToken ct = default);
    public ValueTask<ReadOnlyMemory<byte>> LoadAsync(CancellationToken ct = default);
}
```

### DicomFragmentSequence

```csharp
public sealed class DicomFragmentSequence
{
    public ReadOnlyMemory<byte> OffsetTable { get; }
    public ReadOnlyMemory<byte> ExtendedOffsetTable { get; }
    public IReadOnlyList<ReadOnlyMemory<byte>> Fragments { get; }

    public int FragmentCount { get; }
    public long TotalSize { get; }
}
```

### PixelDataHandling Enum

```csharp
public enum PixelDataHandling
{
    /// <summary>Load pixel data into memory immediately.</summary>
    LoadInMemory,

    /// <summary>Keep stream reference, load on first access (requires seekable stream).</summary>
    LazyLoad,

    /// <summary>Skip pixel data entirely (metadata-only use case).</summary>
    Skip,

    /// <summary>Let callback decide per-instance.</summary>
    Callback
}
```

---

*Created: 2026-01-27*
*Status: Ready for research and planning*
