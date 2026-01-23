---
phase: 02-basic-file-reading
plan: 03
subsystem: io
tags: [async, streaming, file-reading, IAsyncEnumerable]

dependency-graph:
  requires: [02-01-stream-reader, 02-02-part10-reader]
  provides: [high-level-file-reader, async-streaming, IAsyncEnumerable-api]
  affects: [03-implicit-vr, 05-pixel-data]

tech-stack:
  added: [Microsoft.Bcl.AsyncInterfaces]
  patterns: [IAsyncEnumerable, IAsyncDisposable, ArrayPool]

key-files:
  created:
    - src/SharpDicom/IO/DicomFileReader.cs
    - tests/SharpDicom.Tests/IO/DicomFileReaderTests.cs
  modified:
    - Directory.Packages.props
    - src/SharpDicom/SharpDicom.csproj

decisions:
  - id: "02-03-001"
    title: "List-based element batch to avoid ref struct across yield"
    choice: "Parse buffer into List<IDicomElement> synchronously, then yield from list"
    rationale: "C# does not allow ref struct (DicomStreamReader) or Span<T> across yield boundaries"
  - id: "02-03-002"
    title: "Microsoft.Bcl.AsyncInterfaces for netstandard2.0"
    choice: "Add package dependency for IAsyncEnumerable/IAsyncDisposable support"
    rationale: "Standard polyfill package, well-maintained by Microsoft"

metrics:
  duration: "10 minutes"
  completed: "2026-01-27"
---

# Phase 2 Plan 03: DicomFileReader Summary

High-level async DICOM file reader with IAsyncEnumerable streaming support.

## What Was Built

### DicomFileReader

A sealed class implementing `IAsyncDisposable` that provides convenient async APIs for reading DICOM files.

**Key capabilities:**
- Parse file header (preamble, DICM, File Meta Information) asynchronously
- Stream dataset elements via `IAsyncEnumerable<IDicomElement>`
- Load complete dataset into memory with `ReadDatasetAsync`
- Proper resource management with ArrayPool buffer recycling
- Support for cancellation tokens throughout all operations
- Optional `leaveOpen` parameter to control stream lifetime

**API:**
```csharp
public sealed class DicomFileReader : IAsyncDisposable
{
    public DicomFileReader(Stream stream, DicomReaderOptions? options = null, bool leaveOpen = false);

    // Properties (available after header parsed)
    public DicomDataset? FileMetaInfo { get; }
    public TransferSyntax TransferSyntax { get; }
    public ReadOnlyMemory<byte> Preamble { get; }
    public bool IsHeaderParsed { get; }

    // Methods
    public ValueTask ReadFileMetaInfoAsync(CancellationToken ct = default);
    public IAsyncEnumerable<IDicomElement> ReadElementsAsync(CancellationToken ct = default);
    public ValueTask<DicomDataset> ReadDatasetAsync(CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

### Usage Examples

**Stream elements for memory-efficient processing:**
```csharp
await using var reader = new DicomFileReader(stream);
await foreach (var element in reader.ReadElementsAsync())
{
    // Process each element without loading entire file
    Console.WriteLine($"{element.Tag}: {element.VR}");
}
```

**Load complete dataset:**
```csharp
await using var reader = new DicomFileReader(stream);
var dataset = await reader.ReadDatasetAsync();
var patientName = dataset.GetString(DicomTag.PatientName);
```

**Access metadata before processing:**
```csharp
await using var reader = new DicomFileReader(stream);
await reader.ReadFileMetaInfoAsync();
var ts = reader.TransferSyntax;
// Now process based on transfer syntax...
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] netstandard2.0 missing async interfaces**

- **Found during:** Task 1 build
- **Issue:** IAsyncEnumerable, IAsyncDisposable, ValueTask not available on netstandard2.0
- **Fix:** Added Microsoft.Bcl.AsyncInterfaces package reference
- **Files modified:** Directory.Packages.props, SharpDicom.csproj
- **Commit:** 10988e1

**2. [Rule 3 - Blocking] ref struct cannot cross yield boundary**

- **Found during:** Task 1 build
- **Issue:** C# compiler error CS4007 - DicomStreamReader (ref struct) and Span<T> cannot be preserved across await/yield
- **Fix:** Changed ParseElements to return List<IDicomElement> synchronously, then yield from the list
- **Files modified:** DicomFileReader.cs
- **Commit:** 10988e1

## Test Coverage

16 new tests covering:
- Header parsing (basic, empty file, double call)
- Element streaming (basic, auto-header-parse, cancellation, expected tags)
- Dataset loading (complete, correct values)
- Resource disposal (stream disposal, leaveOpen, double dispose, post-dispose throws)
- Preamble access (after/before header)
- Transfer syntax recognition

**Total test count:** 300 tests (284 previous + 16 new)

## Technical Notes

### Buffer Management

DicomFileReader uses `ArrayPool<byte>.Shared` for buffer allocation:
- 64 KB buffer rented on first read
- Returned to pool on disposal
- Avoids repeated large allocations

### Ref Struct Workaround

The `DicomStreamReader` is a `ref struct` for zero-copy parsing, but C# does not allow ref structs or Span<T> across yield boundaries. Solution:

```csharp
// Instead of yielding during parsing:
private List<IDicomElement> ParseElementsFromBuffer(byte[] buffer, int offset, int length, ...)
{
    var elements = new List<IDicomElement>();
    var reader = new DicomStreamReader(buffer.AsSpan(offset, length), ...);

    while (!reader.IsAtEnd)
    {
        // Parse synchronously, collect into list
        elements.Add(CreateElement(...));
    }

    return elements;  // List returned, then yielded from outside
}
```

This maintains the streaming pattern at the async level while keeping ref struct usage synchronous.

### Multi-TFM Considerations

| TFM | Stream.ReadAsync | Stream.DisposeAsync |
|-----|------------------|---------------------|
| netstandard2.0 | byte[],int,int,CT | .Dispose() + await Task.CompletedTask |
| net6.0 | byte[],int,int,CT | DisposeAsync() |
| net8.0+ | Memory<byte>,CT | DisposeAsync() |

Conditional compilation handles these differences.

## Commits

| Hash | Message |
|------|---------|
| 10988e1 | feat(02-03): implement DicomFileReader with async streaming |

## Next Phase Readiness

Ready for:
- **02-04**: Integration testing with real DICOM files
- **Phase 3**: Implicit VR and sequence parsing (DicomFileReader will need enhancement)
- **Phase 5**: Pixel data handling (streaming callback integration)

Current limitations to address in future phases:
- Sequences with undefined length skipped (Phase 3)
- Elements spanning buffer boundaries not handled (enhancement)
- Implicit VR uses dictionary lookup (Phase 3 will improve)
