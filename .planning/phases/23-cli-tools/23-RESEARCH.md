# Phase 23: CLI Tools (sharpdcm) - Research

**Researched:** 2026-02-05
**Domain:** .NET CLI application with DICOM toolkit subcommands
**Confidence:** HIGH

## Summary

Phase 23 builds a `sharpdcm` command-line tool as a standalone .NET console application with subcommands (`dump`, `store`, `find`, `lint`, `fix`). The CLI wraps the existing SharpDicom library which already provides comprehensive DICOM file I/O, networking (C-STORE, C-FIND, C-ECHO), validation profiles, and data dictionary lookups.

The standard approach for .NET CLI tools in 2026 is **System.CommandLine** for argument parsing (now stable at v2.0.2) combined with **Spectre.Console** for rich terminal output (progress bars, colored tables, markup). The CLI project should target net9.0/net10.0 as a single executable, with Native AOT compatibility as a stretch goal.

The existing SharpDicom APIs are well-suited for CLI consumption: `DicomFile.Open/OpenAsync` for reading, `DicomFileWriter` for writing, `ValidationProfile` with Strict/Lenient/Permissive presets for lint, `DicomClient`/`CStoreScu`/`CFindScu` for networking, and `UidGenerator` for fix operations.

**Primary recommendation:** Use System.CommandLine 2.0.2 for parsing with Spectre.Console 0.54.0 for terminal output. Create a new `src/SharpDicom.Cli` console project targeting net10.0. Use TOML (via Tomlyn) for the configuration file format.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.CommandLine | 2.0.2 | CLI argument parsing, subcommands, help, shell completions | Now stable; used by dotnet CLI itself; supports subcommands, validators, middleware, tab completion |
| Spectre.Console | 0.54.0 | Rich terminal output: colored text, tables, progress bars, tree display | De facto standard for .NET console output; auto TTY detection; cross-platform |
| System.Text.Json | 10.0.2 | JSON output format | Already a SharpDicom dependency; built-in, no extra dep |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Tomlyn | 0.20.0 | TOML configuration file parsing | Config file at `~/.sharpdcm/config.toml` |
| Spectre.Console.Cli | 0.54.0 | (NOT recommended - see below) | Alternative to System.CommandLine |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.CommandLine | Spectre.Console.Cli | Spectre.Console.Cli is more opinionated (class-per-command); System.CommandLine is more flexible, has shell completion, and is the Microsoft-blessed approach |
| System.CommandLine | Ookii.CommandLine | Good AOT support, but less ecosystem adoption than System.CommandLine |
| Tomlyn (TOML) | System.Text.Json (JSON) | JSON is noisier for humans; TOML is more readable for config. JSON already available for output format |
| Tomlyn (TOML) | YamlDotNet (YAML) | YAML is error-prone with indentation; TOML is more explicit |

**Installation:**
```bash
dotnet add package System.CommandLine --version 2.0.2
dotnet add package Spectre.Console --version 0.54.0
dotnet add package Tomlyn --version 0.20.0
```

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom.Cli/
├── SharpDicom.Cli.csproj
├── Program.cs                  # Root command setup, main entry point
├── Commands/
│   ├── DumpCommand.cs          # sharpdcm dump
│   ├── StoreCommand.cs         # sharpdcm store
│   ├── FindCommand.cs          # sharpdcm find
│   ├── LintCommand.cs          # sharpdcm lint
│   └── FixCommand.cs           # sharpdcm fix
├── Output/
│   ├── IOutputFormatter.cs     # Interface for format-agnostic output
│   ├── TextFormatter.cs        # Human-readable text output (dcmdump-style)
│   ├── JsonFormatter.cs        # JSON output
│   └── XmlFormatter.cs         # XML output
├── Configuration/
│   ├── CliConfig.cs            # Configuration model
│   ├── ConfigLoader.cs         # Load from file/env/flags
│   └── PacsProfile.cs          # Named PACS connection profiles
├── Helpers/
│   ├── FileEnumerator.cs       # Recursive .dcm file discovery
│   ├── ProgressReporter.cs     # TTY-aware progress (Spectre.Console wrapper)
│   ├── ExitCodes.cs            # Exit code constants
│   └── ConnectionStringParser.cs # Parse pacs://AET@host:port
└── Diagnostics/
    ├── DicomFixer.cs           # Fix engine: UID, dates, encoding, element removal
    └── FixAction.cs            # Individual fix operation descriptor
```

### Pattern 1: System.CommandLine Subcommand Setup
**What:** Define root command with verb subcommands using System.CommandLine API
**When to use:** Program.cs entry point
**Example:**
```csharp
// Source: System.CommandLine docs (Context7 /dotnet/command-line-api)
var rootCommand = new RootCommand("SharpDicom command-line toolkit");

var dumpCommand = new Command("dump", "Display DICOM file contents");
var fileArg = new Argument<FileSystemInfo[]>("files", "DICOM files or directories to process");
var formatOption = new Option<string>("--format", () => "text", "Output format: text, json, xml");
formatOption.AddAlias("-f");

dumpCommand.Arguments.Add(fileArg);
dumpCommand.Options.Add(formatOption);
dumpCommand.SetAction(async (parseResult, ct) =>
{
    var files = parseResult.GetValue(fileArg);
    var format = parseResult.GetValue(formatOption);
    // ... handler logic
});

rootCommand.Subcommands.Add(dumpCommand);
return rootCommand.Parse(args).Invoke();
```

### Pattern 2: TTY-Aware Output with Spectre.Console
**What:** Detect terminal vs pipe and adapt output accordingly
**When to use:** Any command producing progress or colored output
**Example:**
```csharp
// Source: Spectre.Console docs (Context7 /websites/spectreconsole_net)
if (AnsiConsole.Profile.Capabilities.Interactive)
{
    // TTY - show progress bar
    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new RemainingTimeColumn(),
        })
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Processing files[/]", maxValue: fileCount);
            foreach (var file in files)
            {
                await ProcessFileAsync(file, ct);
                task.Increment(1);
            }
        });
}
else
{
    // Piped - line-per-file logging
    foreach (var file in files)
    {
        await ProcessFileAsync(file, ct);
        Console.WriteLine($"{file.Name} ... OK");
    }
}
```

### Pattern 3: dcmdump-Style Text Output
**What:** Format DICOM elements in the standard dcmdump output convention
**When to use:** Default text output for `sharpdcm dump`
**Example:**
```
(0008,0005) CS [ISO_IR 100]                            # SpecificCharacterSet
(0008,0016) UI =CTImageStorage                         # SOPClassUID
(0008,0018) UI [1.2.840.113619.2.55.3.604688119.969.1]# SOPInstanceUID
(0010,0010) PN [Smith^John]                            # PatientName
(0010,0020) LO [12345678]                              # PatientID
(0020,000D) UI [1.2.3.4.5]                             # StudyInstanceUID
(7FE0,0010) OW (pixel data, length=524288)             # PixelData
```
Convention notes:
- String values in square brackets: `[value]`
- Known UIDs with `=Name` prefix
- Tag keyword as trailing comment
- Sequences indented with `>` prefix for nesting depth
- Private tags show creator name when known

### Pattern 4: Configuration Precedence Chain
**What:** Layer configuration from file, env vars, and flags
**When to use:** All commands that need configuration
**Example:**
```csharp
// Precedence: flags > env vars > config file > defaults
public static CliConfig Load(string? configPath = null)
{
    var config = new CliConfig();

    // 1. Config file
    var path = configPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".sharpdcm", "config.toml");
    if (File.Exists(path))
    {
        var toml = Tomlyn.Toml.ToModel<CliConfig>(File.ReadAllText(path));
        config = config.MergeWith(toml);
    }

    // 2. Environment variables
    var envFormat = Environment.GetEnvironmentVariable("SHARPDCM_OUTPUT_FORMAT");
    if (envFormat != null) config.OutputFormat = envFormat;

    // 3. Command-line flags override in handler
    return config;
}
```

### Anti-Patterns to Avoid
- **Monolithic command handlers:** Each command's handler should be thin -- delegate to SharpDicom library APIs, not re-implement logic. The CLI is a facade.
- **Blocking async calls:** The CLI must be async-native. Never `.Result` or `.GetAwaiter().GetResult()` in command handlers; System.CommandLine supports async actions.
- **Hard-coded output format:** Always route through `IOutputFormatter` so every command can produce text/JSON/XML. Never `Console.WriteLine` directly in command logic.
- **Ignoring CancellationToken:** All commands must respect Ctrl+C via the CancellationToken from System.CommandLine. Network commands especially must clean up associations.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CLI argument parsing | Custom arg parser | System.CommandLine 2.0.2 | Subcommands, validation, help, completions, response files all built-in |
| Progress bars | Custom console progress | Spectre.Console `Progress()` | TTY detection, multiple columns, transfer speed, remaining time all built-in |
| TOML parsing | Custom config parser | Tomlyn 0.20.0 | Full TOML v1.0 compliance, model binding, comment preservation |
| JSON serialization | Custom JSON writer | System.Text.Json | Already a dependency, handles escaping, streaming, source-gen serialization |
| Terminal color detection | Custom ANSI detection | Spectre.Console `AnsiConsole.Profile` | Handles NO_COLOR, TERM, Windows/Unix differences |
| File glob expansion | Custom recursive enumerator | `Directory.EnumerateFiles` with `SearchOption.AllDirectories` | Built into .NET, handles symlinks and permissions |
| UID generation | Custom UID builder | `UidGenerator` (existing in SharpDicom) | Already validates, generates 2.25.xxx format |
| UID validation | Custom regex | `UidGenerator.IsValidUid` (existing) | Handles leading zeros, length limits, component rules |
| DICOM validation | Custom checks | `ValidationProfile.Strict/Lenient/Permissive` (existing) | Full rule set with configurable behavior |
| DICOM tag lookup | Custom dictionary | `DicomDictionary.Default.GetEntry()` (existing) | Source-generated from NEMA XML, includes name/keyword/VR/VM |
| Private tag names | Custom vendor lookup | `VendorDictionary.GetInfo()` (existing) | Source-generated vendor dictionaries, creator-aware |

**Key insight:** The SharpDicom library already implements the hard parts (parsing, validation, networking, dictionary). The CLI should be a thin wrapper that maps CLI arguments to library API calls and formats output.

## Common Pitfalls

### Pitfall 1: Mixing Output and Progress on stdout
**What goes wrong:** Progress bars and structured output (JSON, CSV) get interleaved on stdout, corrupting machine-readable output.
**Why it happens:** Progress bars write to the same stream as results.
**How to avoid:** Write progress to stderr, results to stdout. Spectre.Console supports `AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) })` for progress, while structured output goes to `Console.Out`.
**Warning signs:** JSON output that fails to parse, or progress bars appearing in piped output.

### Pitfall 2: Not Handling Network Cleanup on Ctrl+C
**What goes wrong:** DICOM association left open, remote PACS may have stale connections.
**Why it happens:** CancellationToken is not properly threaded through to `DicomClient.ReleaseAsync()`.
**How to avoid:** Always use `await using` on `DicomClient`. Register a cancellation callback that calls `Abort()` if `ReleaseAsync()` times out. System.CommandLine provides `CancellationToken` in the action's `ParseResult`.
**Warning signs:** Remote PACS logs showing aborted connections instead of graceful releases.

### Pitfall 3: Memory Explosion on Large Directories
**What goes wrong:** Loading all DICOM files into memory before processing.
**Why it happens:** Eagerly reading all files in a directory.
**How to avoid:** Process files one at a time in a streaming fashion. Use `Directory.EnumerateFiles` (lazy) not `Directory.GetFiles` (eager). For `store`, send each file before loading the next. For `dump`, output each file's contents before moving to the next.
**Warning signs:** High memory usage when processing directories with thousands of files.

### Pitfall 4: Config File Errors Blocking All Commands
**What goes wrong:** Malformed config file prevents even `--help` from working.
**Why it happens:** Config loading runs before argument parsing.
**How to avoid:** Config loading should be lazy (per-command, not at startup) and errors should be warnings, not fatal. A command that doesn't need config (e.g., `dump` with explicit flags) should work even if the config file is corrupt.
**Warning signs:** User can't run any command after editing config file.

### Pitfall 5: Inconsistent Exit Codes
**What goes wrong:** Different commands return different exit codes for the same error type, or success/failure is ambiguous.
**Why it happens:** No centralized exit code constants.
**How to avoid:** Define `ExitCodes` class with constants (0=Success, 1=Usage, 2=Runtime, 3=Validation). All command handlers return through this class. Tests verify exit codes.
**Warning signs:** CI scripts can't reliably detect success/failure from exit codes.

### Pitfall 6: XML Output Without Proper Escaping
**What goes wrong:** DICOM values containing `<`, `>`, `&` break XML output.
**Why it happens:** String concatenation instead of proper XML serialization.
**How to avoid:** Use `System.Xml.Linq` (XDocument/XElement) or `System.Xml.XmlWriter` for XML output. Never concatenate XML strings.
**Warning signs:** XML parsers failing on the tool's output.

## Code Examples

Verified patterns from the existing SharpDicom codebase:

### Reading and Displaying a DICOM File (dump core)
```csharp
// Source: SharpDicom DicomFile.cs, DicomDataset.cs, DicomDictionary.cs
var file = await DicomFile.OpenAsync(path, ct: ct);
foreach (var element in file.Dataset)
{
    var entry = DicomDictionary.Default.GetEntry(element.Tag);
    var name = entry?.Keyword ?? "Unknown";
    var vr = element.VR;

    if (element is DicomSequence seq)
    {
        // Recurse into sequence items
        WriteSequence(seq, depth: 0, maxDepth);
    }
    else if (element is DicomStringElement se)
    {
        var value = se.GetString(file.Dataset.Encoding);
        Write($"({element.Tag.Group:X4},{element.Tag.Element:X4}) {vr} [{value}] # {name}");
    }
    else
    {
        Write($"({element.Tag.Group:X4},{element.Tag.Element:X4}) {vr} ({element.Length} bytes) # {name}");
    }

    // Private tag vendor lookup
    if (element.Tag.IsPrivate && !element.Tag.IsPrivateCreator)
    {
        var creator = file.Dataset.PrivateCreators.GetCreator(element.Tag);
        if (creator != null)
        {
            var info = VendorDictionary.GetInfo(creator, element.Tag.Element);
            if (info != null)
                // Append vendor name: e.g., "# SIEMENS CSA HEADER: CSAImageHeaderInfo"
        }
    }
}
```

### Sending Files with Progress (store core)
```csharp
// Source: SharpDicom CStoreScu.cs, DicomClient.cs, DicomClientOptions.cs
var options = new DicomClientOptions
{
    Host = host,
    Port = port,
    CalledAE = calledAe,
    CallingAE = callingAe,
};

await using var client = new DicomClient(options);
var contexts = new[] { PresentationContext.CreateProposed(/* SOP classes */) };
await client.ConnectAsync(contexts, ct);

var storeScu = new CStoreScu(client);
var progress = new Progress<DicomTransferProgress>(p =>
{
    // Update Spectre.Console progress task
    progressTask.Value = p.BytesTransferred;
});

var response = await storeScu.SendAsync(file, progress, ct);
if (!response.IsSuccess)
    // Report error with status code
```

### Querying PACS (find core)
```csharp
// Source: SharpDicom CFindScu.cs, DicomQuery.cs
var findScu = new CFindScu(client);
var query = DicomQuery.ForStudies()
    .WithPatientName(patientName)
    .WithModality(modality)
    .ReturnField(DicomTag.StudyDescription)
    .ReturnField(DicomTag.StudyDate);

await foreach (var result in findScu.QueryAsync(query, ct))
{
    var patName = result.GetString(DicomTag.PatientName);
    var studyDate = result.GetString(DicomTag.StudyDate);
    var desc = result.GetString(DicomTag.StudyDescription);
    formatter.WriteResult(patName, studyDate, desc);
}
```

### Validating Files (lint core)
```csharp
// Source: SharpDicom ValidationProfile.cs, DicomReaderOptions.cs
var profile = strictness switch
{
    "strict" => ValidationProfile.Strict,
    "lenient" => ValidationProfile.Lenient,
    "permissive" => ValidationProfile.Permissive,
    _ => ValidationProfile.Strict
};

var readerOptions = new DicomReaderOptions
{
    ValidationProfile = profile,
    CollectValidationIssues = true
};

var file = await DicomFile.OpenAsync(path, readerOptions, ct);
var result = file.ValidationResult;

if (result != null && result.HasIssues)
{
    foreach (var issue in result.Issues)
    {
        // issue.Code: "DICOM-003"
        // issue.Severity: Error/Warning/Info
        // issue.Tag: the affected tag
        // issue.Message: human-readable description
        // issue.SuggestedFix: remediation hint
        formatter.WriteIssue(issue);
    }
}

return result?.IsValid ?? true ? ExitCodes.Success : ExitCodes.ValidationError;
```

### Fixing Common Issues (fix core)
```csharp
// Source: SharpDicom UidGenerator.cs, DicomFile.cs, DicomDataset.cs
var file = await DicomFile.OpenAsync(path, ct: ct);
var dataset = file.Dataset;
var changes = new List<FixAction>();

// Fix invalid UIDs
foreach (var element in dataset.Where(e => e.VR == DicomVR.UI))
{
    if (element is DicomStringElement se)
    {
        var uid = se.GetString();
        if (uid != null && !UidGenerator.IsValidUid(uid))
        {
            var newUid = UidGenerator.GenerateUid();
            dataset.Add(new DicomStringElement(element.Tag, DicomVR.UI,
                System.Text.Encoding.ASCII.GetBytes(newUid)));
            changes.Add(new FixAction(element.Tag, "Invalid UID replaced", uid, newUid));
        }
    }
}

// Fix invalid dates (DA VR)
foreach (var element in dataset.Where(e => e.VR == DicomVR.DA))
{
    // Validate format, attempt repair, log change
}

if (!dryRun && changes.Count > 0)
{
    var outputPath = GetOutputPath(path); // file.fixed.dcm
    await file.SaveAsync(outputPath, ct: ct);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| System.CommandLine beta (2.0.0-beta*) | System.CommandLine stable (2.0.2) | Jan 2026 | No longer prerelease; stable API with breaking changes from beta resolved |
| Manual ANSI escape codes | Spectre.Console markup and widgets | 2021+ | Cross-platform terminal rendering without manual escape sequences |
| JSON config files | TOML for CLI config | Convention trend | Readable config without syntax noise; Rust/Python/Go CLIs standardized on TOML |
| Custom progress indicators | Spectre.Console Progress API | 2021+ | Built-in transfer speed, ETA, multi-task progress |

**Deprecated/outdated:**
- System.CommandLine `InvocationContext` pattern: Replaced with `SetAction` and `ParseResult` in 2.0 stable. Do not use beta-era handler patterns.
- `DragonFruit` approach (magic Main conventions): Removed from System.CommandLine in the stable release path. Use explicit command/option setup.

## Open Questions

1. **Native AOT compatibility**
   - What we know: System.CommandLine 2.0.2 is trimming-compatible. Spectre.Console uses some reflection internally.
   - What's unclear: Whether the full Spectre.Console widget set is AOT-safe. SharpDicom uses System.Text.Encoding.CodePages which may have AOT limitations.
   - Recommendation: Target net10.0 without AOT initially. Add `<PublishAot>true</PublishAot>` as a stretch goal after core functionality works.

2. **Man page generation**
   - What we know: System.CommandLine can generate help text. There's no built-in man page generator.
   - What's unclear: Best approach for generating actual man pages (groff format) from command definitions.
   - Recommendation: Defer man pages to a later phase. `--help` is sufficient for initial release. Could generate from XML docs or custom attribute extraction.

3. **Config file schema evolution**
   - What we know: TOML is the recommended format. Tomlyn handles parsing.
   - What's unclear: How to handle config file schema changes across versions.
   - Recommendation: Start with a simple flat structure, add a `version` key. Validate on load, warn on unknown keys.

4. **`sharpdcm fix` scope**
   - What we know: The phase description lists UID, date, encoding, and element removal fixes. UidGenerator exists.
   - What's unclear: What "fix character encoding issues" means precisely -- re-encode from detected charset? Replace invalid bytes?
   - Recommendation: Start with: (a) replace invalid UIDs, (b) reformat invalid dates to valid YYYYMMDD, (c) remove elements that fail validation. Defer character re-encoding to future phase.

## Sources

### Primary (HIGH confidence)
- Context7 `/dotnet/command-line-api` - Subcommand API, shell completions, command structure
- Context7 `/websites/spectreconsole_net` - Progress bars, CLI framework, settings/commands, live display
- SharpDicom codebase (local) - DicomFile, DicomDataset, CStoreScu, CFindScu, ValidationProfile, UidGenerator, VendorDictionary APIs

### Secondary (MEDIUM confidence)
- [NuGet: System.CommandLine 2.0.2](https://www.nuget.org/packages/System.CommandLine) - Confirmed stable release, Jan 2026
- [NuGet: Spectre.Console 0.54.0](https://www.nuget.org/packages/spectre.console) - Latest version confirmed
- [NuGet: Tomlyn 0.20.0](https://www.nuget.org/packages/Tomlyn) - Latest version, TOML v1.0 compatible
- [DCMTK dcmdump docs](https://support.dcmtk.org/docs/dcmdump.html) - Output format conventions
- [System.CommandLine roadmap](https://github.com/dotnet/command-line-api/issues/2576) - Path to stable release confirmed

### Tertiary (LOW confidence)
- [Spectre.Console + System.CommandLine integration blog](https://anthonysimmon.com/beautiful-interactive-console-apps-with-system-commandline-and-spectre-console/) - Integration patterns
- [Native AOT docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) - AOT deployment overview

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - System.CommandLine 2.0.2 is now stable and verified on NuGet. Spectre.Console is well-established. Tomlyn is the standard .NET TOML library.
- Architecture: HIGH - Pattern follows standard .NET CLI conventions. SharpDicom APIs are read from source and confirmed to exist with documented signatures.
- Pitfalls: HIGH - Based on direct analysis of existing APIs and common CLI development patterns. Network cleanup verified against DicomClient source code.

**Research date:** 2026-02-05
**Valid until:** 2026-03-07 (stable libraries, 30-day validity)
