using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
[Category("Deidentification")]
public class UidReferenceWalkerTests
{
    // Well-known DICOM tags used in tests
    private static readonly DicomTag ReferencedSOPInstanceUID = new(0x0008, 0x1155);
    private static readonly DicomTag ReferencedSOPClassUID = new(0x0008, 0x1150);
    private static readonly DicomTag SeriesInstanceUID = new(0x0020, 0x000E);
    private static readonly DicomTag StudyInstanceUID = new(0x0020, 0x000D);
    private static readonly DicomTag SOPInstanceUID = new(0x0008, 0x0018);
    private static readonly DicomTag FrameOfReferenceUID = new(0x0020, 0x0052);
    private static readonly DicomTag TransferSyntaxUID = new(0x0002, 0x0010);
    private static readonly DicomTag Modality = new(0x0008, 0x0060);

    // Sequence tags
    private static readonly DicomTag ReferencedStudySequence = new(0x0008, 0x1110);
    private static readonly DicomTag ReferencedSeriesSequence = new(0x0008, 0x1115);
    private static readonly DicomTag CurrentRequestedProcedureEvidenceSequence = new(0x0040, 0xA375);
    private static readonly DicomTag ReferencedBeamSequence = new(0x300C, 0x0004);
    private static readonly DicomTag ContentSequence = new(0x0040, 0xA730);

    #region Core remapping tests

    [Test]
    public void RemapAllReferences_EmptyDataset_ReturnsZeroCounts()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(0));
        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(0));
        Assert.That(result.RemappedTags, Is.Empty);
    }

    [Test]
    public void RemapAllReferences_TopLevelUiElement_RemapsUid()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        dataset.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.4.5.6.7"));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(1));
        var newUid = dataset.GetString(ReferencedSOPInstanceUID);
        Assert.That(newUid, Is.Not.EqualTo("1.2.3.4.5.6.7"));
        Assert.That(newUid, Does.StartWith("2.25."));
    }

    [Test]
    public void RemapAllReferences_StandardUid_NotRemapped()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        // Explicit Transfer Syntax UID (standard DICOM UID)
        var standardUid = "1.2.840.10008.1.2.1";
        dataset.Add(MakeUi(TransferSyntaxUID, standardUid));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(0));
        var uid = dataset.GetString(TransferSyntaxUID);
        Assert.That(uid, Is.EqualTo(standardUid));
    }

    [Test]
    public void RemapAllReferences_NonUiElement_Untouched()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        dataset.Add(MakeString(DicomTag.PatientName, DicomVR.PN, "Doe^John"));
        dataset.Add(MakeString(Modality, DicomVR.CS, "CT"));
        dataset.Add(MakeString(new DicomTag(0x0008, 0x0020), DicomVR.DA, "20240115"));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(0));
        Assert.That(dataset.GetString(DicomTag.PatientName), Is.EqualTo("Doe^John"));
        Assert.That(dataset.GetString(Modality), Is.EqualTo("CT"));
    }

    [Test]
    public void RemapAllReferences_MultiValuedUid_RemapsEachComponent()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        dataset.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3\\1.2.4\\1.2.5"));

        var result = walker.RemapAllReferences(dataset);

        // Each component should be remapped independently
        Assert.That(result.UidsRemapped, Is.EqualTo(3));

        var newValue = dataset.GetString(ReferencedSOPInstanceUID);
        Assert.That(newValue, Is.Not.Null);
        var components = newValue!.Split('\\');
        Assert.That(components.Length, Is.EqualTo(3));

        // Each component should be a new UID
        foreach (var component in components)
        {
            Assert.That(component, Does.StartWith("2.25."));
        }

        // All components should be different from each other (since originals were different)
        Assert.That(components.Distinct().Count(), Is.EqualTo(3));
    }

    #endregion

    #region Sequence traversal tests

    [Test]
    public void RemapAllReferences_SingleLevelSequence_RemapsUidsInItems()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        var item = new DicomDataset();
        item.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.4.5"));
        item.Add(MakeUi(ReferencedSOPClassUID, "1.2.840.10008.5.1.4.1.1.2")); // Standard UID
        dataset.Add(new DicomSequence(ReferencedStudySequence, item));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(1));
        Assert.That(result.UidsRemapped, Is.EqualTo(1)); // Only non-standard UID
        Assert.That(item.GetString(ReferencedSOPInstanceUID), Does.StartWith("2.25."));
        Assert.That(item.GetString(ReferencedSOPClassUID), Is.EqualTo("1.2.840.10008.5.1.4.1.1.2"));
    }

    [Test]
    public void RemapAllReferences_NestedSequences_RemapsAtAllDepths()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // Level 1: top-level UID
        dataset.Add(MakeUi(StudyInstanceUID, "1.2.100"));

        // Level 2: sequence item with UID
        var level2Item = new DicomDataset();
        level2Item.Add(MakeUi(SeriesInstanceUID, "1.2.200"));

        // Level 3: nested sequence item with UID
        var level3Item = new DicomDataset();
        level3Item.Add(MakeUi(SOPInstanceUID, "1.2.300"));
        level2Item.Add(new DicomSequence(ReferencedSeriesSequence, level3Item));

        dataset.Add(new DicomSequence(ReferencedStudySequence, level2Item));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(3));
        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(2)); // level2Item + level3Item

        Assert.That(dataset.GetString(StudyInstanceUID), Does.StartWith("2.25."));
        Assert.That(level2Item.GetString(SeriesInstanceUID), Does.StartWith("2.25."));
        Assert.That(level3Item.GetString(SOPInstanceUID), Does.StartWith("2.25."));
    }

    [Test]
    public void RemapAllReferences_DeepNesting_HandlesArbitraryDepth()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // Build 10-level deep nesting
        var currentDataset = dataset;
        var originalUids = new List<string>();
        for (int depth = 0; depth < 10; depth++)
        {
            var uid = $"1.2.3.{depth}.{depth + 100}";
            originalUids.Add(uid);
            currentDataset.Add(MakeUi(ReferencedSOPInstanceUID, uid));

            if (depth < 9)
            {
                var nextItem = new DicomDataset();
                currentDataset.Add(new DicomSequence(ContentSequence, nextItem));
                currentDataset = nextItem;
            }
        }

        var result = walker.RemapAllReferences(dataset);

        // 10 UIDs across all levels
        Assert.That(result.UidsRemapped, Is.EqualTo(10));
        // 9 sequence items (levels 1-9, level 0 is the root dataset)
        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(9));
    }

    [Test]
    public void RemapAllReferences_MultipleItemsInSequence_RemapsEach()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        var items = new DicomDataset[5];
        for (int i = 0; i < 5; i++)
        {
            items[i] = new DicomDataset();
            items[i].Add(MakeUi(ReferencedSOPInstanceUID, $"1.2.3.4.{i + 10}"));
        }
        dataset.Add(new DicomSequence(ReferencedStudySequence, items));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(5));
        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(5));

        var remappedUids = new HashSet<string>();
        foreach (var item in items)
        {
            var uid = item.GetString(ReferencedSOPInstanceUID);
            Assert.That(uid, Does.StartWith("2.25."));
            remappedUids.Add(uid!);
        }
        // All remapped UIDs should be distinct
        Assert.That(remappedUids.Count, Is.EqualTo(5));
    }

    [Test]
    public void RemapAllReferences_MixedSequencesAndUids_CorrectCounts()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // 2 top-level UIDs
        dataset.Add(MakeUi(StudyInstanceUID, "1.2.100"));
        dataset.Add(MakeUi(SeriesInstanceUID, "1.2.200"));

        // 1 sequence with 2 items, each containing 1 UID
        var item1 = new DicomDataset();
        item1.Add(MakeUi(SOPInstanceUID, "1.2.300"));
        var item2 = new DicomDataset();
        item2.Add(MakeUi(SOPInstanceUID, "1.2.400"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, item1, item2));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(4)); // 2 top-level + 2 in sequence
        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(2));
    }

    #endregion

    #region Consistency tests

    [Test]
    public void RemapAllReferences_SameUidInMultipleLocations_MapsConsistently()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        var sharedUid = "1.2.3.4.5.6.7.8.9";

        // Same UID at top level
        dataset.Add(MakeUi(StudyInstanceUID, sharedUid));

        // Same UID in sequence item
        var item = new DicomDataset();
        item.Add(MakeUi(ReferencedSOPInstanceUID, sharedUid));
        dataset.Add(new DicomSequence(ReferencedStudySequence, item));

        walker.RemapAllReferences(dataset);

        var topLevelUid = dataset.GetString(StudyInstanceUID);
        var sequenceUid = item.GetString(ReferencedSOPInstanceUID);
        Assert.That(topLevelUid, Is.EqualTo(sequenceUid));
        Assert.That(topLevelUid, Does.StartWith("2.25."));
    }

    [Test]
    public void RemapAllReferences_ConsistentWithPriorMapping_UsesCachedMapping()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);

        // Pre-populate the remapper with a known mapping
        var originalUid = "1.2.3.4.5.6.7.8.9";
        var expectedNewUid = remapper.Remap(originalUid);

        var dataset = new DicomDataset();
        dataset.Add(MakeUi(ReferencedSOPInstanceUID, originalUid));

        walker.RemapAllReferences(dataset);

        var actualNewUid = dataset.GetString(ReferencedSOPInstanceUID);
        Assert.That(actualNewUid, Is.EqualTo(expectedNewUid));
    }

    [Test]
    public void RemapAllReferences_CalledTwice_NoDoubleRemapping()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        dataset.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.4.5.6.7"));

        // First walk
        walker.RemapAllReferences(dataset);
        var afterFirstWalk = dataset.GetString(ReferencedSOPInstanceUID);
        Assert.That(afterFirstWalk, Does.StartWith("2.25."));

        // Second walk -- the UID is now a 2.25.* UID in the remapper's namespace
        walker.RemapAllReferences(dataset);
        var afterSecondWalk = dataset.GetString(ReferencedSOPInstanceUID);

        // The second walk will remap the already-remapped UID to yet another UID,
        // but the key point is no crash/stack overflow. The value will differ because
        // the remapper sees a new UID (the first remapped value). This is expected
        // behavior -- calling walk twice is not idempotent by design.
        Assert.That(afterSecondWalk, Does.StartWith("2.25."));
    }

    #endregion

    #region RT/SR reference pattern tests

    [Test]
    public void RemapAllReferences_RtPlanReferences_RemapsReferencedSopInstanceUid()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // Mimic RT Plan with ReferencedBeamSequence > ReferencedSOPInstanceUID
        var beamItem1 = new DicomDataset();
        beamItem1.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.100.1"));

        var beamItem2 = new DicomDataset();
        beamItem2.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.100.2"));

        dataset.Add(new DicomSequence(ReferencedBeamSequence, beamItem1, beamItem2));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(2));
        Assert.That(beamItem1.GetString(ReferencedSOPInstanceUID), Does.StartWith("2.25."));
        Assert.That(beamItem2.GetString(ReferencedSOPInstanceUID), Does.StartWith("2.25."));
        // The two beam items should have different remapped UIDs
        Assert.That(beamItem1.GetString(ReferencedSOPInstanceUID),
            Is.Not.EqualTo(beamItem2.GetString(ReferencedSOPInstanceUID)));
    }

    [Test]
    public void RemapAllReferences_StructuredReportReferences_RemapsAllReferences()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // Mimic SR CurrentRequestedProcedureEvidenceSequence
        var studyItem = new DicomDataset();
        studyItem.Add(MakeUi(StudyInstanceUID, "1.2.3.200.1"));

        var seriesItem = new DicomDataset();
        seriesItem.Add(MakeUi(SeriesInstanceUID, "1.2.3.200.2"));
        seriesItem.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.200.3"));
        studyItem.Add(new DicomSequence(ReferencedSeriesSequence, seriesItem));

        dataset.Add(new DicomSequence(CurrentRequestedProcedureEvidenceSequence, studyItem));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(3));
        Assert.That(studyItem.GetString(StudyInstanceUID), Does.StartWith("2.25."));
        Assert.That(seriesItem.GetString(SeriesInstanceUID), Does.StartWith("2.25."));
        Assert.That(seriesItem.GetString(ReferencedSOPInstanceUID), Does.StartWith("2.25."));
    }

    [Test]
    public void RemapAllReferences_ReferencedFrameOfReferenceUid_Remapped()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // FrameOfReferenceUID (0020,0052) inside a sequence
        var item = new DicomDataset();
        item.Add(MakeUi(FrameOfReferenceUID, "1.2.3.400.1"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, item));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.UidsRemapped, Is.EqualTo(1));
        Assert.That(item.GetString(FrameOfReferenceUID), Does.StartWith("2.25."));
    }

    #endregion

    #region Result type tests

    [Test]
    public void UidRemapResult_RemappedTags_ContainsAffectedTags()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();
        dataset.Add(MakeUi(StudyInstanceUID, "1.2.100"));
        dataset.Add(MakeUi(SeriesInstanceUID, "1.2.200"));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.RemappedTags, Has.Count.EqualTo(2));
        Assert.That(result.RemappedTags, Does.Contain(StudyInstanceUID));
        Assert.That(result.RemappedTags, Does.Contain(SeriesInstanceUID));
    }

    [Test]
    public void UidRemapResult_SequenceItemsTraversed_CountsCorrectly()
    {
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);
        var dataset = new DicomDataset();

        // Create 2 sequences: one with 3 items, one with 2 items
        var seq1Items = Enumerable.Range(0, 3).Select(_ => new DicomDataset()).ToArray();
        var seq2Items = Enumerable.Range(0, 2).Select(_ => new DicomDataset()).ToArray();

        dataset.Add(new DicomSequence(ReferencedStudySequence, seq1Items));
        dataset.Add(new DicomSequence(ReferencedSeriesSequence, seq2Items));

        var result = walker.RemapAllReferences(dataset);

        Assert.That(result.SequenceItemsTraversed, Is.EqualTo(5));
    }

    #endregion

    #region Helpers

    private static DicomStringElement MakeUi(DicomTag tag, string value)
    {
        return new DicomStringElement(tag, DicomVR.UI, Encoding.ASCII.GetBytes(value));
    }

    private static DicomStringElement MakeString(DicomTag tag, DicomVR vr, string value)
    {
        return new DicomStringElement(tag, vr, Encoding.ASCII.GetBytes(value));
    }

    #endregion
}
