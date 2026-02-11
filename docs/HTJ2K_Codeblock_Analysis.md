# HTJ2K Codeblock Encoding Analysis

Date: 2026-02-09

## Status

Marker-level fixes are complete and verified. OpenJPH now accepts our codestream headers but fails at codeblock decode with "Error decoding a codeblock."

## Problem

The codeblock data (packet contents after SOD marker) is completely different from OpenJPH output:

### Single Pixel Test (pixel[0]=1, rest=0)

**Our output after SOD:**
```
FF C4 7F C3 FF 88 00 FE 00 63 00 00 63 00 FE 00 63 00 FF D9
```

**OpenJPH output after SOD:**
```
C0 25 FD 01 07 74 00 00 00 C0 11 A0 09 00 00 63 00 FE 00 63 00 00 00 FF D9
```

Both end with `FF D9` (EOI) but the packet data is entirely different.

## Code Investigation

The encoder IS using VLC tables (not raw 4-bit patterns as initially suspected):

```csharp
// From HtCleanup.cs line 107-112
ushort tuple0 = encTbl[(cq0 << 8) | (rho0 << 4) | eps0];
int cwd0 = tuple0 >> 8;
int cwdLen0 = (tuple0 >> 4) & 7;
ek0 = tuple0 & 0xF;
writer.WriteVlcBits((uint)cwd0, cwdLen0);
```

The VLC tables are populated from correct source data (Table0SourceData, Table1SourceData) matching OpenJPH's table.h format.

## Root Causes (Likely)

The issue is in the **Tier 1 codeblock encoding** implementation, specifically in one or more of:

1. **HtCleanupWriter stream assembly**: The MagSgn, MEL, and VLC streams may not be assembled in the correct format or byte order

2. **Bit ordering**: VLC codewords might be written in wrong bit order (MSB vs LSB first)

3. **Stream interleaving**: The three streams (MagSgn forward, VLC backward, MEL forward) might not be correctly positioned or sized

4. **Packet header encoding**: The packet header format might be incorrect (though less likely since headers parse OK)

5. **Context calculation**: The 3-bit context `c_q` for VLC table lookup might be calculated incorrectly

6. **Kappa calculation**: The `kappa` value for magnitude coding bound might be wrong

7. **EMB (Embedded Magnitude Bits)**: The calculation of which samples have MSB equal to quad's max exponent might be incorrect

## Recommendations

### Option 1: Detailed Bit-Level Debugging

Create instrumented versions of:
- HtCleanupWriter that logs every bit written to each stream
- HtCleanup.Encode that logs VLC table lookups and context values
- Compare bit-by-bit with OpenJPH debug output for same input

This is time-consuming but methodical.

### Option 2: Reference OpenJPH Directly

Study OpenJPH's `ojph_block_encoder.cpp` in extreme detail:
- `ojph_encode_codeblock()` function
- `frwd_xform()` for VLC encoding
- `frwd_init()` for stream initialization
- Compare our implementation line-by-line

### Option 3: Use OpenJPH Block Encoder

Consider P/Invoking to OpenJPH's block encoder for HTJ2K while keeping our JPEG 2000 baseline encoder. This would:
- Guarantee conformance for HTJ2K
- Reduce maintenance burden
- Allow focus on other codec features

### Option 4: Incremental Validation

Start with the absolute simplest case (all zeros) and verify:
1. VLC stream is correct (should be minimal/empty)
2. MEL stream is correct
3. MagSgn stream is correct
4. Then gradually add complexity (one pixel, two pixels, etc.)

## Files to Focus On

| File | Purpose |
|------|---------|
| `Tier1/HtCleanup.cs` | Main HT Cleanup pass logic |
| `Tier1/HtBitIO.cs` | HtCleanupWriter - stream assembly |
| `Tier1/VlcTable.cs` | VLC table data and lookup |
| `Tier1/MelCoder.cs` | MEL encoding/decoding |
| `Tier2/PacketEncoder.cs` | Packet header formatting |

## External References

- OpenJPH source: https://github.com/aous72/OpenJPH
  - `src/core/coding/ojph_block_encoder.cpp`
  - `src/core/coding/ojph_block_common.h`
- ITU-T T.814 (HTJ2K spec): Section 7.3 (HT Cleanup pass)
- ITU-T T.800 (JPEG 2000 baseline): Section B (Coding processes)

## Next Steps

1. Create minimal test harness that encodes single codeblock
2. Add extensive logging to HtCleanupWriter
3. Compare our stream output byte-by-byte with OpenJPH
4. Fix stream assembly issues
5. Verify with OpenJPH decode

Estimated effort: 2-4 hours for debugging, potentially more for fixes depending on root cause complexity.
