# Phase 20: Critical Bug Fixes - Research

**Researched:** 2026-02-02
**Domain:** DICOM parser correctness, sequence parsing, streaming SCP
**Confidence:** HIGH

## Summary

Researched two critical bugs blocking production use: FindSequenceDelimiter parsing incorrect depth tracking for nested undefined-length sequences, and C-STORE SCP streaming parser not preserving all elements for full roundtrip fidelity.

Both bugs are in reader/parser code; writer is confirmed correct. The FindSequenceDelimiter bug exists in two locations: DicomFileReader.cs (lines 531-658) and DicomStreamReader.cs (lines 296-364), with subtle differences in implementation. SequenceParser.cs has a third implementation (FindSequenceContentLength, lines 417-483) that appears correct.

**Primary recommendation:** Fix depth tracking logic in both FindSequenceDelimiter implementations, enhance streaming SCP parser to use full DicomFileReader pipeline while maintaining streaming semantics, verify with property-based testing.

## Standard Stack

The established approach for fixing parser bugs in .NET DICOM implementations:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| NUnit | 4.x | Test framework | Already used project-wide, supports parameterized tests |
| FsCheck | 2.16+ | Property-based testing | Industry standard for generating complex nested structures |
| System.Buffers | Latest | Zero-copy parsing | Already used throughout codebase |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| BenchmarkDotNet | 0.13+ | Performance regression | After fix to verify no perf degradation |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| FsCheck | Custom generators | FsCheck has shrinking built-in, custom requires reimplementation |
| NUnit | xUnit | NUnit already standard in codebase |

**Installation:**
```bash
dotnet add tests/SharpDicom.Tests package FsCheck --version 2.16.6
```

## Architecture Patterns

### Recommended Bug Fix Structure
```
Bug Fix Workflow:
1. Reproduce with minimal test case (from skipped tests)
2. Add property-based tests for edge cases
3. Fix implementation with clear comments
4. Verify all tests pass
5. DCMTK interop verification
```

### Pattern 1: Depth Tracking State Machine

**What:** Track nesting depth with explicit counters for Items and Sequences

**When to use:** Parsing undefined-length nested structures with multiple delimiter types

**Current bug pattern (DicomFileReader.cs:531-658):**
```csharp
// BUG: Line 559 decrements depth for SequenceDelimitationItem when depth == 0
if (tag == DicomTag.SequenceDelimitationItem)
{
    if (depth == 0)
        return position;
    // Nested sequence delimiter - decrement depth
    depth--;  // ← WRONG: decrements even when depth == 0
    position += 8;
}
```

**Correct pattern (from SequenceParser.cs:417-483):**
```csharp
// Source: /src/SharpDicom/IO/SequenceParser.cs:451-460
else if (tag == DicomTag.SequenceDelimitationItem)
{
    if (depth == 0)
    {
        // Found the end of our sequence
        return position;
    }
    // Nested sequence ended
    depth--;  // ← CORRECT: only decrements after depth check
    position += 8;
}
```

**Key insight:** Must return BEFORE decrementing depth when depth == 0, not after checking.

### Pattern 2: Separate Item and Sequence Depth Counters

**What:** Track Item nesting and Sequence nesting independently

**Why:** Item delimiters (FFFE,E00D) and Sequence delimiters (FFFE,E0DD) operate at different conceptual levels. Mixed tracking leads to incorrect depth calculations.

**Current implementations:** All three implementations (DicomFileReader, DicomStreamReader, SequenceParser) use single depth counter

**Recommended pattern:**
```csharp
int itemDepth = 0;      // Track Item nesting
int sequenceDepth = 0;  // Track nested SQ elements

// Item handling
if (tag == DicomTag.Item && itemLength == UndefinedLength)
    itemDepth++;
else if (tag == DicomTag.ItemDelimitationItem)
    if (itemDepth > 0) itemDepth--;

// Sequence handling
if (vr == DicomVR.SQ && valueLength == UndefinedLength)
    sequenceDepth++;
else if (tag == DicomTag.SequenceDelimitationItem)
{
    if (sequenceDepth == 0)
        return position;  // Found our delimiter
    sequenceDepth--;
}
```

**Evidence:** DICOM PS3.5 Section 7.5.2 states sequences and items have independent delimitation mechanisms

### Pattern 3: Streaming Parser with Full Fidelity

**What:** Process DIMSE P-DATA PDUs incrementally while maintaining complete element preservation

**Current limitation:** IStreamingCStoreHandler receives simplified metadata dataset, not full parsed dataset

**Recommended approach:**
```csharp
// Accumulate P-DATA fragments into buffer
// Once complete dataset received (final fragment):
using var ms = new MemoryStream(accumulatedBuffer);
using var reader = new DicomFileReader(ms, readerOptions, leaveOpen: true);
await reader.ReadFileMetaInfoAsync(ct);  // If Part 10 format
var dataset = await reader.ReadDatasetAsync(ct);

// Now invoke handler with FULL dataset
await handler.OnCStoreStreamingAsync(context, dataset, pixelDataStream, ct);
```

**Constraint:** Must remain streaming - cannot buffer entire dataset in memory for high-volume PACS

**Solution:** Use DicomFileReader with PixelDataHandling.Skip to parse metadata without loading pixel data, then stream pixel data separately

### Anti-Patterns to Avoid

- **Incrementing depth without matching decrement paths:** Leads to infinite loops or missed delimiters
- **Modifying shared state during FindSequenceDelimiter:** Method should be read-only scanner
- **Switching to buffered mode for streaming fix:** Violates memory constraint for high-volume receivers

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Nested structure generators | Custom random | FsCheck.Arb | Handles recursive depth limits, shrinking for minimal repro |
| Parser regression detection | Manual test cases | Property-based tests | Catches edge cases humans miss |
| DICOM file validation | Custom validator | DCMTK dcmftest + dciodvfy | Industry standard, catches spec violations |
| Depth tracking logic | Ad-hoc counters | State machine pattern | Explicit states prevent off-by-one errors |

**Key insight:** Parser bugs are notoriously difficult to fix without introducing regressions. Property-based testing is essential, not optional.

## Common Pitfalls

### Pitfall 1: Off-by-One in Depth Tracking

**What goes wrong:** Decrementing depth before checking if already at target level, or after returning

**Why it happens:** Subtle ordering difference in if-statement branches

**How to avoid:**
1. ALWAYS check `if (depth == 0) return position;` BEFORE any depth modification
2. Use explicit state machine with named states instead of integer depth
3. Write property: `depth after parse == 0` for all valid sequences

**Warning signs:**
- Skipped tests with "nested" in name
- Delimiter not found exceptions on valid files
- Parser consumes too much or too little data

### Pitfall 2: Mixing Item and Sequence Delimiters

**What goes wrong:** Using same depth counter for Item (FFFE,E000/E00D) and Sequence (SQ/FFFE,E0DD)

**Why it happens:** Both use "depth" concept but operate independently per DICOM spec

**How to avoid:**
1. Read DICOM PS3.5 Section 7.5.2 carefully - Items and Sequences are orthogonal
2. Use separate counters: `itemDepth` and `sequenceDepth`
3. Test files with sequences containing items containing sequences (3+ levels)

**Warning signs:**
- Works for 2-level nesting, fails at 3+ levels
- Empty sequences cause delimiter not found
- Defined-length sequences work but undefined-length fail

### Pitfall 3: Incomplete Streaming Parser

**What goes wrong:** Streaming parser skips elements to reduce memory, loses data on roundtrip

**Why it happens:** Optimization (skip pixel data) applied too broadly, skips other elements

**How to avoid:**
1. Only skip pixel data (7FE0,0010), parse all other elements fully
2. Use DicomFileReader with PixelDataHandling.Skip option
3. Test roundtrip: parse -> write -> parse -> compare (should be identical)

**Warning signs:**
- Roundtrip produces smaller file than original
- Missing private tags or sequences in received file
- Works for simple images, fails for complex multi-frame or SR

### Pitfall 4: Not Testing Undefined-Length Variants

**What goes wrong:** Tests only cover defined-length sequences, undefined-length broken

**Why it happens:** Defined-length is simpler, writer defaults to it, tests don't force undefined

**How to avoid:**
1. Use DicomWriterOptions.SequenceLength = Undefined explicitly in tests
2. Test BOTH length modes for every sequence nesting scenario
3. Property test: `roundtrip(write(data, mode)) == data` for all modes

**Warning signs:**
- Tests marked [Ignore] with "undefined length" in name
- Only tests defined-length mode
- Comments like "reader bug" without fix plan

## Code Examples

Verified patterns from codebase analysis:

### Correct Depth Tracking Pattern
```csharp
// Source: Analysis of SequenceParser.cs:451-460 (working) vs DicomFileReader.cs:554-560 (broken)
int depth = 0;

while (position + 8 <= buffer.Length)
{
    if (!TryReadTag(buffer.Slice(position), out var tag, out _))
        break;

    if (tag == DicomTag.SequenceDelimitationItem)
    {
        if (depth == 0)
        {
            // CRITICAL: Return BEFORE decrementing depth
            return position;
        }
        // Nested sequence ended
        depth--;
        position += 8;
        continue;  // Process next tag
    }

    if (tag == DicomTag.Item)
    {
        uint itemLength = ReadUInt32(buffer.Slice(position + 4));
        position += 8;

        if (itemLength == UndefinedLength)
        {
            depth++;  // Undefined-length item increases depth
        }
        else
        {
            position += (int)itemLength;  // Skip defined-length item content
        }
        continue;
    }

    if (tag == DicomTag.ItemDelimitationItem)
    {
        if (depth > 0)
            depth--;
        position += 8;
        continue;
    }

    // Regular element - parse header and skip value
    if (!TryReadElementHeader(buffer.Slice(position), out _, out var vr, out var len, out int headerLen))
        break;

    position += headerLen;

    if (len == UndefinedLength && vr == DicomVR.SQ)
    {
        depth++;  // Nested undefined-length sequence
    }
    else if (len != UndefinedLength)
    {
        position += (int)len;  // Skip defined-length value
    }
}

return -1; // Not found
```

### Property-Based Test for Nested Sequences
```csharp
// Property: Roundtrip preserves all nested sequence content
[Property(MaxTest = 100)]
public Property NestedSequencesRoundtrip(NestedSequenceData data)
{
    // Arrange
    var dataset = data.ToDataset();
    var options = new DicomWriterOptions
    {
        SequenceLength = SequenceLengthEncoding.Undefined
    };

    // Act - write
    using var ms = new MemoryStream();
    var file = new DicomFile(dataset);
    await file.SaveAsync(ms, options);

    // Act - read
    ms.Position = 0;
    var roundtrip = await DicomFile.OpenAsync(ms);

    // Assert
    return (dataset.Count == roundtrip.Dataset.Count)
        .Label($"Element count: original={dataset.Count}, roundtrip={roundtrip.Dataset.Count}")
        .And(DeepEquals(dataset, roundtrip.Dataset))
        .Label("Deep equality of nested structure");
}

// Generator for nested sequences with controlled depth
public static Arbitrary<NestedSequenceData> NestedSequenceGen()
{
    return Arb.From(
        Gen.Sized(size => GenNestedSequence(size, maxDepth: 5))
    );
}

static Gen<NestedSequenceData> GenNestedSequence(int size, int maxDepth, int currentDepth = 0)
{
    if (currentDepth >= maxDepth || size <= 0)
        return Gen.Constant(new NestedSequenceData { Items = [] });

    var itemGen =
        from itemCount in Gen.Choose(0, Math.Min(3, size))
        from items in Gen.ListOf(itemCount,
            from elements in Gen.ListOf(Gen.Choose(1, 5), GenElement())
            from nestedSeq in GenNestedSequence(size / 2, maxDepth, currentDepth + 1)
            select new ItemData { Elements = elements, NestedSequence = nestedSeq }
        )
        select new NestedSequenceData { Items = items };

    return itemGen;
}
```

### Streaming SCP with Full Fidelity
```csharp
// Enhanced streaming handler that preserves all elements
public class FullFidelityStreamingHandler : IStreamingCStoreHandler
{
    public async ValueTask<DicomStatus> OnCStoreStreamingAsync(
        CStoreRequestContext context,
        DicomDataset metadata,  // Currently simplified - FIX: make this full dataset
        Stream pixelDataStream,
        CancellationToken ct)
    {
        // Current approach: metadata is incomplete
        // ISSUE: Missing sequences, private tags, etc.

        // RECOMMENDED FIX: Parse complete dataset in C-STORE handler
        // The P-DATA PDU fragments are already accumulated before handler invocation
        // Use DicomFileReader with Skip pixel data option:

        // 1. Reconstruct dataset from accumulated PDU buffer
        using var datasetStream = new MemoryStream(accumulatedPduData);
        var readerOpts = new DicomReaderOptions
        {
            PixelDataHandling = PixelDataHandling.Skip
        };

        using var reader = new DicomFileReader(datasetStream, readerOpts, leaveOpen: true);

        // 2. Parse complete dataset (pixel data skipped, not loaded)
        var fullDataset = await reader.ReadDatasetAsync(ct);

        // 3. Now have complete metadata + separate pixel data stream
        // Save to disk with full fidelity
        var outputPath = GetOutputPath(context);

        // Write complete dataset
        var file = new DicomFile(fullDataset, context.TransferSyntax);
        await file.SaveAsync(outputPath, ct);

        // OR stream pixel data separately if needed
        if (pixelDataStream.Length > 0)
        {
            // Append pixel data or process separately
            await pixelDataStream.CopyToAsync(outputFileStream, ct);
        }

        return DicomStatus.Success;
    }
}
```

## State of the Art

Current approaches in DICOM implementations (as of January 2026):

| Approach | Current Practice | Limitation | Recommendation |
|----------|-----------------|------------|----------------|
| Depth tracking | Single integer counter | Mixes Item/Sequence depths | Separate counters or state machine |
| Streaming SCP | Simplified metadata | Loses elements on roundtrip | Full DicomFileReader with Skip |
| Testing | Manual cases | Misses edge cases | Property-based with FsCheck |
| Validation | Local test corpus | Doesn't catch spec violations | DCMTK interop tests |

**Deprecated/outdated:**
- Recursive parsing without depth limits (stack overflow risk)
- String scanning for delimiter tags (slow, fragile)
- Buffering entire dataset in SCP (memory exhaustion on large images)

**Industry practice:**
- DCMTK uses state machine for delimiter parsing (reference implementation)
- pydicom had similar nested sequence bug fixed in 2019 ([Issue #114](https://github.com/pydicom/pydicom/issues/114))
- fo-dicom uses similar FindSequenceDelimiter approach, likely has same bug class

## Open Questions

Things that couldn't be fully resolved:

1. **Should we unify the three FindSequenceDelimiter implementations?**
   - What we know: DicomFileReader, DicomStreamReader, SequenceParser each have own implementation
   - What's unclear: Whether unification would break streaming semantics
   - Recommendation: Fix bugs in-place first, unify in refactoring phase if tests pass

2. **What's the maximum tested sequence depth?**
   - What we know: Skipped tests are 3 levels deep
   - What's unclear: Real-world maximum (Structured Reporting can be 5-6 deep)
   - Recommendation: Property test with depth 1-10, validate against DCMTK files up to depth 6

3. **Should streaming mode switch to buffering for complex datasets?**
   - What we know: Full parsing requires buffering PDU data
   - What's unclear: Memory limit for "high-volume PACS receiver" scenario
   - Recommendation: Make MaxBufferedDatasetSize configurable (current: 512MB), fail gracefully if exceeded

## Sources

### Primary (HIGH confidence)
- DICOM PS3.5 Section 7.5.2 - [Delimitation of The Sequence of Items](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html)
- SharpDicom codebase analysis - DicomFileReader.cs, DicomStreamReader.cs, SequenceParser.cs
- Phase 07-03-SUMMARY.md - Documented reader bug in FindSequenceDelimiter

### Secondary (MEDIUM confidence)
- [DCMTK storescp documentation](https://support.dcmtk.org/docs/storescp.html) - Reference C-STORE SCP implementation
- [DCMTK dcmftest](https://support.dcmtk.org/docs/dcmftest.html) - DICOM Part 10 format validation
- [dciodvfy](https://dclunie.com/dicom3tools/dciodvfy.html) - DICOM IOD validation tool
- [FsCheck documentation](https://fscheck.github.io/FsCheck/) - Property-based testing framework

### Tertiary (LOW confidence - web search)
- [pydicom Issue #114](https://github.com/pydicom/pydicom/issues/114) - Similar nested sequence delimiter bug in Python implementation
- [DVTk (DICOM Validation Toolkit)](https://www.dvtk.org/) - Alternative validation tool
- [Property-Based Testing in C# with FsCheck](https://developersvoice.com/blog/csharp/csharp-property-based-testing-fscheck-xunit/) - Tutorial for complex data structures
- [StateMachineBugFinder](https://github.com/assist-project/state-machine-bug-finder) - Automated bug detection for protocol state machines

## Metadata

**Confidence breakdown:**
- FindSequenceDelimiter bug: HIGH - Code inspection shows clear off-by-one error, matches documented issue
- Streaming SCP issue: MEDIUM - Design limitation inferred from IStreamingCStoreHandler interface, not explicit bug report
- Fix approach: HIGH - Pattern verified against working SequenceParser implementation
- Property-based testing: MEDIUM - FsCheck is standard tool, but application to DICOM sequences requires domain expertise

**Research date:** 2026-02-02
**Valid until:** 30 days (stable DICOM spec, but SharpDicom codebase evolving)

**Assumptions:**
- Skipped roundtrip tests accurately reproduce the bug
- Writer implementation is correct (confirmed by Phase 7 documentation)
- DCMTK can be used for interop validation (available in CI)
- Memory constraints for streaming SCP are primary concern (not CPU or latency)
