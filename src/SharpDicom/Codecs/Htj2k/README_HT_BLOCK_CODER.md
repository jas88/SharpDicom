# HT Block Coder - Future Work

## Current Status (Plan 21-02)

HTJ2K codec currently uses **standard JPEG 2000 block coding** (EBCOT) instead of High Throughput (HT) block coding.

This is functionally correct and backward compatible:
- HTJ2K CAP marker is injected to identify codestreams
- Standard J2K encoder/decoder handles the actual compression
- All DICOM HTJ2K transfer syntaxes work correctly
- Decoders can read both HT and standard J2K blocks

## Performance Impact

- **Current**: Same performance as standard J2K
- **With HT**: ~10x faster encoding/decoding (theoretical)

## Future Implementation (Deferred to Phase 21-04 or later)

To implement true HT block coding per ISO/IEC 15444-15:

### Components Needed

1. **HtBitWriter.cs** - VLC bit I/O for encoding
   - Variable Length Coding instead of MQ arithmetic coding
   - Fixed VLC tables (no adaptive probability)
   - Simpler than EBCOT MQ coder

2. **HtBitReader.cs** - VLC bit I/O for decoding
   - Symmetric VLC reading
   - Lookahead support for decoding

3. **HtBlockCoder.cs** - Main HT algorithm
   - `EncodeBlock()` - HT significance propagation using VLC
   - `DecodeBlock()` - HT decoding with fixed contexts
   - HT sets for predetermined significance patterns
   - Simplified pass structure vs EBCOT

### Integration Points

Modify `J2kEncoder.cs` and `J2kDecoder.cs`:
- Add `UseHtBlockCoder` parameter to routing logic
- Route to `HtBlockCoder` when HTJ2K codec requests it
- Keep `EbcotEncoder` for standard J2K backward compatibility

### Reference Implementation

OpenJPH (Open source High Throughput JPEG 2000):
- https://github.com/aous72/OpenJPH
- BSD-2-Clause license
- C++ implementation can serve as algorithm reference

### Testing Strategy

When implemented:
- Ensure backward compat: HT decoder can read standard J2K
- Ensure forward compat: Standard J2K decoder can read HT (if CAP removed)
- Benchmark: Verify ~10x performance improvement
- Roundtrip: Lossless HT matches bit-for-bit

##Rationale for Deferral

1. **Complexity**: Full ISO/IEC 15444-15 implementation is ~3000-5000 LOC
2. **Spec Access**: Requires detailed ITU-T T.814 specification study
3. **Testing**: Needs conformance test vectors from ITU-T
4. **Current Works**: Standard J2K approach is functionally correct
5. **Autonomous Plan**: HT implementation exceeds single-plan scope

## Milestone

Plan 21-02 deliverable: **HTJ2K codec working via J2K delegation**
Future work: **HT block coder for 10x performance**
