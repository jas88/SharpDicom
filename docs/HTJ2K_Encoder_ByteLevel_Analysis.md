# HTJ2K Encoder Byte-Level Comparison with OpenJPH

Date: 2026-02-09
Test case: 8×8 grayscale image, pixels [0-3]=1, rest=0

## File Sizes

- **SharpDicom**: 174 bytes
- **OpenJPH**: 171 bytes
- **Difference**: +3 bytes (ours is larger)

## Marker-by-Marker Comparison

### Markers 0x00-0x2D: IDENTICAL ✅

Both outputs are identical through:
- SOC (0xFF4F)
- SIZ (0xFF51) - Image and tile size
- COD (up to 0x2D) - Coding style

### Divergence #1: CAP Marker Length (offset 0x30-0x37)

**Ours (offset 0x30):**
```
0030: 08 00 02 00 00 00 28 FF 52
      ^^       ^^       ^^
      Lcap     Ccap[0]  Ccap[15]
```

**OpenJPH (offset 0x30):**
```
0030: 08 00 02 00 00 00 02 FF 52
                        ^^
```

**Analysis:**
- Lcap (length): Both = 0x0008 ✅
- Pcap: Both = 0x0002 ✅
- Ccap[0]: Both = 0x0000 ✅
- **Ccap[15]: Ours=0x28 (40), OpenJPH=0x02 (2)** ❌

**Ccap[15] encoding per ITU-T T.814:**
- Bit 0: HTJ2K basic mode (both have this set)
- Bit 5: Number of codeblocks flag (our bit 5 is set, theirs is not)

**Impact:** Our Ccap[15]=0x28 may signal extra capabilities that OpenJPH doesn't recognize.

### Divergence #2: QCD Marker (offset 0x38-0x50)

**Ours:**
```
0038: FF 52 00 0C 00 00 00 01 00 05 04 04 40 01
      Marker Lqcd Sqcd SPqcd (step sizes x5)
0048: FF 5C 00 14 00 08 08 08 08 08 08 08 08 08 08 08 08 08 08 08 00
      QCC marker with 17 step sizes (16 + padding byte)
```

**OpenJPH:**
```
0038: FF 52 00 0C 00 02 00 01 00 05 04 04 40 01
                     ^^
0048: FF 5C 00 13 20 48 50 50 50 50 50 50 50 50 50 50 50 48 48 48
      QCC with different length and data
```

**Analysis - QCD:**
- Sqcd: Ours=0x00 (no quantization), OpenJPH=0x02 (scalar derived with no quantization) ❌
- SPqcd values mostly match

**Analysis - QCC:**
- Our length: 0x0014 (20 bytes)
- OpenJPH length: 0x0013 (19 bytes)
- Our data: All 0x08 (exponent=8, mantissa=0)
- OpenJPH data: Mix of 0x20,0x48,0x50 (different exponent/mantissa encoding)

**Impact:** QCD/QCC parameter mismatch may cause OpenJPH to apply incorrect dequantization.

### Divergence #3: COM Marker (offset 0x64)

**OpenJPH has COM marker:**
```
0060: FF 64 00 17 00 01 4F 70 65 6E 4A 50 48 20 56 65 72 20 30 2E 32 36 2E 30 2E
      Marker Lcom Rcom "OpenJPH Ver 0.26.0."
```

**We don't have COM marker** - goes straight to SOT.

**Impact:** None (comment markers are optional).

### Divergence #4: SOT and Packet Data

**Ours (starting 0x60 in our file, 0x78 in OpenJPH):**
```
Ours offset 0x60:   FF 58 00 04 00 3D
OpenJPH offset 0x78: FF 90 00 0A 00 00 00 00 00 36 00 01

Our 0x90: FF 90 00 0A 00 00 00 00 00 51 00 01
OpenJPH: FF 90 00 0A 00 00 00 00 00 36 00 01
                                    ^^
         SOT    Lsot Isot Psot         Tpsot TNsot
```

**Psot (tile-part length):**
- Ours: 0x00000051 (81 bytes)
- OpenJPH: 0x00000036 (54 bytes)
- **Difference: 27 bytes** (our tile data is larger)

### Divergence #5: Packet Header and Codeblock Data

**Ours (offset 0x80+):**
```
0080: 63 00 00 63 00 00 63 00 04 00 00 00 08 00 00 00
0090: 0D 00 00 00 FE 17 73 00 00 00 00 00 01 00 00 00
00A0: 00 FE 00 63 00 00 63 00 FE 60 23 00
```

**OpenJPH (offset 0x80+):**
```
0080: 93 C0 25 FD 01 07 74 00 00 00 C0 11 C0 11 C0 12
0090: 00 00 63 00 00 63 00 FE 17 73 00 C0 12 00 FE 00
00A0: 63 00 C0 24 00 FE 60 23 00
```

**Analysis:**
The packet data is completely different starting immediately after SOD. This includes:
- Packet headers (progression order, layer info)
- Codeblock contributions (our cleanup pass data)

**Key observation:** Both end with similar sequences:
- Ours: `FE 00 63 00 00 63 00 FE 60 23 00 FF D9`
- OpenJPH: `FE 00 63 00 C0 24 00 FE 60 23 00 FF D9`

The `FF D9` (EOI marker) matches, and the `FE 60 23` pattern appears in both, suggesting some structural similarity in the packet format.

## Root Cause Analysis

### Issue #1: QCD Sqcd mismatch
**Our Sqcd = 0x00** (no quantization)
**OpenJPH Sqcd = 0x02** (scalar derived, no quantization)

Per ITU-T T.800 Table A.28:
- 0x00 = no quantization (reversible)
- 0x02 = scalar derived (can be reversible if step sizes indicate no quantization)

**Fix:** Use Sqcd=0x02 for HTJ2K to match OpenJPH convention.

### Issue #2: QCC step size encoding
Our step sizes are all 0x08, which means exponent=8, mantissa=0.
OpenJPH uses varied step sizes (0x20, 0x48, 0x50).

**Impact:** OpenJPH may interpret our quantization parameters differently, leading to incorrect coefficient reconstruction.

**Fix:** Match OpenJPH's step size calculation for lossless mode.

### Issue #3: CAP Ccap[15] extra flags
Our Ccap[15]=0x28 has bit 5 set, while OpenJPH uses 0x02.

**Fix:** Only set required HTJ2K capability bits (bit 0), not optional extension bits.

### Issue #4: Packet data format mismatch
The codeblock data is completely different. This could be due to:
1. Incorrect VLC codeword encoding (still using raw 4-bit instead of table lookup)
2. MagSgn bit packing differences
3. ILW format issues
4. Packet header differences

**Next steps:** Need to dump the raw HT Cleanup pass output before packet wrapping to isolate whether the issue is in:
- Tier1 (codeblock encoding)
- Tier2 (packet formation)
- Codestream assembly

## Action Items

1. ✅ Fix decoder validation (MaxPasses, empty segments) - **DONE**
2. ✅ Fix COD marker HT mode flag - **DONE**
3. ✅ Fix CAP Ccap[15] to 0x0002 (HTJ2K basic mode only) - **DONE**
4. ✅ Fix QCD Sqcd to use guard bits without quantization style bits - **DONE**
5. ✅ Implement BIBO gain-based QCD step size calculation - **DONE**
6. ✅ Fix QCD marker - **DONE** (was dotnet-script cache issue)
7. 🔴 Fix codeblock encoding - OpenJPH now accepts headers but fails at codeblock decode

## Test Commands

```bash
# Create test image
printf 'P5\n8 8\n255\n' > /tmp/test.pgm
printf '\x01\x01\x01\x01' >> /tmp/test.pgm
dd if=/dev/zero bs=1 count=60 >> /tmp/test.pgm 2>/dev/null

# Encode with OpenJPH
ojph_compress -i /tmp/test.pgm -o /tmp/openjph.j2c -reversible true

# Encode with SharpDicom (use diagnostic tool)
cd /tmp/htj2k_test && dotnet run

# Compare
diff <(xxd /tmp/sharpdicom_htj2k_8x8.j2c) <(xxd /tmp/openjph.j2c)
```
