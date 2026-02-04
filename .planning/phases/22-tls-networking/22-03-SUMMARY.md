---
phase: 22-tls-networking
plan: 03
subsystem: network
tags: [tls, ssl, server, dicom-networking, scp, authentication]

# Dependency graph
requires:
  - phase: 22-01-tls-configuration
    provides: TlsServerOptions, DicomTlsProfile, CertificateValidator
  - phase: 10-network-foundation
    provides: DicomServer, DicomAssociation, PDU handling
provides:
  - TLS-capable DicomServer with optional encryption
  - Server-side TLS handshake with timeout enforcement
  - DICOM BCP 195 profile enforcement for server connections
  - SslStreamCertificateContext caching for performance (NET6+)
  - Mutual TLS support via client certificate validation
affects: [future-tls-tests, 23-tls-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Stream abstraction for TLS/plain TCP transparency
    - Early TLS handshake before ARTIM timer
    - Certificate context pre-building for performance
    - Multi-target TLS API compatibility (netstandard2.0 + NET6+)

key-files:
  created: []
  modified:
    - src/SharpDicom/Network/DicomServerOptions.cs
    - src/SharpDicom/Network/DicomServer.cs

key-decisions:
  - "TLS handshake performed after TCP accept, before ARTIM timer starts"
  - "Stream abstraction: all methods accept Stream instead of NetworkStream for TLS transparency"
  - "SslStreamCertificateContext pre-built in Start() for NET6+ performance optimization"
  - "Authentication failures close connection silently (no DICOM-level error)"
  - "BCP 195 profile enforcement after successful handshake, before ARTIM timer"

patterns-established:
  - "Stream abstraction: unified code path for TLS and plain TCP using Stream base class"
  - "Certificate context caching: pre-build once, reuse for all connections (NET6+)"
  - "TLS failure handling: dispose SslStream and return silently on authentication errors"

# Metrics
duration: 5min
completed: 2026-02-04
---

# Phase 22 Plan 03: DicomServer TLS Integration Summary

**DicomServer accepts TLS connections with optional encryption, mutual TLS support, and DICOM BCP 195 compliance**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-04T23:14:57Z
- **Completed:** 2026-02-04T23:20:05Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- DicomServer accepts TLS connections when TlsServerOptions configured
- Backward compatible plain TCP operation when TlsServerOptions is null
- Server-side TLS handshake with timeout enforcement
- DICOM BCP 195 profile validation after successful handshake
- Mutual TLS support via RequireClientCertificate option
- SslStreamCertificateContext pre-building for NET6+ connection performance
- Stream abstraction for unified TLS/plain TCP code paths
- Multi-target support (netstandard2.0 + NET6+) with appropriate API guards

## Task Commits

Each task was committed atomically:

1. **Task 1: Add TlsServerOptions to DicomServerOptions** - `65c3759` (feat)
2. **Task 2: Integrate SslStream into DicomServer connection handling** - `7cc0b81` (feat)

## Files Created/Modified

**Configuration:**
- `src/SharpDicom/Network/DicomServerOptions.cs` - Added TlsServerOptions? Tls property with validation

**Server implementation:**
- `src/SharpDicom/Network/DicomServer.cs` - Full TLS integration:
  - Added SslStreamCertificateContext caching field (NET6+)
  - Pre-build certificate context in Start() for performance
  - TLS handshake in HandleAssociationAsync before ARTIM timer
  - Changed all method signatures from NetworkStream to Stream
  - Multi-target authentication with SslServerAuthenticationOptions (NET6+) or legacy overload (netstandard2.0)
  - DICOM BCP 195 profile enforcement after handshake
  - Silent connection close on authentication failure

## Decisions Made

**TLS handshake timing:**
- Perform TLS handshake after TCP accept but before ARTIM timer starts
- This ensures encrypted association establishment from the first DICOM PDU
- Authentication failures close connection silently (no DICOM-level error sent)

**Stream abstraction pattern:**
- Changed all internal methods from `NetworkStream stream` to `Stream stream`
- Enables transparent operation with both NetworkStream (plain TCP) and SslStream (TLS)
- No duplication of DIMSE handling logic
- activeStream variable tracks either stream or sslStream throughout connection lifecycle

**Performance optimization:**
- Pre-build SslStreamCertificateContext in Start() when TLS configured (NET6+ only)
- Reuse same context for all connections instead of rebuilding per connection
- Reduces per-connection overhead for certificate chain building

**Multi-target compatibility:**
- NET6+: Use SslServerAuthenticationOptions with ServerCertificateContext
- netstandard2.0: Use legacy AuthenticateAsServerAsync overload with individual parameters
- Both paths support same functionality (protocols, client certs, revocation checking)

**DICOM BCP 195 enforcement:**
- Validate protocol (TLS 1.2+) and cipher suite after successful handshake
- Failed validation closes connection before ARTIM timer
- EnforceDicomTlsProfile property allows opt-out for non-standard scenarios

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed netstandard2.0 SslServerAuthenticationOptions compatibility**
- **Found during:** Task 2 (Build verification)
- **Issue:** SslServerAuthenticationOptions is a .NET 5+ type, not available in netstandard2.0
- **Fix:** Moved SslServerAuthenticationOptions inside #if NET6_0_OR_GREATER guard, use legacy AuthenticateAsServerAsync overload for netstandard2.0
- **Files modified:** src/SharpDicom/Network/DicomServer.cs
- **Verification:** Build succeeds on all target frameworks (netstandard2.0, net8.0, net9.0, net10.0)
- **Committed in:** 7cc0b81 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Essential for multi-target compatibility. No scope creep.

## Issues Encountered

**Multi-target TLS API differences:**
- SslServerAuthenticationOptions (.NET 5+) not available on netstandard2.0
- Solution: Conditional compilation with #if guards
- NET6+: Modern SslServerAuthenticationOptions with ServerCertificateContext
- netstandard2.0: Legacy method overload with individual parameters
- Both paths support same TLS features (protocols, client certs, revocation)

**None** - Plan execution was straightforward beyond the expected multi-target API differences.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**Ready for TLS integration testing (23-XX):**
- DicomServer TLS functionality complete
- Both client (22-02) and server (22-03) support TLS
- BCP 195 compliance enforced by default
- Mutual TLS supported via client certificate requirements

**Testing gaps to address:**
- No automated tests for TLS server functionality yet
- Manual verification needed for:
  - TLS handshake success/failure scenarios
  - Mutual TLS (client certificate validation)
  - BCP 195 profile enforcement
  - Certificate context caching effectiveness
  - Handshake timeout enforcement

**Backward compatibility verified:**
- 79 existing networking tests pass (16 CEcho + 63 CStore)
- Plain TCP operation unchanged when TlsServerOptions is null

**No blockers:** Implementation complete, ready for testing phase.

---
*Phase: 22-tls-networking*
*Completed: 2026-02-04*
