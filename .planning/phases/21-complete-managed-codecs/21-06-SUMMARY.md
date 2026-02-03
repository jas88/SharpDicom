---
phase: 21-complete-managed-codecs
plan: 06
subsystem: codecs
tags: [jpeg2000, htj2k, mq-coder, ebcot, wavelet]

# Dependency graph
requires:
  - phase: 21-05
    provides: JPEG-LS encoder/decoder fixes
provides:
  - MQ coder uniform coding fix for equal-probability symbols
affects: [htj2k-codec, jpeg2000-codec]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "MQ arithmetic coder interval splitting for uniform coding"

key-files:
  created: []
  modified:
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/MqCoder.cs

key-decisions:
  - "Use interval halving for uniform coding (not Qe=0x5601)"
  - "Defer EBCOT and tier-2 fixes to future iteration"

patterns-established:
  - "MQ uniform coding: split interval in half, not subtract Qe"

# Metrics
duration: 90min
completed: 2026-02-03
status: partial
---

# Phase 21 Plan 06: J2K/HTJ2K Bug Fixes Summary

**MQ coder uniform coding fixed; EBCOT and tier-2 issues identified but deferred**

## Performance

- **Duration:** ~90 min
- **Started:** 2026-02-03T05:38:53Z
- **Completed:** 2026-02-03T07:10:00Z
- **Tasks:** 1 of 3 completed
- **Files modified:** 1

## Status: PARTIAL COMPLETION

This gap-closure plan revealed more issues than anticipated. The J2K/HTJ2K roundtrip failures stem from multiple interacting bugs:

1. **MQ coder uniform coding** - FIXED
2. **EBCOT encoder/decoder asymmetry** - Identified, not fixed
3. **Tier-2 packet encoding mismatch** - Identified, not fixed

## Accomplishments

- Fixed MQ coder uniform coding to be symmetric between encoder and decoder
- Identified EBCOT state tracking asymmetry as a source of roundtrip failures
- Identified tier-2 packet number-of-passes encoding mismatch

## Task Commits

1. **Task 1: Diagnose and fix MQ coder** - `4e2a582` (fix)

**Plan incomplete - Tasks 2 and 3 blocked by deeper issues**

## Files Modified

- `src/SharpDicom/Codecs/Jpeg2000/Tier1/MqCoder.cs` - Fixed uniform coding symmetry

## Decisions Made

1. **Use interval halving for uniform coding**: Changed from Qe-based probability estimation to simple interval halving for uniform (equal probability) symbol coding. This is more correct for raw/bypass mode coding.

2. **Defer EBCOT and tier-2 fixes**: The complexity of fixing EBCOT 3-pass encoding and tier-2 packet format exceeds the scope of this gap-closure plan. These require dedicated investigation.

## Deviations from Plan

### Blocked Work

**EBCOT encoder/decoder asymmetry**
- **Found during:** Task 1 diagnostic testing
- **Issue:** The magnitude refinement pass skipping condition differs between encoder and decoder
- **Status:** Investigation complete, fix deferred - requires careful analysis of ITU-T T.800 specification

**Tier-2 packet encoding mismatch**
- **Found during:** Task 1 end-to-end testing
- **Issue:** `WriteNumPasses` uses `1111xxxxx` for passes 6-36, but decoder expects `1111` as prefix for extended coding
- **Status:** Investigation complete, fix deferred - requires coordinated changes to both encoder and decoder

---

**Total deviations:** 2 deferred issues
**Impact on plan:** Plan objectives not fully met. HTJ2K tests remain disabled.

## Issues Encountered

1. **Pre-existing HTJ2K conformance failures**: 5 tests in Htj2kConformanceTests were already failing before any changes (interop with ojph). These are not new regressions.

2. **Complex bug interactions**: The J2K pipeline has multiple interacting components (DWT, EBCOT, tier-2). Fixing one component in isolation doesn't enable roundtrip because other components have bugs.

## Next Phase Readiness

**Ready:**
- MQ coder uniform coding now correct
- DWT 5/3 lossless transform verified correct

**Blocked:**
- HTJ2K tests remain disabled
- Full J2K lossless roundtrip not working

**Recommendations for future work:**
1. Create dedicated plan for EBCOT encoder/decoder synchronization
2. Create dedicated plan for tier-2 packet format compliance
3. Add unit tests for each component in isolation before integration testing

---
*Phase: 21-complete-managed-codecs*
*Completed: 2026-02-03*
*Status: Partial - MQ coder fixed, EBCOT and tier-2 deferred*
