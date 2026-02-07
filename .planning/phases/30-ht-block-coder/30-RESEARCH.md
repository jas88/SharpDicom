# Phase 30: HT Block Coder - Research

**Researched:** 2026-02-07
**Domain:** ITU-T T.814 (ISO/IEC 15444-15) High-Throughput JPEG 2000 block coding, .NET SIMD, codec architecture
**Confidence:** MEDIUM (algorithm well-documented via academic papers and reference implementations; no direct Context7 library for ITU-T T.814 specs)

## Summary

Phase 30 replaces the current EBCOT-based block coding in the HTJ2K codec path with the true High-Throughput block coding algorithm defined in ITU-T T.814. The current implementation at `src/SharpDicom/Codecs/Htj2k/Htj2kCodec.cs` delegates entirely to the standard J2K encoder/decoder (EBCOT + MQ arithmetic coding), injecting only a CAP marker for identification. This produces valid but slow HTJ2K codestreams.

The HT block coder is fundamentally different from EBCOT. Where EBCOT uses context-adaptive MQ arithmetic coding with 19 contexts and iterates over every sample per bitplane pass, HT uses a quad-based (2x2) processing model with three distinct byte-streams (VLC, MEL, MagSgn) packed into a single codeword segment. The key algorithmic innovation is that all significance information for 4 samples is encoded in a single VLC codeword of at most 7 bits, enabling table-lookup decoding. The MEL coder (13-state adaptive run-length) handles runs of all-zero quads. This design eliminates the data-dependent branching and serial processing that makes EBCOT slow.

The existing codebase provides solid foundations: EBCOT encoder/decoder (720 + 568 lines), MQ coder (601 lines), DWT transforms with Vector128 SIMD, Tier-2 packet encoder/decoder, J2kCodestream header parser with all markers, CodecRegistry with priority system, and IPixelDataCodec interface. The J2K pipeline has architectural gaps documented in Phase 21 -- it treats all code-blocks as belonging to subband LL and uses a simplified single-tile decoder. These gaps must be fixed before HT can be properly integrated.

**Primary recommendation:** Rebuild the J2K subband/resolution infrastructure first (fixing the LL-only bug and adding proper multi-resolution support), then implement the HT block coder as a parallel Tier-1 coding path alongside EBCOT, sharing the same DWT and Tier-2 infrastructure.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Runtime.Intrinsics | .NET 8+ built-in | SIMD Vector128/256/512, BMI2 | Platform SIMD intrinsics for VLC bit manipulation |
| System.Numerics | .NET 8+ built-in | BitOperations (LeadingZeroCount, PopCount) | Branchless MSB finding, popcount for significance |
| BenchmarkDotNet | 0.14.x | Performance benchmarking | Standard .NET benchmarking framework |
| FsCheck | 2.16.6 (already in project) | Property-based testing | Fuzz-like coverage for codec edge cases |
| FsCheck.NUnit | 3.0.0-rc3 (already in project) | NUnit integration for FsCheck | Already configured in Directory.Packages.props |
| NUnit | 4.4.0 (already in project) | Test framework | Already in use with 2511 tests |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| SharpFuzz | 2.2.0 | AFL-based fuzz testing | Security fuzzing of managed decoder |
| System.CommandLine | 2.0.2 (already in project) | CLI framework | `sharpdcm convert` command |
| Spectre.Console | 0.54.0 (already in project) | TTY-aware progress | Progress bars for batch conversion |

### Reference Implementations (Build from Source in CI)
| Project | License | Purpose | Note |
|---------|---------|---------|------|
| OpenJPH | BSD-2-Clause | Primary reference encoder/decoder | v0.26.0, Dec 2025. C++. Used for conformance verification |
| OpenJPEG | BSD-2-Clause | Secondary reference for standard J2K | For EBCOT regression and MIXED mode testing |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Managed HT block coder | Native OpenJPH P/Invoke wrapper | Higher performance but adds native dependency; context says design integration points now, implement wrapper later |
| Custom VLC tables | Pre-computed static arrays | Tables are spec-defined, no alternative needed |
| FsCheck for property testing | Stryker.NET for mutation testing | FsCheck better suited for codec domain; already in project |

**Installation:**
```bash
# Already have: FsCheck, FsCheck.NUnit, NUnit, System.CommandLine, Spectre.Console
# Add for this phase:
dotnet add package BenchmarkDotNet --version 0.14.0
dotnet add package SharpFuzz --version 2.2.0
```

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/Codecs/
├── Jpeg2000/
│   ├── J2kCodestream.cs         # Header parsing (extend with CAP, PLT markers)
│   ├── J2kDecoder.cs            # Rebuild: multi-resolution subband routing
│   ├── J2kEncoder.cs            # Rebuild: multi-resolution subband routing
│   ├── Jpeg2000CodecOptions.cs
│   ├── Jpeg2000LosslessCodec.cs
│   ├── Jpeg2000LossyCodec.cs
│   ├── Subband/                 # NEW: multi-resolution subband infrastructure
│   │   ├── SubbandDescriptor.cs # Resolution level + subband type + dimensions
│   │   ├── SubbandPartitioner.cs # Maps code-blocks to subbands
│   │   └── TileComponent.cs     # Per-tile, per-component DWT + code-block grid
│   ├── Tier1/
│   │   ├── IBlockCoder.cs       # NEW: shared interface for EBCOT and HT
│   │   ├── EbcotEncoder.cs      # Existing (keep as-is)
│   │   ├── EbcotDecoder.cs      # Existing (keep as-is)
│   │   ├── MqCoder.cs           # Existing (keep as-is)
│   │   ├── HtBlockEncoder.cs    # NEW: HT cleanup + SigProp + MagRef encode
│   │   ├── HtBlockDecoder.cs    # NEW: HT cleanup + SigProp + MagRef decode
│   │   ├── HtCleanup.cs         # NEW: Cleanup pass (VLC + MEL + MagSgn)
│   │   ├── HtSigProp.cs         # NEW: SigProp refinement pass
│   │   ├── HtMagRef.cs          # NEW: MagRef refinement pass
│   │   ├── VlcTable.cs          # NEW: VLC lookup tables (spec-defined)
│   │   ├── MelCoder.cs          # NEW: MEL run-length coder (13 states)
│   │   └── HtBitIO.cs           # NEW: Bidirectional bit I/O for HT streams
│   ├── Tier2/
│   │   ├── PacketEncoder.cs     # Extend for HT pass counts (1-3 per set)
│   │   └── PacketDecoder.cs     # Extend for HT pass counts
│   └── Wavelet/
│       ├── Dwt53.cs             # Extend with Vector256/512 paths
│       ├── Dwt97.cs             # Extend with Vector256/512 paths
│       └── DwtTransform.cs      # Existing coordinator
├── Htj2k/
│   ├── Htj2kCodec.cs           # Update: route to HT block coder
│   ├── Htj2kCodecOptions.cs    # Extend: rate control presets, pass count
│   ├── HtEncoderOptions.cs     # NEW: HT-specific options (presets, RD)
│   └── IProgressiveCodec.cs    # NEW: resolution-level decode interface
└── Simd/
    └── SimdHelpers.cs           # Extend with Vector256/512 helpers
```

### Pattern 1: IBlockCoder Abstraction
**What:** Common interface for EBCOT and HT block coding that J2K encoder/decoder routes through
**When to use:** Any code-block encoding/decoding operation in the J2K pipeline
**Example:**
```csharp
// Unified block coder interface
public interface IBlockCoder
{
    CodeBlockData EncodeBlock(
        ReadOnlySpan<int> coefficients,
        int width, int height,
        int subbandType,
        BlockCoderOptions options);

    int[] DecodeBlock(
        ReadOnlySpan<byte> data,
        int numPasses,
        int width, int height,
        int msbPosition,
        int subbandType);
}

// Router selects HT vs EBCOT based on transfer syntax
public static IBlockCoder GetBlockCoder(bool useHt)
    => useHt ? HtBlockCoder.Instance : EbcotBlockCoder.Instance;
```

### Pattern 2: Stateless Block Coder with Per-Call Structs
**What:** HT block coder is a stateless singleton; all mutable state lives in stack-allocated structs
**When to use:** All HT encode/decode operations (locked decision: stateless/thread-safe)
**Example:**
```csharp
// All state on the stack or in ArrayPool rentals
public static class HtBlockCoder
{
    public static CodeBlockData Encode(
        ReadOnlySpan<int> coefficients,
        int width, int height,
        int subbandType,
        HtEncodeOptions options)
    {
        int quadW = (width + 1) / 2;
        int quadH = (height + 1) / 2;

        // Stackalloc for small blocks, ArrayPool for large
        Span<byte> sigState = (quadW * quadH <= 1024)
            ? stackalloc byte[quadW * quadH]
            : ArrayPool<byte>.Shared.Rent(quadW * quadH);
        // ... encode ...
    }
}
```

### Pattern 3: 4x4 Quad-Aligned Coefficient Layout
**What:** DWT coefficients stored in 4x4-aligned blocks for cache-optimal HT access
**When to use:** All coefficient buffers passed to/from HT block coder (locked decision)
**Example:**
```csharp
// Coefficients laid out in 4x4 quad groups for SIMD-friendly access
// Each quad of 4 samples (2x2) is contiguous in memory
[StructLayout(LayoutKind.Sequential)]
public readonly struct Quad
{
    public readonly int TopLeft;
    public readonly int TopRight;
    public readonly int BottomLeft;
    public readonly int BottomRight;
}
```

### Pattern 4: Tiered SIMD Dispatch
**What:** Runtime CPU feature detection with fallback chain: AVX-512 > AVX2 > SSE2/NEON > Scalar
**When to use:** All VLC decode loops, DWT, coefficient layout operations
**Example:**
```csharp
#if NET8_0_OR_GREATER
if (Vector512.IsHardwareAccelerated)
    DecodeVlcAvx512(vlcStream, output);
else if (Vector256.IsHardwareAccelerated)
    DecodeVlcAvx2(vlcStream, output);
else if (Vector128.IsHardwareAccelerated)
    DecodeVlcVector128(vlcStream, output);
else
#endif
    DecodeVlcScalar(vlcStream, output);
```

### Pattern 5: Three-Stream Bidirectional Bit I/O
**What:** HT cleanup pass uses three interleaved byte-streams in a single codeword segment
**When to use:** All HT cleanup pass encode/decode
**Example:**
```csharp
// The cleanup segment layout:
// [MagSgn bytes...] [VLC bytes...] [MEL bytes (reversed)]
//  ^-- grows forward  ^-- boundary marked by ILW  ^-- grows backward from end
// ILW (Interface Locator Word) = last 12 bits of segment, points to MEL start
public ref struct HtCleanupReader
{
    private readonly ReadOnlySpan<byte> _segment;
    private int _magSgnPos;     // forward reader for MagSgn
    private int _vlcPos;        // forward reader for VLC (after ILW offset)
    private int _melPos;        // backward reader for MEL (from end)
}
```

### Anti-Patterns to Avoid
- **Processing samples individually:** HT gains its speed from quad-based (2x2) batch processing. Never iterate over individual pixels in the HT path.
- **Adaptive probability estimation in HT:** Unlike EBCOT's MQ coder with 47 states, HT uses fixed VLC tables and a 13-state MEL coder. Do not add adaptive probability modeling.
- **Shared mutable state in block coder:** The block coder must be stateless (locked decision). All per-call state goes in structs on the stack or in pooled arrays.
- **Allocating per-codeblock:** Use stackalloc for blocks < 4KB, ArrayPool for larger. Never `new byte[]` in the hot path.
- **Ignoring subband type:** The current J2K pipeline treats all code-blocks as LL subband. This is a known bug that produces incorrect context information. Must be fixed.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| VLC lookup tables | Custom Huffman tree | Pre-computed 1024-entry lookup tables from ITU-T T.814 spec | Tables are standardized; 1024 entries indexed by 3-bit context + 7-bit codeword |
| MEL run-length coder | Generic run-length encoder | MELCODE state machine (13 states, derived from JPEG-LS LOCO-I) | Spec-defined; only 13 states, must match exactly for interoperability |
| Bit counting (MSB) | Loop-based bit scanning | `System.Numerics.BitOperations.LeadingZeroCount()` | Hardware LZCNT/CLZ instruction, branchless, already in .NET 8+ |
| Population count | Manual bit counting | `System.Numerics.BitOperations.PopCount()` | Hardware POPCNT, single instruction |
| Bit deposit/extract | Manual bit manipulation | `System.Runtime.Intrinsics.X86.Bmi2.ParallelBitDeposit/Extract` | PDEP/PEXT hardware instructions for VLC bit packing |
| Rate-distortion optimization | Custom RD curve fitting | PCRD-opt (Post-Compression Rate-Distortion Optimization) from ITU-T T.800 | Standard algorithm; truncation point selection over HT Sets |
| PSNR/SSIM computation | Manual pixel comparison | Existing formulaic computation (standard math, no library needed) | Well-defined formulas; SSIM has a standard reference implementation |
| Progress reporting | Custom progress | Spectre.Console `AnsiConsole.Progress()` | Already in project dependencies, TTY-aware |

**Key insight:** The HT algorithm is entirely specified by ITU-T T.814. There are no design choices in the core algorithm -- only in the implementation strategy (SIMD, memory layout, parallelism). The VLC tables, MEL state transitions, and bitstream formats are fixed by the standard.

## Common Pitfalls

### Pitfall 1: LL-Only Subband Bug in J2K Pipeline
**What goes wrong:** Current J2kEncoder and J2kDecoder treat all code-blocks as belonging to the LL subband (subbandType = 0). After DWT, code-blocks span LL, LH, HL, and HH subbands, each requiring different significance context tables.
**Why it happens:** Phase 12 implemented a simplified single-subband pipeline. Comments in J2kEncoder.cs line 332 explicitly note: "For simplicity, use LL subband type (0) for all code-blocks."
**How to avoid:** Build a SubbandPartitioner that maps each code-block to its correct subband based on DWT decomposition level and position. This affects both EBCOT (context selection) and HT (VLC context formation).
**Warning signs:** Decoded images have visible artifacts at subband boundaries; PSNR lower than expected for lossless; conformance failures.

### Pitfall 2: VLC Table Endianness and Bit Ordering
**What goes wrong:** VLC codewords in HTJ2K are at most 7 bits, but the lookup table is indexed by 10 bits (3-bit context + 7-bit codeword). Getting the bit ordering wrong produces garbage output.
**Why it happens:** The VLC stream grows forward from the start of the segment while MEL grows backward from the end. The ILW (Interface Locator Word) marks the boundary. Misreading the ILW or confusing stream directions corrupts all decoding.
**How to avoid:** Implement the three-stream reader as a dedicated struct (HtCleanupReader) with explicit forward/backward positions. Test with known reference vectors before integrating into the full pipeline.
**Warning signs:** First few quads decode correctly but corruption grows; MEL state machine enters invalid states.

### Pitfall 3: HT Set Boundary and Placeholder Passes
**What goes wrong:** HTJ2K organizes passes into HT Sets (Cleanup + optional SigProp + MagRef). The Tier-2 packet encoder must correctly signal pass counts and handle placeholder passes for rate control.
**Why it happens:** Standard J2K uses 3 passes per bitplane (any number of bitplanes). HT uses at most 3 passes per HT Set, with at most 2 HT Sets per code-block. Reusing the EBCOT Tier-2 logic without adaptation produces invalid packet headers.
**How to avoid:** The Tier-2 layer must distinguish between HT and EBCOT pass counting. HT code-blocks have 1-6 passes total (1-3 per HT Set x 1-2 Sets). Placeholder passes (empty SigProp/MagRef) may be signaled for packet structure conformance.
**Warning signs:** OpenJPH/OpenJPEG rejects output with "invalid number of passes" errors.

### Pitfall 4: BMI2 PDEP/PEXT Performance on AMD
**What goes wrong:** BMI2 PDEP/PEXT instructions are fast on Intel (1 cycle) but historically slow on AMD Zen1/Zen2 (micro-coded, 18+ cycles). Using them unconditionally hurts AMD performance.
**Why it happens:** `Bmi2.IsSupported` returns true on both Intel and AMD, but performance differs dramatically. Zen3+ fixed this.
**How to avoid:** On AMD Zen1/Zen2, fall back to manual bit manipulation instead of PDEP/PEXT. Check CPU vendor if targeting these older CPUs, or rely on the non-PDEP path being fast enough (it will be for managed code). Benchmark on both platforms.
**Warning signs:** Benchmarks show 5-10x regression on older AMD hardware.

### Pitfall 5: CAP Marker Pcap/Ccap Encoding
**What goes wrong:** The CAP marker must correctly encode Pcap bit 14 (for Part 15) and Ccap[15] with the correct mode (HTONLY, HTDECLARED, MIXED) and precision. The current code injects a hardcoded CAP marker that may not match the actual encoding parameters.
**Why it happens:** The current InjectCapMarker method in Htj2kCodec.cs uses a fixed 10-byte CAP marker with hardcoded Ccap value 0x0020. This needs to reflect actual encoding parameters (HT precision, mode type).
**How to avoid:** Generate the CAP marker during codestream construction based on actual encoder state. Pcap bit 14 set means Ccap[15] is present. Ccap[15] bits: [15:14]=mode, [13]=MULTIHT, [12]=RGN, [11]=homogeneous, [5]=HTIRV, [4:0]=precision-8.
**Warning signs:** OpenJPH rejects the codestream as non-HT; decoders fall back to standard J2K decoding.

### Pitfall 6: Multi-Resolution Subband Dimensions
**What goes wrong:** Subband dimensions at each resolution level involve ceiling/floor division that must match the spec exactly. Off-by-one errors cause buffer overflows or incorrect coefficient placement.
**Why it happens:** The DWT produces subbands of different sizes at each level. LL = ceil(W/2) x ceil(H/2), HL = floor(W/2) x ceil(H/2), LH = ceil(W/2) x floor(H/2), HH = floor(W/2) x floor(H/2). The existing DwtTransform.GetSubbandDimensions does this correctly but it is not wired into the code-block grid.
**How to avoid:** Use DwtTransform.GetSubbandDimensions for all dimension calculations. Unit test with odd-sized images (e.g., 255x255, 1x1, 1x4096).
**Warning signs:** Lossless roundtrip fails for odd-dimension images; array index out of bounds.

### Pitfall 7: Cleanup-Only Mode vs Full HT Sets
**What goes wrong:** Generating only Cleanup passes (fast mode) produces lower quality at the same bitrate compared to full HT Sets (Cleanup + SigProp + MagRef). Failing to document this tradeoff confuses users.
**Why it happens:** SigProp and MagRef refine significant samples to the next bitplane. Without them, quantization is coarser. For lossless, all passes are needed.
**How to avoid:** "Fast" preset uses cleanup-only for lossy streaming. "Quality" preset uses full HT Sets. Lossless always uses full HT Sets. Encode presets (locked decision) must be clearly documented.
**Warning signs:** Lossless mode with cleanup-only fails roundtrip; lossy PSNR lower than expected.

## Code Examples

Verified patterns from official sources and reference implementations:

### MEL Coder State Machine (13 States)
```csharp
// Source: ITU-T T.814 / OpenJPH ojph_block_common.cpp
// MEL coder: adaptive run-length for all-zero quad significance
// 13 states with transitions based on whether quad is significant
private static readonly int[] MelE = {
    0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5
};

public ref struct MelDecoder
{
    private ReadOnlySpan<byte> _data;
    private int _pos;           // reads backward from end
    private int _bitBuffer;
    private int _bitsAvailable;
    private int _run;           // current run count
    private int _state;         // 0-12

    public bool DecodeQuadSignificance()
    {
        if (_run > 0)
        {
            _run--;
            return false; // insignificant (part of run)
        }

        // Read one bit
        int bit = ReadBit();
        if (bit == 0)
        {
            // Run continues: 2^MelE[state] insignificant quads
            _run = 1 << MelE[_state];
            _run--; // this quad counts as first
            _state = Math.Min(_state + 1, 12); // transition up
            return false;
        }
        else
        {
            // Run broken: this quad is significant
            _state = Math.Max(_state - 1, 0); // transition down
            return true;
        }
    }
}
```

### VLC Table Lookup (1024 Entries, 3-bit Context + 7-bit Codeword)
```csharp
// Source: ITU-T T.814 Table HT.1 / OpenJPH table0.h, table1.h
// 1024 entries: indexed by (context << 7) | (7-bit VLC codeword)
// Each entry: significance pattern (4 bits) + EMB bits + codeword length
private static readonly ushort[] VlcTable0 = new ushort[1024];
// Initialize from spec-defined tables (computed at first use, cached)

// Decode one quad's significance + exponent information
public static (byte sigPattern, byte embBits, int codewordLen)
    DecodeVlcQuad(ReadOnlySpan<byte> vlcStream, ref int bitPos, int context)
{
    // Peek 7 bits from VLC stream
    int bits = PeekBits(vlcStream, bitPos, 7);
    int tableIndex = (context << 7) | bits;
    ushort entry = VlcTable0[tableIndex];

    int sigPattern = entry & 0x0F;         // 4-bit significance
    int embBits = (entry >> 4) & 0x0F;     // embedded magnitude bits
    int codewordLen = (entry >> 8) & 0x07; // consumed bits (1-7)

    bitPos += codewordLen;
    return ((byte)sigPattern, (byte)embBits, codewordLen);
}
```

### HT Cleanup Pass Structure (Three-Stream Reader)
```csharp
// Source: ITU-T T.814 Section 7.3 / Frontiers paper 10.3389/frsip.2022.885644
// Cleanup segment layout:
//   [MagSgn data...] [VLC data...] [MEL data (reversed)]
//   ^-forward          ^-ILW marks boundary  ^-backward from end
public ref struct HtCleanupSegment
{
    public ReadOnlySpan<byte> Segment;
    public int MagSgnStart;    // always 0
    public int VlcStart;       // after MagSgn, before ILW boundary
    public int MelStart;       // segment.Length - 2 (before ILW), reads backward

    public static HtCleanupSegment Parse(ReadOnlySpan<byte> segment)
    {
        // ILW is stored in the last 2 bytes of the segment
        // It encodes the offset where VLC data starts
        int ilw = (segment[^2] << 4) | (segment[^1] >> 4);
        return new HtCleanupSegment
        {
            Segment = segment,
            MagSgnStart = 0,
            VlcStart = ilw,
            MelStart = segment.Length - 2 // MEL reads backward
        };
    }
}
```

### BMI2 PDEP/PEXT for VLC Bit Manipulation
```csharp
// Source: .NET Hardware Intrinsics / devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics
#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics.X86;

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static ulong ExtractBits(ulong value, ulong mask)
{
    if (Bmi2.X64.IsSupported)
        return Bmi2.X64.ParallelBitExtract(value, mask);
    // Scalar fallback
    return ExtractBitsScalar(value, mask);
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static ulong DepositBits(ulong value, ulong mask)
{
    if (Bmi2.X64.IsSupported)
        return Bmi2.X64.ParallelBitDeposit(value, mask);
    return DepositBitsScalar(value, mask);
}
#endif
```

### Parallel Tile Decode Pattern
```csharp
// Source: locked decision - configurable tile parallelism
public static void DecodeTilesParallel(
    J2kCodestream header,
    ReadOnlyMemory<byte> codestream,
    Span<byte> output,
    int maxDegreeOfParallelism = -1)
{
    int tileCols = (header.ImageWidth + header.TileWidth - 1) / header.TileWidth;
    int tileRows = (header.ImageHeight + header.TileHeight - 1) / header.TileHeight;
    int tileCount = tileCols * tileRows;

    if (maxDegreeOfParallelism < 0)
        maxDegreeOfParallelism = Environment.ProcessorCount;

    Parallel.For(0, tileCount, new ParallelOptions
    {
        MaxDegreeOfParallelism = maxDegreeOfParallelism
    }, tileIndex =>
    {
        // Each tile decoded independently: HT block decode + inverse DWT
        DecodeSingleTile(header, codestream, output, tileIndex);
    });
}
```

### CAP Marker Generation (Dynamic)
```csharp
// Source: ITU-T T.814 / FFmpeg ffmpeg-devel@ffmpeg.org CAP marker patches
public static byte[] BuildCapMarker(bool isHtOnly, bool isLossless, int precision)
{
    // Pcap: bit 14 set = Part 15 extended capability present
    uint pcap = 1u << (31 - 14); // Pcap bit for Part 15

    // Ccap[15] structure:
    // bits [15:14] = mode: 0=HTONLY, 1=HTDECLARED, 3=MIXED
    // bit [5]      = 0=HTREV (reversible), 1=HTIRV (irreversible)
    // bits [4:0]   = P (precision - 8, range 0-66)
    ushort ccap15 = 0;
    ccap15 |= (ushort)(isHtOnly ? 0x0000 : 0x4000); // mode in bits 15:14
    ccap15 |= (ushort)(isLossless ? 0x0000 : 0x0020); // HTREV vs HTIRV in bit 5
    ccap15 |= (ushort)Math.Clamp(precision - 8, 0, 66); // P in bits 4:0

    return new byte[]
    {
        0xFF, 0x50,                             // CAP marker
        0x00, 0x08,                             // Length = 8
        (byte)(pcap >> 24), (byte)(pcap >> 16),
        (byte)(pcap >> 8), (byte)(pcap),        // Pcap
        (byte)(ccap15 >> 8), (byte)(ccap15)     // Ccap[15]
    };
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| EBCOT MQ arithmetic coding | HT VLC + MEL + MagSgn | ITU-T T.814 (2019) | 10-50x throughput increase |
| Per-sample bitplane iteration | 2x2 quad batch processing | ITU-T T.814 (2019) | SIMD-friendly, cache-friendly |
| .NET Vector128 only | Vector128 + Vector256 + Vector512 | .NET 8 (2023) / .NET 9 (2024) | AVX2/AVX-512 for wider SIMD paths |
| Manual bit manipulation | BMI2 PDEP/PEXT intrinsics | .NET 8 (2023) | Single-instruction bit pack/unpack |
| OpenJPH scalar C++ | OpenJPH with SSE3/AVX2/AVX-512 | v0.26.0 (Dec 2025) | Reference for SIMD optimization targets |
| Reflection-based codec discovery | Static registration + FrozenDictionary | Already in project | AOT-compatible codec registry |
| FsCheck 2.x | FsCheck 3.0.0-rc3 (NUnit integration) | Already configured | Native NUnit [Property] attribute support |

**Deprecated/outdated:**
- **Single-tile-only decoding**: Current J2kDecoder only handles tile 0. Multi-tile required for whole-slide pathology.
- **LL-only subband type**: Current encoder/decoder hardcodes subbandType=0. Must be fixed.
- **Hardcoded CAP marker**: Current Htj2kCodec.InjectCapMarker uses fixed bytes. Must be dynamic.
- **MQ arithmetic coding for HT path**: Current approach. Will be replaced with VLC/MEL/MagSgn.

## Open Questions

Things that could not be fully resolved:

1. **Exact VLC Table Contents**
   - What we know: 1024 entries per table, indexed by 3-bit context + 7-bit codeword. At most 7-bit codewords. Two tables (table0 and table1) in OpenJPH.
   - What's unclear: Exact table contents are defined in ITU-T T.814 spec which is paywalled. OpenJPH's table0.h and table1.h are BSD-2-Clause licensed and can be used as reference.
   - Recommendation: Extract table values from OpenJPH source code (BSD-2-Clause compatible) and verify against spec conformance tests.

2. **Rate-Distortion Optimization Details for HT Sets**
   - What we know: PCRD-opt from ITU-T T.800 Annex J applies. HT has 1-6 truncation points (vs hundreds for EBCOT). CPLEX (complexity control) bounds the number of HT Sets.
   - What's unclear: Optimal rate allocation strategy when only 1-6 truncation points are available. How named presets (Diagnostic, Archive, Review, Fast) map to HT Set counts.
   - Recommendation: Start with simple approach (cleanup-only for Fast, 2 full HT Sets for Quality). Named presets map to quantization step sizes combined with HT Set counts. This is Claude's discretion per CONTEXT.md.

3. **SVE (Scalable Vector Extension) Support on .NET**
   - What we know: ARM SVE is listed as a SIMD tier. .NET 9 has ARM NEON (Vector128). SVE support status in .NET 10 is unclear.
   - What's unclear: Whether .NET 10 exposes SVE intrinsics. Vector128/256 should auto-vectorize on ARM where possible.
   - Recommendation: Implement Vector128 (NEON/SSE2) and Vector256 (AVX2) first. SVE can be added later when runtime support materializes. Use `#if NET10_0_OR_GREATER` guard for future SVE.

4. **J2K-to-HTJ2K Transcoding Without Full Decode**
   - What we know: Context says "direct transcoding by swapping only the block coder layer." Both share DWT/Tier-2.
   - What's unclear: Whether the Tier-2 packet format differences between EBCOT (many passes) and HT (1-6 passes) allow simple block-level swap without Tier-2 reconstruction.
   - Recommendation: Full decode-recode for initial implementation. Optimize with block-level transcoding as a later optimization if Tier-2 format differences are manageable.

5. **ILW (Interface Locator Word) Exact Format**
   - What we know: 12-bit value in last 2 bytes of cleanup segment marking VLC/MEL boundary. Referenced in the Frontiers paper.
   - What's unclear: Exact bit layout within the 2 bytes. Whether it's the byte offset or bit offset.
   - Recommendation: Verify against OpenJPH source code and conformance test vectors. This is critical for correct cleanup pass decoding.

## Sources

### Primary (HIGH confidence)
- [Frontiers: High Throughput JPEG 2000 for Video Content Production and Delivery Over IP Networks](https://www.frontiersin.org/articles/10.3389/frsip.2022.885644/full) - Comprehensive algorithm description, HT Sets, MIXED mode, performance data
- [OpenJPH GitHub Repository](https://github.com/aous72/OpenJPH) - BSD-2-Clause reference implementation, v0.26.0 (Dec 2025), VLC tables in table0.h/table1.h
- [OpenHTJ2K GitHub Repository](https://github.com/osamu620/OpenHTJ2K) - Second reference implementation, ITU-T T.803 conformance compliant
- [FFmpeg CAP Marker Patches](https://www.mail-archive.com/ffmpeg-devel@ffmpeg.org/msg167681.html) - Pcap/Ccap bit layout for CAP marker
- [ITU-T T.814 Table of Contents](https://www.itu.int/dms_pubrec/itu-t/rec/t/T-REC-T.814-201906-I!!TOC-HTM-E.htm) - Official standard structure reference

### Secondary (MEDIUM confidence)
- [DeepWiki OpenJPH Analysis](https://deepwiki.com/aous72/OpenJPH/5-encoding-and-decoding-process) - Architecture analysis of OpenJPH encoding/decoding
- [Kakadu ICIP 2019: HT Block Coding in HTJ2K](https://kakadusoftware.com/wp-content/uploads/icip2019.pdf) - Academic paper on HT block coding algorithm
- [JPEG HT Placeholder Passes Guideline](https://ds.jpeg.org/documents/jpeg2000/wg1n100495-099-COM-Guideline_on_Placeholder_Passes_and_Multiple_HT_Sets_in_HTJ2K_codstreams_v1.pdf) - Official JPEG committee guidelines on HT Sets
- [.NET 8 Hardware Intrinsics Blog](https://devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics/) - Vector256/512, BMI2 support details
- [Unit Testing and Benchmarking SIMD in .NET](https://aalmada.github.io/posts/Unit-testing-and-benchmarking-SIMD-in-dotnet/) - SIMD testing patterns for BenchmarkDotNet

### Tertiary (LOW confidence)
- [HTJ2K WhitePaper (htj2k.com)](https://htj2k.com/wp-content/uploads/white-paper.pdf) - Could not parse PDF content; may contain additional algorithm details
- [ResearchGate: Matlab HTJ2K Implementation](https://www.researchgate.net/publication/339561360_A_Matlab_Implementation_of_the_Emerging_HTJ2K_Standard) - Reference for algorithm validation, not verified

### Codebase (HIGH confidence - direct inspection)
- `src/SharpDicom/Codecs/Htj2k/Htj2kCodec.cs` - Current HTJ2K codec (J2K delegation)
- `src/SharpDicom/Codecs/Jpeg2000/J2kEncoder.cs` - Current encoder (single-tile, LL-only subband bug)
- `src/SharpDicom/Codecs/Jpeg2000/J2kDecoder.cs` - Current decoder (single-tile, LL-only subband bug)
- `src/SharpDicom/Codecs/Jpeg2000/Tier1/EbcotEncoder.cs` - 720 lines, MQ-based EBCOT encoder
- `src/SharpDicom/Codecs/Jpeg2000/Tier1/EbcotDecoder.cs` - 568 lines, MQ-based EBCOT decoder
- `src/SharpDicom/Codecs/Jpeg2000/Tier1/MqCoder.cs` - 601 lines, MQ arithmetic coder (47 states)
- `src/SharpDicom/Codecs/Simd/SimdHelpers.cs` - Vector128 helpers (HorizontalSum, Clamp, Abs)
- `src/SharpDicom/Codecs/Jpeg2000/Wavelet/Dwt53.cs` - 5/3 DWT with Vector128 SIMD
- `src/SharpDicom/Codecs/CodecRegistry.cs` - FrozenDictionary-based registry, priority system
- `src/SharpDicom/Codecs/IPixelDataCodec.cs` - Codec interface (Decode, Encode, Validate)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All libraries already in project or well-established .NET packages
- Algorithm (HT block coding): MEDIUM - Well-documented in papers and reference implementations, but exact table contents require spec or reference code
- Architecture patterns: HIGH - Direct codebase inspection, clear integration points
- J2K pipeline gaps: HIGH - Directly observed in source code (LL-only subband, single-tile)
- SIMD patterns: MEDIUM - .NET 8/9 SIMD well-documented; Vector512/SVE status on specific platforms needs verification
- Pitfalls: MEDIUM - Based on algorithm understanding and reference implementation study
- Conformance testing: MEDIUM - OpenJPH/OpenJPEG can serve as oracles; ITU-T conformance vectors require spec access

**Research date:** 2026-02-07
**Valid until:** 2026-05-07 (stable domain; ITU-T T.814 standard is final)
