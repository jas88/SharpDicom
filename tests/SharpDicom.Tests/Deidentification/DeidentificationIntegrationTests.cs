using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// End-to-end integration tests for de-identification workflows.
/// </summary>
[TestFixture]
public class DeidentificationIntegrationTests
{
    private string _testDir = null!;
    private string _outputDir = null!;

    private static readonly DicomTag PatientIdentityRemoved = new(0x0012, 0x0062);
    private static readonly DicomTag DeidentificationMethodCodeSequence = new(0x0012, 0x0064);
    private static readonly DicomTag StudyDate = new(0x0008, 0x0020);
    private static readonly DicomTag SeriesDate = new(0x0008, 0x0021);
    private static readonly DicomTag AcquisitionDate = new(0x0008, 0x0022);
    private static readonly DicomTag PatientAge = new(0x0010, 0x1010);
    private static readonly DicomTag InstitutionName = new(0x0008, 0x0080);
    private static readonly DicomTag SOPClassUID = new(0x0008, 0x0016);

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deid-test-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(Path.GetTempPath(), $"deid-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    [Test]
    public void EndToEnd_BasicProfile_DeidentifiesPatientData()
    {
        var dataset = CreateTestDataset();

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .Build();

        var result = deidentifier.Deidentify(dataset);

        Assert.That(result.Success, Is.True);

        // Verify patient data removed/modified
        var name = dataset.GetString(DicomTag.PatientName);
        Assert.That(name, Is.Not.EqualTo("Doe^John").Or.Null.Or.Empty);

        // Verify de-identification tags present
        Assert.That(dataset.GetString(PatientIdentityRemoved), Is.EqualTo("YES"));
        Assert.That(dataset.Contains(DeidentificationMethodCodeSequence), Is.True);
    }

    [Test]
    public void EndToEnd_BasicProfile_RemapsUIDs()
    {
        var originalStudyUid = "1.2.3.4.5.6";
        var dataset = CreateTestDataset(originalStudyUid, "1.2.3.4.5.6.1");

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .Build();

        deidentifier.Deidentify(dataset);

        // UIDs should be remapped
        var newStudyUid = dataset.GetString(DicomTag.StudyInstanceUID);
        Assert.That(newStudyUid, Is.Not.EqualTo(originalStudyUid));
        Assert.That(newStudyUid, Does.StartWith("2.25."));
    }

    [Test]
    public void EndToEnd_UidConsistency_SameUidMapsSameResult()
    {
        var studyUid = "1.2.3.4.5.6";
        var dataset1 = CreateTestDataset(studyUid, "1.2.3.4.5.6.1");
        var dataset2 = CreateTestDataset(studyUid, "1.2.3.4.5.6.2");

        // Use shared remapper for consistency
        using var remapper = new UidRemapper();
        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithUidRemapper(remapper)
            .Build();

        deidentifier.Deidentify(dataset1);
        deidentifier.Deidentify(dataset2);

        // Same original study UID should map to same new UID
        var newStudyUid1 = dataset1.GetString(DicomTag.StudyInstanceUID);
        var newStudyUid2 = dataset2.GetString(DicomTag.StudyInstanceUID);

        Assert.That(newStudyUid1, Is.EqualTo(newStudyUid2));
        Assert.That(newStudyUid1, Is.Not.EqualTo(studyUid));

        // Different SOP Instance UIDs should be different
        var newSopUid1 = dataset1.GetString(DicomTag.SOPInstanceUID);
        var newSopUid2 = dataset2.GetString(DicomTag.SOPInstanceUID);

        Assert.That(newSopUid1, Is.Not.EqualTo(newSopUid2));
    }

    [Test]
    public void EndToEnd_WithDateShift_ModifiesDates()
    {
        var dataset = new DicomDataset
        {
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST001"),
            CreateStringElement(StudyDate, DicomVR.DA, "20240115")
        };

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainLongitudinalModifiedDates()
            .WithDateShift(TimeSpan.FromDays(-100))
            .Build();

        deidentifier.Deidentify(dataset);

        var studyDate = dataset.GetString(StudyDate);

        // After de-identification with date shift option, date should be modified
        // (either shifted or replaced with dummy depending on profile implementation)
        Assert.That(studyDate, Is.Not.EqualTo("20240115"));

        // Date should be a valid 8-character date string (YYYYMMDD format)
        Assert.That(studyDate, Has.Length.EqualTo(8));
    }

    [Test]
    public void EndToEnd_WithRetainPatientCharacteristics_KeepsAge()
    {
        var dataset = new DicomDataset
        {
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"),
            CreateStringElement(PatientAge, DicomVR.AS, "045Y"),
            CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient")
        };

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainPatientCharacteristics()
            .Build();

        deidentifier.Deidentify(dataset);

        // Age should be kept with RetainPatientCharacteristics option
        var age = dataset.GetString(PatientAge);
        Assert.That(age, Is.EqualTo("045Y"));
    }

    [Test]
    public void EndToEnd_WithOverride_AppliesOverride()
    {
        var dataset = new DicomDataset
        {
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"),
            CreateStringElement(InstitutionName, DicomVR.LO, "Test Hospital")
        };

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .WithOverride(InstitutionName, DeidentificationAction.Keep)
            .Build();

        deidentifier.Deidentify(dataset);

        // Institution should be kept due to override
        var institution = dataset.GetString(InstitutionName);
        Assert.That(institution, Is.EqualTo("Test Hospital"));
    }

#if NET6_0_OR_GREATER
    [Test]
    public async Task EndToEnd_Context_ExportsMappings()
    {
        var originalStudyUid = "1.2.3.4.5.6.7.8.9";

        var dataset = new DicomDataset
        {
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PATIENT123"),
            CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalStudyUid),
            CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Original^Name")
        };

        var dbPath = Path.Combine(_testDir, "mappings.db");
        await using var context = new DeidentificationContext(dbPath);

        using var deidentifier = context.CreateBuilder()
            .WithBasicProfile()
            .Build();

        deidentifier.Deidentify(dataset);

        var newStudyUid = dataset.GetString(DicomTag.StudyInstanceUID);

        // Export mappings
        var mappingsPath = Path.Combine(_outputDir, "mappings.json");
        await context.ExportMappingsAsync(mappingsPath);

        // Verify mapping file exists and contains expected data
        Assert.That(File.Exists(mappingsPath), Is.True);

        var mappingsJson = await File.ReadAllTextAsync(mappingsPath);
        Assert.That(mappingsJson, Does.Contain(originalStudyUid));
        Assert.That(mappingsJson, Does.Contain(newStudyUid!));

        // Verify bidirectional lookup
        Assert.That(context.UidStore.TryGetOriginal(newStudyUid!, out var lookup), Is.True);
        Assert.That(lookup, Is.EqualTo(originalStudyUid));
    }

    [Test]
    public void EndToEnd_ConfigPreset_LoadsCorrectly()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var builder = DeidentificationConfigLoader.CreateBuilder(config);
        using var deidentifier = builder.Build();

        var dataset = new DicomDataset
        {
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"),
            CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient")
        };

        var result = deidentifier.Deidentify(dataset);

        Assert.That(result.Success, Is.True);
        Assert.That(dataset.GetString(PatientIdentityRemoved), Is.EqualTo("YES"));
    }
#endif

    [Test]
    public void DeidentificationResult_TracksStatistics()
    {
        var dataset = CreateTestDataset("1.2.3.4.5.6", "1.2.3.4.5.6.1");

        using var deidentifier = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .Build();

        var result = deidentifier.Deidentify(dataset);

        Assert.That(result.Summary.TotalModifications, Is.GreaterThan(0));
        Assert.That(result.Summary.UidsRemapped, Is.GreaterThan(0));
    }

    private static DicomDataset CreateTestDataset(
        string? studyUid = null,
        string? sopUid = null)
    {
        return new DicomDataset
        {
            CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Doe^John"),
            CreateStringElement(DicomTag.PatientID, DicomVR.LO, "12345"),
            CreateStringElement(new DicomTag(0x0010, 0x0030), DicomVR.DA, "19800101"), // PatientBirthDate
            CreateStringElement(StudyDate, DicomVR.DA, "20240115"),
            CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid ?? "1.2.3.4.5"),
            CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.3.4.5.1"),
            CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, sopUid ?? "1.2.3.4.5.1.1"),
            CreateStringElement(SOPClassUID, DicomVR.UI, DicomUID.SecondaryCaptureImageStorage.ToString()),
            CreateStringElement(new DicomTag(0x0008, 0x0060), DicomVR.CS, "OT") // Modality
        };
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}
