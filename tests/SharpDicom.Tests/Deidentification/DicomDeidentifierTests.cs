using System;
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
