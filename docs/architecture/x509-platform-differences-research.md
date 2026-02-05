# X.509 Certificate Chain Validation: Platform-Specific Behavior Research

**Date:** 2026-02-04
**Status:** Research Complete
**Phase:** 22 - TLS Security Enhancement

## Executive Summary

X.509 certificate chain validation in .NET exhibits significant platform-specific differences due to reliance on native OS cryptographic libraries. The `X509Chain.Build()` method delegates to Windows CryptoAPI/CNG, Linux OpenSSL, or macOS Security Framework, each with distinct behaviors, trust store implementations, and chain building algorithms. The introduction of `X509ChainTrustMode.CustomRootTrust` in .NET 5+ addresses many cross-platform consistency issues but is unavailable in earlier framework versions.

## 1. Why X509Chain.Build() Uses Platform-Specific Certificate Stores

### Architectural Design Decision

.NET's cryptographic operations depend on OS libraries for several critical reasons:

1. **Security and Compliance**
   - OS vendors maintain and patch cryptographic libraries as high-priority security updates
   - FIPS-validated algorithms available through OS libraries
   - System administrators apply updates centrally
   - Leverages OS vulnerability management

2. **Trust Store Integration**
   - Certificate trust decisions require access to system-wide trust anchors
   - Enterprise environments manage trust stores at OS level
   - Root CA updates distributed through OS update mechanisms
   - User trust decisions (read-write) and system trust decisions (read-only) unified

3. **Performance and Native Integration**
   - Hardware acceleration (TPM, HSM) available through OS APIs
   - Private key storage in OS keychains/keystores
   - Certificate revocation checking (CRL/OCSP) integrated with OS networking

**Source:** [Cross-Platform Cryptography in .NET - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)

### Implementation Philosophy

From Microsoft documentation:
> "The majority of support for X.509 certificates in .NET comes from OS libraries. To load a certificate into an X509Certificate2 or X509Certificate instance in .NET, the certificate must be loaded by the underlying OS library."

This design trades portability for security, correctness, and integration with OS-level security infrastructure.

## 2. Platform-Specific Cryptographic Backends

### Windows: CryptoAPI (CAPI) and Cryptography API Next Generation (CNG)

**Architecture:**
- **CAPI (Legacy):** Used by `RSACryptoServiceProvider`, `DSACryptoServiceProvider`
- **CNG (Modern):** Used by `RSACng`, `ECDsaCng`, `RSA.Create()`, `ECDsa.Create()`
- Certificate stores map directly to Windows Certificate Store APIs
- Full integration with Windows keychain and certificate management UI

**Trust Store Mapping:**
- `CurrentUser\Root` → Windows Trusted Root Certification Authorities (User)
- `LocalMachine\Root` → Windows Trusted Root Certification Authorities (System)
- `CurrentUser\My` → Windows Personal certificate store (User)
- `LocalMachine\My` → Windows Personal certificate store (System)
- `CurrentUser\Intermediate` → Windows Intermediate Certification Authorities (User)
- `LocalMachine\Intermediate` → Windows Intermediate Certification Authorities (System)
- `Disallowed` → Windows Untrusted Certificates store

**Chain Building Behavior:**
- Native Windows chain building via CertGetCertificateChain API
- Supports Authority Information Access (AIA) fetching
- Integrated with Windows Update for root CA updates
- Revocation checking via Windows CRL/OCSP infrastructure
- Respects Windows Group Policy settings

**Platform-Specific Features:**
- Full support for `X509ChainPolicy.UrlRetrievalTimeout`
- Support for all padding modes and digest algorithms
- PSS signature padding support
- SHA-3 support on Windows 11 Build 25324+

**Sources:**
- [Cross-Platform Cryptography in .NET - RSA on Windows](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)
- [Breaking changes in .NET Core 3.0 - Cryptography](https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#cryptography)

### Linux: OpenSSL

**Architecture:**
- Uses system-installed OpenSSL library (libcrypto.so)
- Version-dependent features (.NET prefers OpenSSL 1.1.x over 1.0.x as of .NET Core 3.0)
- OpenSSL 3.0+ support for newer algorithms (SHA-3, KMAC, post-quantum)

**Trust Store Mapping:**
- `LocalMachine\Root` → **Interpretation** of CA bundle at default OpenSSL path
  - Typically `/etc/ssl/certs/ca-certificates.crt` or `/etc/pki/tls/certs/ca-bundle.crt`
  - Not a direct mapping - .NET reads and interprets the bundle
- `CurrentUser\Intermediate` → Used as **cache** for downloaded intermediate CAs
  - Populated via Authority Information Access (AIA) during successful chain builds
  - Stored in user's home directory: `~/.dotnet/corefx/cryptography/x509stores/ca/`
- `LocalMachine\Intermediate` → Interpretation of CA bundle (same as Root)
- `Disallowed` store → **NOT used in chain building** (throws `CryptographicException` if you try to add certificates)

**Chain Building Behavior:**
- Uses OpenSSL's `X509_verify_cert()` or equivalent
- Different algorithm from Windows - may produce different chains for cross-signed certificates
- AIA fetching supported but only via HTTP (HTTPS not supported as of documented issues)
- CRL/OCSP checking uses OpenSSL infrastructure
- No system-wide certificate store UI (file-based)

**Key Differences from Windows:**
- BEGIN TRUSTED CERTIFICATE syntax no longer supported (as of .NET Core 3.0)
- User stores created on first write (may not exist by default)
- Opening `CurrentUser\My` with `ExistingOnly` may fail if store never created
- `Disallowed` store behavior completely different (not used, throws exceptions)

**Known Issues:**
- Same chain built successfully on Windows fails with `PartialChain` on Linux ([#29164](https://github.com/dotnet/runtime/issues/29164))
- Multiple root certificates with same subject name in `ExtraStore` can fail ([#59148](https://github.com/dotnet/runtime/issues/59148))
- Order of certificates in `ExtraStore` matters on Linux
- X509Store.Add() racing with X509Chain.Build ([#32608](https://github.com/dotnet/runtime/issues/32608))

**Sources:**
- [Cross-Platform Cryptography in .NET - X509Store](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#x509store)
- [X509Chain behaviour inconsistent on Windows and Linux](https://github.com/dotnet/runtime/issues/29164)
- [Difference in X509Chain output on Linux and Windows](https://github.com/dotnet/runtime/issues/87010)

### macOS: Security Framework (SecTrust)

**Architecture:**
- Uses Apple Security Framework (Security.framework)
- `SecTrustEvaluate()` API for certificate chain validation
- Keychain Services for certificate storage
- Integrated with macOS Keychain Access UI

**Trust Store Mapping:**
- `CurrentUser\Root` → **Interpretation** of `SecTrustSettings` results for **user trust domain**
  - Not direct access - .NET queries SecTrustSettings and interprets results
- `LocalMachine\Root` → Interpretation of `SecTrustSettings` results for **admin and system trust domains**
- `CurrentUser\My` → User's default keychain (`login.keychain` by default)
- `LocalMachine\My` → System keychain (`System.keychain`)
- `CurrentUser\Intermediate` → Custom store, **does NOT affect chain building**
  - Major limitation: adding certs here has no effect on X509Chain.Build()
- `Disallowed` stores → Interpretation of `SecTrustSettings` for certificates with trust set to "Always Deny"

**Chain Building Behavior:**
- Uses `SecTrustEvaluate()` - completely different algorithm from Windows/Linux
- CRL/OCSP handling controlled by macOS
- `X509RevocationMode.Offline` treated as `X509RevocationMode.Online` (offline CRL not supported)
- **`X509ChainPolicy.UrlRetrievalTimeout` ignored** - no user-initiated timeout on CRL/OCSP/AIA downloads
- Custom keychain creation supported via `new X509Store(IntPtr)` with `SecKeychainCreate`

**Key Differences from Windows/Linux:**
- Cannot write to `CurrentUser\Root` (read-only)
- `CurrentUser\Intermediate` useless for chain building
- Private key handling requires keychain (cannot use `EphemeralKeySet` flag - throws `PlatformNotSupportedException`)
- Keychains automatically created/deleted for PFX loading
- Empty subject certificates throw `CryptographicException` ([#26111](https://github.com/dotnet/runtime/issues/26111))
- User CertificateAuthority store ignored ([#48207](https://github.com/dotnet/runtime/issues/48207))

**Known Issues:**
- `CustomTrustStore` validation fails with only `PartialChain` status ([#1923](https://github.com/dotnet/runtime/issues/1923))
- Chain building fails for valid certificates in some scenarios
- SecTrustSettings interpretation may not match Windows/Linux behavior

**Sources:**
- [Cross-Platform Cryptography in .NET - X509Chain](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#x509chain)
- [X509Chain validation fails on MacOS using CustomTrustStore](https://github.com/dotnet/runtime/issues/1923)
- [certificates added to user CertificateAuthority store are ignored](https://github.com/dotnet/runtime/issues/48207)

## 3. Why X509ChainTrustMode.CustomRootTrust Exists (and Why It's .NET 5+ Only)

### The Problem with ExtraStore (Pre-.NET 5)

Before .NET 5, the only way to provide additional certificates for chain building was `X509ChainPolicy.ExtraStore`. This approach had severe limitations:

**1. Platform-Dependent Behavior**

`ExtraStore` is passed to the underlying OS API, but interpretation varies:
- **Windows CryptoAPI:** Treats `ExtraStore` as additional certificate collection for chain building
- **Linux OpenSSL:** Adds to temporary certificate store, but order matters and behavior inconsistent
- **macOS SecTrust:** May not properly integrate with `SecTrustEvaluate()`

**2. The "Partial Chain Gotcha"**

Critical issue documented in [#49615](https://github.com/dotnet/runtime/issues/49615) and [stewartadam.io](https://stewartadam.io/blog/2021/04/28/how-properly-validate-x509-certificates-c-net-core-31-net-5):

> "The return value of X509Chain.Build() should not be trusted on its own when using ExtraStore; proper certificate verification requires manual and separate verification of correct chain termination after running X509Chain.Build()."

**Why?**
- `X509Chain.Build()` returns `true` even if certificate not issued by any trusted root
- Considers a "chain" consisting only of the certificate under validation (partial chain)
- `AllowUnknownCertificateAuthority` flag ignored or produces unexpected results
- No way to **replace** system trust store, only **augment** it

**3. ExtraStore Not a True Trust Store**

- Certificates in `ExtraStore` used for chain building but **not trusted as roots**
- On Linux, root in `ExtraStore` not in trusted root store → CRL signature verification fails
- Documented in Azure Functions issue: CryptoAPI had no chance to verify CRL certificate signatures

**4. Platform-Specific Failures**

- Linux: Two roots with same subject name fails depending on order ([#59148](https://github.com/dotnet/runtime/issues/59148))
- macOS: Intermediate certificates in custom store ignored ([#48207](https://github.com/dotnet/runtime/issues/48207))
- Windows vs. Linux: Same chain succeeds on Windows, fails with `PartialChain` on Linux ([#29164](https://github.com/dotnet/runtime/issues/29164))

**Sources:**
- [X509Chain.Build() returns true for partial chain](https://github.com/dotnet/runtime/issues/49615)
- [How to properly validate X.509 certificates in C#](https://stewartadam.io/blog/2021/04/28/how-properly-validate-x509-certificates-c-net-core-31-net-5)
- [Add ExtraStore property to CertificateAuthenticationOptions](https://github.com/dotnet/aspnetcore/issues/29679)

### The CustomRootTrust Solution (.NET 5+)

.NET 5 introduced `X509ChainTrustMode` enum and `X509ChainPolicy.CustomTrustStore` to solve these problems:

```csharp
public enum X509ChainTrustMode
{
    System = 0,           // Use default (system) root trust
    CustomRootTrust = 1   // Use CustomTrustStore instead of system trust
}
```

**Key Improvements:**

1. **True Trust Store Replacement**
   - `CustomTrustStore` **replaces** system trust store, not augments it
   - Certificates in `CustomTrustStore` treated as trusted roots
   - Explicit control over trusted roots per chain build

2. **Consistent Cross-Platform Behavior**
   - .NET runtime implements trust store logic, not delegated to OS
   - Same validation behavior on Windows, Linux, macOS
   - Eliminates platform-specific quirks

3. **Proper Chain Termination Validation**
   - Chain must terminate at a certificate in `CustomTrustStore`
   - No more "partial chain" false positives
   - `X509Chain.Build()` return value reliable

4. **Security: Explicit Trust Model**
   - Applications explicitly declare trusted roots
   - No implicit trust of system roots when using `CustomRootTrust`
   - Suitable for certificate pinning scenarios

**Example Usage:**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
chain.ChainPolicy.CustomTrustStore.Add(myCustomRootCA);
chain.ChainPolicy.CustomTrustStore.Add(myIntermediateCA); // Can add intermediates too
chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Or Online

bool isValid = chain.Build(leafCertificate);
// isValid now reliable - chain must terminate at myCustomRootCA
```

**Sources:**
- [X509ChainTrustMode Enum - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chaintrustmode?view=net-10.0)
- [Allow X509Chain to replace the root trust list](https://github.com/dotnet/runtime/issues/20302)
- [Custom Root Trust with HttpClient in .NET](https://stigvoss.dk/post/custom-root-trust-for-httpclient/)

### Why .NET 5+ Only?

**Technical Reasons:**

1. **API Surface Change**
   - New enum (`X509ChainTrustMode`) and property (`CustomTrustStore`) in stable API
   - Breaking change potential if backported (behavior changes)

2. **Runtime Implementation Complexity**
   - Required refactoring of chain building logic across all platforms
   - .NET runtime now implements trust store isolation
   - Platform shims updated to support custom trust mode

3. **Testing and Validation**
   - New feature required extensive cross-platform testing
   - Security-critical feature - could not rush into patch releases

4. **Support Lifecycle**
   - .NET 5 marked the LTS/Current release cadence
   - .NET Framework in maintenance mode (no new features)
   - .NET Core 3.1 LTS approaching end-of-life

**Workarounds for Pre-.NET 5:**

If you must support .NET Framework or .NET Core 3.1:

1. **Manual Chain Validation**
   - Use `X509Chain.Build()` with `ExtraStore`
   - Manually verify `ChainElements[^1]` is in your trusted root set
   - Check `ChainStatus` for `PartialChain` and handle appropriately

2. **RemoteCertificateValidationCallback**
   - Implement custom validation in SslStream callback
   - Build chain with `ExtraStore` and manually validate termination

3. **Accept Platform Differences**
   - Document platform-specific behavior
   - Test on all target platforms
   - Handle `PartialChain` status differently per platform

**Sources:**
- [X509ChainPolicy.CustomTrustStore appears to be readonly in .NET 5](https://github.com/dotnet/runtime/issues/47392)
- [GitHub - dotnet-x509-certificate-verification](https://github.com/stewartadam/dotnet-x509-certificate-verification)

## 4. How Certificate Chain Building Differs Between Platforms

### Chain Building as a Graph Problem

From GitHub issue [#87010](https://github.com/dotnet/runtime/issues/87010):

> "The chain that is built will vary between platforms, or even within the same platform depending on the OS updates and OS version. Chain building is really a graph problem, and there is no one 'single' chain when cross signing gets involved."

**Key Insight:** Multiple valid paths may exist from leaf certificate to trusted root (especially with cross-signed intermediates). Each platform's algorithm may choose different paths.

### Windows Chain Building Algorithm

**Chain Engine:** `CertGetCertificateChain` API (CryptoAPI/CNG)

**Process:**
1. Start with leaf certificate
2. Find issuer by matching `Issuer` field with candidate `Subject` fields
3. Verify signature of current certificate using issuer's public key
4. Check Authority Key Identifier (AKI) extension if present
5. Repeat until reaching a self-signed certificate
6. Verify chain terminates at a trusted root (in Trusted Root Certification Authorities store)
7. Check revocation status via CRL/OCSP if enabled
8. Download missing intermediate certificates via AIA extension (if `RevocationMode` not `NoCheck`)

**Features:**
- Sophisticated AIA fetching with caching
- Parallel CRL/OCSP checks
- Cross-certificate path selection heuristics
- Integrated with Windows Update for CA certificate updates
- Respects Group Policy settings
- LDAP support for enterprise environments

**Performance:**
- Highly optimized (native code)
- Aggressive caching of intermediates and CRL/OCSP responses
- Sub-second typical chain builds

**Sources:**
- [Certificate Chain Validation](https://learn.microsoft.com/en-us/windows/win32/seccrypto/certificate-chain-validation)
- Windows SDK documentation (CertGetCertificateChain)

### Linux Chain Building Algorithm

**Chain Engine:** OpenSSL `X509_verify_cert()` (or equivalent in 1.1.x/3.x)

**Process:**
1. Start with leaf certificate
2. Build untrusted chain from `ExtraStore` and cached intermediates
3. Load trusted roots from CA bundle file (e.g., `/etc/ssl/certs/ca-certificates.crt`)
4. Use OpenSSL's chain building algorithm:
   - Breadth-first or depth-first search depending on OpenSSL version
   - Prioritize chains that avoid revoked certificates
   - May prefer shorter chains
5. Verify each link (signature, validity period, basic constraints, key usage)
6. Check revocation if enabled (CRL/OCSP via OpenSSL infrastructure)
7. Cache downloaded intermediates to `~/.dotnet/corefx/cryptography/x509stores/ca/`

**Features:**
- Simpler than Windows (no GUI integration)
- HTTP-only AIA fetching (HTTPS not supported in some .NET versions)
- File-based trust store (easier to inspect/modify)
- Supports BEGIN CERTIFICATE syntax only (BEGIN TRUSTED CERTIFICATE removed in .NET Core 3.0)

**Limitations:**
- No system-wide certificate cache (per-user only)
- Distribution-dependent CA bundle location
- Order of certificates in `ExtraStore` matters ([#59148](https://github.com/dotnet/runtime/issues/59148))
- Racing condition with X509Store.Add() ([#32608](https://github.com/dotnet/runtime/issues/32608))

**Known Differences from Windows:**
- Same chain succeeds on Windows, fails with `PartialChain` on Linux ([#29164](https://github.com/dotnet/runtime/issues/29164))
- Cross-signed certificate handling produces different chains
- AIA fetching less reliable than Windows

**Sources:**
- [OpenSSL X509_verify_cert documentation](https://www.openssl.org/docs/man1.1.1/man3/X509_verify_cert.html)
- [X509Chain behaviour inconsistent on Windows and Linux](https://github.com/dotnet/runtime/issues/29164)
- [Simplify X509Chain building with OpenSSL](https://github.com/dotnet/runtime/issues/23089)

### macOS Chain Building Algorithm

**Chain Engine:** Security Framework `SecTrustEvaluate()` API

**Process:**
1. Create `SecTrust` object with leaf certificate and policy
2. Optionally set anchor certificates (trusted roots)
3. Call `SecTrustEvaluate()` - macOS performs entire chain build
4. macOS uses proprietary algorithm (not documented publicly)
5. Queries keychains for intermediate certificates
6. Queries `SecTrustSettings` for trust decisions
7. Performs revocation checking via macOS infrastructure
8. Returns trust result (trusted, recoverable trust failure, fatal trust failure)

**Features:**
- Deep integration with macOS Keychain
- System-wide and per-user trust settings
- GUI integration (Keychain Access.app)
- Sophisticated cross-signed certificate handling
- Transparent online certificate validation (OCSP Stapling)

**Limitations:**
- **Offline CRL not supported** - `X509RevocationMode.Offline` treated as `Online`
- **No user-initiated timeout** - `X509ChainPolicy.UrlRetrievalTimeout` ignored
- **`CurrentUser\Intermediate` store not used** - adding certificates has no effect ([#48207](https://github.com/dotnet/runtime/issues/48207))
- **Empty subject certificates fail** - throws exception ([#26111](https://github.com/dotnet/runtime/issues/26111))
- Black-box algorithm - cannot inspect intermediate steps

**Known Differences from Windows/Linux:**
- Different chain path selection for cross-signed certificates
- `CustomTrustStore` behavior unreliable ([#1923](https://github.com/dotnet/runtime/issues/1923))
- Trust settings interpretation differs from Windows Group Policy

**Apple Documentation:**
- [Certificate, Key, and Trust Services](https://developer.apple.com/documentation/security/certificate_key_and_trust_services)
- SecTrustEvaluate is deprecated in favor of `SecTrustEvaluateWithError` (macOS 10.14+)

**Sources:**
- [Cross-Platform Cryptography in .NET - X509Chain](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#x509chain)
- [X509Chain.Build throws on macOS for certificates with an empty subject](https://github.com/dotnet/runtime/issues/26111)

### Summary Table: Platform Chain Building Differences

| Feature | Windows (CryptoAPI/CNG) | Linux (OpenSSL) | macOS (SecTrust) |
|---------|------------------------|-----------------|------------------|
| **Algorithm** | CertGetCertificateChain | X509_verify_cert | SecTrustEvaluate |
| **Trust Store Type** | Registry-based | File-based CA bundle | Keychain-based |
| **Intermediate Cache** | System-wide + user | User only (~/.dotnet/) | Keychain (automatic) |
| **AIA Fetching** | HTTP + HTTPS | HTTP only | Handled by macOS |
| **CRL/OCSP** | Full support | Full support | Online only (no Offline) |
| **UrlRetrievalTimeout** | Respected | Respected | **Ignored** |
| **ExtraStore Order** | Doesn't matter | **Matters** ([#59148](https://github.com/dotnet/runtime/issues/59148)) | May not be used |
| **CurrentUser\Intermediate** | Full read/write | Cache only | **Not used for chain building** |
| **Empty Subject Certs** | Allowed | Allowed | **Throws exception** |
| **Cross-Signing** | Heuristic path selection | Varies by OpenSSL version | Proprietary algorithm |
| **CustomTrustStore (.NET 5+)** | Works well | Works well | Some issues ([#1923](https://github.com/dotnet/runtime/issues/1923)) |

## 5. Why Custom CA Validation Works Differently on Each Platform

### Root Cause: OS API Delegation

.NET does not implement its own certificate chain building algorithm (until `CustomRootTrust` in .NET 5+). Instead, it delegates to:
- Windows: `CertGetCertificateChain()`
- Linux: `X509_verify_cert()`
- macOS: `SecTrustEvaluate()`

Each OS API has:
- Different trust store formats and locations
- Different graph traversal algorithms
- Different caching strategies
- Different revocation checking behavior
- Different AIA/CRL/OCSP handling

### Trust Store Semantics

**Windows:**
- `ExtraStore` → Additional certificates for chain building (not trusted)
- Trusted roots must be in "Trusted Root Certification Authorities" store
- Clear separation between chain building and trust decisions

**Linux:**
- `ExtraStore` → Merged with CA bundle (interpretation varies)
- Trusted roots in `/etc/ssl/certs/ca-certificates.crt`
- Adding root to `ExtraStore` does NOT make it trusted for CRL/OCSP verification

**macOS:**
- `ExtraStore` → May be passed to `SecTrustSetAnchorCertificates()` but behavior undocumented
- Trusted roots determined by `SecTrustSettings` (per-user and system-wide)
- `CurrentUser\Intermediate` not integrated with `SecTrustEvaluate()`

### Practical Implications for Custom CA Validation

**Scenario 1: Self-Signed Certificate**

You have a self-signed root CA and want to trust it for validation.

**Pre-.NET 5 (using ExtraStore):**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.ExtraStore.Add(mySelfSignedCA);
chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
bool isValid = chain.Build(leafCert);

// Windows: May work if CA also in Trusted Root store
// Linux: PartialChain - CA in ExtraStore but not trusted
// macOS: PartialChain - ExtraStore not integrated with SecTrust
```

**Problems:**
- Must manually check chain termination
- `AllowUnknownCertificateAuthority` bypasses trust check but still returns `PartialChain`
- Platform-specific workarounds required

**.NET 5+ (using CustomTrustStore):**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
chain.ChainPolicy.CustomTrustStore.Add(mySelfSignedCA);
bool isValid = chain.Build(leafCert);

// Windows: Works reliably
// Linux: Works reliably
// macOS: Mostly works (some edge cases)
```

**Benefits:**
- .NET runtime implements trust logic, not OS
- Consistent behavior across platforms
- `Build()` return value reliable

**Scenario 2: Custom Intermediate CA**

You have a custom intermediate CA and want to use it for chain building.

**Pre-.NET 5:**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.ExtraStore.Add(myIntermediateCA);
chain.ChainPolicy.ExtraStore.Add(myRootCA);
bool isValid = chain.Build(leafCert);

// Windows: Usually works
// Linux: May fail depending on certificate order in ExtraStore (#59148)
// macOS: May not use intermediate from ExtraStore (#48207)
```

**.NET 5+:**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
chain.ChainPolicy.CustomTrustStore.Add(myRootCA);
chain.ChainPolicy.CustomTrustStore.Add(myIntermediateCA); // Both root and intermediate
bool isValid = chain.Build(leafCert);

// Works reliably on all platforms
```

**Scenario 3: Certificate Pinning**

You want to trust only specific certificates (pinning).

**Pre-.NET 5:**
- Must implement custom `RemoteCertificateValidationCallback`
- Manually check certificate fingerprints
- `ExtraStore` not suitable (allows any certificate from that CA)

**.NET 5+:**

```csharp
var chain = new X509Chain();
chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
chain.ChainPolicy.CustomTrustStore.Add(pinnedCertificate); // Only this certificate trusted
bool isValid = chain.Build(certificateToValidate);

// Works for exact certificate pinning
// For public key pinning, still need custom callback
```

### Recommended Approach by Framework Version

**For .NET 5+ Projects:**
- **Always use `X509ChainTrustMode.CustomRootTrust`** for custom CA validation
- Add all required roots and intermediates to `CustomTrustStore`
- `Build()` return value reliable

**For .NET Framework / .NET Core 3.1 and earlier:**
- Use `ExtraStore` but **do not trust `Build()` return value alone**
- After `Build()`, manually verify:
  ```csharp
  if (chain.Build(cert))
  {
      // Still check:
      var chainRoot = chain.ChainElements[^1].Certificate;
      bool terminatesAtTrustedRoot = myTrustedRoots.Any(root =>
          root.Thumbprint.Equals(chainRoot.Thumbprint, StringComparison.OrdinalIgnoreCase));

      if (!terminatesAtTrustedRoot)
      {
          // Treat as PartialChain even if Build() returned true
      }
  }
  ```
- Or implement `RemoteCertificateValidationCallback` for full control

**For Cross-Platform Libraries:**
- Support both approaches: detect .NET version at build time
- Use `CustomRootTrust` if available (`#if NET5_0_OR_GREATER`)
- Fall back to manual validation for older frameworks
- Document platform-specific behavior in pre-.NET 5 scenarios

**Sources:**
- [How to properly validate X.509 certificates in C#](https://stewartadam.io/blog/2021/04/28/how-properly-validate-x509-certificates-c-net-core-31-net-5)
- [TLS/SSL best practices - Custom certificate trust](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices#custom-certificate-trust)

## 6. Key Takeaways for SharpDicom TLS Implementation

### Requirements Analysis

**SharpDicom Target Frameworks:**
- `netstandard2.0` (includes .NET Framework 4.6.1+, Unity, Xamarin)
- `net8.0` (LTS)
- `net9.0` (Latest)

**Implication:** Must support both pre-.NET 5 (`netstandard2.0`) and .NET 5+ (`net8.0`, `net9.0`) validation strategies.

### Recommended Implementation Strategy

1. **Conditional Compilation for Platform-Specific Code**

```csharp
#if NET5_0_OR_GREATER
    // Use X509ChainTrustMode.CustomRootTrust for reliable cross-platform validation
    private static bool ValidateServerCertificateModern(
        X509Certificate2 certificate,
        X509Certificate2Collection customTrustedRoots)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        foreach (var root in customTrustedRoots)
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
        }

        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;

        return chain.Build(certificate);
    }
#else
    // For .NET Framework and .NET Standard 2.0: manual validation required
    private static bool ValidateServerCertificateLegacy(
        X509Certificate2 certificate,
        X509Certificate2Collection customTrustedRoots)
    {
        using var chain = new X509Chain();

        // Add custom roots to ExtraStore
        foreach (var root in customTrustedRoots)
        {
            chain.ChainPolicy.ExtraStore.Add(root);
        }

        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        bool chainBuilt = chain.Build(certificate);

        if (!chainBuilt)
        {
            return false;
        }

        // CRITICAL: Manually verify chain terminates at trusted root
        var chainRoot = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
        bool terminatesAtTrustedRoot = customTrustedRoots.Cast<X509Certificate2>().Any(root =>
            root.Thumbprint.Equals(chainRoot.Thumbprint, StringComparison.OrdinalIgnoreCase));

        if (!terminatesAtTrustedRoot)
        {
            // Even though Build() returned true, chain doesn't terminate at our trusted root
            return false;
        }

        // Check for PartialChain in status
        foreach (var status in chain.ChainStatus)
        {
            if (status.Status != X509ChainStatusFlags.NoError)
            {
                // Log or handle specific chain status flags
                if (status.Status == X509ChainStatusFlags.PartialChain)
                {
                    return false; // Partial chain not acceptable
                }
            }
        }

        return true;
    }
#endif
```

2. **Public API Design**

Provide options for both system trust and custom trust:

```csharp
public class DicomTlsOptions
{
    /// <summary>
    /// Gets or sets the collection of custom trusted root certificates.
    /// If null or empty, system trust store is used.
    /// If provided, on .NET 5+ uses X509ChainTrustMode.CustomRootTrust.
    /// On .NET Standard 2.0, requires manual validation.
    /// </summary>
    public X509Certificate2Collection? CustomTrustedRoots { get; set; }

    /// <summary>
    /// Gets or sets whether to use system trust store in addition to CustomTrustedRoots.
    /// Only applicable on .NET 5+ with CustomRootTrust.
    /// </summary>
    public bool AllowSystemTrustStore { get; set; } = true;

    /// <summary>
    /// Gets or sets revocation checking mode.
    /// </summary>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Online;
}
```

3. **Platform-Specific Behavior Documentation**

Document clearly in XML docs and user-facing docs:

```csharp
/// <remarks>
/// <para><b>Platform Behavior:</b></para>
/// <list type="bullet">
/// <item>
/// <term>.NET 5+</term>
/// <description>Uses X509ChainTrustMode.CustomRootTrust for consistent cross-platform validation.
/// Trusted roots in CustomTrustedRoots replace system trust store unless AllowSystemTrustStore is true.
/// </description>
/// </item>
/// <item>
/// <term>.NET Framework / .NET Standard 2.0</term>
/// <description>Uses X509ChainPolicy.ExtraStore with manual chain termination validation.
/// Behavior may vary between Windows, Linux, and macOS. See documentation for details.
/// </description>
/// </item>
/// </list>
/// </remarks>
```

4. **Testing Strategy**

- **Unit tests:** Mock certificate validation on all platforms
- **Integration tests:** Test against real DICOM SCP with custom CA on:
  - Windows (CryptoAPI/CNG)
  - Linux (OpenSSL 1.1.x and 3.x)
  - macOS (SecTrust)
- **Matrix testing:** Each target framework × each OS
- **Known issue handling:** Document and test workarounds for GitHub issues:
  - #29164 (Linux PartialChain)
  - #59148 (Linux ExtraStore order)
  - #1923 (macOS CustomTrustStore)

5. **Error Handling and Logging**

Provide detailed diagnostics for certificate validation failures:

```csharp
private static string GetChainStatusDescription(X509Chain chain)
{
    var sb = new StringBuilder();
    sb.AppendLine("Certificate chain validation failed:");

    for (int i = 0; i < chain.ChainElements.Count; i++)
    {
        var element = chain.ChainElements[i];
        sb.AppendLine($"  [{i}] {element.Certificate.Subject}");

        foreach (var status in element.ChainElementStatus)
        {
            sb.AppendLine($"      - {status.Status}: {status.StatusInformation}");
        }
    }

    sb.AppendLine($"Chain built: {chain.ChainElements.Count} certificate(s)");
    sb.AppendLine($"Platform: {RuntimeInformation.OSDescription}");
    sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");

    return sb.ToString();
}
```

6. **Fallback Strategy**

For `netstandard2.0` on problematic platforms:

```csharp
// If automatic validation fails, provide option for custom callback
public delegate bool CustomCertificateValidationCallback(
    X509Certificate2 certificate,
    X509Chain chain,
    SslPolicyErrors sslPolicyErrors);

public class DicomTlsOptions
{
    /// <summary>
    /// Custom validation callback for advanced scenarios.
    /// If provided, overrides built-in validation logic.
    /// Useful for certificate pinning or platform-specific workarounds.
    /// </summary>
    public CustomCertificateValidationCallback? CustomValidation { get; set; }
}
```

### Security Considerations

1. **Do not disable validation by default**
   - Always validate certificates unless explicitly configured
   - Log warnings for insecure configurations

2. **Revocation checking**
   - Default to `X509RevocationMode.Online` for security
   - Allow opt-out for offline/performance scenarios
   - Document that macOS ignores `Offline` mode

3. **Certificate pinning for high-security scenarios**
   - Provide API for pinning specific certificates or public keys
   - Use `CustomRootTrust` on .NET 5+ for reliable pinning

4. **Audit logging**
   - Log all certificate validation decisions
   - Include chain details for forensics

### Documentation Priorities

1. **Platform Differences Section**
   - Document Windows/Linux/macOS behavioral differences
   - Explain why `CustomRootTrust` preferred on .NET 5+
   - Warn about `ExtraStore` limitations on older frameworks

2. **Migration Guide**
   - Guide for users upgrading from .NET Framework to .NET 5+
   - Explain behavioral changes in certificate validation

3. **Troubleshooting Guide**
   - Common error scenarios and solutions
   - Platform-specific known issues and workarounds
   - How to enable detailed logging

4. **Security Best Practices**
   - When to use custom trust vs. system trust
   - Certificate pinning scenarios
   - Revocation checking trade-offs

## 7. References

### Microsoft Documentation
- [Cross-Platform Cryptography in .NET](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)
- [TLS/SSL best practices](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices#custom-certificate-trust)
- [X509ChainTrustMode Enum](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chaintrustmode?view=net-10.0)
- [X509Chain.Build() Method](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chain.build?view=net-10.0)
- [Configure certificate authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0#configure-certificate-validation)
- [Breaking changes in .NET Core 3.0 - Cryptography](https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#cryptography)

### GitHub Issues (dotnet/runtime)
- [#20302: Allow X509Chain to replace the root trust list for a single call](https://github.com/dotnet/runtime/issues/20302)
- [#29164: X509Chain behaviour inconsistent on Windows and Linux](https://github.com/dotnet/runtime/issues/29164)
- [#49615: X509Chain.Build() returns true for partial chain when AllowUnknownCertificateAuthority flag is present](https://github.com/dotnet/runtime/issues/49615)
- [#59148: X509Chain.Build() fails on Linux if two root certificates with same subject name in ExtraStore](https://github.com/dotnet/runtime/issues/59148)
- [#1923: X509Chain validation fails on MacOS using CustomTrustStore](https://github.com/dotnet/runtime/issues/1923)
- [#47392: X509ChainPolicy.CustomTrustStore appears to be readonly in .NET 5](https://github.com/dotnet/runtime/issues/47392)
- [#48207: certificates added to user CertificateAuthority store are ignored by X509Chain.Build on macOS](https://github.com/dotnet/runtime/issues/48207)
- [#26111: X509Chain.Build throws on macOS for certificates with an empty subject](https://github.com/dotnet/runtime/issues/26111)
- [#32608: X509Store.Add() on Linux is racing with X509Chain.Build](https://github.com/dotnet/runtime/issues/32608)
- [#87010: Difference in X509Chain output on Linux and Windows](https://github.com/dotnet/runtime/issues/87010)

### GitHub Issues (dotnet/aspnetcore)
- [#29679: Add ExtraStore property to CertificateAuthenticationOptions](https://github.com/dotnet/aspnetcore/issues/29679)

### External Resources
- [How to properly validate X.509 certificates in C# with .NET Core 3.1 & .NET 5+](https://stewartadam.io/blog/2021/04/28/how-properly-validate-x509-certificates-c-net-core-31-net-5) (stewartadam.io)
- [Custom Root Trust with HttpClient in .NET](https://stigvoss.dk/post/custom-root-trust-for-httpclient/) (stigvoss.dk)
- [Linux SSL Certificate verification in .NET Core](https://blog.andypotts.com/2019/02/ssl-certificate-verification-in-net-core.html) (andypotts.com)
- [GitHub - dotnet-x509-certificate-verification](https://github.com/stewartadam/dotnet-x509-certificate-verification) (Code samples)

### Platform Documentation
- [Windows Certificate Chain Validation](https://learn.microsoft.com/en-us/windows/win32/seccrypto/certificate-chain-validation) (Win32 API)
- [OpenSSL X509_verify_cert](https://www.openssl.org/docs/man1.1.1/man3/X509_verify_cert.html) (OpenSSL 1.1.1)
- [Apple Certificate, Key, and Trust Services](https://developer.apple.com/documentation/security/certificate_key_and_trust_services) (Apple Developer)

---

**End of Research Document**
