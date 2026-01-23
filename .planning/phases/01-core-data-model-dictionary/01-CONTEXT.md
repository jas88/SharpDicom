# Phase 1: Core Data Model & Dictionary - Context

**Gathered:** 2025-01-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Foundation data structures (DicomTag, DicomVR, DicomElement, DicomDataset, DicomSequence, DicomUID, TransferSyntax) plus a Roslyn incremental source generator that produces ~4000 tag definitions from cached NEMA Part 6/7/15/16 XML files.

</domain>

<decisions>
## Implementation Decisions

### Struct Design

**DicomTag:**
- Single uint (4 bytes) representation, group/element as computed properties
- readonly record struct with full interface support (IEquatable, IComparable)
- Static members for common tags via source generation (DicomTag.PatientId)
- ToString includes keyword: "(0010,0020) PatientId"
- Parse accepts hex only, separate keyword lookup via DicomTags.ByKeyword()
- Private creator tracking via separate PrivateCreatorDictionary (tag stays 4 bytes)

**DicomVR:**
- 2-byte struct storing packed ASCII code
- VR validation is configurable: strict throws, lenient maps to UN, permissive accepts anything
- VR metadata (name, padding, max length) embedded in larger struct, not separate lookup
- readonly record struct
- Generate validation methods per VR (IsValidDA, IsValidPN, etc.)

**DicomElement:**
- Interface-based type hierarchy (IDicomElement with DicomStringElement, DicomIntElement, etc.)
- Elements include ReadOnlyMemory<byte> for raw value
- Value semantics - copy on add to dataset
- Stateless - no caching, parse on each accessor call

**DicomDataset:**
- Dictionary<DicomTag, IDicomElement> with sorted cache for enumeration
- Implements IEnumerable<IDicomElement> for LINQ support
- Not thread-safe (documented)
- Collection initializer syntax supported
- ToOwned() method for deep copy (escaping pooled buffer scope)

**DicomSequence:**
- Separate class, Items as IReadOnlyList<DicomDataset>

**DicomUID:**
- Inline 64-byte storage (zero allocation)
- Partial class with source-generated static members (DicomUID.CtImageStorage)

**TransferSyntax:**
- Struct with DicomUID + properties (IsExplicitVR, IsLittleEndian, IsEncapsulated)

**DicomMaskedTag:**
- Include in Phase 1 for dictionary pattern matching (50xx,0010)

**ValueMultiplicity:**
- Struct with Min, Max, IsUnlimited properties

**DicomDictionaryEntry:**
- readonly record struct
- Multi-VR tags: DicomVR[] ValueRepresentations array, first is default

**Other:**
- Nested namespaces (SharpDicom.Data, SharpDicom.IO, etc.)
- Deep exception hierarchy (DicomException → DicomDataException → DicomTagException)
- Full XML documentation comments
- Include internal DateOnly/TimeOnly polyfills for netstandard2.0

### Source Generator

**Input:**
- NEMA XML files cached in repo (data/dicom-standard/)
- Parse Part 6, Part 7, Part 15, Part 16
- Weekly GitHub Action to check for updates, create PR if changed

**Generator:**
- Separate SharpDicom.Generators project
- Incremental generator (IIncrementalGenerator)
- Multiple output files (DicomTag.Generated.cs, DicomUID.Generated.cs, etc.)
- Full C# files with namespace declarations
- Conditional compilation for FrozenDictionary (#if NET8_0_OR_GREATER)

**Output:**
- Include retired tags/UIDs with IsRetired=true
- Transfer syntax definitions parsed from XML
- .NET naming conventions for keywords (PatientId not PatientID)
- Instance-based registry (DicomDictionary.Default.GetEntry)
- Hide internal generated members with EditorBrowsable
- SOP Class categorization (IsStorage, IsQuery properties)
- Fail build on XML parse errors

### API Surface

**Element access:**
- Both indexer (dataset[tag]) and TryGetElement patterns
- Both nullable (GetString()) and throwing (GetStringOrThrow()) variants
- Convenience accessors directly on DicomDataset
- Type-specific (GetInt32Array) and generic (GetValues<T>) array accessors

**Mutation:**
- Add, Update, and AddOrUpdate all available
- Fluent API (WithElement chain) supported

**Construction:**
- Both constructors and factory methods
- Parameterless and capacity-hint constructors for DicomDataset

### Test Approach

- NUnit framework (workspace standard)
- Single test project (SharpDicom.Tests)
- NUnit Assert (no FluentAssertions)
- > 90% coverage target
- Data-driven tests with [TestCase] for VR validation
- Parse NEMA XML in tests to verify generated code
- Both Roslyn testing package and snapshot verification for generator
- Comprehensive edge case coverage
- Direct construction + builder pattern as needed
- CI tests on net9.0 only (primary TFM)
- Defer benchmarks to Phase 2

### Claude's Discretion

- Exact struct layouts and padding
- Internal helper method organization
- XML parsing implementation details
- Test helper utilities design
- Generated code formatting style

</decisions>

<specifics>
## Specific Ideas

- readonly record struct for core types (C# 10+ feature, net6.0+)
- FrozenDictionary for lookup tables on .NET 8+
- Follow Utf8JsonReader pattern (ref struct for hot path, class wrapper for async)
- Collection initializers for dataset construction convenience

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-core-data-model-dictionary*
*Context gathered: 2025-01-26*
