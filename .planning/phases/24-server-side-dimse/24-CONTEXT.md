# Phase 24: Server-Side DIMSE - Context

**Gathered:** 2026-02-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Implement server-side query/retrieve handlers: C-FIND SCP and C-MOVE SCP with pluggable data sources. Includes a built-in filesystem-backed implementation that can serve as a mini-PACS for testing. C-GET SCP is a should-have. Client-side SCU services already exist from Phase 11.

</domain>

<decisions>
## Implementation Decisions

### Data source interface
- Callback delegates, not interfaces — register `Func<DicomQuery, IAsyncEnumerable<DicomDataset>>` for C-FIND
- Single callback handles all query levels; the DicomQuery object contains the level
- Unregistered callbacks return C-FIND failure status (0xA900 Unable to Process), not empty results
- C-MOVE uses split callbacks: C-FIND callback to find matches, then a separate retrieve callback to get the actual DicomFile/stream per match

### Storage architecture
- Ship both: abstract callback interface as the contract, plus a FileSystemDicomStore as a convenience/reference implementation
- Filesystem layout: hierarchical `patient_id/study_uid/series_uid/sop_uid.dcm`
- SQLite index for metadata persistence — survives restarts without re-scanning
- Integrated store + serve: FileSystemDicomStore handles both incoming C-STORE (save + index) and outgoing C-MOVE/C-GET (query + retrieve) as a one-stop mini-PACS

### Query matching behavior
- Full DICOM wildcard support (* and ?) per PS3.4 C.2.2.2 — case-sensitive for UIDs, case-insensitive for names
- Framework parses DICOM date ranges (e.g. '20240101-20241231') into structured DateRange objects before passing to callbacks
- Filter return keys to only those requested in the C-FIND query, per DICOM conformance
- No framework-level result count limit — data source callbacks control how many results to yield

### Sub-operation tracking
- C-MOVE opens a single association to the destination for all files
- On individual file send failure: continue sending remaining files, report failed count in final C-MOVE-RSP
- Send intermediate Pending C-MOVE-RSP after each sub-operation with remaining/completed/failed counts
- Destination AE title resolution via callback delegate `Func<string, (string host, int port)?>` — flexible for config, LDAP, database, etc.

### Claude's Discretion
- SQLite schema design for the filesystem store index
- C-GET SCP implementation details (should-have)
- Thread safety and concurrency model for the filesystem store
- Association management for C-MOVE forwarding (timeouts, retry)

</decisions>

<specifics>
## Specific Ideas

- Success criteria from roadmap: "Can serve as mini-PACS for testing" and "DCMTK findscu/movescu work against SharpDicom SCP"
- The filesystem store should be usable out-of-the-box with minimal configuration (just a root directory path)
- Microsoft.Data.Sqlite already in Directory.Packages.props (used elsewhere in the project)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 24-server-side-dimse*
*Context gathered: 2026-02-05*
