# Phase 21: Complete Managed Codecs - Research

**Researched:** 2026-02-02
**Domain:** Medical image compression - JPEG-LS and HTJ2K codec implementation
**Confidence:** MEDIUM

## Summary

This phase completes the pure C# implementations of JPEG-LS (ITU-T T.87) and HTJ2K (ISO/IEC 15444-15) codecs that were stubbed in v2.0. The existing infrastructure provides codec registration, fragment handling, and basic encoding/decoding scaffolds. Implementation requires completing the core compression algorithms: context-based prediction and Golomb-Rice coding for JPEG-LS, and HT block coding for HTJ2K.

The standard approach is:
- **JPEG-LS**: Context-based predictive coding with quantized gradients selecting from 365 contexts, median edge detection (MED) predictor with 8 modes, Golomb-Rice entropy coding with adaptive k parameter
- **HTJ2K**: Standard JPEG 2000 DWT and tier-2 packet encoding with HT (High Throughput) block coder replacing EBCOT, offering 10x performance improvement with minimal compression loss

Reference implementations (CharLS for JPEG-LS, OpenJPH for HTJ2K) demonstrate C++ implementations achieving 2-3x performance over baseline. Pure C# with SIMD can target 10x slower than native (per phase requirements), which is achievable given the existing infrastructure already has DWT, EBCOT, and packet encoding components.

**Primary recommendation:** Complete JPEG-LS encoder/decoder first (simpler algorithm, fewer components), then HTJ2K by replacing EBCOT with HT block coder. Use CharLS and OpenJPH as algorithmic references but not direct ports. Leverage existing Vector128/256 types for SIMD optimization in hot paths.

## Standard Stack

The established libraries/tools for JPEG-LS and HTJ2K implementation:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Numerics.Vectors | .NET BCL | SIMD operations (Vector128/256) | Built-in .NET SIMD abstraction, cross-platform |
| System.Buffers | .NET BCL | Memory pooling and ArrayBufferWriter | Zero-allocation patterns, standard .NET approach |
| Span&lt;T&gt; | .NET BCL | Zero-copy memory operations | Modern .NET foundation for codec work |

### Reference Implementations (for algorithm understanding, not direct use)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| CharLS | 2.x (C++) | JPEG-LS reference | Understanding algorithm details, test vectors |
| OpenJPH | Latest (C++) | HTJ2K reference | HT block coder algorithm, conformance testing |
| OpenJPEG | 2.5+ (C) | JPEG 2000 reference | Baseline J2K for comparison |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Pure C# | P/Invoke to CharLS/OpenJPH | Native performance but deployment complexity |
| Vector128/256 | Vector&lt;T&gt; | Simpler but less control over instruction selection |
| Manual bit-packing | BinaryPrimitives only | Cleaner code but potentially slower entropy coding |

**Installation:**
```bash
# No external packages needed - all BCL
# For testing against reference implementations:
# - Build CharLS from source (C++ with CMake)
# - Build OpenJPH from source (C++ with CMake)
```

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/Codecs/
├── JpegLs/
│   ├── JpegLsCodec.cs           # Existing codec registration
│   ├── JpegLsEncoder.cs         # COMPLETE: Full algorithm
│   ├── JpegLsDecoder.cs         # COMPLETE: Full algorithm
│   ├── JpegLsPredictor.cs       # NEW: 8 predictor modes
│   ├── JpegLsContext.cs         # NEW: 365 context state machines
│   └── GolombRiceCoder.cs       # NEW: Entropy coding
├── Htj2k/
│   ├── Htj2kCodec.cs            # Existing codec registration
│   ├── HtBlockCoder.cs          # NEW: HT sets encoder/decoder
│   └── CapMarker.cs             # Existing CAP marker injection
└── Jpeg2000/
    ├── Tier1/
    │   ├── EbcotEncoder.cs      # Existing - keep for baseline J2K
    │   ├── EbcotDecoder.cs      # Existing - keep for baseline J2K
    │   └── HtCoder.cs           # NEW: HT replacement for EBCOT
    ├── Tier2/                   # Existing - reuse for HTJ2K
    └── Wavelet/                 # Existing - reuse for HTJ2K
```

### Pattern 1: Context-Based Prediction (JPEG-LS)
**What:** Use neighboring pixel gradients to select one of 365 statistical models for entropy coding
**When to use:** Core JPEG-LS encoding/decoding loop

**Example:**
```csharp
// Simplified pattern from existing JpegLsEncoder.cs
private static int EncodePixel(Span<byte> output, int sample, int x, int y, Context[] contexts)
{
    // Get 4 neighbors: a (left), b (above), c (above-left), d (above-right)
    int a = GetSample(x - 1, y);
    int b = GetSample(x, y - 1);
    int c = GetSample(x - 1, y - 1);
    int d = GetSample(x + 1, y - 1);

    // Compute gradients
    int g1 = d - b;
    int g2 = b - c;
    int g3 = c - a;

    // Quantize to -4..4 range (9 values each) → 9×9×9 = 729 combinations
    // After sign normalization: 365 unique contexts
    int q1 = QuantizeGradient(g1, near);
    int q2 = QuantizeGradient(g2, near);
    int q3 = QuantizeGradient(g3, near);

    // Map to context index
    int contextIndex = ComputeContextIndex(q1, q2, q3);

    // Select predictor based on edge detection
    int predicted = MedianEdgeDetection(a, b, c);

    // Compute error and encode with context-specific Golomb-Rice
    ref var ctx = ref contexts[contextIndex];
    int error = sample - predicted;
    int k = ctx.ComputeK(); // Adaptive parameter based on context statistics

    return EncodeGolombRice(error, k);
}
```

### Pattern 2: SIMD Wavelet Transform (HTJ2K reuses existing)
**What:** Leverage Vector128&lt;T&gt; for parallel wavelet lifting steps
**When to use:** DWT forward/inverse operations on large images

**Example from existing Dwt53.cs pattern:**
```csharp
// This pattern already exists in codebase - HTJ2K reuses it
private static void LiftingStep(Span<int> signal, int stride)
{
    if (Vector128.IsHardwareAccelerated && signal.Length >= Vector128<int>.Count)
    {
        // Process 4 or 8 values at once
        for (int i = 0; i < signal.Length - Vector128<int>.Count; i += Vector128<int>.Count)
        {
            var v = Vector128.LoadUnsafe(ref signal[i]);
            var result = PerformLift(v);
            result.StoreUnsafe(ref signal[i]);
        }
    }
    // Scalar fallback for remainder
}
```

### Pattern 3: HT Block Coding (HTJ2K new component)
**What:** Replace EBCOT with HT sets for 10x performance
**When to use:** HTJ2K encoding/decoding code-blocks

**Example structure (to be implemented):**
```csharp
// NEW class needed
internal static class HtCoder
{
    // HT encoding uses fixed VLC tables instead of adaptive MQ coder
    public static CodeBlockData EncodeHtBlock(int[] coefficients, int width, int height)
    {
        // HT uses predetermined significance propagation patterns (HT Sets)
        // Much faster than EBCOT's context-adaptive arithmetic coding

        // 1. Magnitude refinement using VLC
        // 2. Sign coding with context
        // 3. Cleanup pass with HT-specific optimizations

        return new CodeBlockData(compressed, passes, zeroBitPlanes);
    }
}
```

### Anti-Patterns to Avoid
- **Don't allocate per-pixel:** Context state should be reused across entire image
- **Don't use generic Dictionary for contexts:** Fixed array of 365 elements is faster
- **Don't compute same gradients multiple times:** Cache neighbor lookups
- **Don't ignore bit-stuffing:** JPEG markers require 0x00 insertion after 0xFF bytes
- **Don't mix EBCOT and HT:** HTJ2K must use HT block coder exclusively for conformance

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SIMD abstraction | Custom x86 intrinsics | Vector128&lt;T&gt;/Vector256&lt;T&gt; | Cross-platform, JIT optimized, future-proof |
| Bit I/O streams | byte[] with manual shifting | Existing JlsBitReader/Writer pattern | Already handles JPEG bit-stuffing correctly |
| Memory pooling | new byte[] per operation | ArrayPool&lt;byte&gt;.Shared | Zero-allocation hot paths |
| Gradient quantization | Complex threshold logic | ITU-T T.87 lookup tables | Standard defines exact thresholds |
| Context indexing | Hash tables or binary search | Direct array mapping formula | O(1) with no cache misses |
| HTJ2K CAP marker | Custom JPEG2000 marker parsing | Existing InjectCapMarker helper | Already implemented and tested |

**Key insight:** Both codecs are **specification-driven**, not heuristic. The ITU-T T.87 and ISO/IEC 15444-15 standards provide exact formulas, threshold values, and state machine definitions. Implementing from spec is safer than trying to optimize prematurely.

## Common Pitfalls

### Pitfall 1: Context Index Calculation Off-By-One
**What goes wrong:** Computing context index as `(q1 * 9 + q2) * 9 + q3` without offset yields negative indices when gradients are negative
**Why it happens:** Quantized gradients range -4..4, but array indices must be 0..364
**How to avoid:** Add offset before indexing: `contextIndex = (q1 * 9 + q2) * 9 + q3 + 364`
**Warning signs:** IndexOutOfRangeException during encode/decode, especially on edge pixels

### Pitfall 2: Golomb-Rice Parameter Overflow
**What goes wrong:** Computing k parameter as `k = 0; while (N << k < A * N) k++;` can infinite loop
**Why it happens:** Integer overflow when N is large makes shifted value wrap to negative
**How to avoid:** Add limit: `while (k < 32 && (N << k) < A * N) k++;`
**Warning signs:** Encoder hangs on large images, k values exceeding 16

### Pitfall 3: Bit Stuffing Violations
**What goes wrong:** Writing 0xFF byte in entropy-coded stream without following 0x00 causes decoder to interpret as marker
**Why it happens:** JPEG format reserves 0xFF** sequences for markers
**How to avoid:** After writing 0xFF to bitstream, always insert 0x00. Existing JlsBitWriter pattern does this correctly.
**Warning signs:** Decoder fails with "unexpected marker" error, works on some images but not others

### Pitfall 4: Sign Handling in Near-Lossless Mode
**What goes wrong:** Applying NEAR parameter without sign-correcting prediction errors
**Why it happens:** Context correction terms assume signed errors, but quantization is magnitude-based
**How to avoid:** Follow ITU-T T.87 Section 4.4 exactly - normalize sign before context lookup, denormalize after
**Warning signs:** Near-lossless mode produces artifacts, lossless mode works fine

### Pitfall 5: HTJ2K CAP Marker Position
**What goes wrong:** Injecting CAP marker after SOT (Start of Tile) instead of in main header
**Why it happens:** Confusion between main header markers and tile-part header markers
**How to avoid:** CAP must be in main header between SIZ and first tile (after SIZ, before SOT). Existing InjectCapMarker finds SIZ end.
**Warning signs:** HTJ2K decoder (OpenJPH) rejects file as standard JPEG2000 instead of HTJ2K

### Pitfall 6: Component Interleaving Assumptions
**What goes wrong:** Hardcoding line-interleaved mode for RGB images
**Why it happens:** Many examples use ILV=1 (line interleaved), but standard supports ILV=0,1,2
**How to avoid:** Respect JpegLsCodecOptions.InterleaveMode parameter. Default is None (ILV=0) per DICOM.
**Warning signs:** Color images decode incorrectly, channel order swapped

## Code Examples

Verified patterns from ITU-T T.87 and existing codebase:

### JPEG-LS Context Update (from standard)
```csharp
// Source: ITU-T T.87 Section 4.3 - Context modeling
// Already partially implemented in JpegLsDecoder.cs
private struct JlsContext
{
    public int A;  // Accumulated absolute error
    public int B;  // Accumulated signed error
    public int C;  // Bias correction
    public int N;  // Sample count

    public void Initialize(int range)
    {
        A = Math.Max(2, (range + 32) / 64);
        B = 0;
        C = 0;
        N = 1;
    }

    public int ComputeK(int limit)
    {
        int k = 0;
        // Find k such that 2^k ≈ A/N (expected absolute error)
        while (k < limit && (N << k) < A * N)
            k++;
        return k;
    }

    public void Update(int error, int reset, int range)
    {
        // Update statistics
        A += Math.Abs(error);
        B += error;
        N++;

        // Halve statistics periodically to prevent overflow
        if (N == reset)
        {
            A = (A + 1) >> 1;
            B = (B + 1) >> 1;
            N = (N + 1) >> 1;
        }

        // Bias correction when prediction consistently over/under
        if (B <= -N)
        {
            B = Math.Max(B + N, 1 - N);
            if (C > -128) C--;
        }
        else if (B > 0)
        {
            B = Math.Min(B - N, 0);
            if (C < 127) C++;
        }
    }
}
```

### Median Edge Detection Predictor
```csharp
// Source: ITU-T T.87 Section 4.2 - Prediction
// Core predictor used in regular mode
private static int MedianEdgeDetection(int a, int b, int c)
{
    // a = left, b = above, c = above-left
    // Detects horizontal/vertical edges

    if (c >= Math.Max(a, b))
        return Math.Min(a, b);      // Horizontal edge
    else if (c <= Math.Min(a, b))
        return Math.Max(a, b);      // Vertical edge
    else
        return a + b - c;           // Diagonal gradient
}
```

### HT Block Coder Skeleton (to implement)
```csharp
// Source: ISO/IEC 15444-15 Section 7 - HT block coding
// NEW implementation needed for HTJ2K
internal static class HtBlockCoder
{
    // HT uses magnitude refinement + sign coding + cleanup pass
    // Similar to EBCOT but with fixed VLC instead of MQ coder

    public static CodeBlockData EncodeBlock(int[] coefficients, int width, int height, int msbPosition)
    {
        var writer = new HtBitWriter();

        // Significance propagation pass (HT uses fixed patterns)
        EncodeSigPropPass(coefficients, width, height, msbPosition, writer);

        // Magnitude refinement pass (VLC instead of MQ)
        EncodeMagRefPass(coefficients, width, height, msbPosition, writer);

        // Cleanup pass (optimized for HT)
        EncodeCleanupPass(coefficients, width, height, msbPosition, writer);

        return new CodeBlockData(
            writer.GetBytes(),
            passes: 3,
            zeroBitPlanes: 32 - msbPosition
        );
    }

    private static void EncodeSigPropPass(int[] data, int w, int h, int msb, HtBitWriter writer)
    {
        // TODO: Implement HT significance propagation
        // Uses predetermined neighborhood patterns (HT Sets)
        // Much simpler than EBCOT context formation
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| JPEG-LS Part 1 only | Part 1 + Part 2 (arithmetic coding) | Part 2 published 2003 | User requested full compliance |
| JPEG 2000 EBCOT | HTJ2K HT block coder | HTJ2K standardized 2019 | 10x encoding performance improvement |
| Hand-tuned x86 SIMD | Vector128/256 abstractions | .NET Core 3.0+ | Cross-platform SIMD, easier maintenance |
| Separate J2K encoder | Reuse J2K infrastructure for HTJ2K | HTJ2K design | ~80% code reuse, just swap block coder |

**Deprecated/outdated:**
- **CharLS 1.x API:** CharLS 2.x changed to C++14/17, better performance
- **OpenJPEG for HTJ2K:** OpenJPEG 2.5+ supports HTJ2K decode only, OpenJPH is reference encoder
- **Manual SIMD via Intrinsics:** Vector&lt;T&gt; API preferred unless extreme optimization needed
- **JPEG-LS arithmetic coding (Part 2):** Rarely used in practice, but user requested it for completeness

## Open Questions

Things that couldn't be fully resolved:

1. **HTJ2K Quality Layers for DICOM**
   - What we know: Standard supports 1-255 layers, most implementations use 1 for lossless
   - What's unclear: DICOM PS3.5 doesn't specify layer requirements for HTJ2K transfer syntaxes
   - Recommendation: Default to 1 layer, make configurable via Htj2kCodecOptions if needed

2. **JPEG-LS Part 2 Arithmetic Coding Priority**
   - What we know: User requested "Part 2 including arithmetic coding" in context
   - What's unclear: No DICOM transfer syntax uses Part 2, CharLS doesn't implement it
   - Recommendation: Implement Part 1 fully first, defer Part 2 to future phase if actually needed

3. **Auto-Parallel Threshold**
   - What we know: User wants auto-parallel for large images without caller config
   - What's unclear: What image size threshold justifies parallelism overhead
   - Recommendation: Profile with 256×256, 512×512, 1024×1024 test images, choose threshold where parallel is 1.5x+ faster

4. **SIMD Instruction Selection**
   - What we know: Vector128/256 provide abstraction, JIT selects best instructions
   - What's unclear: Whether to use Intrinsics for critical paths (DWT, Golomb-Rice)
   - Recommendation: Start with Vector128/256, profile, add Intrinsics only if 10x target not met

## Sources

### Primary (HIGH confidence)
- [ITU-T Rec. T.87:1998](https://www.itu.int/rec/T-REC-T.87/en) - Official JPEG-LS specification
- [ISO/IEC 15444-15:2019](https://www.iso.org/standard/76621.html) - Official HTJ2K specification
- [DICOM PS3.5 Section 8.2.14](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_8.2.14.html) - HTJ2K in DICOM
- Existing codebase: JpegLsEncoder.cs, JpegLsDecoder.cs, J2kEncoder.cs, J2kDecoder.cs - Current implementation patterns

### Secondary (MEDIUM confidence)
- [JPEG-LS official page](https://jpeg.org/jpegls/) - Standard overview and references
- [HTJ2K official page](https://jpeg.org/jpeg2000/htj2k.html) - Standard overview and use cases
- [HTJ2K whitepaper](https://ds.jpeg.org/whitepapers/jpeg-htj2k-whitepaper.pdf) - Technical details on HT block coder
- [Library of Congress HTJ2K format](https://loc.gov/preservation/digital/formats/fdd/fdd000565.shtml) - Format documentation

### Tertiary (LOW confidence - reference implementations)
- [CharLS GitHub](https://github.com/team-charls/charls) - C++ JPEG-LS implementation (algorithmic reference only)
- [OpenJPH GitHub](https://github.com/aous72/OpenJPH) - C++ HTJ2K implementation (algorithmic reference only)
- [IEEE paper on SIMD DWT](https://ieeexplore.ieee.org/document/5350182/) - SIMD wavelet optimization techniques
- [Frontiers HTJ2K paper](https://www.frontiersin.org/articles/10.3389/frsip.2022.885644/full) - HTJ2K performance analysis

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All .NET BCL components, well-documented
- Architecture: HIGH - Existing infrastructure validates patterns
- Pitfalls: MEDIUM - Based on existing partial implementation and standard gotchas
- HTJ2K details: MEDIUM - Standard available but HT block coder specifics require deep reading
- Performance targets: MEDIUM - 10x slower than native is achievable based on existing RLE/JPEG codecs

**Research date:** 2026-02-02
**Valid until:** 2026-03-02 (30 days - standards are stable)
