# Phase 12: Pure C# Codecs - Research

**Researched:** 2026-01-29
**Domain:** JPEG/JPEG 2000 Image Compression Codecs
**Confidence:** MEDIUM (verified standards, limited pure C# precedent for all codecs)

## Summary

Phase 12 implements JPEG Baseline, JPEG Lossless (Process 14 SV1), and JPEG 2000 codecs in pure C# with no native dependencies. The research confirms this is technically feasible but complex, requiring deep understanding of multiple compression algorithms.

**JPEG Baseline** (8-bit lossy) uses DCT-based compression with Huffman coding. Pure C# implementations exist (JpegLibrary) that demonstrate this is achievable with good performance. **JPEG Lossless** uses DPCM predictive coding - simpler than DCT but less commonly implemented in pure C#. **JPEG 2000** uses wavelet transforms with EBCOT arithmetic coding - the most complex of the three. CoreJ2K provides a reference implementation but may need adaptation for AOT compatibility.

The existing RLE codec in SharpDicom provides an excellent template for codec structure, SIMD usage patterns, and integration with `IPixelDataCodec` and `CodecRegistry`. The error handling architecture defined in CONTEXT.md is comprehensive and should be followed precisely.

**Primary recommendation:** Implement codecs in priority order (JPEG Baseline -> JPEG Lossless -> JPEG 2000 Lossless -> JPEG 2000 Lossy), using established algorithm patterns from existing libraries while ensuring AOT compatibility through explicit registration and no reflection.

## Standard Stack

### Core Implementation Strategy

| Component | Approach | Why Standard |
|-----------|----------|--------------|
| JPEG Baseline | From-scratch implementation | Control over medical imaging requirements, DCT well-understood |
| JPEG Lossless | From-scratch implementation | Simpler than DCT, must handle DICOM-specific predictors |
| JPEG 2000 | Adapt/port CoreJ2K patterns | Complex algorithm (DWT+EBCOT), reference implementation exists |

### Supporting Infrastructure

| Component | Purpose | Existing in Codebase |
|-----------|---------|---------------------|
| `IPixelDataCodec` | Codec interface | Yes - Phase 9 |
| `CodecRegistry` | Transfer syntax lookup | Yes - Phase 9 |
| `DicomFragmentSequence` | Encapsulated pixel data | Yes - Phase 5 |
| `DicomCodecException` | Error handling | Yes - Phase 9 |
| `ArrayPool<byte>` | Buffer pooling | Yes - used in RLE codec |

### Transfer Syntaxes to Support

| Transfer Syntax | UID | Compression Type | Priority |
|-----------------|-----|------------------|----------|
| JPEG Baseline (Process 1) | 1.2.840.10008.1.2.4.50 | JPEGBaseline | Must |
| JPEG Lossless P14 SV1 | 1.2.840.10008.1.2.4.70 | JPEGLossless | Must |
| JPEG 2000 Lossless | 1.2.840.10008.1.2.4.90 | JPEG2000Lossless | Must |
| JPEG 2000 Lossy | 1.2.840.10008.1.2.4.91 | JPEG2000Lossy | Should |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| From-scratch JPEG | JpegLibrary port | JpegLibrary is MIT-licensed but would require adaptation for DICOM needs |
| From-scratch J2K | CoreJ2K port | CoreJ2K is BSD-3, needs AOT audit, may be worth adapting |
| Native wrappers | libjpeg-turbo, OpenJPEG | Deferred to Phase 13 - breaks pure C# requirement |

## Architecture Patterns

### Recommended Codec Structure

```
src/SharpDicom/Codecs/
├── Jpeg/
│   ├── JpegBaselineCodec.cs       # IPixelDataCodec implementation
│   ├── JpegBaselineDecoder.cs     # Static decode methods (like RleDecoder)
│   ├── JpegBaselineEncoder.cs     # Static encode methods (like RleEncoder)
│   ├── JpegCodecOptions.cs        # Quality settings, subsampling options
│   ├── HuffmanTable.cs            # Huffman coding structures
│   ├── DctTransform.cs            # 8x8 DCT implementation with SIMD
│   ├── QuantizationTable.cs       # Quantization matrices
│   └── JpegMarkers.cs             # SOI, SOF, DHT, DQT, SOS markers
├── JpegLossless/
│   ├── JpegLosslessCodec.cs       # IPixelDataCodec implementation
│   ├── JpegLosslessDecoder.cs     # DPCM predictor decode
│   ├── JpegLosslessEncoder.cs     # DPCM predictor encode
│   ├── Predictor.cs               # Selection Value predictors 1-7
│   └── LosslessHuffman.cs         # Huffman for prediction residuals
└── Jpeg2000/
    ├── Jpeg2000LosslessCodec.cs   # IPixelDataCodec for TS 1.2.840.10008.1.2.4.90
    ├── Jpeg2000LossyCodec.cs      # IPixelDataCodec for TS 1.2.840.10008.1.2.4.91
    ├── J2kDecoder.cs              # Main decode entry point
    ├── J2kEncoder.cs              # Main encode entry point
    ├── Wavelet/
    │   ├── DwtTransform.cs        # Discrete wavelet transform
    │   ├── Dwt53.cs               # Reversible 5/3 lifting
    │   └── Dwt97.cs               # Irreversible 9/7 lifting
    ├── Tier1/
    │   ├── EbcotEncoder.cs        # Bitplane coding passes
    │   ├── EbcotDecoder.cs        # Bitplane decoding
    │   └── MqCoder.cs             # Arithmetic coder
    ├── Tier2/
    │   ├── PacketEncoder.cs       # Packet/layer formation
    │   └── PacketDecoder.cs       # Packet parsing
    ├── ColorTransform/
    │   ├── IctTransform.cs        # Irreversible color transform
    │   └── RctTransform.cs        # Reversible color transform
    └── J2kCodestream.cs           # Marker segment parsing
```

### Pattern 1: Codec Registration (AOT-Compatible)

**What:** Explicit registration without reflection for AOT compatibility
**When to use:** All codec registrations
**Example:**
```csharp
// In application startup or module initializer
public static class CodecInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Explicit registration - no reflection
        CodecRegistry.Register(new JpegBaselineCodec());
        CodecRegistry.Register(new JpegLosslessCodec());
        CodecRegistry.Register(new Jpeg2000LosslessCodec());
        CodecRegistry.Register(new Jpeg2000LossyCodec());
    }
}
```

### Pattern 2: SIMD-Accelerated DCT (from existing RleEncoder)

**What:** Use Vector128/256 for DCT transform acceleration
**When to use:** Hot paths in encoding/decoding
**Example:**
```csharp
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

private static void ForwardDct8x8Simd(Span<float> block)
{
    if (Avx2.IsSupported)
    {
        // Use 256-bit vectors for parallel butterfly operations
        var row0 = Vector256.Create(block.Slice(0, 8));
        // ... DCT butterfly operations
    }
    else if (Vector128.IsHardwareAccelerated)
    {
        // Fallback to 128-bit vectors
    }
    else
    {
        ForwardDct8x8Scalar(block);
    }
}
#endif
```

### Pattern 3: Decode Result Pattern (from existing RleDecoder)

**What:** Return DecodeResult with diagnostic information
**When to use:** All decode operations
**Example:**
```csharp
public static DecodeResult DecodeFrame(
    ReadOnlySpan<byte> compressedFrame,
    PixelDataInfo info,
    Span<byte> output,
    int frameIndex)
{
    // 1. Parse JPEG markers
    if (!TryParseMarkers(compressedFrame, out var frameInfo, out var error))
    {
        return DecodeResult.Fail(frameIndex, 0, error);
    }

    // 2. Validate against PixelDataInfo
    if (frameInfo.Width != info.Columns || frameInfo.Height != info.Rows)
    {
        return DecodeResult.Fail(frameIndex, 0,
            "JPEG dimensions mismatch",
            $"{info.Columns}x{info.Rows}",
            $"{frameInfo.Width}x{frameInfo.Height}");
    }

    // 3. Decode and return success
    int bytesWritten = DecodeScans(compressedFrame, frameInfo, output);
    return DecodeResult.Ok(bytesWritten);
}
```

### Anti-Patterns to Avoid

- **Reflection for codec discovery:** Use explicit `CodecRegistry.Register<TCodec>()` calls, never `RegisterFromAssembly()`
- **Dynamic code generation:** No Expression.Compile() or Reflection.Emit for hot paths
- **Unsafe string-based type resolution:** No `Type.GetType(string)` for codec lookup
- **Unbounded recursion:** Use iterative algorithms for stack safety (per CONTEXT.md)
- **Platform-specific P/Invoke:** This phase is pure C# only; native wrappers are Phase 13

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Huffman table generation | Custom tree builder | Standard JPEG default tables | ITU-T.81 defines optimal tables |
| DCT/IDCT transform | Naive O(n^4) matrix | Fast DCT algorithms (AAN, Loeffler) | Well-optimized algorithms exist |
| Zigzag scan order | Compute at runtime | Pre-computed lookup table | 64-element constant array |
| MQ arithmetic coder | From-spec implementation | Adapt existing C# implementation | Complex state machine |
| Wavelet lifting | Direct convolution | Lifting scheme implementation | 2x faster, in-place operation |
| Color space conversion | Custom matrix math | CCIR 601/ITU-R BT.709 formulas | Standardized coefficients |

**Key insight:** Image compression algorithms have decades of optimization research. Use proven algorithms and optimize for C#/.NET rather than inventing new approaches.

## Common Pitfalls

### Pitfall 1: Photometric Interpretation Mismatch

**What goes wrong:** Encoded pixel data doesn't match PhotometricInterpretation tag
**Why it happens:** JPEG naturally produces YCbCr; developers forget to update DICOM metadata
**How to avoid:**
- Always check APP0 marker for color space hints
- Update PhotometricInterpretation after encode (CONTEXT.md requirement)
- Implement bidirectional conversion (RGB <-> YCbCr)
**Warning signs:** Colors appear inverted or shifted after decode

### Pitfall 2: Bit Depth Truncation

**What goes wrong:** 12-bit or 16-bit images lose precision
**Why it happens:** JPEG Baseline only supports 8-bit; incorrect codec selection
**How to avoid:**
- JPEG Baseline: Only 8-bit samples
- JPEG Lossless: 2-16 bit samples
- JPEG 2000: Up to 38 bits per component
- Validate BitsAllocated/BitsStored against codec capabilities
**Warning signs:** Medical images appear banded or posterized

### Pitfall 3: Chroma Subsampling for Medical Images

**What goes wrong:** Diagnostic details lost in color images
**Why it happens:** Default 4:2:0 subsampling aggressive for medical use
**How to avoid:**
- Default to 4:4:4 (no subsampling) for medical images
- Document subsampling as explicit option
- Per CONTEXT.md: "medical imaging defaults (conservative lossy)"
**Warning signs:** Fine color details (tissue boundaries) appear blurred

### Pitfall 4: Endianness in Multi-Byte Samples

**What goes wrong:** 16-bit pixel values corrupted
**Why it happens:** JPEG uses big-endian markers, DICOM uses little-endian
**How to avoid:**
- JPEG frame data: Big-endian internally
- DICOM output: Little-endian after decode
- Use `BinaryPrimitives.ReadInt16BigEndian()` for JPEG parsing
**Warning signs:** Alternating bright/dark pixels in 16-bit images

### Pitfall 5: JPEG 2000 Tile Boundary Artifacts

**What goes wrong:** Visible seams at tile boundaries in large images
**Why it happens:** Independent tile encoding without overlap
**How to avoid:**
- Use single tile for smaller images (<4096x4096)
- Apply post-processing deblocking filter if needed
- Document tile size implications
**Warning signs:** Grid pattern visible in decoded images

### Pitfall 6: AOT/Trimming Compatibility Breaking

**What goes wrong:** Codec registration fails in trimmed/AOT applications
**Why it happens:** Reflection-based discovery trimmed away
**How to avoid:**
- Never use `RegisterFromAssembly()` in AOT scenarios
- Mark all codec types with `[DynamicallyAccessedMembers]` if needed
- Test with `<IsAotCompatible>true</IsAotCompatible>`
**Warning signs:** "No codec registered for transfer syntax" at runtime

## Code Examples

### JPEG Baseline DCT Transform (Scalar Reference)

```csharp
// Forward DCT using AAN algorithm (Arai, Agui, Nakajima)
// Source: ITU-T.81 Annex A, optimized per Loeffler et al.
private static void ForwardDct8x8(Span<float> block)
{
    // Constants for AAN DCT
    const float c1 = 0.980785280f; // cos(1*pi/16)
    const float c2 = 0.923879533f; // cos(2*pi/16)
    const float c3 = 0.831469612f; // cos(3*pi/16)
    const float c5 = 0.555570233f; // cos(5*pi/16)
    const float c6 = 0.382683432f; // cos(6*pi/16)
    const float c7 = 0.195090322f; // cos(7*pi/16)

    // Process rows then columns (separable 2D DCT)
    for (int i = 0; i < 8; i++)
    {
        var row = block.Slice(i * 8, 8);
        Dct1D(row, c1, c2, c3, c5, c6, c7);
    }

    for (int j = 0; j < 8; j++)
    {
        Span<float> col = stackalloc float[8];
        for (int i = 0; i < 8; i++) col[i] = block[i * 8 + j];
        Dct1D(col, c1, c2, c3, c5, c6, c7);
        for (int i = 0; i < 8; i++) block[i * 8 + j] = col[i];
    }
}
```

### JPEG Lossless Predictor Implementation

```csharp
// JPEG Lossless predictors per ITU-T.81 Table H.1
// A=left, B=above, C=above-left
public static int Predict(int selectionValue, int a, int b, int c)
{
    return selectionValue switch
    {
        0 => 0,                    // No prediction (hierarchical only)
        1 => a,                    // P_a (horizontal)
        2 => b,                    // P_b (vertical)
        3 => c,                    // P_c (diagonal)
        4 => a + b - c,           // P_a + P_b - P_c
        5 => a + ((b - c) >> 1),  // P_a + (P_b - P_c) / 2
        6 => b + ((a - c) >> 1),  // P_b + (P_a - P_c) / 2
        7 => (a + b) >> 1,        // (P_a + P_b) / 2
        _ => throw new ArgumentOutOfRangeException(nameof(selectionValue))
    };
}

// DICOM default: Selection Value 1 (horizontal prediction)
// Transfer Syntax 1.2.840.10008.1.2.4.70 MUST use SV1
```

### JPEG 2000 Reversible Color Transform (RCT)

```csharp
// RCT forward transform (RGB to YUV-like)
// Lossless and reversible with integer arithmetic
public static void ForwardRct(Span<int> r, Span<int> g, Span<int> b)
{
    for (int i = 0; i < r.Length; i++)
    {
        int y  = (r[i] + 2 * g[i] + b[i]) >> 2;  // Floor division
        int cb = b[i] - g[i];
        int cr = r[i] - g[i];

        r[i] = y;   // Reuse buffers: Y in R
        g[i] = cb;  // Cb in G
        b[i] = cr;  // Cr in B
    }
}

// RCT inverse transform (back to RGB)
public static void InverseRct(Span<int> y, Span<int> cb, Span<int> cr)
{
    for (int i = 0; i < y.Length; i++)
    {
        int g = y[i] - ((cb[i] + cr[i]) >> 2);
        int r = cr[i] + g;
        int b = cb[i] + g;

        y[i]  = r;
        cb[i] = g;
        cr[i] = b;
    }
}
```

### YCbCr to RGB Conversion (JPEG Baseline)

```csharp
// CCIR 601 / ITU-R BT.601 conversion for 8-bit samples
// Source: DICOM PS3.3 C.7.6.3.1.2
public static void YCbCrToRgb(
    ReadOnlySpan<byte> y,
    ReadOnlySpan<byte> cb,
    ReadOnlySpan<byte> cr,
    Span<byte> rgb)
{
    for (int i = 0; i < y.Length; i++)
    {
        // Remove 128 offset from Cb/Cr
        int yVal  = y[i];
        int cbVal = cb[i] - 128;
        int crVal = cr[i] - 128;

        // ITU-R BT.601 matrix
        int r = yVal + (int)(1.402f * crVal);
        int g = yVal - (int)(0.344136f * cbVal) - (int)(0.714136f * crVal);
        int b = yVal + (int)(1.772f * cbVal);

        // Clamp to [0, 255]
        rgb[i * 3 + 0] = (byte)Math.Clamp(r, 0, 255);
        rgb[i * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
        rgb[i * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| libjpeg (C library) | Pure managed implementations | 2018+ | AOT/trimming support |
| OpenJPEG P/Invoke | CoreJ2K pure C# | 2020+ | Cross-platform without native deps |
| System.Drawing JPEG | Platform-agnostic codecs | 2019+ | Linux/macOS support |
| Scalar DCT | SIMD-accelerated DCT | .NET Core 3.0+ | 4-8x speedup on AVX2 |
| Reflection-based codecs | Explicit registration | .NET 8+ AOT | Trim/AOT compatibility |

**Deprecated/outdated:**
- `System.Drawing.Common` for JPEG: Windows-only, deprecated on non-Windows
- `BitMiracle.LibJpeg.NET`: Still maintained but older API design
- Assembly scanning for codecs: Incompatible with trimming/AOT

## Open Questions

1. **JpegLibrary License Compatibility**
   - What we know: JpegLibrary is MIT-licensed
   - What's unclear: Whether to port/adapt vs implement from spec
   - Recommendation: Implement from ITU-T.81 spec to avoid any licensing concerns with GPL codebase

2. **CoreJ2K AOT Compatibility**
   - What we know: CoreJ2K is BSD-3, pure C#, actively maintained
   - What's unclear: Whether it uses reflection patterns incompatible with AOT
   - Recommendation: Audit CoreJ2K for AOT compatibility before deciding to adapt vs reimplement

3. **JPEG 2000 Complexity vs Timeline**
   - What we know: EBCOT + MQ-coder is significantly more complex than JPEG
   - What's unclear: Realistic implementation timeline
   - Recommendation: Consider CoreJ2K adaptation as first approach; from-scratch only if required

4. **Performance Targets**
   - What we know: Context.md mentions "2-3x faster than fo-dicom" goal
   - What's unclear: Whether pure C# can achieve this for J2K without native acceleration
   - Recommendation: Benchmark early, accept that native codecs (Phase 13) may be needed for performance-critical paths

## Sources

### Primary (HIGH confidence)

- ITU-T T.81 (09/92) - JPEG standard specification
- ITU-T T.800 (08/2002) - JPEG 2000 Part 1 core coding system
- DICOM PS3.5 - Data Structures and Encoding (Section 8.2, Section 10)
- DICOM PS3.5 Annex A - Transfer Syntax Specifications
- SharpDicom existing code: `IPixelDataCodec.cs`, `CodecRegistry.cs`, `RleCodec.cs`, `RleEncoder.cs`, `RleDecoder.cs`

### Secondary (MEDIUM confidence)

- [JpegLibrary GitHub](https://github.com/yigolden/JpegLibrary) - Pure C# JPEG implementation reference
- [CoreJ2K GitHub](https://github.com/cinderblocks/CoreJ2K) - Pure C# JPEG 2000 implementation
- [Microsoft AOT Compatibility Guide](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/) - AOT library requirements
- [DICOM Test Files (WG-04)](ftp://medical.nema.org/medical/dicom/DataSets/WG04) - Reference test images

### Tertiary (LOW confidence, needs validation)

- WebSearch results for DCT/DWT optimization techniques
- Community implementations of Huffman coding
- General SIMD optimization patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Based on DICOM spec, existing codebase patterns, verified libraries
- Architecture: HIGH - Follows established RLE codec pattern from Phase 9
- Pitfalls: MEDIUM - Based on general image codec knowledge and DICOM experience
- Implementation details: MEDIUM - Algorithm details verified but C# specifics need validation

**Research date:** 2026-01-29
**Valid until:** 2026-03-01 (30 days - stable domain, but verify AOT recommendations against .NET updates)
