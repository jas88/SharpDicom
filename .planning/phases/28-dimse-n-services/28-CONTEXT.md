---
phase: 28
title: DIMSE-N Services
status: context-gathered
date: 2026-02-06
---

# Phase 28: DIMSE-N Services — Implementation Context

## Phase Goal

Normalized object services (N-CREATE, N-SET, N-GET, N-DELETE, N-ACTION, N-EVENT-REPORT), MPPS workflow, Storage Commitment, and Async Operations Window negotiation with enforcement.

## Decisions

### 1. N-Service API Shape

**Handler registration (server-side): Separate interfaces**
- `INCreateHandler`, `INSetHandler`, `INGetHandler`, `INDeleteHandler`, `INActionHandler`, `INEventReportHandler`
- Services implement only the interfaces they support
- Mirrors DICOM reality: SOP classes use subsets of N-Services (MPPS uses N-CREATE + N-SET; Storage Commitment uses N-ACTION + N-EVENT-REPORT)

**Client API: Typed request classes**
- `NCreateRequest`, `NSetRequest`, `NGetRequest`, `NDeleteRequest`, `NActionRequest`, `NEventReportRequest`
- Each with typed properties: `AffectedSOPClassUID`, `AffectedSOPInstanceUID`, `AttributeList`, etc.
- Corresponding typed response classes with status codes

**N-EVENT-REPORT: Push events supported**
- Server can send N-EVENT-REPORT to connected SCU at any time during association
- Required for real MPPS and Storage Commitment workflows
- SCU registers event handler callback for incoming unsolicited events

**Instance lifecycle: Handler-managed**
- Framework passes SOP Instance UID to handlers; handlers manage their own persistence
- No framework-managed registry — avoids coupling to any particular storage mechanism
- Handlers decide how to store, retrieve, and validate instances

### 2. MPPS Workflow

**Scope: Full SCP + SCU**
- Complete MPPS provider (SCP) with state machine: InProgress → Completed, InProgress → Discontinued
- Complete MPPS user (SCU) with typed helper classes for creating/updating steps
- State transitions validated by framework

**IOD typing: Typed IOD helpers**
- `MppsInstance` class with typed properties:
  - `PerformedProcedureStepStatus` (enum: InProgress, Completed, Discontinued)
  - `ScheduledStepAttributesSequence`
  - `PerformedSeriesSequence`
  - `PerformedProtocolCodeSequence`
  - Required attribute enforcement
- Builder or factory methods for common workflows

**State machine: Validated**
- Framework enforces valid transitions:
  - InProgress → Completed ✓
  - InProgress → Discontinued ✓
  - Completed → anything ✗ (returns failure status)
  - Discontinued → anything ✗ (returns failure status)
- Invalid transitions return DIMSE failure status to requester

**Persistence: Pluggable**
- `IMppsPersistence` interface with Get/Put/Update operations
- Framework provides in-memory default implementation (cross-association within process lifetime)
- Users implement interface for database-backed production persistence
- Persistence injected via constructor/configuration on MPPS SCP handler

### 3. Storage Commitment

**Scope: Full SCP + SCU**
- Push Model SCP: receives N-ACTION requests, sends N-EVENT-REPORT results
- Push Model SCU: sends N-ACTION requests, receives N-EVENT-REPORT results
- Transaction UID tracking for correlating requests with results

**Association model: Both modes**
- Same-association (synchronous): results returned on the requesting association
- Reverse-association (asynchronous): SCP connects back to SCU on separate association to deliver results
- Configuration option per request
- SCU can optionally listen for incoming reverse associations (mini-server mode)

**Result typing: Typed result classes**
- `StorageCommitmentRequest` with `ReferencedSOPSequence` (list of SOP Class/Instance UIDs)
- `StorageCommitmentResult` with:
  - `TransactionUID`
  - `SuccessInstances` collection (SOP Class UID, SOP Instance UID)
  - `FailureInstances` collection (SOP Class UID, SOP Instance UID, FailureReason)
  - `EventTypeID` (Storage Commitment Request Successful / Failures Exist)

**Verification: Protocol only**
- `IStorageVerifier` callback interface
- Framework handles DIMSE protocol (N-ACTION/N-EVENT-REPORT encoding, association management)
- Users implement `IStorageVerifier.VerifyAsync(IReadOnlyList<SopInstanceReference>)` to check actual storage
- Clean separation: framework owns protocol, users own storage verification logic

### 4. Async Operations Window

**Scope: Full negotiation + enforcement**
- Negotiate `MaxOperationsInvoked` / `MaxOperationsPerformed` in A-ASSOCIATE
- Encode/decode 0x53 sub-item in User Information
- Track outstanding operations at runtime
- Enforce negotiated limits with backpressure

**Pipeline model: Channel-based**
- `System.Threading.Channels` for request/response correlation
- Bounded channel capacity = negotiated window size
- Natural fit for streaming patterns and async/await
- Enables concurrent DIMSE operations on a single association

**Window overflow: Reject with status**
- Operations exceeding negotiated window receive Processing Failure status
- Strict DICOM compliance — peer should respect negotiated limits
- No unbounded buffering, no association abort for minor violations

**Configuration: Per-association**
- Set window size when creating `DicomClient` or configuring `DicomServer` association handler
- `DicomClientOptions.AsyncOperationsInvoked` / `AsyncOperationsPerformed`
- Server-side handler can accept/reduce requested window during negotiation
- Optional per-presentation-context override for advanced use cases

## Existing Infrastructure

Phase 28 builds on:
- **Network layer** (Phase 10): DicomAssociation, PDU encoding, A-ASSOCIATE negotiation
- **DIMSE-C services** (Phase 11): C-STORE, C-FIND, C-MOVE, C-GET, C-ECHO — establishes patterns for DIMSE message handling
- **Server-side DIMSE** (Phase 24): DicomServer with handler dispatch, presentation context negotiation
- **FoDicom5.Compat** (Phase 26): NegotiateAsyncOps property exists but not wired to actual negotiation

## Key Constraints

1. **Zero-copy PDU parsing**: N-Service PDUs must follow the existing pattern of referencing pooled buffers
2. **Span<T>-first**: Attribute lists parsed via spans, minimal allocations
3. **Async-native**: All network operations async, CancellationToken throughout
4. **Multi-target**: Must work on netstandard2.0, net8.0, net9.0
5. **No external dependencies**: Pure .NET implementation

## Estimated Scope

- ~6 N-Service DIMSE message types (request + response pairs)
- ~4-6 handler interfaces
- MPPS SCP/SCU with state machine and typed IOD (~500-800 LOC)
- Storage Commitment SCP/SCU with typed results (~400-600 LOC)
- Async Operations Window negotiation + Channel-based enforcement (~300-500 LOC)
- Wire into existing FoDicom5.Compat NegotiateAsyncOps
- Tests for all workflows
