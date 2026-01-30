---
phase: 11-dimse-services
type: uat
status: in_progress
started: 2026-01-29
current_test: 1
---

# Phase 11: DIMSE Services - User Acceptance Testing

## Test Status Summary

| # | Test | Status | Result |
|---|------|--------|--------|
| 1 | C-STORE SCU sends file to SCP | pending | |
| 2 | C-STORE progress reporting | pending | |
| 3 | C-FIND fluent query builder | pending | |
| 4 | C-FIND IAsyncEnumerable streaming | pending | |
| 5 | C-MOVE SCU with destination AE | pending | |
| 6 | C-GET SCU receives inline datasets | pending | |
| 7 | DIMSE roundtrip tests pass | pending | |
| 8 | Protocol verification tests pass | pending | |

---

## Test 1: C-STORE SCU sends file to SCP

**Source:** 11-02-SUMMARY.md, 11-04-SUMMARY.md

**What to verify:**
CStoreScu can send a DICOM file to a DicomServer with C-STORE handler configured.

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "DimseRoundtripTests.CStore"
```

**Expected:** Tests pass showing C-STORE roundtrip works between SharpDicom SCU and SCP.

**Status:** pending
**Result:**
**Notes:**

---

## Test 2: C-STORE progress reporting

**Source:** 11-02-SUMMARY.md

**What to verify:**
CStoreScu reports progress via IProgress<DicomTransferProgress> during file transfer.

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "CStoreScuTests"
```

**Expected:** Tests pass validating DicomTransferProgress with BytesTransferred, TotalBytes, PercentComplete, EstimatedTimeRemaining.

**Status:** pending
**Result:**
**Notes:**

---

## Test 3: C-FIND fluent query builder

**Source:** 11-03-SUMMARY.md

**What to verify:**
DicomQuery fluent builder creates proper C-FIND identifiers:
- ForStudies(), ForPatients(), ForSeries(), ForImages()
- WithPatientName(), WithStudyDate(), WithModality()
- ReturnField() for additional return keys

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "CFindScuTests"
```

**Expected:** Tests pass showing DicomQuery creates correct datasets with Q/R level and return fields.

**Status:** pending
**Result:**
**Notes:**

---

## Test 4: C-FIND IAsyncEnumerable streaming

**Source:** 11-03-SUMMARY.md

**What to verify:**
CFindScu.QueryAsync returns IAsyncEnumerable<DicomDataset> that streams results as they arrive.

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "CFindScuTests.QueryAsync"
```

**Expected:** Tests pass showing streaming behavior with yield return pattern.

**Status:** pending
**Result:**
**Notes:**

---

## Test 5: C-MOVE SCU with destination AE

**Source:** 11-05-SUMMARY.md

**What to verify:**
CMoveScu.MoveAsync sends C-MOVE request with MoveDestination field and tracks sub-operation progress.

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "CMoveScuTests"
```

**Expected:** Tests pass showing:
- MoveDestination included in C-MOVE-RQ command
- CMoveProgress tracks Remaining/Completed/Failed/Warning counts
- IsFinal, IsSuccess, IsPartialSuccess properties work correctly

**Status:** pending
**Result:**
**Notes:**

---

## Test 6: C-GET SCU receives inline datasets

**Source:** 11-06-SUMMARY.md

**What to verify:**
CGetScu handles interleaved C-STORE sub-operations on same association:
- Receives C-STORE-RQ with dataset
- Sends C-STORE-RSP
- Tracks sub-operation progress
- CGetProgress.ReceivedDataset contains retrieved data

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "CGetScuTests"
```

**Expected:** 86 tests pass validating C-GET protocol handling.

**Status:** pending
**Result:**
**Notes:**

---

## Test 7: DIMSE roundtrip tests pass

**Source:** 11-07-SUMMARY.md

**What to verify:**
Internal roundtrip tests with SharpDicom client and server work correctly.

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "DimseRoundtripTests"
```

**Expected:** 9 tests pass covering:
- C-STORE file and dataset roundtrip
- Progress reporting
- Cancellation handling
- Multiple files on same association
- Transfer syntax negotiation

**Status:** pending
**Result:**
**Notes:**

---

## Test 8: Protocol verification tests pass

**Source:** 11-07-SUMMARY.md

**What to verify:**
Protocol correctness per DICOM PS3.7/PS3.8:
1. Identifier uses negotiated Transfer Syntax
2. PDV fragmentation respects MaxPduLength
3. Last fragment flag set correctly
4. Sub-operation counts extracted properly
5. MoveDestination AE padded to even length
6. SCP Role Selection for C-GET

**How to verify:**
```bash
dotnet test tests/SharpDicom.Tests/SharpDicom.Tests.csproj --filter "DimseProtocolVerificationTests"
```

**Expected:** 12 tests pass validating protocol compliance.

**Status:** pending
**Result:**
**Notes:**

---

## UAT Session Log

### Session Started: 2026-01-29

