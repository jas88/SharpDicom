# OpenJPH HT Cleanup Pass Encoder Reference

This document contains the extracted OpenJPH encoder implementation for comparison with SharpDicom's HTJ2K encoder.

Source: https://github.com/aous72/OpenJPH (BSD 2-Clause License)

## Key Encoder Functions

### 1. EPS (Embedded Magnitude Bits) Computation

The `eps` (epsilon) value determines which samples in a quad have magnitude equal to the maximum magnitude in that quad. This is a 4-bit value where each bit corresponds to one sample in the quad.

**From `ojph_encode_codeblock32` (lines ~813-822):**

```cpp
int eps0 = 0;
if (u_q0 > 0)
{
  eps0 |= (e_q[0] == e_qmax[0]);
  eps0 |= (e_q[1] == e_qmax[0]) << 1;
  eps0 |= (e_q[2] == e_qmax[0]) << 2;
  eps0 |= (e_q[3] == e_qmax[0]) << 3;
}
```

**Key observations:**
- `eps` is only computed when `u_q > 0` (i.e., when there are magnitude bits to encode beyond the VLC)
- Each bit position corresponds to whether that sample's magnitude (`e_q[i]`) equals the quad's maximum magnitude (`e_qmax`)
- `e_q` values are computed as `32 - count_leading_zeros(--val)` where `val` is the doubled coefficient magnitude

### 2. VLC Table Lookup

**VLC table structure (lines ~64-66):**

```cpp
// VLC encoding
// index is (c_q << 8) + (rho << 4) + eps
// data is  (cwd << 8) + (cwd_len << 4) + eps
// table 0 is for the initial line of quads
static ui16 vlc_tbl0[2048] = { 0 };
static ui16 vlc_tbl1[2048] = { 0 };
```

**Lookup and encoding (lines ~827-829):**

```cpp
ui16 tuple0 = vlc_tbl0[(c_q0 << 8) + (rho[0] << 4) + eps0];
vlc_encode(&vlc, tuple0 >> 8, (tuple0 >> 4) & 7);
```

**Table indexing:**
- `c_q`: Context (4 bits) - computed from significance of neighboring quads
- `rho`: Significance pattern (4 bits) - which samples in the quad are significant
- `eps`: Embedded magnitude bits (4 bits) - which samples have max magnitude

**Table output format (ui16):**
- Bits 8-15: VLC codeword
- Bits 4-7: VLC codeword length (3 bits)
- Bits 0-3: Updated `eps` mask (which magnitude bits were conveyed by VLC)

### 3. MagSgn Encoding

**After VLC encoding, remaining magnitude bits are encoded (lines ~834-841):**

```cpp
int m = (rho[0] & 1) ? Uq0 - (tuple0 & 1) : 0;
ms_encode(&ms, s[0] & ((1U<<m)-1), m);
m = (rho[0] & 2) ? Uq0 - ((tuple0 & 2) >> 1) : 0;
ms_encode(&ms, s[1] & ((1U<<m)-1), m);
m = (rho[0] & 4) ? Uq0 - ((tuple0 & 4) >> 2) : 0;
ms_encode(&ms, s[2] & ((1U<<m)-1), m);
m = (rho[0] & 8) ? Uq0 - ((tuple0 & 8) >> 3) : 0;
ms_encode(&ms, s[3] & ((1U<<m)-1), m);
```

**Key formula:**
```
m = Uq - eps_bit_from_tuple
```

Where:
- `Uq`: Maximum magnitude bits in the quad (= `e_qmax + 1`)
- `eps_bit_from_tuple`: The bottom 4 bits of the VLC table result indicate which magnitude bits were conveyed
- `m`: Number of remaining magnitude bits to encode in MagSgn

**The `s` array contains (lines ~747-750, example for first sample):**

```cpp
val = t + t; //multiply by 2 and get rid of sign
val >>= p;  // 2 μ_p + x
val &= ~1u; // 2 μ_p
if (val)
{
  rho[0] = 1;
  e_q[0] = 32 - (int)count_leading_zeros(--val); //2μ_p - 1
  e_qmax[0] = e_q[0];
  s[0] = --val + (t >> 31); //v_n = 2(μ_p-1) + s_n
}
```

**Sample encoding format:**
```
s[i] = v_n = 2(μ_p - 1) + sign_bit
```

Where `μ_p` is the magnitude bitplane and the sign bit is added at the LSB.

### 4. ms_encode Function (lines ~520-540)

```cpp
static inline void
ms_encode(ms_struct* msp, ui32 cwd, int cwd_len)
{
  while (cwd_len > 0)
  {
    if (msp->pos >= msp->buf_size)
      OJPH_ERROR(0x00020005, "magnitude sign encoder's buffer is full");
    int t = ojph_min(msp->max_bits - msp->used_bits, cwd_len);
    msp->tmp |= (cwd & ((1U << t) - 1)) << msp->used_bits;
    msp->used_bits += t;
    cwd >>= t;
    cwd_len -= t;
    if (msp->used_bits >= msp->max_bits)
    {
      msp->buf[msp->pos++] = (ui8)msp->tmp;
      msp->max_bits = (msp->tmp == 0xFF) ? 7 : 8;
      msp->tmp = 0;
      msp->used_bits = 0;
    }
  }
}
```

**Bit stuffing:**
- Normally packs 8 bits per byte
- After writing 0xFF, the next byte can only hold 7 bits (bit stuffing for preventing marker codes)

### 5. VLC Table Initialization (lines ~95-142)

The VLC tables are built from source tables (`table0.h` and `table1.h`) with the following structure:

```cpp
struct vlc_src_table { int c_q, rho, u_off, e_k, e_1, cwd, cwd_len; };
```

**Table selection logic:**
- If `emb` (embedded bits available): select entry with `u_off == 1` and `(emb & e_k) == e_1`
- If no embedded bits: select entry with `u_off == 0`

**Result packing:**
```cpp
tgt_tbl[i] = (ui16)((best_entry->cwd<<8) + (best_entry->cwd_len<<4) + best_entry->e_k);
```

## Critical Divergence Points to Check

1. **EPS computation**: Verify that your encoder computes `eps` the same way (comparing each sample's magnitude to quad maximum)

2. **VLC table indexing**: Confirm the index formula `(c_q << 8) + (rho << 4) + eps` matches

3. **MagSgn bit count**: The formula `m = Uq - eps_conveyed_by_vlc` where `eps_conveyed_by_vlc` comes from the bottom 4 bits of the VLC table lookup result

4. **Sample format**: The `s[i]` values should be `2*(magnitude - 1) + sign_bit`

5. **Bit stuffing**: After 0xFF byte, only 7 bits available in next byte

6. **VLC table structure**: Two separate tables (tbl0 for first line, tbl1 for subsequent lines)

## Table0.h and Table1.h Format

Each entry in the source tables has the format:
```
{c_q, rho, u_off, e_k, e_1, cwd, cwd_len}
```

Example from table0.h:
```cpp
{0, 0x1, 0x0, 0x0, 0x0, 0x06, 4},  // c_q=0, rho=0x1, u_off=0, cwd=0x06 (4 bits)
{0, 0x1, 0x1, 0x1, 0x1, 0x3F, 7},  // c_q=0, rho=0x1, u_off=1, e_k=0x1, e_1=0x1, cwd=0x3F (7 bits)
```

The compiled VLC tables contain:
- **Bits 8-15**: The codeword to emit
- **Bits 4-7**: The codeword length (3 bits)
- **Bits 0-3**: The `e_k` mask (which magnitude bits were conveyed)

## Complete Encoder Flow

For each quad (2×2 samples):

1. **Compute magnitudes**: `e_q[i] = 32 - clz(2*abs(coeff) - 1)` for significant samples
2. **Find max**: `e_qmax = max(e_q[0..3])`
3. **Compute Uq**: `Uq = max(e_qmax, kappa)` where kappa depends on context
4. **Compute u_q**: `u_q = Uq - kappa`
5. **Compute eps**: If `u_q > 0`, set bit `i` of `eps` if `e_q[i] == e_qmax`
6. **VLC lookup**: `tuple = vlc_tbl[(c_q << 8) + (rho << 4) + eps]`
7. **Emit VLC**: Write codeword from tuple
8. **MagSgn encoding**: For each significant sample, encode `m = Uq - tuple_eps_bit[i]` bits from sample value

## UVLC Encoding (for u_q values)

UVLC encodes the u_q (remaining magnitude bitplanes) using a structured prefix/suffix/extension format. See `uvlc_init_tables()` (lines ~189-225) for the complete mapping.

## Additional Notes

- The encoder uses three separate bitstreams: MEL (forward), VLC (backward), and MagSgn (forward)
- Final layout: `[MagSgn data][MEL data][VLC data][Lcup word]`
- The Lcup word (last 2 bytes) encodes the MEL+VLC length for decoder navigation
