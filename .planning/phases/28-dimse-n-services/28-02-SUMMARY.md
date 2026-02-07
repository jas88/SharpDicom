# Phase 28 Plan 02: Async Operations Window Negotiation Summary

Async Operations Window (0x53) sub-item negotiation in PDU layer with UserInformation extensions, DicomClientOptions wiring, and FoDicom5.Compat NegotiateAsyncOps integration.

## Execution Details

| Field | Value |
|-------|-------|
| Phase | 28 |
| Plan | 02 |
| Duration | 5m 48s |
| Completed | 2026-02-07 |
| Tasks | 2/2 |
| Test Status | 4844 tests (4661 pass, 183 skipped, 0 failed) |

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Extend UserInformation and update PduWriter/PduReader | b6cbbcc | UserInformation.cs, PduWriter.cs, PduReader.cs |
| 2 | Add async ops to DicomClientOptions, wire FoDicom5.Compat, add Channels | 9efe644 | DicomClientOptions.cs, DicomClient.cs, SharpDicom.csproj, Directory.Packages.props, FoDicom5.Compat DicomClient.cs, NetworkCompatTests.cs |

## What Was Built

### UserInformation Async Operations Window (Task 1)

Added `MaxOperationsInvoked` and `MaxOperationsPerformed` ushort properties to `UserInformation`, defaulting to 1 (synchronous-only per DICOM spec). Added `HasAsyncOperations` computed property that returns true when either value differs from the default. Added `WithAsyncOperations(ushort, ushort)` convenience method following the existing `WithMaxPduLength` pattern. Updated `WithMaxPduLength` to preserve async ops values when creating copies.

### PDU Writer 0x53 Encoding (Task 1)

Added `WriteAsyncOperationsWindow` private method to `PduWriter` that writes the 0x53 sub-item with the standard variable item header (type byte, reserved byte, 2-byte length) followed by two uint16 big-endian values (invoked, performed). The `WriteUserInformation` method now conditionally calls this after writing the implementation version name, but only when `HasAsyncOperations` is true. `CalculateVariableItemsLength` now adds 8 bytes (4 header + 4 data) when async ops are non-default.

### PDU Reader 0x53 Decoding (Task 1)

Added `TryReadAsyncOperationsWindow` public method to `PduReader` that reads two uint16 big-endian values from a 4-byte payload. Defaults output parameters to 1 (synchronous) when insufficient data is available.

### DicomClientOptions Async Ops Properties (Task 2)

Added `AsyncOperationsInvoked` and `AsyncOperationsPerformed` ushort properties to `DicomClientOptions`, both defaulting to 1 (synchronous). These feed into the `UserInformation` created during association negotiation.

### DicomClient Association Wiring (Task 2)

Updated `DicomClient.ConnectAsync` and `SendAssociateRequestAsync` to create `UserInformation` with async ops values from `DicomClientOptions`. Added `_negotiatedMaxInvoked` and `_negotiatedMaxPerformed` private fields. Updated `ParseUserInformation` to handle the 0x53 sub-item type via a switch statement, calling `TryReadAsyncOperationsWindow` and computing the effective negotiated value as the minimum of proposed and accepted (with 0 = unlimited handled correctly).

### System.Threading.Channels Dependency (Task 2)

Added `System.Threading.Channels 10.0.2` to `Directory.Packages.props` and a conditional `PackageReference` in `SharpDicom.csproj` for `netstandard2.0` only (the type is inbox on net8.0+).

### FoDicom5.Compat NegotiateAsyncOps Wiring (Task 2)

Replaced the `NotSupportedException`-throwing implementation with one that maps fo-dicom convention (0 = default/synchronous) to DICOM spec convention (0 = unlimited, 1 = synchronous) and stores values as private fields. These values are applied to `DicomClientOptions` in `SendAsync`. Updated the test from expecting `NotSupportedException` to verifying successful acceptance of non-zero values.

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Conditional 0x53 encoding (only when non-default) | Avoids unnecessary sub-item in PDUs when using synchronous default, matching most DICOM implementations |
| fo-dicom 0 -> SharpDicom 1 mapping in compat layer | fo-dicom uses 0 for "default/synchronous" while DICOM spec uses 0 for "unlimited"; mapping prevents accidental unlimited negotiation |
| Negotiated value = min(proposed, accepted) with 0=unlimited | Follows DICOM PS3.7 D.3.3.3 semantics for async operations window negotiation |
| System.Threading.Channels conditional on netstandard2.0 only | The type is inbox on net8.0+ so the NuGet package is only needed for the netstandard2.0 target |

## Deviations from Plan

None - plan executed exactly as written.

## Files Modified

### Created
- (none)

### Modified
- `src/SharpDicom/Network/Items/UserInformation.cs` - Async ops properties, constructor params, convenience methods
- `src/SharpDicom/Network/Pdu/PduWriter.cs` - 0x53 sub-item encoding, length calculation
- `src/SharpDicom/Network/Pdu/PduReader.cs` - 0x53 sub-item decoding
- `src/SharpDicom/Network/DicomClientOptions.cs` - AsyncOperationsInvoked/Performed properties
- `src/SharpDicom/Network/DicomClient.cs` - Association negotiation wiring, negotiated value tracking
- `src/SharpDicom/SharpDicom.csproj` - System.Threading.Channels conditional reference
- `Directory.Packages.props` - System.Threading.Channels version
- `src/SharpDicom.FoDicom5.Compat/Network/Client/DicomClient.cs` - NegotiateAsyncOps wiring
- `tests/SharpDicom.FoDicom5.Compat.Tests/NetworkCompatTests.cs` - Updated test for non-zero values

## Next Phase Readiness

Plan 28-02 delivers the async operations window negotiation infrastructure. Plan 28-03 can build on this for DIMSE-N service implementations that benefit from concurrent operations. The `_negotiatedMaxInvoked`/`_negotiatedMaxPerformed` fields on `DicomClient` are ready for use by the DIMSE dispatch pipeline to control concurrency.
