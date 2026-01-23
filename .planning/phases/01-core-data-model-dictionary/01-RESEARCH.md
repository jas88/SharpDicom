# Phase 1: Core Data Model & Dictionary - Research

**Researched:** 2026-01-26
**Domain:** C# struct design, Roslyn source generators, DICOM dictionary parsing
**Confidence:** HIGH

## Summary

Phase 1 establishes the foundation data structures for SharpDicom and implements a Roslyn incremental source generator to parse NEMA Part 6 XML files into C# code. This is well-trodden ground with established patterns in both domains.

The core challenge is designing high-performance value types (structs) that provide zero-allocation operations while maintaining full .NET semantics (equality, comparison, hashing). Modern C# features (readonly record struct, IEquatable<T>) provide excellent building blocks.

Source generators have matured significantly with the IIncrementalGenerator API in .NET 6+. The standard approach uses AdditionalTextsProvider to consume XML files, XDocument for parsing, and produces multiple output files with conditional compilation for multi-targeting.

**Primary recommendation:** Use readonly record struct for all core types, implement IEquatable<T> explicitly for performance, build incremental generator following official cookbook patterns, test with Verify.SourceGenerators snapshot testing.

## Standard Stack

The established libraries/tools for this domain:

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.CodeAnalysis.CSharp | Latest (4.x+) | Roslyn compiler APIs for source generators | Official .NET compiler platform, required for IIncrementalGenerator |
| System.Xml.Linq | Built-in | XML parsing with XDocument/XElement | Standard .NET XML API, LINQ query support, built into BCL |
| System.Collections.Frozen | .NET 8+ | FrozenDictionary for read-optimized lookups | 50% faster lookups than Dictionary, zero-allocation reads |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Verify.SourceGenerators | Latest (v3.x+) | Snapshot testing for source generators | All source generator tests - handles multiple files and diagnostics automatically |
| NUnit | 4.x | Test framework | Workspace standard, excellent data-driven test support with [TestCase] |
| Microsoft.NET.Test.Sdk | Latest | Test SDK | Required for all .NET test projects |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| XDocument | XmlReader | XmlReader is faster but more verbose; XDocument offers better developer ergonomics and sufficient performance for build-time parsing |
| FrozenDictionary | Dictionary | Dictionary for netstandard2.0 fallback; FrozenDictionary has high creation cost but 50% faster reads - worth it for static dictionaries |
| readonly record struct | class | Classes for mutable types; structs avoid allocations and provide value semantics which is correct for DICOM elements |

**Installation:**

```bash
# Generator project dependencies
dotnet add package Microsoft.CodeAnalysis.CSharp
dotnet add package Microsoft.CodeAnalysis.Analyzers

# Test project dependencies
dotnet add package NUnit
dotnet add package NUnit3TestAdapter
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package Verify.SourceGenerators
dotnet add package Verify.NUnit
```

## Architecture Patterns

### Recommended Project Structure

```
SharpDicom/
├── src/
│   ├── SharpDicom/
│   │   ├── Data/                # Core data structures
│   │   │   ├── DicomTag.cs
│   │   │   ├── DicomVR.cs
│   │   │   ├── DicomElement.cs
│   │   │   ├── DicomDataset.cs
│   │   │   ├── DicomSequence.cs
│   │   │   ├── DicomUID.cs
│   │   │   ├── TransferSyntax.cs
│   │   │   └── Exceptions/
│   │   └── Internal/            # Polyfills for netstandard2.0
│   │       ├── DateOnly.cs
│   │       └── TimeOnly.cs
│   └── SharpDicom.Generators/
│       ├── DicomDictionaryGenerator.cs
│       ├── Parsing/
│       │   ├── Part6Parser.cs
│       │   ├── Part7Parser.cs
│       │   ├── Part15Parser.cs
│       │   └── Part16Parser.cs
│       └── Emitters/
│           ├── TagEmitter.cs
│           ├── UidEmitter.cs
│           └── VrInfoEmitter.cs
├── tests/
│   └── SharpDicom.Tests/
│       ├── Data/
│       │   ├── DicomTagTests.cs
│       │   ├── DicomVRTests.cs
│       │   └── DicomDatasetTests.cs
│       └── Generators/
│           └── DicomDictionaryGeneratorTests.cs
└── data/
    └── dicom-standard/
        ├── part06.xml
        ├── part07.xml
        ├── part15.xml
        ├── part16.xml
        └── VERSION
```

### Pattern 1: Readonly Record Struct with IEquatable<T>

**What:** Value type with compiler-generated equality, explicit IEquatable<T> for performance

**When to use:** All core DICOM value types (Tag, VR, UID, Element)

**Example:**

```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/struct
// Performance analysis: https://montemagno.com/optimizing-c-struct-equality-with-iequatable/
public readonly record struct DicomTag : IEquatable<DicomTag>, IComparable<DicomTag>
{
    private readonly uint _value;

    public DicomTag(ushort group, ushort element)
        => _value = ((uint)group << 16) | element;

    public ushort Group => (ushort)(_value >> 16);
    public ushort Element => (ushort)_value;
    public uint Value => _value;

    public bool IsPrivate => (Group & 1) == 1;

    // IEquatable<T> provides type-safe equality without boxing
    // ~35% faster than default struct equality
    public bool Equals(DicomTag other) => _value == other._value;

    public int CompareTo(DicomTag other) => _value.CompareTo(other._value);

    public override int GetHashCode() => (int)_value;

    public override string ToString() => $"({Group:X4},{Element:X4})";
}
```

**Benefits:**
- `readonly record` provides value equality by default
- Explicit `IEquatable<T>` avoids boxing (~35% faster lookups)
- Single uint field = 4 bytes, trivial equality/comparison
- Stack-allocated, zero GC pressure

### Pattern 2: Incremental Source Generator with AdditionalTextsProvider

**What:** IIncrementalGenerator consuming XML files via AdditionalTextsProvider

**When to use:** All build-time code generation from external files

**Example:**

```csharp
// Source: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md
[Generator]
public class DicomDictionaryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter to DICOM XML files only
        var xmlFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("part06.xml") ||
                                   file.Path.EndsWith("part07.xml"));

        // Parse XML and extract tag definitions
        var tagDefinitions = xmlFiles
            .Select(static (text, ct) =>
            {
                var xml = XDocument.Parse(text.GetText(ct)!.ToString());
                return Part6Parser.ParseTags(xml);
            })
            .SelectMany(static (tags, _) => tags);

        // Collect all tags and generate single file
        var allTags = tagDefinitions.Collect();

        context.RegisterSourceOutput(allTags,
            static (context, tags) =>
            {
                var source = TagEmitter.GenerateTagClass(tags);
                context.AddSource("DicomTag.Generated.cs",
                    SourceText.From(source, Encoding.UTF8));
            });
    }
}
```

**Benefits:**
- Incremental - only reprocesses changed XML files
- `AdditionalTextsProvider` handles file tracking automatically
- `Select` + `Collect` pattern standard for aggregating multiple inputs
- Diagnostics can be reported via `context.ReportDiagnostic()`

### Pattern 3: Multi-Targeting with Conditional Compilation

**What:** Generate different code for different TFMs using preprocessor directives

**When to use:** Leveraging .NET 8+ features (FrozenDictionary) with fallbacks

**Example:**

```csharp
// Source: Generated code pattern from context decisions
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

public static partial class DicomTags
{
#if NET8_0_OR_GREATER
    private static readonly FrozenDictionary<uint, DicomDictionaryEntry> s_byValue =
        FrozenDictionary.ToFrozenDictionary(GetAllEntries(), e => e.Tag.Value);
#else
    private static readonly Dictionary<uint, DicomDictionaryEntry> s_byValue =
        GetAllEntries().ToDictionary(e => e.Tag.Value);
#endif

    public static DicomDictionaryEntry? GetEntry(DicomTag tag)
    {
        return s_byValue.TryGetValue(tag.Value, out var entry) ? entry : null;
    }
}
```

**Benefits:**
- FrozenDictionary on .NET 8+ provides 50% faster lookups
- Graceful fallback to Dictionary on older TFMs
- Single source file, compiler handles branching

### Pattern 4: Snapshot Testing for Source Generators

**What:** Use Verify.SourceGenerators to validate generated code via snapshots

**When to use:** All source generator tests

**Example:**

```csharp
// Source: https://andrewlock.net/creating-a-source-generator-part-2-testing-an-incremental-generator-with-snapshot-testing/
using Verify;
using VerifyNUnit;

[TestFixture]
public class DicomDictionaryGeneratorTests
{
    [ModuleInitializer]
    public static void InitializeVerify()
    {
        VerifySourceGenerators.Enable();
    }

    [Test]
    public Task GeneratesTagDefinitions()
    {
        // Arrange: Create compilation with additional XML file
        var source = ""; // Empty source, generator uses additional files
        var compilation = CreateCompilation(source)
            .AddAdditionalFiles("part06.xml");

        var generator = new DicomDictionaryGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        // Act: Run generator
        driver = driver.RunGenerators(compilation);

        // Assert: Verify snapshot matches
        return Verifier.Verify(driver);
    }
}
```

**Benefits:**
- Automatically handles multiple generated files
- Captures diagnostics (errors/warnings)
- First run creates `.verified.cs` files for review
- Subsequent runs compare against verified snapshots
- Failed tests show exact diff in IDE

### Anti-Patterns to Avoid

- **Using ISourceGenerator instead of IIncrementalGenerator:** The old API is deprecated and performs poorly in the IDE. Always use IIncrementalGenerator (available since .NET 6).

- **Combining with CompilationProvider unnecessarily:** CompilationProvider fires on every code change, killing incremental performance. Only combine when you truly need semantic model for all types.

- **Large data in pipeline transforms:** Carry minimal data through pipeline. Extract only what you need early, use value types and immutable collections for caching.

- **Custom equality for pipeline values:** Use value types (struct, record) or ImmutableArray. The generator driver compares pipeline values to detect changes - custom reference types break caching.

- **Using ref struct for data model:** ref struct cannot be stored in fields, preventing use in datasets and collections. Use readonly record struct instead.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Value equality | Manual Equals/GetHashCode | readonly record struct + IEquatable<T> | Compiler-generated equality is correct, IEquatable avoids boxing (35% faster) |
| Frozen collections | Custom immutable dictionary | FrozenDictionary (.NET 8+) | 50% faster lookups with perfect hashing, tested and optimized by runtime team |
| XML parsing | String manipulation | XDocument + LINQ to XML | Handles namespaces, entities, encoding automatically; query syntax prevents brittle parsing |
| Source generator testing | Manual compilation + string comparison | Verify.SourceGenerators | Handles multiple files, diagnostics, diff display, snapshot management |
| Date/time polyfills | Custom structs | Use community polyfills or fork existing | DateOnly/TimeOnly polyfills are well-tested, edge cases are subtle |

**Key insight:** Value types with proper equality are tricky. Boxing, defensive copies, and GetHashCode distribution matter for performance. readonly record struct + explicit IEquatable<T> is the proven pattern.

## Common Pitfalls

### Pitfall 1: Struct Defensive Copies

**What goes wrong:** Calling methods on readonly struct properties creates defensive copies, killing performance.

**Why it happens:** C# compiler doesn't know if a method mutates state, so it copies the struct to protect against mutation.

**How to avoid:**
- Mark all value type fields as readonly
- Use readonly record struct (implicitly readonly)
- Make all methods readonly if struct is mutable

**Warning signs:**
- Profiler shows unexpected memory allocations
- Methods called on struct properties are slow
- PerfView shows many struct copies

**Example:**

```csharp
// BAD: Defensive copy on every call
public struct MutableTag
{
    private uint _value;
    public ushort Group => (ushort)(_value >> 16); // Copies entire struct!
}

// GOOD: No defensive copies
public readonly struct ImmutableTag
{
    private readonly uint _value;
    public ushort Group => (ushort)(_value >> 16); // Direct access
}
```

### Pitfall 2: Source Generator Pipeline Caching Breaks

**What goes wrong:** Generator re-runs on every keystroke despite using IIncrementalGenerator.

**Why it happens:** Pipeline values don't implement proper equality, or carry too much data that changes frequently.

**How to avoid:**
- Use value types (struct, record) or ImmutableArray in pipeline
- Carry minimal data - extract only needed properties early in pipeline
- Never use classes without proper IEquatable<T> implementation
- Test caching with incremental generator tests

**Warning signs:**
- IDE becomes sluggish when typing
- Generator diagnostics show in output window constantly
- Build times increase dramatically

**Example:**

```csharp
// BAD: SyntaxNode changes on every edit, breaks caching
var nodes = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: (node, _) => node is ClassDeclarationSyntax,
        transform: (ctx, _) => ctx.Node); // Carries entire syntax tree!

// GOOD: Extract only needed data as value type
var classInfo = context.SyntaxProvider
    .CreateSyntaxProvider(
        predicate: (node, _) => node is ClassDeclarationSyntax,
        transform: (ctx, _) => new ClassInfo( // Value type
            Name: ((ClassDeclarationSyntax)ctx.Node).Identifier.Text,
            Namespace: ctx.SemanticModel.GetDeclaredSymbol(ctx.Node)?.ContainingNamespace?.ToDisplayString()
        ));

record struct ClassInfo(string Name, string? Namespace);
```

### Pitfall 3: FrozenDictionary Creation Cost

**What goes wrong:** Using FrozenDictionary for frequently changing data causes severe performance degradation.

**Why it happens:** FrozenDictionary has high creation cost (630K+ reads needed to break even vs Dictionary creation).

**How to avoid:**
- Only use FrozenDictionary for data created once at startup
- Source generators perfect use case - created at build time, never mutated
- Runtime data that changes? Use Dictionary

**Warning signs:**
- Startup/load times are slow
- Profiler shows significant time in ToFrozenDictionary()
- Frequent dictionary creation during app lifetime

**Reference:** [.NET 8 performance: Dictionary vs. FrozenDictionary](https://startdebugging.net/2024/04/net-8-performance-dictionary-vs-frozendictionary/)

### Pitfall 4: Multi-TFM Generator Testing

**What goes wrong:** Generator tests pass on net8.0 but fail on netstandard2.0 due to API availability.

**Why it happens:** Generated code uses #if directives but tests only run against one TFM.

**How to avoid:**
- Test project should multi-target same TFMs as library
- Separate test methods for TFM-specific behavior
- Use compilation with correct LanguageVersion and references

**Warning signs:**
- CI fails on specific TFMs
- Consumers report compilation errors
- Runtime type initialization fails

## Code Examples

Verified patterns from official sources:

### Creating Incremental Generator with Multiple Output Files

```csharp
// Source: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md
[Generator]
public class DicomDictionaryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Parse Part 6 XML
        var part6Tags = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("part06.xml"))
            .Select(static (text, ct) =>
                Part6Parser.ParseTags(XDocument.Parse(text.GetText(ct)!.ToString())))
            .SelectMany(static (tags, _) => tags)
            .Collect();

        // Generate DicomTag.Generated.cs
        context.RegisterSourceOutput(part6Tags,
            static (context, tags) =>
                context.AddSource("DicomTag.Generated.cs",
                    SourceText.From(TagEmitter.Emit(tags), Encoding.UTF8)));

        // Parse Part 6 UIDs
        var part6Uids = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith("part06.xml"))
            .Select(static (text, ct) =>
                Part6Parser.ParseUids(XDocument.Parse(text.GetText(ct)!.ToString())))
            .SelectMany(static (uids, _) => uids)
            .Collect();

        // Generate DicomUID.Generated.cs
        context.RegisterSourceOutput(part6Uids,
            static (context, uids) =>
                context.AddSource("DicomUID.Generated.cs",
                    SourceText.From(UidEmitter.Emit(uids), Encoding.UTF8)));
    }
}
```

### Reporting Generator Diagnostics on Parse Errors

```csharp
// Source: https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md
private static readonly DiagnosticDescriptor InvalidXmlError = new(
    id: "DICOM001",
    title: "Invalid DICOM XML",
    messageFormat: "Failed to parse {0}: {1}",
    category: "DicomDictionary",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var xmlFiles = context.AdditionalTextsProvider
        .Where(static file => file.Path.EndsWith(".xml"));

    var parsed = xmlFiles.Select(static (text, ct) =>
    {
        try
        {
            var xml = XDocument.Parse(text.GetText(ct)!.ToString());
            return (Success: true, Xml: xml, Error: (string?)null, Path: text.Path);
        }
        catch (XmlException ex)
        {
            return (Success: false, Xml: (XDocument?)null, Error: ex.Message, Path: text.Path);
        }
    });

    // Report diagnostics for failures
    context.RegisterSourceOutput(parsed.Where(static p => !p.Success),
        static (context, result) =>
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidXmlError,
                Location.None,
                result.Path,
                result.Error));
        });

    // Generate code for successes
    var validXml = parsed.Where(static p => p.Success)
        .Select(static (p, _) => p.Xml!);
    // ... continue pipeline
}
```

### NUnit Data-Driven Tests with TestCase

```csharp
// Source: https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html
[TestFixture]
public class DicomVRTests
{
    [TestCase("AE", true)]  // Application Entity - valid
    [TestCase("DA", true)]  // Date - valid
    [TestCase("XX", false)] // Unknown - invalid
    [TestCase("", false)]   // Empty - invalid
    public void IsValidVR_ReturnsExpected(string vrCode, bool expected)
    {
        var vr = new DicomVR(vrCode);
        Assert.That(vr.IsKnown, Is.EqualTo(expected));
    }

    [TestCase("AE", (byte)0x20)] // Space padding
    [TestCase("UI", (byte)0x00)] // Null padding
    [TestCase("DS", (byte)0x20)] // Space padding
    public void PaddingByte_ReturnsCorrectValue(string vrCode, byte expectedPadding)
    {
        var vr = new DicomVR(vrCode);
        var info = DicomVRInfo.GetInfo(vr);
        Assert.That(info.PaddingByte, Is.EqualTo(expectedPadding));
    }
}
```

### Verify.SourceGenerators Snapshot Test

```csharp
// Source: https://andrewlock.net/creating-a-source-generator-part-2-testing-an-incremental-generator-with-snapshot-testing/
[TestFixture]
public class DicomDictionaryGeneratorTests : VerifyBase
{
    [ModuleInitializer]
    public static void Initialize() => VerifySourceGenerators.Enable();

    [Test]
    public Task GeneratesExpectedTags()
    {
        // Create minimal compilation
        var compilation = CSharpCompilation.Create("TestAssembly",
            references: new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        // Add XML as additional file
        var xmlContent = """
            <?xml version="1.0"?>
            <book>
              <chapter num="6">
                <table>
                  <tbody>
                    <tr><td>(0010,0020)</td><td>PatientID</td><td>LO</td><td>1</td></tr>
                  </tbody>
                </table>
              </chapter>
            </book>
            """;

        var additionalText = new InMemoryAdditionalText("part06.xml", xmlContent);
        var driver = CSharpGeneratorDriver.Create(new DicomDictionaryGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additionalText));

        // Run and verify
        driver = driver.RunGenerators(compilation);
        return Verify(driver);
    }
}

// Helper class for in-memory additional files
internal class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text, Encoding.UTF8);
    }

    public override string Path { get; }
    public override SourceText GetText(CancellationToken ct = default) => _text;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| ISourceGenerator | IIncrementalGenerator | .NET 6 (2021) | Massive IDE performance improvement, must migrate |
| CreateSyntaxProvider + Compile | ForAttributeWithMetadataName | .NET 7 (2022) | 99% reduction in nodes evaluated for attribute-driven generation |
| Manual equality on structs | readonly record struct | C# 10 (2021) | Compiler-generated value equality, less boilerplate |
| Dictionary | FrozenDictionary | .NET 8 (2023) | 50% faster lookups for static data, zero allocations |
| Custom snapshot logic | Verify.SourceGenerators | 2022+ | Standard tooling for generator tests, handles multiple files |

**Deprecated/outdated:**
- **ISourceGenerator:** Replaced by IIncrementalGenerator, don't use
- **ref struct for data model:** Cannot be stored in fields, use readonly record struct instead
- **Manual IEquatable on record types:** Compiler generates it, adding manual implementation is redundant

## Open Questions

Things that couldn't be fully resolved:

1. **DICOM Part 6 XML Schema Documentation**
   - What we know: NEMA publishes part06.xml with DocBook format, contains tags/VRs/UIDs
   - What's unclear: Exact XPath queries for extracting data, schema version changes
   - Recommendation: Parse actual XML file from NEMA, inspect structure, write parser incrementally with tests

2. **Multi-VR Tag Context Resolution**
   - What we know: Some tags allow multiple VRs (e.g., Pixel Data OB/OW), context determines which
   - What's unclear: Whether to resolve at parse time or provide both in dictionary
   - Recommendation: Dictionary provides all VRs, mark which is default, let reader resolve based on context (matches CLAUDE.md design)

3. **Masked Tag (50xx,0010) Representation**
   - What we know: Dictionary has pattern tags like (50xx,0010) meaning (5000,0010), (5002,0010), etc.
   - What's unclear: Whether to generate all instances or provide pattern matching
   - Recommendation: Generate DicomMaskedTag struct, provide matching logic, avoid generating 256 variants per pattern

## Sources

### Primary (HIGH confidence)

- [dotnet/roslyn - Incremental Generators Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md) - Official Microsoft documentation
- [dotnet/roslyn - Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) - API reference
- [Microsoft Learn - Structure types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/struct) - Official C# documentation
- [Microsoft Learn - Value equality](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/statements-expressions-operators/how-to-define-value-equality-for-a-type) - Official guidance
- [NUnit Docs - TestCaseSource](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html) - Official NUnit documentation

### Secondary (MEDIUM confidence)

- [Andrew Lock - Creating a source generator Part 1](https://andrewlock.net/creating-a-source-generator-part-1-creating-an-incremental-source-generator/) - Comprehensive tutorial series (2023-2024)
- [Andrew Lock - Testing with snapshot testing Part 2](https://andrewlock.net/creating-a-source-generator-part-2-testing-an-incremental-generator-with-snapshot-testing/) - Verify.SourceGenerators tutorial
- [Andrew Lock - Avoiding performance pitfalls Part 9](https://andrewlock.net/creating-a-source-generator-part-9-avoiding-performance-pitfalls-in-incremental-generators/) - Performance best practices
- [Elmah.io - Mastering Incremental Source Generators](https://blog.elmah.io/mastering-incremental-source-generators-in-csharp-a-complete-guide-with-example/) - Complete guide with examples
- [Start Debugging - Dictionary vs. FrozenDictionary](https://startdebugging.net/2024/04/net-8-performance-dictionary-vs-frozendictionary/) - Performance benchmarks
- [Code Corner - FrozenDictionary performance](https://code-corner.dev/2023/11/08/NET-8-%E2%80%94-FrozenDictionary-performance/) - Detailed benchmarks
- [James Montemagno - Optimizing C# Struct Equality](https://montemagno.com/optimizing-c-struct-equality-with-iequatable/) - IEquatable<T> performance analysis
- [Meziantou - Struct equality performance](https://www.meziantou.net/struct-equality-performance-in-dotnet.htm) - Performance measurements
- [Don't Code Tired - Improving Struct Equality Performance](http://dontcodetired.com/blog/post/Improving-Struct-Equality-Performance-in-C) - Practical improvements

### Tertiary (LOW confidence - WebSearch only)

- Medium articles on record struct performance - Multiple sources agree on benefits but lack official benchmarks
- Various blog posts on source generator patterns - Consistent with official docs, marked for validation during implementation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All recommendations from official Microsoft documentation and established NuGet packages
- Architecture: HIGH - Patterns from official Roslyn cookbook and proven C# language features
- Pitfalls: MEDIUM - Based on community experience and blog posts, verified against official docs where possible

**Research date:** 2026-01-26
**Valid until:** 2026-07-26 (6 months - stable domain, unlikely major changes)

**Key findings verified via:**
- Context7: Microsoft.CodeAnalysis/Roslyn documentation
- Official Microsoft Learn documentation for C# features
- GitHub official Roslyn repository cookbooks
- Multiple independent sources confirming performance characteristics

**Research constraints:**
- CONTEXT.md specifies implementation decisions already made (readonly record struct, interface-based elements, source generator approach)
- Research focused on best practices within those constraints rather than exploring alternatives
- Multi-targeting to netstandard2.0 requires polyfills (DateOnly/TimeOnly) - implementation details deferred to planning phase
