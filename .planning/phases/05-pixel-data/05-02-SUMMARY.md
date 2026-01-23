---
phase: 05-pixel-data
plan: 02
subsystem: pixel-data
tags: [pixel-data, lazy-loading, streaming, frame-access, IDisposable]
dependencies:
  requires: [05-01]
  provides:
    - IPixelDataSource interface for data access abstraction
    - ImmediatePixelDataSource for in-memory data
    - LazyPixelDataSource for deferred loading from streams
    - SkippedPixelDataSource for metadata-only mode
    - DicomPixelDataElement with frame-level access
    - PixelDataContext for callback decisions
    - Extended DicomReaderOptions with PixelDataHandling
  affects: [05-03, file-reading-integration]
tech-stack:
  added: []
  patterns:
    - Source abstraction for different loading strategies
    - Thread-safe lazy loading with SemaphoreSlim
    - IDisposable for resource cleanup
    - Span<T>-based frame access
    - MemoryMarshal for typed pixel arrays
decisions:
  - title: IPixelDataSource as common interface
    rationale: Unified API for accessing pixel data regardless of loading strategy
    alternatives: Separate classes without common interface
    tradeoffs: Interface dispatch overhead (minimal)
  - title: Thread-safe LazyPixelDataSource
    rationale: Multiple threads may request data concurrently
    alternatives: No synchronization (caller's responsibility)
    tradeoffs: SemaphoreSlim overhead for thread safety
  - title: Stream not disposed by LazyPixelDataSource
    rationale: Stream lifecycle managed externally by caller/DicomFile
    alternatives: Take ownership of stream
    tradeoffs: Caller must manage stream lifetime
  - title: DicomPixelDataElement implements IDisposable
    rationale: Contains IPixelDataSource which holds stream references
    alternatives: No disposal (rely on GC)
    tradeoffs: Caller must dispose, but ensures timely resource release
key-files:
  created:
    - src/SharpDicom/IO/IPixelDataSource.cs: Interface for pixel data access
    - src/SharpDicom/IO/ImmediatePixelDataSource.cs: In-memory data source
    - src/SharpDicom/IO/LazyPixelDataSource.cs: Stream-backed lazy source
    - src/SharpDicom/IO/SkippedPixelDataSource.cs: Metadata-only placeholder
    - src/SharpDicom/Data/DicomPixelDataElement.cs: Unified pixel data element
    - src/SharpDicom/Data/PixelDataContext.cs: Callback context struct
    - tests/SharpDicom.Tests/IO/PixelDataSourceTests.cs: 31 tests
    - tests/SharpDicom.Tests/Data/DicomPixelDataElementTests.cs: 26 tests
  modified:
    - src/SharpDicom/IO/DicomReaderOptions.cs: Added PixelDataHandling, Callback, TempDirectory
metrics:
  duration: 12 minutes
  tests-added: 57
  tests-total: 868
  completed: 2026-01-27
---

# Phase 5 Plan 2: Lazy Loading Infrastructure Summary

**One-liner:** Implemented IPixelDataSource abstraction with immediate, lazy, and skipped implementations plus DicomPixelDataElement for frame-level pixel access.

## What Was Built

### Core Functionality

1. **IPixelDataSource Interface**
   - `IsLoaded` - Whether data is currently in memory
   - `Length` - Pixel data length in bytes
   - `State` - Current PixelDataLoadState
   - `GetData()` - Synchronous data access
   - `GetDataAsync(CancellationToken)` - Async data access
   - `CopyToAsync(Stream, CancellationToken)` - Stream pixel data to destination
   - `ToOwned()` - Create stream-detached copy
   - `Dispose()` - Release resources

2. **ImmediatePixelDataSource**
   - For pixel data already loaded in memory
   - `IsLoaded` always true
   - `GetData()` returns data directly (no copy)
   - `ToOwned()` returns self if array-backed
   - Zero overhead for pre-loaded data

3. **LazyPixelDataSource**
   - For deferred loading from seekable streams
   - Thread-safe with SemaphoreSlim locking
   - Caches data after first load
   - `GetDataAsync()` seeks to offset and reads length bytes
   - `CopyToAsync()` can stream without caching (for large files)
   - Stream is NOT disposed (managed externally)
   - Throws ObjectDisposedException if accessed after disposal

4. **SkippedPixelDataSource**
   - For metadata-only parsing mode
   - `IsLoaded` always false
   - All data access methods throw InvalidOperationException
   - Stores offset and length as metadata
   - Helpful error message guides user to use ToOwned() before stream disposal

5. **DicomPixelDataElement**
   - Implements IDicomElement and IDisposable
   - Wraps IPixelDataSource with DICOM element interface
   - `Tag` always DicomTag.PixelData
   - `VR` - OB or OW based on context
   - `Info` - PixelDataInfo metadata
   - `LoadState` - Reflects source state
   - `IsEncapsulated` - Whether compressed
   - `Fragments` - DicomFragmentSequence for encapsulated
   - `NumberOfFrames` - Defaults to 1 if not specified

6. **Frame Access Methods**
   - `GetFrameSpan(int frameIndex)` - Returns span to frame bytes
   - `GetFrame<T>(int frameIndex)` - Returns typed array (byte[], ushort[], etc.)
   - Frame offset calculated from FrameSize
   - Throws NotSupportedException for encapsulated data
   - Validates frame index bounds

7. **PixelDataContext**
   - Readonly struct for callback decisions
   - Properties: Rows, Columns, BitsAllocated, NumberOfFrames, SamplesPerPixel, TransferSyntax, IsEncapsulated, ValueLength
   - `EstimatedSize` - Calculated total pixel data size
   - `HasImageDimensions` - True if Rows and Columns present
   - `FromDataset()` factory method

8. **Extended DicomReaderOptions**
   - `PixelDataHandling` - LoadInMemory, LazyLoad, Skip, Callback
   - `PixelDataCallback` - Func<PixelDataContext, PixelDataHandling>
   - `TempDirectory` - For buffering non-seekable streams
   - All presets (Strict, Lenient, Permissive) include PixelDataHandling.LoadInMemory

### Test Coverage

#### PixelDataSourceTests (31 tests)

**ImmediatePixelDataSource (8 tests):**
- GetData returns provided data
- IsLoaded always true
- State always Loaded
- Length returns data length
- GetDataAsync returns data
- CopyToAsync writes to stream
- ToOwned returns equivalent source
- ToOwned with array-backed returns self

**LazyPixelDataSource (15 tests):**
- GetData returns correct data after seek
- IsLoaded false before load
- IsLoaded true after load
- State NotLoaded before access
- State Loaded after access
- Multiple GetData returns cached data
- GetDataAsync returns correct data
- CopyToAsync works when loaded
- CopyToAsync works when not loaded
- Concurrent access thread safety
- After Dispose throws ObjectDisposedException
- ToOwned returns ImmediateSource
- Non-seekable stream throws ArgumentException
- Negative offset throws ArgumentOutOfRangeException
- Negative length throws ArgumentOutOfRangeException

**SkippedPixelDataSource (8 tests):**
- GetData throws InvalidOperationException
- GetDataAsync throws InvalidOperationException
- CopyToAsync throws InvalidOperationException
- ToOwned throws InvalidOperationException
- Length returns stored value
- Offset returns stored value
- IsLoaded always false
- State always NotLoaded

#### DicomPixelDataElementTests (26 tests)

**Basic Properties (9 tests):**
- Tag returns PixelData
- VR returns provided VR
- IsEncapsulated false for native
- IsEncapsulated true for encapsulated
- Fragments null for native
- NumberOfFrames defaults to 1
- Length returns source length
- Length returns -1 for encapsulated
- RawValue when loaded/not loaded

**Frame Access (8 tests):**
- GetFrameSpan single frame returns all bytes
- GetFrameSpan multi-frame returns correct frame
- GetFrameSpan invalid index throws
- GetFrameSpan encapsulated throws NotSupportedException
- GetFrame byte type returns array
- GetFrame ushort type 16-bit data
- GetFrame multi-frame returns correct frame
- LoadState reflects source state

**Async Methods (2 tests):**
- LoadAsync returns pixel data
- CopyToAsync copies to stream

**ToOwned (2 tests):**
- Creates independent copy
- Preserves metadata

**PixelDataContext (4 tests):**
- EstimatedSize calculates correctly
- EstimatedSize returns null when incomplete
- HasImageDimensions true when complete
- HasImageDimensions false when incomplete

## Deviations from Plan

None - plan executed exactly as written.

## Next Phase Readiness

Phase 5 Plan 3 can proceed with:
- Integration of IPixelDataSource into file reader
- Wiring PixelDataHandling option to control loading behavior
- Creating DicomPixelDataElement during file parsing

All infrastructure is in place for pixel data loading strategies.

## Technical Notes

1. **Thread Safety**: LazyPixelDataSource uses SemaphoreSlim for thread-safe concurrent access. Only one thread loads data; others wait.

2. **Stream Ownership**: LazyPixelDataSource does NOT dispose the stream. Stream lifecycle is managed by the caller (typically DicomFile). This allows shared streams across multiple elements.

3. **Memory Efficiency**: For streaming scenarios, CopyToAsync streams directly without caching when data isn't already loaded, avoiding double memory usage.

4. **Type Safety**: GetFrame<T> uses MemoryMarshal.Cast for zero-copy interpretation of bytes as typed values. Works for any unmanaged type (byte, ushort, short, float, etc.).

5. **Error Messages**: SkippedPixelDataSource provides helpful guidance in exception message, pointing users to ToOwned() before stream disposal.
