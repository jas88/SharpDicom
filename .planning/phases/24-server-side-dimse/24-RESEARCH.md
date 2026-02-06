# Phase 24: Server-Side DIMSE (SCP) - Research

**Researched:** 2026-02-05
**Domain:** DICOM Query/Retrieve SCP implementation, SQLite metadata indexing, filesystem storage
**Confidence:** HIGH

## Summary

This phase implements C-FIND SCP, C-MOVE SCP, and C-GET SCP (should-have) handlers within the existing DicomServer infrastructure, plus a FileSystemDicomStore reference implementation backed by SQLite. The existing codebase already has a robust DicomServer with C-ECHO and C-STORE SCP support, DicomCommand factory methods for all Q/R responses, SubOperationProgress tracking, and SCU implementations (CFindScu, CMoveScu, CGetScu) that serve as specification reference for the protocol.

The DICOM standard (PS3.4 Annex C) defines precise matching rules, status codes, and sub-operation behaviors. The project already contains all necessary data structures (DicomCommand, DicomStatus, SubOperationProgress, DicomQuery, QueryRetrieveLevel, PresentationContext) -- the SCP handlers need to wire these together with pluggable data source callbacks.

Microsoft.Data.Sqlite (version 10.0.2, already in Directory.Packages.props) is the right tool for the SQLite index in FileSystemDicomStore. It uses the standard ADO.NET pattern (SqliteConnection, SqliteCommand, SqliteDataReader) with parameterized queries.

**Primary recommendation:** Extend DicomServer's DIMSE dispatch loop and DicomServerOptions to handle C-FIND-RQ, C-MOVE-RQ, and C-GET-RQ using callback delegates, then build FileSystemDicomStore as an integrated store+serve implementation.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Data.Sqlite | 10.0.2 | SQLite index for FileSystemDicomStore | Already in Directory.Packages.props; official Microsoft ADO.NET provider for SQLite |
| System.IO.Pipelines | (in-box) | High-perf I/O for streaming files | Already used elsewhere in project for network I/O |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Memory | 4.6.3 | Span<T> polyfill for netstandard2.0 | Already referenced; needed for span-based parsing |
| Microsoft.Bcl.AsyncInterfaces | 10.0.2 | IAsyncEnumerable for netstandard2.0 | Already referenced; needed for C-FIND result streaming |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Microsoft.Data.Sqlite (raw ADO.NET) | sqlite-net ORM | ORM is simpler but uses reflection (breaks AOT), less control over schema/queries |
| Microsoft.Data.Sqlite (raw ADO.NET) | EF Core + SQLite | Massive dependency, overkill for a 4-table schema |
| SQLite index | In-memory dictionary | No persistence across restarts -- user decided SQLite |
| Callback delegates | Interface-based handlers | User decided callbacks -- more flexible, less ceremony |

## Architecture Patterns

### Recommended File Structure

```
src/SharpDicom/
├── Network/
│   ├── DicomServer.cs                    # Extended: dispatch C-FIND/C-MOVE/C-GET
│   ├── DicomServerOptions.cs             # Extended: OnCFind, OnCMove, OnCGet callbacks
│   └── Dimse/
│       └── Services/
│           ├── CFindScp.cs               # [NEW] C-FIND SCP handler logic
│           ├── CMoveScp.cs               # [NEW] C-MOVE SCP handler logic
│           ├── CGetScp.cs                # [NEW] C-GET SCP handler logic
│           ├── DicomQueryMatcher.cs       # [NEW] DICOM wildcard/range matching
│           └── DicomDateRange.cs          # [NEW] Structured date range for callbacks
├── Storage/
│   ├── FileSystemDicomStore.cs           # [NEW] Integrated store + serve
│   ├── FileSystemDicomStoreOptions.cs    # [NEW] Configuration
│   └── DicomMetadataIndex.cs             # [NEW] SQLite-backed metadata index
```

### Pattern 1: Callback-Based SCP Registration

**What:** Register typed callback delegates on DicomServerOptions for each DIMSE service
**When to use:** All SCP implementations
**Example:**

```csharp
// Per CONTEXT.md decisions: callback delegates, not interfaces
var options = new DicomServerOptions
{
    AETitle = "MY_SCP",
    Port = 11112,

    // C-FIND: returns IAsyncEnumerable<DicomDataset> for streaming results
    OnCFind = (query, ct) => FindMatchesAsync(query, ct),

    // C-MOVE: split callbacks per CONTEXT.md
    // Reuses OnCFind to find matches, then OnCMoveRetrieve to get files
    OnCMoveRetrieve = (dataset, ct) => GetFileForInstance(dataset, ct),

    // Destination AE resolution
    OnResolveMoveDestination = aeTitle => LookupDestination(aeTitle),

    // C-GET: same as C-MOVE but sends on same association
    OnCGetRetrieve = (dataset, ct) => GetFileForInstance(dataset, ct),
};
```

**Source:** CONTEXT.md decisions + existing DicomServerOptions pattern

### Pattern 2: DIMSE Dispatch Loop Extension

**What:** Extend ExtractDimseRequests to recognize C-FIND-RQ, C-MOVE-RQ, C-GET-RQ, and C-CANCEL-RQ command fields
**When to use:** Core server DIMSE loop
**Example:**

The existing `DicomServer.ExtractDimseRequests()` already handles C-ECHO (0x0030) and C-STORE (0x0001). Extend with:
- C-FIND-RQ (0x0020): Parse identifier dataset, invoke callback, stream results as Pending responses
- C-MOVE-RQ (0x0021): Parse identifier + MoveDestination, resolve destination, forward via C-STORE
- C-GET-RQ (0x0010): Parse identifier, send C-STORE sub-operations on same association
- C-CANCEL-RQ (0x0FFF): Signal cancellation to in-progress operations

**Source:** Existing DicomServer.cs (lines 383-463), CommandField.cs constants

### Pattern 3: Streaming C-FIND Response Pattern

**What:** For each matching dataset, send Pending C-FIND-RSP with identifier, then final Success
**When to use:** C-FIND SCP handler

```csharp
// Send Pending responses with matching datasets
await foreach (var match in callback(queryDataset, ct))
{
    // Filter return keys per DICOM conformance
    var filtered = FilterReturnKeys(match, requestedKeys);

    // Send C-FIND-RSP (Pending) with identifier dataset
    var response = DicomCommand.CreateCFindResponse(messageId, sopClassUid, DicomStatus.Pending);
    await SendDimseResponseAsync(stream, contextId, response, filtered, ct);
}

// Send final C-FIND-RSP (Success) with no dataset
var final = DicomCommand.CreateCFindResponse(messageId, sopClassUid, DicomStatus.Success);
await SendDimseResponseAsync(stream, contextId, final, null, ct);
```

**Source:** DICOM PS3.4 C.4.1.3 C-FIND SCP Behavior, existing DicomCommand.CreateCFindResponse

### Pattern 4: C-MOVE Sub-Operation Forwarding

**What:** Open separate association to destination, send C-STORE for each match, report progress
**When to use:** C-MOVE SCP handler

```csharp
// Resolve destination per CONTEXT.md: callback delegate
var destination = onResolveMoveDestination(moveDestination);
if (destination == null)
{
    // Send failure: Move Destination Unknown (0xA801)
    var fail = DicomCommand.CreateCMoveResponse(messageId, sopClassUid,
        new DicomStatus(0xA801), SubOperationProgress.Empty);
    await SendDimseResponseAsync(stream, contextId, fail, null, ct);
    return;
}

// Open single association to destination for all files (CONTEXT.md decision)
await using var forwardClient = new DicomClient(new DicomClientOptions { ... });
await forwardClient.ConnectAsync(storageContexts, ct);

ushort completed = 0, failed = 0, remaining = (ushort)matches.Count;

foreach (var match in matches)
{
    remaining--;
    try
    {
        // Retrieve actual file/dataset via callback
        var file = await onRetrieve(match, ct);
        await forwardClient.StoreAsync(file, ct);
        completed++;
    }
    catch
    {
        failed++;
    }

    // Send intermediate Pending C-MOVE-RSP per CONTEXT.md decision
    var progress = new SubOperationProgress(remaining, completed, failed, 0);
    var pending = DicomCommand.CreateCMoveResponse(messageId, sopClassUid,
        DicomStatus.Pending, progress);
    await SendDimseResponseAsync(stream, contextId, pending, null, ct);
}

// Send final response
var finalStatus = failed > 0 ? new DicomStatus(0xB000) : DicomStatus.Success;
var finalProgress = new SubOperationProgress(0, completed, failed, 0);
```

**Source:** DICOM PS3.4 C.4.2 C-MOVE Operation, CONTEXT.md sub-operation tracking decisions

### Pattern 5: SQLite Metadata Index Schema

**What:** Normalized schema for DICOM instance metadata enabling fast Q/R queries
**When to use:** FileSystemDicomStore (Claude's discretion area per CONTEXT.md)

```sql
-- WAL mode for concurrent read/write
PRAGMA journal_mode = WAL;

CREATE TABLE IF NOT EXISTS patients (
    patient_id TEXT PRIMARY KEY,
    patient_name TEXT,
    patient_birth_date TEXT,
    patient_sex TEXT
);

CREATE TABLE IF NOT EXISTS studies (
    study_instance_uid TEXT PRIMARY KEY,
    patient_id TEXT NOT NULL REFERENCES patients(patient_id),
    study_date TEXT,
    study_time TEXT,
    study_description TEXT,
    accession_number TEXT,
    referring_physician TEXT,
    modalities_in_study TEXT   -- comma-separated
);

CREATE TABLE IF NOT EXISTS series (
    series_instance_uid TEXT PRIMARY KEY,
    study_instance_uid TEXT NOT NULL REFERENCES studies(study_instance_uid),
    modality TEXT,
    series_number TEXT,
    series_description TEXT,
    body_part_examined TEXT
);

CREATE TABLE IF NOT EXISTS instances (
    sop_instance_uid TEXT PRIMARY KEY,
    series_instance_uid TEXT NOT NULL REFERENCES series(series_instance_uid),
    sop_class_uid TEXT NOT NULL,
    instance_number TEXT,
    file_path TEXT NOT NULL,        -- relative path from store root
    file_size INTEGER,
    transfer_syntax_uid TEXT,
    indexed_at TEXT NOT NULL         -- ISO 8601 timestamp
);

-- Indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_patients_name ON patients(patient_name);
CREATE INDEX IF NOT EXISTS idx_studies_date ON studies(study_date);
CREATE INDEX IF NOT EXISTS idx_studies_accession ON studies(accession_number);
CREATE INDEX IF NOT EXISTS idx_studies_patient ON studies(patient_id);
CREATE INDEX IF NOT EXISTS idx_series_study ON series(study_instance_uid);
CREATE INDEX IF NOT EXISTS idx_series_modality ON series(modality);
CREATE INDEX IF NOT EXISTS idx_instances_series ON instances(series_instance_uid);
CREATE INDEX IF NOT EXISTS idx_instances_sop_class ON instances(sop_class_uid);
```

**Source:** DICOM PS3.4 Annex C Information Model (Patient/Study/Series/Instance hierarchy), Claude's discretion per CONTEXT.md

### Anti-Patterns to Avoid

- **Hand-rolling wildcard matching with regex:** DICOM wildcards are simpler than regex (`*` = `%`, `?` = `_` in SQL LIKE). Use SQLite LIKE with ESCAPE for the index, implement DICOM-to-SQL translation.
- **Blocking on IAsyncEnumerable consumption:** The C-FIND callback returns IAsyncEnumerable -- must stream results incrementally, not collect-then-send.
- **Opening new association per C-STORE sub-op in C-MOVE:** CONTEXT.md specifies single association for all files. Must reuse.
- **Including all dataset elements in C-FIND response:** Per DICOM conformance, only return keys that were requested in the query identifier. Non-requested tags must be filtered out.
- **Using in-memory collections for the index:** User specifically chose SQLite for persistence across restarts.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Date range parsing | Custom date parser | Structured DicomDateRange with DateTime.ParseExact | DICOM dates are YYYYMMDD, ranges use dash separator; edge cases with open-ended ranges |
| SQLite connection management | Custom pooling | Single long-lived SqliteConnection with WAL mode | SQLite async is actually sync (Microsoft docs state this); WAL gives concurrent read/write |
| DICOM command building | Raw byte manipulation | Existing DicomCommand factory methods | CreateCFindResponse, CreateCMoveResponse, CreateCGetResponse already exist |
| Sub-operation tracking | Custom counters | Existing SubOperationProgress record struct | Already has Remaining, Completed, Failed, Warning fields |
| Presentation context negotiation | Custom logic | Existing DicomServer.CreateDefaultAcceptResult + association handling | Already validates and accepts contexts |

**Key insight:** Almost all the building blocks exist. DicomCommand has all factory methods. SubOperationProgress tracks counts. The SCU implementations show exactly what the SCP must produce. The work is wiring them together in the server dispatch loop.

## Common Pitfalls

### Pitfall 1: C-FIND Return Key Filtering

**What goes wrong:** SCP returns all elements from the stored dataset, not just those requested in the query identifier. This violates DICOM PS3.4 C.2.2 which states the response shall contain "All Required Keys from the request" and supported Optional Keys.
**Why it happens:** It's easy to just return the full stored dataset as a match result.
**How to avoid:** After the data source callback returns a matching dataset, filter it to only include tags that were present in the query identifier (either as matching keys with values or return keys with zero length). Always include QueryRetrieveLevel.
**Warning signs:** DCMTK findscu returns unexpected extra fields; conformance test tools flag the response.

### Pitfall 2: DICOM Wildcard vs SQL Wildcard Mismatch

**What goes wrong:** DICOM uses `*` and `?` while SQL LIKE uses `%` and `_`. Naive translation breaks when values contain literal `%` or `_`.
**Why it happens:** Forgetting that SQL LIKE metacharacters need escaping.
**How to avoid:** Use SQLite LIKE with ESCAPE clause: translate DICOM `*` to `%`, `?` to `_`, and escape any literal `%` or `_` in the query value. Example: `WHERE patient_name LIKE $pattern ESCAPE '\'`.
**Warning signs:** Searches for patients with underscores in names return incorrect results.

### Pitfall 3: C-MOVE Association Must Be Separate

**What goes wrong:** Attempting to send C-STORE sub-operations on the same association as the C-MOVE request.
**Why it happens:** Confusing C-MOVE (separate association) with C-GET (same association).
**How to avoid:** DICOM PS3.4 C.4.2 is explicit: "C-STORE sub-operations shall always be accomplished over an Association different from the Association that accomplishes the C-MOVE operation." Create a new DicomClient for forwarding.
**Warning signs:** Protocol violation errors from the destination SCP.

### Pitfall 4: SQLite Async Is Actually Sync

**What goes wrong:** Calling SqliteConnection.OpenAsync() or command.ExecuteReaderAsync() expecting true async I/O.
**Why it happens:** Microsoft.Data.Sqlite docs clearly state: "SQLite doesn't support asynchronous I/O. Async ADO.NET methods will execute synchronously."
**How to avoid:** Use synchronous methods and offload to thread pool if needed (`Task.Run`). Enable WAL mode (`PRAGMA journal_mode = WAL`) for concurrent read/write. The FileSystemDicomStore should use a dedicated thread or SemaphoreSlim to serialize writes while allowing concurrent reads.
**Warning signs:** Thread pool starvation under high load if awaiting "async" SQLite calls on the main I/O loop.

### Pitfall 5: Case Sensitivity Mismatch in Wildcard Matching

**What goes wrong:** Applying case-insensitive matching to UIDs or case-sensitive matching to patient names.
**Why it happens:** DICOM PS3.4 C.2.2.2.4 has specific rules: wildcard matching is case-sensitive for all VRs EXCEPT PN (Person Name). UIDs don't support wildcards at all.
**How to avoid:** For the SQLite index, use `COLLATE NOCASE` on patient_name column. For UIDs (UI VR), use exact match only (no wildcards). For other string VRs (LO, SH, CS, etc.), use case-sensitive LIKE.
**Warning signs:** Case-insensitive UID matching returns false positives; case-sensitive name matching misses patients.

### Pitfall 6: Missing Status Codes for Unregistered Handlers

**What goes wrong:** Returning empty results when no C-FIND callback is registered, instead of a failure status.
**Why it happens:** Defaulting to "return nothing" instead of signaling "unsupported."
**How to avoid:** Per CONTEXT.md: "Unregistered callbacks return C-FIND failure status (0xA900 Unable to Process), not empty results." Check for null callbacks before processing.
**Warning signs:** SCU receives Success with zero results instead of an error indicating the service is not supported.

### Pitfall 7: Forgetting to Read and Discard C-FIND/C-MOVE/C-GET Identifier

**What goes wrong:** After reading the command PDU, failing to read the subsequent identifier dataset, corrupting the PDU stream.
**Why it happens:** C-FIND/C-MOVE/C-GET requests have `CommandDataSetType != 0x0101`, meaning a dataset follows. Must consume it.
**How to avoid:** Always check HasDataset on the parsed command. If the handler is not registered, still read and discard the dataset (like existing C-STORE handler does with `ReadAndDiscardDatasetAsync`).
**Warning signs:** Association hangs or next PDU parse fails with garbled data.

## Code Examples

### Microsoft.Data.Sqlite Connection with WAL

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Enable WAL for concurrent reads during writes
using var walCmd = connection.CreateCommand();
walCmd.CommandText = "PRAGMA journal_mode = WAL";
walCmd.ExecuteNonQuery();
```

### Microsoft.Data.Sqlite Bulk Insert Pattern

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/bulk-insert
using var transaction = connection.BeginTransaction();
using var command = connection.CreateCommand();
command.CommandText = @"
    INSERT OR REPLACE INTO instances
    (sop_instance_uid, series_instance_uid, sop_class_uid, instance_number,
     file_path, file_size, transfer_syntax_uid, indexed_at)
    VALUES ($uid, $series, $class, $num, $path, $size, $ts, $time)";

var pUid = command.CreateParameter(); pUid.ParameterName = "$uid";
var pSeries = command.CreateParameter(); pSeries.ParameterName = "$series";
// ... add all parameters

command.Parameters.Add(pUid);
command.Parameters.Add(pSeries);
// ... add all

foreach (var instance in instances)
{
    pUid.Value = instance.SopInstanceUid;
    pSeries.Value = instance.SeriesInstanceUid;
    // ... set all
    command.ExecuteNonQuery();
}

transaction.Commit();
```

### DICOM Wildcard to SQL LIKE Translation

```csharp
// DICOM PS3.4 C.2.2.2.4: * matches any sequence, ? matches single character
// SQL LIKE: % matches any sequence, _ matches single character
// Must escape literal % and _ in the input
static string DicomWildcardToSqlLike(string dicomPattern)
{
    var sb = new StringBuilder(dicomPattern.Length + 4);
    foreach (char c in dicomPattern)
    {
        switch (c)
        {
            case '*': sb.Append('%'); break;
            case '?': sb.Append('_'); break;
            case '%': sb.Append(@"\%"); break;  // Escape literal %
            case '_': sb.Append(@"\_"); break;  // Escape literal _
            case '\\': sb.Append(@"\\"); break; // Escape literal backslash
            default: sb.Append(c); break;
        }
    }
    return sb.ToString();
}
// Use with: WHERE col LIKE $pattern ESCAPE '\'
```

### DICOM Date Range Parsing

```csharp
// DICOM PS3.4 C.2.2.2.5: Range matching uses "-" delimiter
// Formats: "YYYYMMDD-YYYYMMDD", "YYYYMMDD-", "-YYYYMMDD", "YYYYMMDD"
static (DateTime? From, DateTime? To) ParseDicomDateRange(string value)
{
    if (string.IsNullOrEmpty(value)) return (null, null);

    var dashIndex = value.IndexOf('-');
    if (dashIndex < 0)
    {
        // Single date value
        var date = DateTime.ParseExact(value.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture);
        return (date, date);
    }

    DateTime? from = null, to = null;
    var fromStr = value.Substring(0, dashIndex).Trim();
    var toStr = value.Substring(dashIndex + 1).Trim();

    if (!string.IsNullOrEmpty(fromStr))
        from = DateTime.ParseExact(fromStr, "yyyyMMdd", CultureInfo.InvariantCulture);
    if (!string.IsNullOrEmpty(toStr))
        to = DateTime.ParseExact(toStr, "yyyyMMdd", CultureInfo.InvariantCulture);

    return (from, to);
}
```

### Existing DicomCommand Factory Usage (Already in Codebase)

```csharp
// Source: src/SharpDicom/Network/Dimse/DicomCommand.cs
// C-FIND Response (Pending with identifier dataset)
var response = DicomCommand.CreateCFindResponse(messageId, sopClassUid, DicomStatus.Pending);
// ... send with identifier dataset

// C-FIND Response (Final success, no dataset)
var final = DicomCommand.CreateCFindResponse(messageId, sopClassUid, DicomStatus.Success);
// ... send without dataset

// C-MOVE Response (with sub-operation progress)
var progress = new SubOperationProgress(remaining, completed, failed, 0);
var moveRsp = DicomCommand.CreateCMoveResponse(messageId, sopClassUid, status, progress);

// C-GET Response (same pattern as C-MOVE)
var getRsp = DicomCommand.CreateCGetResponse(messageId, sopClassUid, status, progress);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Interface-based SCP handlers | Callback delegates (Func<>) | CONTEXT.md decision | More flexible, less ceremony, consistent with existing OnCEcho/OnCStoreRequest |
| Separate find + store classes | Integrated FileSystemDicomStore | CONTEXT.md decision | One class handles C-STORE (save+index) and C-FIND/C-MOVE/C-GET (query+retrieve) |
| Custom file scanning | SQLite index + DicomFile.OpenAsync | CONTEXT.md decision | Survives restarts without re-scanning |

## DICOM Standard Reference (Verified)

### C-FIND SCP (PS3.4 C.4.1.3)

- **SCP matches all keys** in the request identifier against stored data
- **Pending responses** (0xFF00) include matching identifier dataset
- **Final response** (0x0000 Success) has no dataset
- **Failure**: 0xA700 (Out of Resources), 0xA900 (Identifier does not match SOP Class), 0xCxxx (Unable to process)
- **Cancel**: 0xFE00 (in response to C-CANCEL-FIND)
- **Return keys**: Only keys present in the request identifier should be returned

**Confidence: HIGH** -- Verified from DICOM PS3.4 official docs

### C-MOVE SCP (PS3.4 C.4.2)

- **C-STORE sub-operations on SEPARATE association** from the C-MOVE association
- **Status codes**: 0x0000 (Success), 0xFF00 (Pending), 0xB000 (Warning - some failed), 0xFE00 (Cancel), 0xA701/0xA702 (Out of resources), 0xA801 (Move Destination Unknown), 0xA900 (Identifier mismatch), 0xCxxx (Unable to process)
- **Sub-operation counts** (Remaining, Completed, Failed, Warning) required in Pending responses
- **The SCP serves as SCU of Storage Service Class** for forwarding

**Confidence: HIGH** -- Verified from DICOM PS3.4 C.4.2

### C-GET SCP (PS3.4 C.4.3)

- **C-STORE sub-operations on SAME association** as the C-GET request
- **SCP serves as SCU of Storage Service Class** and sends C-STORE-RQ on the same association
- **Status code pattern identical to C-MOVE** except no MoveDestination
- **The requesting SCU must support SCP role** for Storage SOP Classes (role selection in association negotiation)

**Confidence: HIGH** -- Verified from DICOM PS3.4 C.4.3

### Attribute Matching (PS3.4 C.2.2.2)

| Match Type | Description | VRs |
|------------|-------------|-----|
| Single Value | Exact match (case-sensitive except PN) | All string VRs |
| Wild Card | `*` = any sequence, `?` = single char | AE, CS, LO, LT, PN, SH, ST, UC, UR, UT |
| List of UID | Backslash-separated UIDs | UI only |
| Range | `YYYYMMDD-YYYYMMDD` format | DA, DT, TM |
| Universal | Zero-length value matches all | All |
| Sequence | Matching within sequence items | SQ |

**Critical rules:**
- Wildcard matching is **case-sensitive** for all VRs **except PN** (Person Name)
- UIDs (**UI VR**) do **NOT support wildcard matching** -- exact match or list only
- Zero-length value = Universal Matching (return all values)

**Confidence: HIGH** -- Verified from DICOM PS3.4 C.2.2.2 and C.2.2.2.4

### Microsoft.Data.Sqlite

- **SQLite does NOT support async I/O** -- async ADO.NET methods execute synchronously
- **Use WAL mode** (`PRAGMA journal_mode = WAL`) for concurrent read/write
- **Parameter prefix**: `$name` or `@name` both work
- **Connection string**: `Data Source=path/to/db.sqlite`
- Already at version **10.0.2** in Directory.Packages.props

**Confidence: HIGH** -- Verified from Microsoft Learn official docs

## Open Questions

1. **Thread safety model for FileSystemDicomStore**
   - What we know: SQLite with WAL supports concurrent reads + one writer. Microsoft.Data.Sqlite connections are not thread-safe.
   - What's unclear: Best concurrency pattern for high-throughput C-STORE (write) + simultaneous C-FIND (read).
   - Recommendation: Use separate connections for read and write paths. Use SemaphoreSlim(1,1) for write serialization. Read connections can be pooled or created per-query since WAL allows concurrent reads.

2. **C-GET SCP role selection negotiation**
   - What we know: The SCU must propose Storage SOP Classes with SCP role for C-GET to work (existing CGetScu shows this with `WithScpRole()`).
   - What's unclear: How DicomServer should validate that the requesting SCU has accepted SCP role for the required Storage SOP Classes before attempting C-STORE sub-operations.
   - Recommendation: Check the association's accepted presentation contexts for Storage SOP Class with SCU role (which means the SCP can send on it). If not present, return Unable to Process status.

3. **Association management for C-MOVE forwarding**
   - What we know: CONTEXT.md says single association for all files. Need timeout and failure handling.
   - What's unclear: Exact timeout values, retry behavior on transient failures.
   - Recommendation: Use DicomClient with reasonable defaults (30s connection timeout, 30s per-operation timeout). No retry on individual failures -- just increment failed count per CONTEXT.md decision. Close forwarding association after all sub-operations complete or fail.

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.4 C.4 DIMSE-C Service Groups](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_c.4.html) -- C-FIND SCP behavior
- [DICOM PS3.4 C.4.2 C-MOVE Operation](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_c.4.2.html) -- C-MOVE SCP behavior, status codes
- [DICOM PS3.4 C.4.3 C-GET Operation](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_C.4.3.html) -- C-GET SCP behavior
- [DICOM PS3.4 C.2.2.2 Attribute Matching](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_C.2.2.2.html) -- Matching rules
- [DICOM PS3.4 C.2.2.2.4 Wild Card Matching](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/sect_c.2.2.2.4.html) -- Wildcard rules
- [Microsoft.Data.Sqlite Overview](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) -- ADO.NET SQLite provider
- [Microsoft.Data.Sqlite Async Limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async) -- WAL mode, sync-only I/O
- [Microsoft.Data.Sqlite Bulk Insert](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/bulk-insert) -- Performance pattern
- Existing codebase: DicomServer.cs, DicomCommand.cs, CFindScu.cs, CMoveScu.cs, CGetScu.cs, DicomServerOptions.cs

### Secondary (MEDIUM confidence)
- [DICOM PS3.4 Chapter C](https://dicom.nema.org/medical/dicom/current/output/chtml/part04/chapter_c.html) -- Q/R Service Class overview
- [Microsoft.Data.Sqlite Connection Strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings) -- Configuration keywords

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- Microsoft.Data.Sqlite already in project, verified official docs
- Architecture: HIGH -- Based on existing codebase patterns (DicomServer, DicomCommand factories), CONTEXT.md decisions
- DICOM protocol: HIGH -- Verified against official NEMA DICOM standard (PS3.4)
- Pitfalls: HIGH -- Derived from DICOM standard rules and verified Microsoft.Data.Sqlite behavior

**Research date:** 2026-02-05
**Valid until:** 2026-03-07 (stable domain -- DICOM standard changes infrequently)
