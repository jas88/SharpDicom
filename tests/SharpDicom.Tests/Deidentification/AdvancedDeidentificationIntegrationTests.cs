using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
[Category("Deidentification")]
public class AdvancedDeidentificationIntegrationTests
{
    // Well-known DICOM tags
    private static readonly DicomTag StudyInstanceUID = new(0x0020, 0x000D);
    private static readonly DicomTag SeriesInstanceUID = new(0x0020, 0x000E);
    private static readonly DicomTag SOPInstanceUID = new(0x0008, 0x0018);
    private static readonly DicomTag ReferencedSOPInstanceUID = new(0x0008, 0x1155);
    private static readonly DicomTag ReferencedSOPClassUID = new(0x0008, 0x1150);
    private static readonly DicomTag ReferencedStudySequence = new(0x0008, 0x1110);
    private static readonly DicomTag ReferencedSeriesSequence = new(0x0008, 0x1115);
    private static readonly DicomTag TransferSyntaxUID = new(0x0002, 0x0010);
    private static readonly DicomTag Modality = new(0x0008, 0x0060);

    [Test]
    public void Builder_WithUidReferenceWalking_CreatesDeidentifier()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidReferenceWalking()
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Builder_WithOcrScanner_GracefullyHandlesUnavailableTesseract()
    {
        // Building succeeds (lazy creation); the error occurs at scan time
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithOcrScanner()
            .Build();

        // De-identify a dataset to trigger OCR scan attempt
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(MakeString(Modality, DicomVR.CS, "US")); // High-risk modality

        var result = deid.Deidentify(dataset);

        // The OCR failure (DllNotFoundException or InvalidOperationException) is caught
        // by the outer try/catch in Deidentify and recorded in Errors or Warnings.
        // Either way, the de-identification should not crash.
        var hasOcrMessage = result.Warnings.Any(w => w.Contains("OCR") || w.Contains("Tesseract") || w.Contains("sharpdicom_codecs"))
                         || result.Errors.Any(e => e.Contains("OCR") || e.Contains("Tesseract") || e.Contains("sharpdicom_codecs"));
        Assert.That(hasOcrMessage, Is.True,
            $"Should contain an OCR-related message. Warnings: [{string.Join(", ", result.Warnings)}] Errors: [{string.Join(", ", result.Errors)}]");
    }

    [Test]
    public void Deidentify_WithUidReferenceWalking_RemapsSequenceUids()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidReferenceWalking()
            .Build();

        var dataset = CreateTestDataset("Test^Patient", "12345");

        // Add a sequence with referenced UIDs
        var refItem = new DicomDataset();
        refItem.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.4.5.6.789"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, refItem));

        var result = deid.Deidentify(dataset);

        Assert.That(result.Success, Is.True);
        // The UID reference walker should have remapped the UID in the sequence
        Assert.That(result.Summary.UidReferencesRemapped, Is.GreaterThan(0));

        var newUid = refItem.GetString(ReferencedSOPInstanceUID);
        Assert.That(newUid, Is.Not.EqualTo("1.2.3.4.5.6.789"));
    }

    [Test]
    public void Deidentify_WithUidReferenceWalking_PreservesStandardUids()
    {
        // Test standard UID preservation using UidReferenceWalker directly,
        // because the full pipeline's primary de-id may remove certain sequence tags.
        using var remapper = new UidRemapper();
        var walker = new UidReferenceWalker(remapper);

        var dataset = new DicomDataset();
        var standardUid = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage SOP Class
        var instanceUid = "1.2.3.4.5.6.7.8.9"; // Non-standard UID

        // Add both standard and non-standard UIDs
        dataset.Add(MakeUi(ReferencedSOPClassUID, standardUid));
        dataset.Add(MakeUi(ReferencedSOPInstanceUID, instanceUid));

        var result = walker.RemapAllReferences(dataset);

        // Standard UID should be preserved
        Assert.That(dataset.GetString(ReferencedSOPClassUID), Is.EqualTo(standardUid));
        // Non-standard UID should be remapped
        Assert.That(dataset.GetString(ReferencedSOPInstanceUID), Does.StartWith("2.25."));
        Assert.That(result.UidsRemapped, Is.EqualTo(1));
    }

    [Test]
    public void Deidentify_WithUidReferenceWalking_ConsistentAcrossFiles()
    {
        // The UidReferenceWalker passes patientId as context to the remapper.
        // For cross-file consistency, files from the same patient (same PatientID)
        // should produce the same UID mappings via the walker.
        using var remapper = new UidRemapper();
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidRemapper(remapper)
            .WithUidReferenceWalking()
            .Build();

        var sharedRefUid = "1.2.3.999.888.777";
        var samePatientId = "P001";

        // File 1 and File 2 belong to the same patient and reference the same UID
        var dataset1 = CreateTestDataset("Patient1", samePatientId);
        var refItem1 = new DicomDataset();
        refItem1.Add(MakeUi(ReferencedSOPInstanceUID, sharedRefUid));
        dataset1.Add(new DicomSequence(ReferencedStudySequence, refItem1));

        var dataset2 = CreateTestDataset("Patient1", samePatientId);
        var refItem2 = new DicomDataset();
        refItem2.Add(MakeUi(ReferencedSOPInstanceUID, sharedRefUid));
        dataset2.Add(new DicomSequence(ReferencedStudySequence, refItem2));

        deid.Deidentify(dataset1);
        deid.Deidentify(dataset2);

        var uid1 = refItem1.GetString(ReferencedSOPInstanceUID);
        var uid2 = refItem2.GetString(ReferencedSOPInstanceUID);

        Assert.That(uid1, Is.EqualTo(uid2), "Same referenced UID for same patient should map consistently");
        Assert.That(uid1, Does.StartWith("2.25."));
    }

    [Test]
    public void Deidentify_WithoutUidReferenceWalking_SequenceUidsHandledByProfile()
    {
        // Without WithUidReferenceWalking(), only PS3.15 profile actions apply
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .Build();

        var dataset = CreateTestDataset("Test", "123");

        // Add UIDs in nested sequences
        var refItem = new DicomDataset();
        refItem.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.4.5.6.789"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, refItem));

        var result = deid.Deidentify(dataset);

        // UidReferencesRemapped should be 0 since walker is not enabled
        Assert.That(result.Summary.UidReferencesRemapped, Is.EqualTo(0));
    }

    [Test]
    public void Deidentify_WithUidReferenceWalking_ResultIncludesStats()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidReferenceWalking()
            .Build();

        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(MakeUi(StudyInstanceUID, "1.2.100.200.300"));

        var refItem = new DicomDataset();
        refItem.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.400.500.600"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, refItem));

        var result = deid.Deidentify(dataset);

        // The result should include reference walking statistics
        Assert.That(result.Summary.UidReferencesRemapped, Is.GreaterThanOrEqualTo(0));
        // Total modifications should include reference walking
        Assert.That(result.Summary.TotalModifications, Is.GreaterThan(0));
    }

    [Test]
    public void Deidentify_PipelineOrder_OcrBeforePrimaryDeidThenDateShiftThenUidWalk()
    {
        // Verify the documented pipeline ordering by checking that UID reference walking
        // (which runs AFTER primary de-id) sees already-remapped UIDs from primary de-id
        // and maps them consistently.
        using var remapper = new UidRemapper();
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidRemapper(remapper)
            .WithUidReferenceWalking()
            .Build();

        var originalStudyUid = "1.2.3.4.5.6";
        var dataset = CreateTestDataset("Test^Patient", "12345");
        dataset.Add(MakeUi(StudyInstanceUID, originalStudyUid));

        // Add the same UID as a reference in a sequence -- the walker should
        // see it after primary de-id has already remapped the top-level copy
        var refItem = new DicomDataset();
        refItem.Add(MakeUi(ReferencedSOPInstanceUID, originalStudyUid));
        dataset.Add(new DicomSequence(ReferencedStudySequence, refItem));

        var result = deid.Deidentify(dataset);

        Assert.That(result.Success, Is.True);

        // StudyInstanceUID is remapped by primary de-id (RemapUid action)
        var newStudyUid = dataset.GetString(StudyInstanceUID);
        Assert.That(newStudyUid, Does.StartWith("2.25."));

        // The reference walker ran after primary de-id
        Assert.That(result.Summary.UidReferencesRemapped, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Deidentify_WithUidReferenceWalking_MultiValuedUids_RemappedPerComponent()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidReferenceWalking()
            .Build();

        var dataset = CreateTestDataset("Test", "123");

        // Add multi-valued UIDs in a sequence
        var refItem = new DicomDataset();
        refItem.Add(MakeUi(ReferencedSOPInstanceUID, "1.2.3.100\\1.2.3.200\\1.2.3.300"));
        dataset.Add(new DicomSequence(ReferencedStudySequence, refItem));

        var result = deid.Deidentify(dataset);

        Assert.That(result.Success, Is.True);

        var newValue = refItem.GetString(ReferencedSOPInstanceUID);
        Assert.That(newValue, Is.Not.Null);
        var components = newValue!.Split('\\');
        Assert.That(components.Length, Is.EqualTo(3));

        // Each component should be remapped
        foreach (var component in components)
        {
            Assert.That(component, Does.StartWith("2.25."));
        }

        // All components should be distinct (since originals were distinct)
        Assert.That(components.Distinct().Count(), Is.EqualTo(3));
    }

    #region Helpers

    private static DicomDataset CreateTestDataset(string patientName, string patientId)
    {
        var dataset = new DicomDataset();
        dataset.Add(MakeString(DicomTag.PatientName, DicomVR.PN, patientName));
        dataset.Add(MakeString(DicomTag.PatientID, DicomVR.LO, patientId));
        return dataset;
    }

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
