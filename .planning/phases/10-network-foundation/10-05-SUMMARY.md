---
phase: 10-network-foundation
plan: 05
subsystem: network
tags: [dicom-client, dimse, c-echo, scu, tcp]
dependency-graph:
  requires: ["10-03", "10-04"]
  provides: ["DicomClient SCU", "DIMSE command infrastructure", "C-ECHO operation"]
  affects: ["10-06 DicomServer", "10-07 integration tests", "11-* DIMSE services"]
tech-stack:
  added: []
  patterns: ["IAsyncDisposable", "async TCP", "CancellationToken", "Implicit VR command serialization"]
file-tracking:
  created:
    - src/SharpDicom/Network/Dimse/CommandField.cs
    - src/SharpDicom/Network/Dimse/DicomCommand.cs
    - src/SharpDicom/Network/DicomClient.cs
    - src/SharpDicom/Network/DicomClientOptions.cs
    - tests/SharpDicom.Tests/Network/DicomClientTests.cs
  modified:
    - src/SharpDicom/Data/DicomTag.WellKnown.cs
    - src/SharpDicom/Data/DicomUID.WellKnown.cs
decisions:
  - id: "10-05-01"
    description: "Command datasets always use Implicit VR Little Endian"
    rationale: "DICOM PS3.7 requires command elements to use Implicit VR regardless of association transfer syntax"
  - id: "10-05-02"
    description: "Static VR lookup table for command elements"
    rationale: "Group 0000 elements have fixed VRs per PS3.7, no dictionary lookup needed"
  - id: "10-05-03"
    description: "BufferWriter type alias for netstandard2.0 compatibility"
    rationale: "ArrayBufferWriter<T> not available on netstandard2.0, use polyfill"
  - id: "10-05-04"
    description: "IDicomElement interface for command dataset iteration"
    rationale: "DicomDataset implements IEnumerable<IDicomElement>, not IEnumerable<DicomElement>"
metrics:
  duration: "45 minutes"
  completed: "2026-01-28"
---

# Phase 10 Plan 05: DicomClient SCU with C-ECHO Summary

DicomClient SCU implementation with TCP connectivity, association negotiation, and C-ECHO verification support.

## One-liner

DicomClient with async Connect/Release/Abort and CEchoAsync for DICOM SCU operations

## What Was Built

### Task 1: DIMSE Command Infrastructure

Created the Network/Dimse directory with command field constants and DicomCommand wrapper.

**CommandField.cs**: Constants for all DIMSE command types per PS3.7 Section 9.1

| Command | Request | Response |
|---------|---------|----------|
| C-STORE | 0x0001 | 0x8001 |
| C-GET | 0x0010 | 0x8010 |
| C-FIND | 0x0020 | 0x8020 |
| C-MOVE | 0x0021 | 0x8021 |
| C-ECHO | 0x0030 | 0x8030 |
| N-EVENT-REPORT | 0x0100 | 0x8100 |
| N-GET | 0x0110 | 0x8110 |
| N-SET | 0x0120 | 0x8120 |
| N-ACTION | 0x0130 | 0x8130 |
| N-CREATE | 0x0140 | 0x8140 |
| N-DELETE | 0x0150 | 0x8150 |
| C-CANCEL | 0x0FFF | - |

Helper methods: `IsRequest()`, `IsResponse()`, `ToRequest()`, `ToResponse()`

**DicomCommand.cs**: Wrapper for DIMSE command datasets with factory methods

- Properties for accessing command elements: AffectedSOPClassUID, CommandFieldValue, MessageID, Status, etc.
- Factory methods: CreateCEchoRequest, CreateCEchoResponse, CreateCStoreRequest, CreateCStoreResponse
- Helper properties: HasDataset, IsRequest, IsResponse, IsCEchoRequest, etc.

**Well-known tags and UIDs**: Added Group 0000 command tags to DicomTag.WellKnown.cs (25+ tags) and Verification SOP Class UID to DicomUID.WellKnown.cs

### Task 2: DicomClient and DicomClientOptions

**DicomClientOptions.cs**: Configuration for DICOM SCU connections

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Host | string | required | Remote DICOM AE hostname/IP |
| Port | int | required | TCP port |
| CalledAE | string | required | Remote AE title |
| CallingAE | string | required | Local AE title |
| ConnectionTimeout | TimeSpan | 30s | TCP connect timeout |
| AssociationTimeout | TimeSpan | 30s | ARTIM timeout |
| DimseTimeout | TimeSpan | 60s | Response timeout |
| MaxPduLength | uint | 16384 | Max PDU size |

Validate() method throws for invalid options.

**DicomClient.cs**: Full SCU implementation with IAsyncDisposable

| Method | Description |
|--------|-------------|
| ConnectAsync | TCP connect + A-ASSOCIATE-RQ/AC negotiation |
| CEchoAsync | Send C-ECHO request, await response |
| ReleaseAsync | A-RELEASE-RQ/RP graceful disconnect |
| Abort | A-ABORT immediate disconnect |
| DisposeAsync | Release if connected, close resources |

Key implementation details:
- TCP connection with timeout via CancellationTokenSource.CreateLinkedTokenSource
- Association negotiation parsing A-ASSOCIATE-AC variable items
- DIMSE command serialization using Implicit VR Little Endian (per PS3.7)
- PDU receive with proper TCP fragmentation handling
- ArrayBufferWriter polyfill for netstandard2.0 compatibility

### Task 3: Unit Tests

**DicomClientTests.cs**: 30 unit tests covering:

1. DicomClientOptions validation (15 tests)
   - Host, Port, CalledAE, CallingAE validation
   - Timeout validation
   - MaxPduLength minimum (4096)

2. DicomClient state tests (5 tests)
   - Null options throws ArgumentNullException
   - Invalid options throws ArgumentException
   - IsConnected false before connect
   - Association null before connect
   - Disposal can be called multiple times

3. DicomCommand tests (6 tests)
   - CreateCEchoRequest produces valid command
   - CreateCEchoResponse produces valid command
   - CreateCStoreRequest/Response tests

4. CommandField tests (4 tests)
   - IsRequest returns true for request commands
   - IsResponse returns true for response commands
   - ToRequest/ToResponse convert correctly

## Technical Details

### Command Serialization

Commands are serialized to Implicit VR Little Endian per PS3.7:
1. Calculate total length of all elements (excluding group length)
2. Write CommandGroupLength (0000,0000) with calculated value
3. Write elements in tag order: Tag (4 bytes) + VL (4 bytes) + Value

VR lookup is hardcoded since command elements have fixed VRs:
```csharp
private static DicomVR GetCommandVR(ushort element) => element switch
{
    0x0000 => DicomVR.UL, // CommandGroupLength
    0x0002 => DicomVR.UI, // AffectedSOPClassUID
    0x0100 => DicomVR.US, // CommandField
    // ... etc
};
```

### PDU Handling

- PDU header: 6 bytes (type + reserved + 4-byte length)
- All PDU lengths are Big-Endian (network byte order)
- ReadExactlyAsync (.NET 6+) or manual loop (netstandard2.0) for complete reads

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 1445b9b | feat | DIMSE command infrastructure |
| e1c9f2b | feat | DicomClient SCU with CEchoAsync |
| 7f65044 | test | DicomClient and DicomClientOptions tests |

## Verification

```bash
$ dotnet build src/SharpDicom/SharpDicom.csproj
Build succeeded. 0 Warning(s) 0 Error(s)

$ dotnet test --project tests/SharpDicom.Tests --filter "DicomClientTests"
Passed: 30, Failed: 0, Skipped: 0

$ dotnet test --project tests/SharpDicom.Tests
Passed: 1219, Failed: 0, Skipped: 1
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed DicomElement type hierarchy**
- Issue: Plan assumed DicomNumericElement/DicomStringElement inherit from DicomElement
- Reality: They implement IDicomElement interface
- Fix: Changed to use IDicomElement and RawValue property

**2. [Rule 3 - Blocking] Fixed ArrayBufferWriter compatibility**
- Issue: ArrayBufferWriter<T> not available on netstandard2.0
- Fix: Added type alias pattern (BufferWriter) matching DicomServer.cs

**3. [Rule 1 - Bug] Fixed DicomDataset enumeration type**
- Issue: Plan assumed `foreach (var element in dataset)` yields DicomElement
- Reality: DicomDataset implements IEnumerable<IDicomElement>
- Fix: Changed variable type to IDicomElement

## Next Phase Readiness

Plan 05 provides DicomClient SCU that Plan 07 (Integration Tests) will test against DCMTK.

**Ready for:**
- Plan 07: Integration testing with DCMTK storescp/echoscu
- Phase 11: Additional DIMSE services (C-STORE, C-FIND, C-MOVE, C-GET)

**Dependencies satisfied:**
- DicomClient uses DicomAssociation (10-04)
- DicomClient uses PduReader/PduWriter (10-03)
- DicomCommand uses DicomDataset, DicomTag, DicomUID (Phase 1)

**Test coverage:**
- 30 new tests for DicomClient/DicomClientOptions/DicomCommand
- Full test suite: 1220 tests (1219 passed, 1 skipped)
