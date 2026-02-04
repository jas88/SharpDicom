---
phase: 22-tls-networking
plan: 01
subsystem: network
tags: [tls, ssl, certificates, security, dicom-networking]

# Dependency graph
requires:
  - phase: 10-network-foundation
    provides: DicomNetworkException base exception hierarchy
provides:
  - TLS configuration types for client and server
  - Certificate validation strategies (system, thumbprint, custom CA, self-signed)
  - DICOM BCP 195 compliant TLS profile
  - TLS-specific exception hierarchy
affects: [22-02-tls-client, 22-03-tls-server]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - TLS options pattern (client/server separation)
    - Certificate validation callback composition
    - Runtime protocol version detection (netstandard2.0 compatibility)

key-files:
  created:
    - src/SharpDicom/Network/Tls/TlsOptions.cs
    - src/SharpDicom/Network/Tls/TlsServerOptions.cs
    - src/SharpDicom/Network/Tls/TlsConnectionInfo.cs
    - src/SharpDicom/Network/Tls/CertificateValidator.cs
    - src/SharpDicom/Network/Tls/DicomTlsProfile.cs
    - src/SharpDicom/Network/Exceptions/DicomTlsException.cs
    - src/SharpDicom/Network/Exceptions/CertificateValidationException.cs
    - src/SharpDicom/Network/Exceptions/TlsHandshakeException.cs
    - tests/SharpDicom.Tests/Network/Tls/TlsOptionsTests.cs
    - tests/SharpDicom.Tests/Network/Tls/CertificateValidatorTests.cs
    - tests/SharpDicom.Tests/Network/Tls/TlsServerOptionsTests.cs
  modified:
    - tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj

key-decisions:
  - "Runtime TLS 1.3 detection via numeric value (0x3000) for netstandard2.0 compatibility"
  - "Separate TlsOptions and TlsServerOptions for clear client/server distinction"
  - "CertificateValidator with factory methods for common validation strategies"
  - "DicomTlsProfile enforces DICOM PS3.15 Annex B.3 (BCP 195) by default with opt-out"
  - "Exclude TlsServerOptionsTests from Polyfills project (uses NET5+ SslStreamCertificateContext)"

patterns-established:
  - "Multi-target TLS support: #if guards for NET5+/NET6+ APIs with netstandard2.0 fallbacks"
  - "Certificate validation composition via callbacks and factory methods"
  - "Validation methods on configuration types throw early for invalid state"

# Metrics
duration: 9min
completed: 2026-02-04
---

# Phase 22 Plan 01: TLS Configuration and Validation

**TLS configuration foundation with DICOM BCP 195 compliance, certificate validation strategies, and comprehensive multi-target support**

## Performance

- **Duration:** 9 min
- **Started:** 2026-02-04T23:03:00Z
- **Completed:** 2026-02-04T23:12:28Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments

- TLS configuration types (TlsOptions, TlsServerOptions) with DICOM compliance validation
- CertificateValidator with 4 validation strategies (system-only, thumbprint whitelist, custom CA, self-signed)
- DicomTlsProfile with BCP 195 cipher suites and runtime TLS 1.3 detection for netstandard2.0
- TLS exception hierarchy (DicomTlsException, CertificateValidationException, TlsHandshakeException)
- 38 unit tests covering all configuration scenarios and validation strategies

## Task Commits

Each task was committed atomically:

1. **Task 1: TLS configuration types and exception hierarchy** - `2566cd1` (feat)
2. **Task 2: CertificateValidator and unit tests** - `091bdc3` (feat)

## Files Created/Modified

**Configuration types:**
- `src/SharpDicom/Network/Tls/TlsOptions.cs` - Client-side TLS configuration with protocol selection, certificate validation callbacks, client certificates for mTLS
- `src/SharpDicom/Network/Tls/TlsServerOptions.cs` - Server-side TLS configuration with server certificate, client cert requirements, SslStreamCertificateContext support (NET5+)
- `src/SharpDicom/Network/Tls/TlsConnectionInfo.cs` - Post-handshake connection state (protocol, cipher suite, certificates, mutual auth status)
- `src/SharpDicom/Network/Tls/DicomTlsProfile.cs` - DICOM BCP 195 compliant cipher suites, protocol validation, runtime TLS 1.3 detection

**Validation:**
- `src/SharpDicom/Network/Tls/CertificateValidator.cs` - Reusable certificate validation strategies with factory methods

**Exceptions:**
- `src/SharpDicom/Network/Exceptions/DicomTlsException.cs` - Base TLS exception with RemoteCertificate and PolicyErrors properties
- `src/SharpDicom/Network/Exceptions/CertificateValidationException.cs` - Certificate validation failures with chain status details
- `src/SharpDicom/Network/Exceptions/TlsHandshakeException.cs` - Handshake failures (timeout, protocol mismatch, auth failure)

**Tests:**
- `tests/SharpDicom.Tests/Network/Tls/TlsOptionsTests.cs` - 18 tests for client TLS configuration
- `tests/SharpDicom.Tests/Network/Tls/CertificateValidatorTests.cs` - 9 tests for validation strategies
- `tests/SharpDicom.Tests/Network/Tls/TlsServerOptionsTests.cs` - 11 tests for server TLS configuration (excluded from Polyfills)
- `tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj` - Excluded TlsServerOptionsTests (NET5+ APIs)

## Decisions Made

**Runtime TLS 1.3 detection for netstandard2.0:**
- DicomTlsProfile.IsCompliantProtocol checks TLS 1.3 by numeric value (0x3000) at runtime
- Handles netstandard2.0 builds running on .NET 5+ runtimes where TLS 1.3 enum value exists but not at compile-time
- Allows Polyfills tests to pass when testing netstandard2.0 build on .NET 10 runtime

**Separate client/server configuration types:**
- TlsOptions: client-side with ServerCertificateValidationCallback, ClientCertificates for mTLS
- TlsServerOptions: server-side with ServerCertificate, RequireClientCertificate, SslStreamCertificateContext (NET5+)
- Clear separation prevents confusion about which properties apply to which role

**CertificateValidator factory methods:**
- SystemOnly(): strict system certificate store validation
- AcceptThumbprints(params string[]): SHA256 thumbprint whitelist for self-signed certs
- AcceptSelfSigned(): accepts self-signed certs with only UntrustedRoot error
- WithCustomCAs(params X509Certificate2[]): custom CA trust for closed networks
- Enables composition via RemoteCertificateValidationCallback delegate

**DICOM BCP 195 enforcement by default:**
- EnforceDicomTlsProfile = true on both TlsOptions and TlsServerOptions
- Validates TLS 1.2+ and DICOM-compliant cipher suites
- Opt-out available for non-standard scenarios (set to false)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed netstandard2.0 GetCertHashString compatibility**
- **Found during:** Task 2 (CertificateValidator implementation)
- **Issue:** GetCertHashString(HashAlgorithmName) overload not available on netstandard2.0
- **Fix:** Added #if NET6_0_OR_GREATER guard with manual SHA256 computation for netstandard2.0
- **Files modified:** src/SharpDicom/Network/Tls/CertificateValidator.cs
- **Verification:** Build succeeds on all target frameworks
- **Committed in:** 091bdc3 (Task 2 commit)

**2. [Rule 1 - Bug] Fixed runtime TLS 1.3 detection for netstandard2.0**
- **Found during:** Task 2 (Test execution - Polyfills project)
- **Issue:** TLS 1.3 enum value exists at runtime (.NET 10) but not at compile-time (netstandard2.0 build)
- **Fix:** Check TLS 1.3 by numeric value (0x3000) at runtime instead of compile-time conditional
- **Files modified:** src/SharpDicom/Network/Tls/DicomTlsProfile.cs
- **Verification:** Validate_Tls13Protocol_Succeeds test passes in Polyfills project
- **Committed in:** 091bdc3 (Task 2 commit)

**3. [Rule 2 - Missing Critical] Added pragma warnings for obsolete TLS protocol tests**
- **Found during:** Task 2 (Test compilation)
- **Issue:** Tests for non-compliant protocols (TLS 1.1) triggered obsolete warnings (SYSLIB0039, CA5397)
- **Fix:** Added #pragma warning disable CS0618, CA5397, SYSLIB0039 for validation tests only
- **Files modified:** TlsOptionsTests.cs, TlsServerOptionsTests.cs
- **Verification:** Zero warnings on all target frameworks
- **Committed in:** 091bdc3 (Task 2 commit)

**4. [Rule 2 - Missing Critical] Excluded TlsServerOptionsTests from Polyfills compilation**
- **Found during:** Task 2 (Polyfills project build)
- **Issue:** TlsServerOptionsTests uses SslStreamCertificateContext (NET5+) causing compile errors in Polyfills (netstandard2.0 test)
- **Fix:** Excluded TlsServerOptionsTests.cs from Polyfills project compilation in csproj
- **Files modified:** tests/SharpDicom.Tests.Polyfills/SharpDicom.Tests.Polyfills.csproj
- **Verification:** Polyfills project builds and tests run successfully
- **Committed in:** 091bdc3 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (2 bugs, 2 missing critical)
**Impact on plan:** All auto-fixes necessary for multi-target compatibility and warning-free compilation. No scope creep.

## Issues Encountered

**Multi-target TLS API differences:**
- SslStreamCertificateContext (NET5+), TlsCipherSuite (NET6+), CipherSuitesPolicy (NET6+) not available on netstandard2.0
- Solution: #if NET5_0_OR_GREATER / NET6_0_OR_GREATER guards with netstandard2.0 fallbacks
- TlsConnectionInfo: readonly record struct (NET6+) vs sealed class (netstandard2.0)
- Tests: Some excluded from Polyfills project to avoid NET5+ API compilation issues

**Runtime vs compile-time protocol detection:**
- netstandard2.0 build runs on .NET 10 runtime which supports TLS 1.3
- Compile-time conditional (#if NET6_0_OR_GREATER) doesn't help at runtime
- Solution: Numeric value check (0x3000) for TLS 1.3 detection works across all runtime versions

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**Ready for DicomClient TLS integration (22-02):**
- TlsOptions provides complete client-side configuration
- CertificateValidator enables flexible server certificate validation
- DicomTlsProfile ensures DICOM compliance by default

**Ready for DicomServer TLS integration (22-03):**
- TlsServerOptions provides complete server-side configuration
- Support for SslStreamCertificateContext (NET5+) for performance
- Client certificate validation strategies available

**No blockers:** All types compile and test successfully on all target frameworks (netstandard2.0, net8.0, net9.0, net10.0).

---
*Phase: 22-tls-networking*
*Completed: 2026-02-04*
