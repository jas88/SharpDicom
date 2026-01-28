# Phase 10: Network Foundation - Context

**Gathered:** 2026-01-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish DICOM networking infrastructure with PDU handling, association negotiation, and basic connectivity verification (C-ECHO). This phase delivers DicomClient (SCU) and DicomServer (SCP) with C-ECHO support. Advanced DIMSE services (C-STORE, C-FIND, C-MOVE, C-GET) belong in Phase 11.

</domain>

<decisions>
## Implementation Decisions

### Connection handling
- No automatic retry on connection timeout — caller decides retry strategy
- No connection pooling — one DicomClient = one connection
- Configurable PDU parsing strictness (like DicomReaderOptions)
- Default ARTIM timeout: 30 seconds (standard DICOM conformance value)

### Server event model
- Func delegates for handlers (OnCEcho, OnAssociationRequest as `Func<T, ValueTask<R>>`)
- Task per association — simple model, .NET handles scheduling
- Graceful shutdown with timeout — StopAsync waits for active associations up to timeout, then aborts
- Reject unhandled SOP classes in negotiation — don't accept presentation contexts without handlers

### Association behavior
- Presentation contexts: infer defaults from operation but allow explicit override
- Default transfer syntax: Explicit VR Little Endian only
- Server transfer syntax acceptance: configurable whitelist
- Expose PDU hooks (OnPduReceived/OnPduSent) for debugging/logging

### Error reporting
- Exceptions extend existing DicomException hierarchy (DicomNetworkException, DicomAssociationException, etc.)
- Association rejection throws DicomAssociationRejectedException with RejectReason, Source
- Integrate with Microsoft.Extensions.Logging via ILogger<T>
- DicomStatus as first-class struct with Category, IsSuccess, well-known instances

### Claude's Discretion
- Exact state machine implementation for association
- Internal buffer sizes and pooling strategy
- Specific log event IDs and levels
- PDU reader/writer internal design (ref struct vs class)

</decisions>

<specifics>
## Specific Ideas

- "I want it to feel like a modern .NET library" — async-first, CancellationToken everywhere
- Match DCMTK for interoperability testing — success criteria include DCMTK storescu/storescp compatibility
- Follow patterns established in v1.0.0 — DicomReaderOptions-style configuration, similar exception hierarchy

</specifics>

<deferred>
## Deferred Ideas

- Connection pooling — may revisit if common pattern emerges
- Auto-retry with exponential backoff — could be a separate helper/extension

</deferred>

---

*Phase: 10-network-foundation*
*Context gathered: 2026-01-27*
