# Phase 26: Migration Tooling - Research

**Researched:** 2026-02-06
**Domain:** fo-dicom compatibility layer, Roslyn analyzers, API migration tooling
**Confidence:** HIGH (code analysis of actual repos) / MEDIUM (Roslyn patterns from docs)

## Summary

This phase creates two deliverables: (1) compatibility shim packages (`SharpDicom.FoDicom4.Compat` and `SharpDicom.FoDicom5.Compat`) that replicate fo-dicom's public API surface while delegating to SharpDicom internally, and (2) a Roslyn analyzer package (`SharpDicom.Analyzers`) that detects fo-dicom usage patterns and provides automated code fixes.

Research focused on three key areas: the actual fo-dicom API surface used by the two validation targets (dcm2csv and nccid), the SharpDicom API that the compat layer will delegate to, and the Roslyn analyzer/code fix provider development patterns. The validation targets use a focused subset of fo-dicom's API (file I/O, dataset access, network client, C-FIND), which significantly bounds the required compat surface for phase completion.

**Primary recommendation:** Build the FoDicom5.Compat package first (dcm2csv and nccid both use fo-dicom 5.x / `FellowOakDicom` namespace), validate with dcm2csv (simpler, no network), then nccid (adds network client). Build the analyzer second since it depends on understanding the full compat API surface.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.CodeAnalysis.CSharp | 5.0.0 | Roslyn APIs for analyzer/code fix | Already in use by SharpDicom.Generators |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 | Analyzer development helpers | Already in use by SharpDicom.Generators |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.0.0 | Workspace APIs for CodeFixProvider | Required for document manipulation in code fixes |

### Supporting (Testing)
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.CodeAnalysis.CSharp.Analyzer.Testing | 1.1.2 | Analyzer test infrastructure | Testing DiagnosticAnalyzer |
| Microsoft.CodeAnalysis.CSharp.CodeFix.Testing | 1.1.2 | Code fix test infrastructure | Testing CodeFixProvider |
| Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.NUnit | 1.1.2 | NUnit adapter for code fix tests | Matches existing NUnit usage |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Composition wrappers | Inheritance from SharpDicom types | Inheritance creates coupling; composition decided in CONTEXT.md |
| Semantic analysis only | Syntax analysis for simple patterns | Syntax is faster but misses type info; need semantic for reliable detection |

**Installation (new packages for analyzer testing):**
```bash
# Add to Directory.Packages.props
dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces --version 5.0.0
dotnet add package Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.NUnit --version 1.1.2
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── SharpDicom.FoDicom5.Compat/     # FellowOakDicom namespace compat
│   ├── DicomFile.cs                  # Open/Save shim
│   ├── DicomDataset.cs               # GetValue/GetSingleValue/etc shim
│   ├── DicomItem.cs                  # Base element type
│   ├── DicomElement.cs               # Value element types
│   ├── DicomStringElement.cs         # String element with Get<T>
│   ├── DicomAttributeTag.cs          # AT element with .Values
│   ├── DicomSequence.cs              # Sequence wrapper
│   ├── DicomTag.cs                   # Tag wrapper with .DictionaryEntry
│   ├── DicomUID.cs                   # UID wrapper
│   ├── DicomVR.cs                    # VR wrapper
│   ├── Exceptions/                   # DicomDataException etc
│   ├── Network/                      # DicomCFindRequest, DicomClient etc
│   │   ├── DicomCFindRequest.cs
│   │   ├── DicomQueryRetrieveLevel.cs
│   │   └── Client/
│   │       ├── DicomClient.cs
│   │       └── DicomClientFactory.cs
│   └── Compatibility.cs             # .Unwrap() extension methods
├── SharpDicom.FoDicom4.Compat/      # Dicom namespace compat (lower priority)
│   └── (mirrors FoDicom5 with Dicom.* namespaces)
├── SharpDicom.Analyzers/            # Roslyn analyzer + code fixes
│   ├── Analyzers/
│   │   ├── FoDicomUsageAnalyzer.cs   # Detects FellowOakDicom.* usage
│   │   └── CompatUsageAnalyzer.cs    # Detects compat layer usage (step 2)
│   ├── CodeFixes/
│   │   ├── FoDicomToCompatFix.cs     # Quick migration: fo-dicom -> compat
│   │   └── CompatToNativeFix.cs      # Full migration: compat -> native
│   ├── DiagnosticIds.cs              # SD0001, SD0002 etc
│   └── AnalyzerReleases.Shipped.md
└── SharpDicom.Analyzers.Package/     # NuGet packaging project
    └── SharpDicom.Analyzers.csproj
tests/
├── SharpDicom.FoDicom5.Compat.Tests/ # Unit tests for compat layer
├── SharpDicom.Analyzers.Tests/       # Analyzer/code fix tests
└── SharpDicom.Migration.Integration/ # Integration: dcm2csv + nccid builds
```

### Pattern 1: Compat Wrapper with Unwrap

**What:** Each fo-dicom type gets a compat class wrapping the SharpDicom equivalent.
**When to use:** For all types that need API-level compatibility.

```csharp
// Source: CONTEXT.md decision - composition with Unwrap
namespace SharpDicom.FoDicom5.Compat.FellowOakDicom
{
    public class DicomDataset : IEnumerable<DicomItem>
    {
        private readonly SharpDicom.Data.DicomDataset _inner;

        internal DicomDataset(SharpDicom.Data.DicomDataset inner)
        {
            _inner = inner;
        }

        public DicomDataset()
        {
            _inner = new SharpDicom.Data.DicomDataset();
        }

        /// <summary>Unwrap to access native SharpDicom type.</summary>
        public SharpDicom.Data.DicomDataset Unwrap() => _inner;

        // fo-dicom 5.x API surface
        public T GetSingleValue<T>(DicomTag tag)
        {
            // Typed dispatch for common types
            if (typeof(T) == typeof(string))
                return (T)(object)(_inner.GetString(
                    new SharpDicom.Data.DicomTag(tag.Group, tag.Element)) ?? "");
            if (typeof(T) == typeof(int))
                return (T)(object)(_inner.GetInt32(
                    new SharpDicom.Data.DicomTag(tag.Group, tag.Element)) ?? 0);
            // ... reflection fallback for exotic types
            throw new DicomDataException($"Cannot convert to {typeof(T).Name}");
        }

        public DicomDataset AddOrUpdate(DicomTag tag, params string[] values)
        {
            // Create string element from values
            var sdTag = new SharpDicom.Data.DicomTag(tag.Group, tag.Element);
            var joined = string.Join("\\", values);
            var bytes = System.Text.Encoding.ASCII.GetBytes(joined);
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                bytes.CopyTo(padded, 0);
                padded[^1] = (byte)' ';
                bytes = padded;
            }
            var entry = SharpDicom.Data.DicomDictionary.Default.GetEntry(sdTag);
            var vr = entry?.DefaultVR ?? SharpDicom.Data.DicomVR.LO;
            _inner.AddOrUpdate(new SharpDicom.Data.DicomStringElement(sdTag, vr, bytes));
            return this;
        }
    }
}
```

### Pattern 2: Roslyn Analyzer with EditorConfig
**What:** DiagnosticAnalyzer that detects fo-dicom `using` directives and API usage.
**When to use:** For detecting migration targets in user code.

```csharp
// Source: Microsoft Learn Roslyn tutorial pattern
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class FoDicomUsageAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SD0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "fo-dicom API usage detected",
        messageFormat: "'{0}' is a fo-dicom type. Use SharpDicom equivalent.",
        category: "Migration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Detect using directives for fo-dicom namespaces
        context.RegisterSyntaxNodeAction(
            AnalyzeUsingDirective,
            SyntaxKind.UsingDirective);

        // Detect type usage (semantic analysis)
        context.RegisterSymbolAction(
            AnalyzeSymbol,
            SymbolKind.NamedType);
    }
}
```

### Pattern 3: Code Fix Provider
**What:** Provides lightbulb fix actions to rewrite fo-dicom code.
**When to use:** Paired with each diagnostic analyzer.

```csharp
// Source: Microsoft Learn CodeFixProvider pattern
[ExportCodeFixProvider(LanguageNames.CSharp)]
[Shared]
public class FoDicomToCompatFix : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FoDicomUsageAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics.First();
        var span = diagnostic.Location.SourceSpan;

        // Find the using directive or type reference
        var node = root?.FindNode(span);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with SharpDicom.FoDicom5.Compat",
                createChangedDocument: c =>
                    ReplaceNamespaceAsync(context.Document, node!, c),
                equivalenceKey: "FoDicomToCompat"),
            diagnostic);
    }
}
```

### Anti-Patterns to Avoid
- **Inheriting from SharpDicom types:** Creates brittle coupling. Use composition (CONTEXT.md decision).
- **Depending on fo-dicom NuGet package:** The compat layer must be standalone (CONTEXT.md decision).
- **Using syntax-only analysis for type detection:** Must use semantic analysis to reliably identify fo-dicom types vs. similarly-named user types.
- **Testing analyzers by running real compilations:** Use `Microsoft.CodeAnalysis.Testing` infrastructure with in-memory compilation instead.

## fo-dicom API Surface Analysis

### dcm2csv (FoDicom 5.x) - API Usage Inventory

dcm2csv is the simpler validation target. Its complete fo-dicom usage:

| API | Namespace | SharpDicom Equivalent |
|-----|-----------|----------------------|
| `DicomFile.Open(string)` | `FellowOakDicom` | `SharpDicom.DicomFile.Open(string)` |
| `DicomFile.Dataset` | `FellowOakDicom` | `SharpDicom.DicomFile.Dataset` |
| `DicomDataset` enumeration (IEnumerable) | `FellowOakDicom` | `DicomDataset : IEnumerable<IDicomElement>` |
| `DicomItem` base type | `FellowOakDicom` | `IDicomElement` interface |
| `DicomItem.Tag` | `FellowOakDicom` | `IDicomElement.Tag` |
| `DicomTag.DictionaryEntry.Name` | `FellowOakDicom` | `DicomDictionary.Default.GetEntry(tag)?.Name` |
| `DicomAttributeTag.Values` | `FellowOakDicom` | No direct equivalent (need to add) |
| `DicomStringElement` type | `FellowOakDicom` | `SharpDicom.Data.DicomStringElement` |
| `DicomStringElement.Count` | `FellowOakDicom` | Need to add (count of VM values) |
| `DicomStringElement.Get<string>(index)` | `FellowOakDicom` | `GetStrings()?[index]` |
| `DicomSequence.Items` | `FellowOakDicom` | `SharpDicom.Data.DicomSequence.Items` |
| `DicomItem.ToString()` | `FellowOakDicom` | `IDicomElement` (need ToString) |

### nccid (FoDicom 5.x) - API Usage Inventory

nccid adds networking on top of file I/O:

| API | Namespace | SharpDicom Equivalent |
|-----|-----------|----------------------|
| `DicomClientFactory.Create(host, port, useTls, callingAE, calledAE)` | `FellowOakDicom.Network.Client` | `new DicomClient(new DicomClientOptions{...})` |
| `IDicomClient.NegotiateAsyncOps()` | `FellowOakDicom.Network.Client` | No equivalent (may not be needed) |
| `IDicomClient.AddRequestAsync(DicomRequest)` | `FellowOakDicom.Network.Client` | Different pattern - need adapter |
| `IDicomClient.SendAsync()` | `FellowOakDicom.Network.Client` | Different pattern - need adapter |
| `DicomCFindRequest(DicomQueryRetrieveLevel)` | `FellowOakDicom.Network` | `CFindScu` + `DicomQuery` |
| `DicomCFindRequest.Dataset` | `FellowOakDicom.Network` | Query builder pattern |
| `DicomCFindRequest.OnResponseReceived` | `FellowOakDicom.Network` | Callback/event model |
| `DicomDataset.AddOrUpdate(DicomTag, string)` | `FellowOakDicom` | Element creation + `dataset.AddOrUpdate()` |
| `DicomDataset.GetSingleValue<string>(DicomTag)` | `FellowOakDicom` | `dataset.GetString(tag)` |
| `DicomTag.StudyDate` (static) | `FellowOakDicom` | `SharpDicom.Data.DicomTag.StudyDate` |
| `DicomTag.PatientID` (static) | `FellowOakDicom` | `SharpDicom.Data.DicomTag.PatientID` |
| `DicomTag.StudyInstanceUID` (static) | `FellowOakDicom` | `SharpDicom.Data.DicomTag.StudyInstanceUID` |
| `new DicomTag(0x8, 0x5)` constructor | `FellowOakDicom` | `new SharpDicom.Data.DicomTag(0x8, 0x5)` |

### fo-dicom 4.x vs 5.x Key Differences

| Aspect | fo-dicom 4.x (`Dicom.*`) | fo-dicom 5.x (`FellowOakDicom.*`) |
|--------|--------------------------|-----------------------------------|
| Root namespace | `Dicom` | `FellowOakDicom` |
| Client creation | `new DicomClient(host, port, ...)` | `DicomClientFactory.Create(host, port, ...)` |
| Dataset.Get | `dataset.Get<T>(tag)` | `dataset.GetValue<T>` / `GetSingleValue<T>` |
| Encoding param | `DicomStringElement(tag, encoding, ...)` | No encoding param (use SpecificCharacterSet) |
| Server providers | Sync methods | Async-only |
| DI pattern | Static managers | DI with `IDicomClientFactory` |
| Minimum C# | 7.x | 8.0 |

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Analyzer test infrastructure | Custom compilation harness | `Microsoft.CodeAnalysis.Testing` | Handles compilation, diagnostic verification, code fix application |
| NuGet analyzer packaging | Manual .nuspec | MSBuild `PackagePath="analyzers/dotnet/cs"` | Standard convention, auto-discovery by compiler |
| Fix All support | Custom batch application | `WellKnownFixAllProviders.BatchFixer` | Handles document/project/solution-wide fixes |
| Generic type dispatch in `Get<T>` | Giant switch on `typeof(T)` | Pattern matching + reflection fallback | fo-dicom uses this pattern; typed dispatch for performance, reflection for coverage |
| EditorConfig integration for severity | Custom config parsing | Roslyn's built-in `.editorconfig` support | `dotnet_diagnostic.SD0001.severity = error` works automatically |

**Key insight:** The Roslyn analyzer framework provides built-in support for `.editorconfig` severity overrides, Fix All providers, and NuGet distribution. The entire infrastructure for analyzer configuration and deployment is already solved.

## Common Pitfalls

### Pitfall 1: DicomTag as struct vs class
**What goes wrong:** fo-dicom's `DicomTag` is a class with `.DictionaryEntry` property. SharpDicom's `DicomTag` is a 4-byte readonly struct without dictionary access.
**Why it happens:** Different design philosophy (value type for perf vs reference type for rich API).
**How to avoid:** The compat `DicomTag` must be a class that wraps SharpDicom's struct, adding `.DictionaryEntry` property via dictionary lookup. This is a critical mapping point.
**Warning signs:** Null reference exceptions on `.DictionaryEntry` if tag is unknown.

### Pitfall 2: DicomDataset enumeration type mismatch
**What goes wrong:** fo-dicom enumerates `DicomItem` (base class). SharpDicom enumerates `IDicomElement` (interface). dcm2csv pattern-matches on `DicomItem` subtypes.
**Why it happens:** Different element type hierarchies.
**How to avoid:** Compat `DicomDataset` must enumerate compat `DicomItem` wrappers, with pattern matching supporting compat subtypes (`DicomStringElement`, `DicomSequence`, `DicomAttributeTag`).
**Warning signs:** `switch` expressions on `DicomItem` not matching expected types.

### Pitfall 3: fo-dicom's AddOrUpdate overloads
**What goes wrong:** fo-dicom has dozens of `AddOrUpdate` overloads accepting tags with typed values directly (e.g., `AddOrUpdate(DicomTag.PatientID, "value")`). Missing overloads break compilation.
**Why it happens:** fo-dicom uses convenience overloads extensively; SharpDicom requires explicit element creation.
**How to avoid:** Implement the commonly-used overloads: `AddOrUpdate(DicomTag, string)`, `AddOrUpdate(DicomTag, params string[])`, `AddOrUpdate(new DicomTag(g, e), string)`. Check dcm2csv and nccid for the exact signatures used.
**Warning signs:** CS1501 (wrong argument count) or CS1503 (wrong argument type) compilation errors.

### Pitfall 4: Network client API mismatch
**What goes wrong:** fo-dicom uses a request-queue pattern (`AddRequestAsync` then `SendAsync`). SharpDicom uses a direct async pattern (`ConnectAsync`, then service methods).
**Why it happens:** Fundamentally different client architectures.
**How to avoid:** The compat `DicomClient` must internally buffer requests and execute them on `SendAsync`, translating between the two patterns. This is the most complex adapter.
**Warning signs:** Requests executing out of order or association not being established.

### Pitfall 5: DicomCFindRequest callback pattern
**What goes wrong:** fo-dicom uses `OnResponseReceived += handler` event pattern. SharpDicom's `CFindScu` returns results differently.
**Why it happens:** Event-based vs. structured result patterns.
**How to avoid:** The compat `DicomCFindRequest` must store the event handler and invoke it during the adapter's internal send logic, converting SharpDicom C-FIND results to compat response objects.
**Warning signs:** Response handlers never firing, or firing after `SendAsync` returns.

### Pitfall 6: Roslyn analyzer must target netstandard2.0
**What goes wrong:** Analyzer fails to load in Visual Studio or `dotnet build`.
**Why it happens:** The Roslyn compiler host requires analyzers to target netstandard2.0.
**How to avoid:** Analyzer project must target `netstandard2.0` only (same as existing `SharpDicom.Generators`). Cannot use modern .NET APIs in analyzer code.
**Warning signs:** MSBuild warnings about analyzer load failure.

### Pitfall 7: Analyzer testing requires specific Roslyn version alignment
**What goes wrong:** Test compilation errors about mismatched `Microsoft.CodeAnalysis` versions.
**Why it happens:** Analyzer references one version, test infrastructure references another.
**How to avoid:** Pin all `Microsoft.CodeAnalysis.*` packages to the same version (5.0.0) in Directory.Packages.props.
**Warning signs:** `MissingMethodException` or `TypeLoadException` at test runtime.

## Code Examples

### fo-dicom DicomFile.Open (what compat must replicate)
```csharp
// fo-dicom 5.x usage (from dcm2csv)
using FellowOakDicom;

DicomFile.Open(dcm).Dataset.SelectMany(t => Entry.ProcessTag(dcm, t))
```

### Compat DicomFile wrapper
```csharp
namespace SharpDicom.FoDicom5.Compat.FellowOakDicom
{
    public class DicomFile
    {
        private readonly SharpDicom.DicomFile _inner;

        private DicomFile(SharpDicom.DicomFile inner) => _inner = inner;

        public DicomDataset Dataset => new DicomDataset(_inner.Dataset);

        public static DicomFile Open(string path)
        {
            var sdFile = SharpDicom.DicomFile.Open(path);
            return new DicomFile(sdFile);
        }

        public SharpDicom.DicomFile Unwrap() => _inner;
    }
}
```

### Compat DicomItem hierarchy (required for dcm2csv pattern matching)
```csharp
namespace SharpDicom.FoDicom5.Compat.FellowOakDicom
{
    // Base type - fo-dicom calls this DicomItem
    public abstract class DicomItem
    {
        public DicomTag Tag { get; }
        internal SharpDicom.Data.IDicomElement Inner { get; }

        protected DicomItem(SharpDicom.Data.IDicomElement inner)
        {
            Inner = inner;
            Tag = new DicomTag(inner.Tag.Group, inner.Tag.Element);
        }

        // Factory to wrap SharpDicom elements as compat types
        internal static DicomItem Wrap(SharpDicom.Data.IDicomElement element)
        {
            return element switch
            {
                SharpDicom.Data.DicomSequence seq => new DicomSequence(seq),
                SharpDicom.Data.DicomNumericElement ne when ne.VR == SharpDicom.Data.DicomVR.AT
                    => new DicomAttributeTag(ne),
                SharpDicom.Data.DicomStringElement se => new DicomStringElement(se),
                _ => new DicomOtherElement(element)
            };
        }
    }
}
```

### Analyzer NuGet packaging (.csproj pattern)
```xml
<!-- SharpDicom.Analyzers.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <IncludeBuildOutput>false</IncludeBuildOutput>

    <PackageId>SharpDicom.Analyzers</PackageId>
    <Description>Roslyn analyzer for migrating from fo-dicom to SharpDicom</Description>
    <DevelopmentDependency>true</DevelopmentDependency>
    <NoPackageAnalysis>true</NoPackageAnalysis>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces"
                      PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers"
                      PrivateAssets="all" />
  </ItemGroup>

  <!-- Pack analyzer DLL into correct NuGet location -->
  <ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll"
          Pack="true"
          PackagePath="analyzers/dotnet/cs"
          Visible="false" />
  </ItemGroup>
</Project>
```

### Analyzer test pattern
```csharp
// Source: Microsoft.CodeAnalysis.Testing patterns
using Microsoft.CodeAnalysis.Testing;

[Test]
public async Task DetectsFoDicomUsing()
{
    var test = new CSharpAnalyzerTest<FoDicomUsageAnalyzer, NUnitVerifier>
    {
        TestCode = @"
using {|SD0001:FellowOakDicom|};

class Test
{
    void Method()
    {
        var file = {|SD0002:DicomFile.Open(""test.dcm"")|};
    }
}
"
    };
    await test.RunAsync();
}

[Test]
public async Task FixesUsingDirective()
{
    var test = new CSharpCodeFixTest<FoDicomUsageAnalyzer, FoDicomToCompatFix, NUnitVerifier>
    {
        TestCode = @"
using {|SD0001:FellowOakDicom|};
",
        FixedCode = @"
using SharpDicom.FoDicom5.Compat.FellowOakDicom;
"
    };
    await test.RunAsync();
}
```

## Detailed API Mapping: fo-dicom 5.x to SharpDicom

### DicomTag Mapping
| fo-dicom | SharpDicom | Notes |
|----------|-----------|-------|
| `DicomTag` (class) | `DicomTag` (struct) | Compat must be class for null/DictionaryEntry |
| `DicomTag.DictionaryEntry` | `DicomDictionary.Default.GetEntry(tag)` | Lazy lookup |
| `DicomTag.DictionaryEntry.Name` | `entry.Name` | |
| `new DicomTag(0x08, 0x05)` | `new DicomTag(0x08, 0x05)` | Compatible constructors |
| `DicomTag.StudyDate` (static) | `DicomTag.StudyDate` (static) | Generated, same names |
| `DicomTag.PatientID` (static) | `DicomTag.PatientID` (static) | Generated, same names |

### DicomDataset Mapping
| fo-dicom | SharpDicom | Notes |
|----------|-----------|-------|
| `GetSingleValue<string>(tag)` | `GetString(tag)` | Direct |
| `GetSingleValue<int>(tag)` | `GetInt32(tag)` | Nullable difference |
| `GetValue<T>(tag, index)` | `GetStrings(tag)?[index]` | Need index support |
| `GetValues<T>(tag)` | `GetStrings(tag)` | Type conversion needed |
| `TryGetSingleValue<T>(tag, out val)` | Null check pattern | Wrap with try |
| `GetSequence(tag)` | `GetSequence(tag)` | Compatible |
| `TryGetSequence(tag, out seq)` | Null check | Wrap |
| `AddOrUpdate(tag, values)` | Element creation + AddOrUpdate | Complex overloads |
| `GetString(tag)` | `GetString(tag)` | Direct |
| IEnumerable<DicomItem> | IEnumerable<IDicomElement> | Wrapping needed |

### DicomFile Mapping
| fo-dicom | SharpDicom | Notes |
|----------|-----------|-------|
| `DicomFile.Open(path)` | `DicomFile.Open(path)` | Direct |
| `DicomFile.OpenAsync(path)` | `DicomFile.OpenAsync(path)` | Return type differs |
| `DicomFile.Dataset` | `DicomFile.Dataset` | Direct |
| `DicomFile.FileMetaInfo` | `DicomFile.FileMetaInfo` | Type differs |
| `DicomFile.Save(path)` | `DicomFile.Save(path)` | Direct |

### Network Client Mapping
| fo-dicom | SharpDicom | Notes |
|----------|-----------|-------|
| `DicomClientFactory.Create(h,p,tls,ae,ae)` | `new DicomClient(options)` | Factory pattern |
| `client.NegotiateAsyncOps()` | N/A | May be no-op in compat |
| `client.AddRequestAsync(req)` | N/A - different pattern | Buffer internally |
| `client.SendAsync()` | `ConnectAsync` + service calls | Complex adapter |
| `DicomCFindRequest(level)` | `CFindScu` + `DicomQuery` | Major restructure |
| `req.Dataset.AddOrUpdate(tag, val)` | Query builder | |
| `req.OnResponseReceived += handler` | Callback/result model | Event adapter |

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| fo-dicom 4.x `Dicom.*` namespace | fo-dicom 5.x `FellowOakDicom.*` | 2021 (fo-dicom 5.0) | Complete namespace rename |
| `DicomDataset.Get<T>()` | `GetValue/GetSingleValue/GetValues<T>()` | fo-dicom 5.0 | Multiple methods replace one generic |
| Static `DicomClient` constructor | `DicomClientFactory.Create()` | fo-dicom 5.0 | DI-friendly factory pattern |
| Sync server providers | Async-only server providers | fo-dicom 5.0 | C# 8.0 minimum requirement |

**Deprecated/outdated:**
- fo-dicom `DicomValidation.AutoValidation`: Replaced by `DicomSetupBuilder.SkipValidation()`
- fo-dicom `DicomDataset.Get<T>`: Replaced by specialized `GetValue`, `GetSingleValue`, `GetValues`
- fo-dicom `IOManager`: Replaced by DI + `IFileReference`

## Compat Layer Scope Assessment

### Minimum Viable Compat (dcm2csv gate)
Types required: `DicomFile`, `DicomDataset`, `DicomItem`, `DicomStringElement`, `DicomSequence`, `DicomAttributeTag`, `DicomTag` (with `.DictionaryEntry`), `DicomTag.DictionaryEntry.Name`.

Methods required: `DicomFile.Open(string)`, dataset enumeration (`IEnumerable<DicomItem>`), `DicomStringElement.Count`, `DicomStringElement.Get<string>(int)`, `DicomSequence.Items`, `DicomAttributeTag.Values`, `DicomItem.Tag`, `DicomItem.ToString()`.

### Extended Compat (nccid gate)
Additional types: `DicomClientFactory`, `IDicomClient`, `DicomCFindRequest`, `DicomQueryRetrieveLevel`.

Additional methods: `DicomClientFactory.Create()`, `client.AddRequestAsync()`, `client.SendAsync()`, `client.NegotiateAsyncOps()`, `DicomCFindRequest(level)`, `req.Dataset.AddOrUpdate(tag, string)`, `dataset.GetSingleValue<string>(tag)`, `req.OnResponseReceived`.

### Broad API Surface (future, CONTEXT.md "broad API mirror")
Beyond phase gates: All `GetValue`/`GetSingleValue`/`GetValues` overloads, `TryGet*` methods, `DicomFile.Save`/`SaveAsync`, `DicomFile.HasValidHeader`, `DicomFileMetaInformation`, pixel data access, all DIMSE request types (C-STORE, C-MOVE, C-GET), server-side types.

## Diagnostic IDs Plan

| ID | Category | Description | Severity |
|----|----------|-------------|----------|
| SD0001 | Migration | `using FellowOakDicom` detected | Warning |
| SD0002 | Migration | fo-dicom type instantiation | Warning |
| SD0003 | Migration | fo-dicom static method call | Warning |
| SD0010 | Migration | Compat layer `using` detected (step 2) | Info |
| SD0011 | Migration | Compat type usage detected (step 2) | Info |

## Open Questions

1. **DicomAttributeTag in SharpDicom**
   - What we know: dcm2csv uses `DicomAttributeTag.Values` to get an array of `DicomTag` values from AT elements.
   - What's unclear: SharpDicom's `DicomNumericElement` handles AT VR but doesn't expose a `DicomTag[]` accessor.
   - Recommendation: The compat layer can implement this by reading raw bytes from `DicomNumericElement` and parsing as tag pairs. May also want to add this to SharpDicom natively.

2. **DicomStringElement.Count property**
   - What we know: dcm2csv uses `e.Count` to iterate VM values. SharpDicom's `DicomStringElement` has `GetStrings()` but no `Count`.
   - What's unclear: Whether `Count` in fo-dicom counts backslash-separated values or something else.
   - Recommendation: Compat layer implements `Count` as `GetStrings()?.Length ?? 0`.

3. **fo-dicom 4.x validation target**
   - What we know: CONTEXT.md requires both 4.x and 5.x compat packages. Neither dcm2csv nor nccid use 4.x.
   - What's unclear: Which real-world projects use fo-dicom 4.x that would validate the FoDicom4.Compat package.
   - Recommendation: Build FoDicom4.Compat as a namespace-adjusted copy of FoDicom5.Compat (replace `FellowOakDicom` with `Dicom`, adjust API differences like `Get<T>` vs `GetValue<T>`). Defer 4.x validation to SmiServices/RdmpDicom migration.

4. **NegotiateAsyncOps() semantics**
   - What we know: nccid calls `pacs.NegotiateAsyncOps()` on the fo-dicom client.
   - What's unclear: Whether SharpDicom's association negotiation already handles async operations negotiation.
   - Recommendation: Implement as no-op in compat layer initially; SharpDicom's association may handle this at the PDU level already.

## Sources

### Primary (HIGH confidence)
- dcm2csv source code at `/Users/jas88/Developer/Github/dcm2csv/` - Direct analysis of fo-dicom 5.x usage
- nccid source code at `/Users/jas88/Developer/Github/nccid/` - Direct analysis of fo-dicom 5.x + network usage
- SharpDicom source code at `/Users/jas88/Developer/Github/SharpDicom/src/` - Complete API surface analysis
- [fo-dicom DicomFile API](https://fo-dicom.github.io/stable/v5/api/FellowOakDicom.DicomFile.html) - Official API reference
- [fo-dicom Getting Data](https://fo-dicom.github.io/stable/v5/usage/getting_data.html) - DicomDataset method signatures
- [fo-dicom Upgrade 4 to 5](https://fo-dicom.github.io/stable/v5/usage/upgrade4to5.html) - Breaking changes between versions

### Secondary (MEDIUM confidence)
- [Microsoft Learn: Write Analyzer and Code Fix](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix) - Roslyn analyzer patterns
- [Roslyn Analyzer NuGet Distribution](https://aaronstannard.com/roslyn-nuget/) - NuGet packaging patterns
- [Customize Roslyn Analyzer Rules](https://learn.microsoft.com/en-us/visualstudio/code-quality/use-roslyn-analyzers) - EditorConfig severity configuration
- [Microsoft.CodeAnalysis.Testing README](https://github.com/dotnet/roslyn-sdk/blob/main/src/Microsoft.CodeAnalysis.Testing/README.md) - Test infrastructure

### Tertiary (LOW confidence)
- WebSearch results on fo-dicom 4.x API differences (verified against upgrade guide)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Using same Roslyn packages as existing SharpDicom.Generators; well-documented
- Architecture: HIGH - Based on direct code analysis of dcm2csv and nccid; standard Roslyn patterns
- Pitfalls: HIGH - Identified from actual API incompatibilities between fo-dicom and SharpDicom
- fo-dicom API surface: HIGH - Analyzed actual code rather than speculating about API
- Roslyn analyzer patterns: MEDIUM - Based on official Microsoft docs, not project-specific experience

**Research date:** 2026-02-06
**Valid until:** 2026-03-06 (stable domain, fo-dicom API unlikely to change)
