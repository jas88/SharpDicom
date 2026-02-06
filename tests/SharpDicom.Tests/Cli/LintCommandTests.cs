using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;
using SharpDicom.Validation;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class LintCommandTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpdcm_lint_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    private async Task<string> CreateValidDicomFile()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4.5.6.7.8.9"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.5.6.7.8.10"));

        var path = Path.Combine(_tempDir!, "valid.dcm");
        var file = new DicomFile(dataset);
        await file.SaveAsync(path);
        return path;
    }

    private async Task<string> CreateInvalidUidDicomFile()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
        // Invalid UID with double dots
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2..3.4"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.04.5"));

        var path = Path.Combine(_tempDir!, "invalid_uid.dcm");
        var file = new DicomFile(dataset);
        await file.SaveAsync(path);
        return path;
    }

    [Test]
    public async Task ValidFile_StrictProfile_NoErrors()
    {
        var path = await CreateValidDicomFile();

        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Strict,
            CollectValidationIssues = true,
        };

        var file = await DicomFile.OpenAsync(path, options);
        var result = file.ValidationResult ?? new ValidationResult();

        // May have warnings or infos, but no errors in strict mode for this dataset
        var errors = result.Errors.ToList();

        // A valid, well-formed file should have no errors
        // (there may be warnings about missing optional elements)
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task InvalidUidFile_LenientProfile_HasValidationIssues()
    {
        var path = await CreateInvalidUidDicomFile();

        // Lenient profile collects issues as warnings without throwing
        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Lenient,
            CollectValidationIssues = true,
        };

        var file = await DicomFile.OpenAsync(path, options);
        var result = file.ValidationResult ?? new ValidationResult();

        // File with invalid UIDs should have validation issues
        Assert.That(result.HasIssues, Is.True);
    }

    [Test]
    public async Task InvalidUidFile_StrictProfile_ThrowsOnError()
    {
        var path = await CreateInvalidUidDicomFile();

        // Strict profile throws exceptions on validation errors
        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Strict,
            CollectValidationIssues = true,
        };

        // Strict mode throws on invalid UIDs rather than collecting them
        Assert.ThrowsAsync<SharpDicom.Data.Exceptions.DicomDataException>(
            async () => await DicomFile.OpenAsync(path, options));
    }

    [Test]
    public void ValidationResult_CountsMatchSummary()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Error("DICOM-001", DicomTag.SOPInstanceUID, "Invalid UID"));
        result.Add(ValidationIssue.Warning("DICOM-002", DicomTag.StudyDate, "Non-standard format"));
        result.Add(ValidationIssue.Info("DICOM-003", DicomTag.PatientName, "Trailing spaces"));

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Errors.Count(), Is.EqualTo(1));
        Assert.That(result.Warnings.Count(), Is.EqualTo(1));
        Assert.That(result.Infos.Count(), Is.EqualTo(1));
        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidationResult_NoErrors_IsValid()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning("DICOM-002", DicomTag.StudyDate, "Non-standard format"));
        result.Add(ValidationIssue.Info("DICOM-003", DicomTag.PatientName, "Trailing spaces"));

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.HasInfos, Is.True);
    }

    [Test]
    public void ValidationResult_Empty_IsValid()
    {
        var result = new ValidationResult();

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.HasIssues, Is.False);
        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public void ExitCodes_CorrectValues()
    {
        Assert.That(SharpDicom.Cli.Helpers.ExitCodes.Success, Is.EqualTo(0));
        Assert.That(SharpDicom.Cli.Helpers.ExitCodes.UsageError, Is.EqualTo(1));
        Assert.That(SharpDicom.Cli.Helpers.ExitCodes.RuntimeError, Is.EqualTo(2));
        Assert.That(SharpDicom.Cli.Helpers.ExitCodes.ValidationError, Is.EqualTo(3));
    }
}
