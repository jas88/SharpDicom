# HTJ2K Encoder/Decoder Divergences from OpenJPH

Analysis date: 2026-02-09

## Executive Summary

After fixing the COD marker bugs (HT mode flag), the encoder now produces codestreams that OpenJPH recognizes as HTJ2K, but deeper incompatibilities remain in both encoder and decoder.

## Fixed Issues

### ✅ COD Marker - HT Mode Flag (FIXED)

**File:** `src/SharpDicom/Codecs/Jpeg2000/J2kEncoder.cs`

**What was wrong:** SPcod byte 8 (code-block style) was set to `0x00` instead of `0x40`, not signaling HT mode.

**Fix applied:**
```csharp
// Code-block style: bit 6 = HT mode flag per ITU-T T.814
span[offset++] = isHtj2k ? (byte)0x40 : (byte)0x00;
```

**Result:** OpenJPH now recognizes our output as HTJ2K but fails at codeblock decode: `"Error decoding a codeblock"`

## Remaining Encoder Issues

### 🔴 Encoder Issue #1: Unknown codeblock encoding problem

**Evidence:** OpenJPH error: `ojph error 0x000300A1 at ojph_codeblock.cpp:219: Error decoding a codeblock`

**Status:** The exact divergence is unknown. Possibilities:
- VLC codeword encoding differs from OpenJPH expectations
- MagSgn bit packing differs
- ILW (Intermediate Length Word) format incorrect
- Pass data segmentation incorrect

**Next steps:**
1. Compare our encoder output byte-by-byte with OpenJPH output for same input
2. Dump the codeblock data structure before encoding
3. Use OpenJPH source code tracing to identify exact failure point

### 📝 Encoder Observation: Limited pass support

Our encoder only supports:
- 1 pass: Cleanup only
- 3 passes: Cleanup + SigProp + MagRef
- 6 passes: Two rounds of three passes

This may be acceptable for lossless encoding but could limit lossy encoding flexibility.

## Remaining Decoder Issues

### 🔴 Decoder Issue #1: MaxPasses validation too restrictive

**File:** `src/SharpDicom/Codecs/Jpeg2000/Tier1/HtBlockEncoder.cs:215-220`

**Error:** `Number of passes must be between 1 and 6, got 35`

**Problem:**
```csharp
internal const int MaxPasses = 6;

if (numPasses < 1 || numPasses > MaxPasses)
{
    throw new ArgumentException(...);
}
```

OpenJPH encodes 35 passes for test images, but we reject anything > 6.

**Root cause:** Our decoder assumes HTJ2K uses ≤6 passes, but:
- ITU-T T.800 Table B.4 supports up to 164 passes
- OpenJPH may use many passes for quality layers or fractional bitplanes
- The packet header format is shared with baseline JPEG 2000

**Solution options:**
1. **Remove the check** - Trust the packet header (riskier but more flexible)
2. **Increase limit to 164** - Match the spec maximum
3. **Process first 6, skip rest** - Degraded quality but no crash
4. **Architectural change** - Support variable pass counts properly

**Recommended:** Option 2 (increase to 164) as a short-term fix, but investigate whether our 3-pass model needs redesign.

### 🔴 Decoder Issue #2: Cleanup segment size validation too strict

**File:** `src/SharpDicom/Codecs/Jpeg2000/Tier1/HtBitIO.cs:63-67`

**Error:** `Cleanup segment must be at least 2 bytes (ILW)`

**Problem:**
```csharp
if (segment.Length < 2)
{
    throw new ArgumentException(
        "Cleanup segment must be at least 2 bytes (ILW).", nameof(segment));
}
```

OpenJPH may encode empty or 1-byte segments for trivial codeblocks (all zeros).

**Solution:**
```csharp
if (segment.IsEmpty)
{
    // Empty segment - all coefficients are zero
    // (already initialized to zero by caller)
    return;
}

if (segment.Length < 2)
{
    throw new ArgumentException(...);
}
```

**Status:** Needs verification - check if empty segments are legal per ITU-T T.814.

## Fundamental Architecture Questions

1. **Pass count model:**
   - Our encoder: Fixed 1/3/6 passes
   - OpenJPH: Variable pass counts (saw 35)
   - Is OpenJPH using fractional bitplane coding?
   - Or is it encoding multiple quality layers?

2. **Quality layers:**
   - Do we need to support multi-layer encoding?
   - How does HTJ2K map quality layers to passes?

3. **Empty codeblocks:**
   - How should empty/trivial codeblocks be handled?
   - What's the minimum valid segment size?

## Testing Strategy

To resolve encoder issues:
1. Generate minimal test case (2×2 image, single codeblock)
2. Encode with our encoder, dump raw bytes
3. Encode same input with OpenJPH, dump raw bytes
4. Compare byte-by-byte to find first divergence
5. Use OpenJPH source tracing at failure point

To resolve decoder issues:
1. Short-term: Apply validation fixes (MaxPasses, empty segments)
2. Test against OpenJPH-encoded images with various pass counts
3. Long-term: Investigate if we need architectural changes for variable pass support

## Cross-Decoder Test Results

After COD marker fix:

| Test | Direction | Status | Error |
|------|-----------|--------|-------|
| 8-bit | Our→OJPH | ❌ | Error decoding a codeblock |
| 16-bit | Our→OJPH | ❌ | Error decoding a codeblock |
| 8-bit | OJPH→Our | ❌ | Cleanup segment < 2 bytes |
| 16-bit | OJPH→Our | ❌ | numPasses > 6 (got 35) |

All self-referential round-trip tests (86/86) still pass.

## References

- ITU-T T.814: HTJ2K specification
- ITU-T T.800: JPEG 2000 core (packet header format)
- OpenJPH source: `ojph_codeblock.cpp`, `ojph_block_encoder.cpp`
- Test failure logs: See conformance test output above
