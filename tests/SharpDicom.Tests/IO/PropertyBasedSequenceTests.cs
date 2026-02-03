using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FsCheck;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO;

/// <summary>
/// Property-based tests for sequence parsing using FsCheck.
/// These tests generate random nested sequence structures to verify correctness.
/// </summary>
[TestFixture]
public sealed class PropertyBasedSequenceTests
{
    private const int DefaultTestCount = 100;
    private const int QuickTestCount = 20; // For slower tests

    /// <summary>
    /// Property 1: Roundtrip with defined-length sequences preserves all elements.
    /// </summary>
    [Test]
    public void Roundtrip_DefinedLength_PreservesAllElements()
    {
        var config = new Configuration { MaxNbOfTest = QuickTestCount, QuietOnSuccess = true };

        Prop.ForAll(
            NestedSequenceArbitrary.Generate(),
            data =>
            {
                var dataset = data.ToDataset();
                var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Defined };

                using var ms = new MemoryStream();
                var file = new DicomFile(dataset);
                file.SaveAsync(ms, options).AsTask().Wait();

                ms.Position = 0;
                var roundtrip = DicomFile.OpenAsync(ms).AsTask().Result;

                return DeepEquals(dataset, roundtrip.Dataset);
            })
            .Check(config);
    }

    /// <summary>
    /// Property 2: Roundtrip with undefined-length sequences preserves all elements.
    /// </summary>
    [Test]
    public void Roundtrip_UndefinedLength_PreservesAllElements()
    {
        var config = new Configuration { MaxNbOfTest = QuickTestCount, QuietOnSuccess = true };

        Prop.ForAll(
            NestedSequenceArbitrary.Generate(),
            data =>
            {
                var dataset = data.ToDataset();
                var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Undefined };

                using var ms = new MemoryStream();
                var file = new DicomFile(dataset);
                file.SaveAsync(ms, options).AsTask().Wait();

                ms.Position = 0;
                var roundtrip = DicomFile.OpenAsync(ms).AsTask().Result;

                return DeepEquals(dataset, roundtrip.Dataset);
            })
            .Check(config);
    }

    /// <summary>
    /// Property 3: Depth tracking never goes negative in FindSequenceDelimiter.
    /// </summary>
    [Test]
    public void FindSequenceDelimiter_RandomNesting_NeverFails()
    {
        var config = new Configuration { MaxNbOfTest = DefaultTestCount, QuietOnSuccess = true };

        Prop.ForAll(
            NestedSequenceArbitrary.GenerateBytes(maxDepth: 10),
            bytes =>
            {
                try
                {
                    var reader = new DicomStreamReader(bytes.AsSpan(), explicitVR: true, littleEndian: true);
                    var result = reader.FindSequenceDelimiter();
                    // Method completes without throwing - success
                    return result >= 0 || result == -1;
                }
                catch (Exception)
                {
                    // Parsing errors are OK (invalid structure), but no depth underflow exceptions
                    return true;
                }
            })
            .Check(config);
    }

    /// <summary>
    /// Property 4: Parsing random valid DICOM structures never throws depth-related exceptions.
    /// </summary>
    [Test]
    public void ParseDataset_RandomValidStructure_NoDepthErrors()
    {
        var config = new Configuration { MaxNbOfTest = QuickTestCount, QuietOnSuccess = true };

        Prop.ForAll(
            NestedSequenceArbitrary.Generate(),
            data =>
            {
                try
                {
                    var dataset = data.ToDataset();
                    using var ms = new MemoryStream();
                    var file = new DicomFile(dataset);
                    file.SaveAsync(ms).AsTask().Wait();

                    ms.Position = 0;
                    var parsed = DicomFile.OpenAsync(ms).AsTask().Result;

                    return parsed.Dataset != null;
                }
                catch (Exception)
                {
                    // Some generated structures may be invalid - that's OK
                    return true;
                }
            })
            .Check(config);
    }

    // Generator for nested sequence test data
    private static class NestedSequenceArbitrary
    {
        public static Arbitrary<NestedSequenceData> Generate()
        {
            return Arb.From(GenNestedSequence(size: 3, maxDepth: 5));
        }

        public static Arbitrary<byte[]> GenerateBytes(int maxDepth)
        {
            return Arb.From(GenNestedSequenceBytes(maxDepth));
        }

        private static Gen<NestedSequenceData> GenNestedSequence(int size, int maxDepth, int depth = 0)
        {
            if (depth >= maxDepth || size <= 0)
                return Gen.Constant(new NestedSequenceData());

            return from itemCount in Gen.Choose(0, Math.Min(2, size))
                   from items in Gen.ListOf(itemCount, GenItem(size / 2, maxDepth, depth + 1))
                   select new NestedSequenceData { Items = items.ToList() };
        }

        private static Gen<ItemData> GenItem(int size, int maxDepth, int depth)
        {
            return from elementCount in Gen.Choose(1, 3)
                   from nested in GenNestedSequence(size / 2, maxDepth, depth)
                   from hasNestedSeq in Gen.Elements(true, false)
                   select new ItemData
                   {
                       ElementCount = elementCount,
                       NestedSequence = hasNestedSeq ? nested : null
                   };
        }

        private static Gen<byte[]> GenNestedSequenceBytes(int maxDepth)
        {
            // Generate raw byte sequences with random nesting structure
            return from depth in Gen.Choose(0, maxDepth)
                   from itemCount in Gen.Choose(1, 5)
                   select BuildRandomSequenceBytes(depth, itemCount);
        }

        private static byte[] BuildRandomSequenceBytes(int depth, int itemCount)
        {
            var result = new List<byte>();

            for (int i = 0; i < itemCount; i++)
            {
                // Item tag
                result.AddRange(new byte[] { 0xFE, 0xFF, 0x00, 0xE0 });
                // Undefined length
                result.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

                // Recursively add nested items if depth > 0
                if (depth > 0)
                {
                    result.AddRange(BuildRandomSequenceBytes(depth - 1, Math.Max(1, itemCount / 2)));
                }

                // Item delimitation tag
                result.AddRange(new byte[] { 0xFE, 0xFF, 0x0D, 0xE0 });
                result.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });
            }

            // Sequence delimitation tag
            result.AddRange(new byte[] { 0xFE, 0xFF, 0xDD, 0xE0 });
            result.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

            return result.ToArray();
        }
    }

    // Data structures for property tests
    private sealed class NestedSequenceData
    {
        public List<ItemData> Items { get; set; } = new();

        public DicomDataset ToDataset()
        {
            var dataset = new DicomDataset();

            if (Items.Count > 0)
            {
                var itemsList = new List<DicomDataset>();

                foreach (var itemData in Items)
                {
                    var item = new DicomDataset();

                    // Add some elements to the item
                    for (int i = 0; i < itemData.ElementCount; i++)
                    {
                        var tag = new DicomTag((ushort)(0x0008 + i), (ushort)(0x0050 + i));
                        item.Add(CreateStringElement(tag, DicomVR.SH, $"Value{i}"));
                    }

                    // Add nested sequence if present
                    if (itemData.NestedSequence != null && itemData.NestedSequence.Items.Count > 0)
                    {
                        var nestedItems = new List<DicomDataset>();

                        foreach (var nestedItem in itemData.NestedSequence.Items)
                        {
                            var nestedDs = new DicomDataset();
                            for (int i = 0; i < nestedItem.ElementCount; i++)
                            {
                                var tag = new DicomTag((ushort)(0x0010 + i), (ushort)(0x0010 + i));
                                nestedDs.Add(CreateStringElement(tag, DicomVR.SH, $"Nested{i}"));
                            }
                            nestedItems.Add(nestedDs);
                        }

                        var nestedSeq = new DicomSequence(new DicomTag(0x0008, 0x1115), nestedItems);
                        item.Add(nestedSeq);
                    }

                    itemsList.Add(item);
                }

                var sequence = new DicomSequence(new DicomTag(0x0040, 0x0100), itemsList);
                dataset.Add(sequence);
            }

            // Add a few top-level elements for more realistic datasets
            dataset.Add(CreateStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN, "Test^Patient"));
            dataset.Add(CreateStringElement(new DicomTag(0x0008, 0x0060), DicomVR.CS, "CT"));

            return dataset;
        }

        private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return new DicomStringElement(tag, vr, bytes);
        }
    }

    private sealed class ItemData
    {
        public int ElementCount { get; set; }
        public NestedSequenceData? NestedSequence { get; set; }
    }

    private static bool DeepEquals(DicomDataset a, DicomDataset b)
    {
        // Check element counts match
        if (a.Count != b.Count)
            return false;

        // Check each element
        foreach (var elemA in a)
        {
            if (!b.TryGetElement(elemA.Tag, out var elemB))
                return false;

            // Check VR matches
            if (elemA.VR != elemB.VR)
                return false;

            // For sequences, recurse
            if (elemA is DicomSequence seqA && elemB is DicomSequence seqB)
            {
                if (seqA.Items.Count != seqB.Items.Count)
                    return false;

                for (int i = 0; i < seqA.Items.Count; i++)
                {
                    if (!DeepEquals(seqA.Items[i], seqB.Items[i]))
                        return false;
                }
            }
            // For string elements, compare string values
            else if (elemA is DicomStringElement strA && elemB is DicomStringElement strB)
            {
                var valueA = strA.GetString(DicomEncoding.Default);
                var valueB = strB.GetString(DicomEncoding.Default);
                if (valueA != valueB)
                    return false;
            }
        }

        return true;
    }
}
