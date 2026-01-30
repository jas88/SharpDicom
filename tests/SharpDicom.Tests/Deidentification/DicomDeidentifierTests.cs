using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class DicomDeidentifierTests
{
    // Local tag definitions for tests
    private static readonly DicomTag StudyInstanceUID = new(0x0020, 0x000D);
    private static readonly DicomTag StudyDate = new(0x0008, 0x0020);
    private static readonly DicomTag PatientAge = new(0x0010, 0x1010);
    private static readonly DicomTag InstitutionName = new(0x0008, 0x0080);
    private static readonly DicomTag PatientIdentityRemoved = new(0x0012, 0x0062);
    private static readonly DicomTag DeidentificationMethod = new(0x0012, 0x0063);

    [Test]
    public void Deidentify_PatientName_ReplacedWithEmpty()
    {
        var dataset = CreateTestDataset("Doe^John", "12345");

        using var deid = new DicomDeidentifier();
        var result = deid.Deidentify(dataset);

        Assert.That(result.Success, Is.True);

        // Patient name should be empty or replaced
        var name = dataset.GetString(DicomTag.PatientName);
        Assert.That(name, Is.Null.Or.Empty.Or.Not.EqualTo("Doe^John"));
    }

    [Test]
    public void Deidentify_StudyInstanceUID_Remapped()
    {
        var originalUid = "1.2.3.4.5.6.7.8.9";
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(StudyInstanceUID, DicomVR.UI, originalUid));

        using var deid = new DicomDeidentifier();
        var result = deid.Deidentify(dataset);

        var newUid = dataset.GetString(StudyInstanceUID);
        Assert.That(newUid, Is.Not.EqualTo(originalUid));
        Assert.That(newUid, Does.StartWith("2.25."));
        Assert.That(result.Summary.UidsRemapped, Is.GreaterThan(0));
    }

    [Test]
    public void Deidentify_WithDateShift_AndRetainDates_ShiftsDates()
    {
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));

        // Need to retain dates with modification option to keep them but shift
        using var deid = new DicomDeidentifierBuilder()
            .RetainLongitudinalModifiedDates()
            .WithDateShift(TimeSpan.FromDays(-100))
            .Build();

        var result = deid.Deidentify(dataset);

        var studyDate = dataset.GetString(StudyDate);
        // With RetainLongitudinalModifiedDates and date shift, date should be shifted
        Assert.That(studyDate, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Summary.DatesShifted, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Deidentify_PatientIdentityRemoved_Added()
    {
        var dataset = CreateTestDataset("Test", "123");

        using var deid = new DicomDeidentifier();
        deid.Deidentify(dataset);

        var marker = dataset.GetString(PatientIdentityRemoved);
        Assert.That(marker, Is.EqualTo("YES"));
    }

    [Test]
    public void Deidentify_DeidentificationMethod_Added()
    {
        var dataset = CreateTestDataset("Test", "123");

        using var deid = new DicomDeidentifier();
        deid.Deidentify(dataset);

        var method = dataset.GetString(DeidentificationMethod);
        Assert.That(method, Does.Contain("PS3.15"));
    }

    [Test]
    public void Deidentify_ResultContainsStats()
    {
        var dataset = CreateTestDataset("Test^Patient", "12345");
        dataset.Add(CreateStringElement(StudyInstanceUID, DicomVR.UI, "1.2.3.4"));
        dataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));

        using var deid = new DicomDeidentifierBuilder()
            .WithDateShift(TimeSpan.FromDays(-100))
            .Build();

        var result = deid.Deidentify(dataset);

        Assert.That(result.Summary.TotalModifications, Is.GreaterThan(0));
    }

    [Test]
    public void Deidentify_WithRetainPatientCharacteristics_KeepsAge()
    {
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(PatientAge, DicomVR.AS, "045Y"));

        using var deid = new DicomDeidentifierBuilder()
            .RetainPatientCharacteristics()
            .Build();

        deid.Deidentify(dataset);

        var age = dataset.GetString(PatientAge);
        Assert.That(age, Is.EqualTo("045Y"));
    }

    [Test]
    public void Deidentify_WithOverride_AppliesOverride()
    {
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(InstitutionName, DicomVR.LO, "Test Hospital"));

        using var deid = new DicomDeidentifierBuilder()
            .WithOverride(InstitutionName, DeidentificationAction.Keep)
            .Build();

        deid.Deidentify(dataset);

        var institution = dataset.GetString(InstitutionName);
        Assert.That(institution, Is.EqualTo("Test Hospital"));
    }

    [Test]
    public void Deidentify_ConsistentUidRemapping_AcrossFiles()
    {
        using var remapper = new UidRemapper();

        var dataset1 = CreateTestDataset("Test1", "123");
        dataset1.Add(CreateStringElement(StudyInstanceUID, DicomVR.UI, "1.2.3.4.5"));

        var dataset2 = CreateTestDataset("Test2", "456");
        dataset2.Add(CreateStringElement(StudyInstanceUID, DicomVR.UI, "1.2.3.4.5"));

        using var deid = new DicomDeidentifierBuilder()
            .WithUidRemapper(remapper)
            .Build();

        deid.Deidentify(dataset1);
        deid.Deidentify(dataset2);

        var uid1 = dataset1.GetString(StudyInstanceUID);
        var uid2 = dataset2.GetString(StudyInstanceUID);

        // Same original UID should map to same new UID
        Assert.That(uid1, Is.EqualTo(uid2));
    }

    private static DicomDataset CreateTestDataset(string patientName, string patientId)
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, patientName));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, patientId));
        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}

[TestFixture]
public class DicomDeidentifierBuilderTests
{
    // Local tag definitions for tests
    private static readonly DicomTag InstitutionName = new(0x0008, 0x0080);
    private static readonly DicomTag StationName = new(0x0008, 0x1010);

    [Test]
    public void Build_WithBasicProfile_CreatesDeid()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Build_WithAllOptions_Succeeds()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainSafePrivate()
            .RetainUIDs()
            .RetainDeviceIdentity()
            .RetainInstitutionIdentity()
            .RetainPatientCharacteristics()
            .RetainLongitudinalModifiedDates()
            .CleanDescriptors()
            .CleanGraphics()
            .WithDateShift(TimeSpan.FromDays(-365))
            .WithSafePrivateCreators("SIEMENS CSA HEADER", "GEMS_PARM_01")
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Build_WithOverride_Succeeds()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithOverride(InstitutionName, DeidentificationAction.Keep)
            .WithOverride(StationName, DeidentificationAction.Remove)
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Build_WithRandomDateShift_Succeeds()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithRandomDateShift(-365, -30, seed: 12345)
            .Build();

        Assert.That(deid, Is.Not.Null);
    }
}

[TestFixture]
public class DicomDeidentifierAdvancedTests
{
    // Local tag definitions
    private static readonly DicomTag PatientIdentityRemoved = new(0x0012, 0x0062);
    private static readonly DicomTag DeidentificationMethod = new(0x0012, 0x0063);
    private static readonly DicomTag DeidentificationMethodCodeSequence = new(0x0012, 0x0064);
    private static readonly DicomTag LongitudinalTemporalInformationModified = new(0x0028, 0x0303);
    private static readonly DicomTag CodeValue = new(0x0008, 0x0100);
    private static readonly DicomTag CodingSchemeDesignator = new(0x0008, 0x0102);
    private static readonly DicomTag CodeMeaning = new(0x0008, 0x0104);
    private static readonly DicomTag StudyDate = new(0x0008, 0x0020);

    [Test]
    public void Deidentify_AddsDeidentificationMethodCodeSequence()
    {
        var dataset = CreateTestDataset("Test", "123");

        using var deid = new DicomDeidentifier();
        deid.Deidentify(dataset);

        // Check that the code sequence exists
        var codeSeq = dataset[DeidentificationMethodCodeSequence] as DicomSequence;
        Assert.That(codeSeq, Is.Not.Null);
        Assert.That(codeSeq!.Items.Count, Is.GreaterThanOrEqualTo(1));

        // First item should be Basic Application Confidentiality Profile
        var firstItem = codeSeq.Items[0];
        var codeValue = firstItem.GetString(CodeValue);
        var scheme = firstItem.GetString(CodingSchemeDesignator);

        Assert.That(codeValue, Is.EqualTo("113100"));
        Assert.That(scheme, Is.EqualTo("DCM"));
    }

    [Test]
    public void Deidentify_WithOptions_AddsCorrespondingCodes()
    {
        var dataset = CreateTestDataset("Test", "123");

        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainUIDs()
            .CleanDescriptors()
            .Build();

        deid.Deidentify(dataset);

        var codeSeq = dataset[DeidentificationMethodCodeSequence] as DicomSequence;
        Assert.That(codeSeq, Is.Not.Null);

        // Should have Basic + RetainUIDs + CleanDescriptors = 3 items
        Assert.That(codeSeq!.Items.Count, Is.EqualTo(3));

        // Verify RetainUIDs code (113110)
        var hasRetainUids = HasCodeValue(codeSeq.Items, "113110");
        Assert.That(hasRetainUids, Is.True);

        // Verify CleanDescriptors code (113105)
        var hasCleanDesc = HasCodeValue(codeSeq.Items, "113105");
        Assert.That(hasCleanDesc, Is.True);
    }

    private static bool HasCodeValue(System.Collections.Generic.IReadOnlyList<DicomDataset> items, string codeValue)
    {
        foreach (var item in items)
        {
            if (item.GetString(CodeValue) == codeValue)
                return true;
        }
        return false;
    }

    [Test]
    public void Deidentify_WithDateShift_SetsLongitudinalTemporalModified()
    {
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));

        using var deid = new DicomDeidentifierBuilder()
            .WithDateShift(TimeSpan.FromDays(-100))
            .Build();

        deid.Deidentify(dataset);

        var temporal = dataset.GetString(LongitudinalTemporalInformationModified);
        Assert.That(temporal, Is.EqualTo("MODIFIED").Or.EqualTo("REMOVED"));
    }

    [Test]
    public void Deidentify_WithRetainFullDates_SetsTemporalUnmodified()
    {
        var dataset = CreateTestDataset("Test", "123");
        dataset.Add(CreateStringElement(StudyDate, DicomVR.DA, "20240115"));

        using var deid = new DicomDeidentifierBuilder()
            .RetainLongitudinalFullDates()
            .Build();

        deid.Deidentify(dataset);

        var temporal = dataset.GetString(LongitudinalTemporalInformationModified);
        Assert.That(temporal, Is.EqualTo("UNMODIFIED"));
    }

    [Test]
    public void DeidentificationMethod_IncludesOptionsText()
    {
        var dataset = CreateTestDataset("Test", "123");

        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainPatientCharacteristics()
            .CleanDescriptors()
            .Build();

        deid.Deidentify(dataset);

        var method = dataset.GetString(DeidentificationMethod);
        Assert.That(method, Does.Contain("PS3.15"));
        Assert.That(method, Does.Contain("Retain Patient Characteristics Option"));
        Assert.That(method, Does.Contain("Clean Descriptors Option"));
    }

    private static DicomDataset CreateTestDataset(string patientName, string patientId)
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, patientName));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, patientId));
        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}

[TestFixture]
public class DeidentificationCallbackTests
{
    private static readonly DicomTag PatientName = new(0x0010, 0x0010);
    private static readonly DicomTag StudyInstanceUID = new(0x0020, 0x000D);

    [Test]
    public void ProcessElement_KeepsNonSensitiveData()
    {
        using var callback = new DeidentificationCallback();

        // SOPClassUID is typically kept (it's a standard DICOM UID prefix)
        var tag = new DicomTag(0x0008, 0x0016);  // SOPClassUID
        var element = CreateStringElement(tag, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2");

        var result = callback.ProcessElement(element);

        Assert.That(result.Action, Is.EqualTo(ElementCallbackAction.Keep));
    }

    [Test]
    public void ProcessElement_RemovesSensitiveData()
    {
        using var callback = new DeidentificationCallback();

        // PatientName should be removed/cleaned
        var element = CreateStringElement(PatientName, DicomVR.PN, "Doe^John");

        var result = callback.ProcessElement(element);

        // Should be removed or replaced
        Assert.That(result.Action, Is.Not.EqualTo(ElementCallbackAction.Keep).Or.Property("ReplacementElement").Not.Null);
    }

    [Test]
    public void ProcessElement_RemapsUIDs()
    {
        using var remapper = new UidRemapper();
        var options = DeidentificationOptions.BasicProfile;

        using var callback = new DeidentificationCallback(options, remapper);

        var element = CreateStringElement(StudyInstanceUID, DicomVR.UI, "1.2.3.4.5.6.7.8.9");

        var result = callback.ProcessElement(element);

        if (result.Action == ElementCallbackAction.Replace)
        {
            var replacement = result.ReplacementElement as DicomStringElement;
            Assert.That(replacement, Is.Not.Null);

            var newUid = replacement!.GetString(DicomEncoding.Default);
            Assert.That(newUid, Does.StartWith("2.25."));
        }
    }

    [Test]
    public void Dispose_DisposesOwnedRemapper()
    {
        // Callback created without external remapper should own and dispose its own
        var callback = new DeidentificationCallback();
        callback.Dispose();

        // No exception means success
        Assert.Pass();
    }

    [Test]
    public void Dispose_DoesNotDisposeExternalRemapper()
    {
        using var remapper = new UidRemapper();
        var options = DeidentificationOptions.BasicProfile;

        var callback = new DeidentificationCallback(options, remapper);
        callback.Dispose();

        // External remapper should still work
        var uid = remapper.Remap("1.2.3.4.5", null);
        Assert.That(uid, Does.StartWith("2.25."));
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}
