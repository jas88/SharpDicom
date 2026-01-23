using System.Linq;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;

namespace SharpDicom.Tests.Validation;

/// <summary>
/// Unit tests for ValidationResult class.
/// </summary>
[TestFixture]
public class ValidationResultTests
{
    private static readonly DicomTag TestTag = new(0x0008, 0x0010);

    [Test]
    public void NewResult_IsValid()
    {
        var result = new ValidationResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.HasWarnings, Is.False);
            Assert.That(result.HasInfos, Is.False);
            Assert.That(result.HasIssues, Is.False);
            Assert.That(result.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void IsValid_WithOnlyInfos_ReturnsTrue()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 1"));
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 2"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.HasInfos, Is.True);
        });
    }

    [Test]
    public void IsValid_WithOnlyWarnings_ReturnsTrue()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning 1"));
        result.Add(ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Warning 2"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.HasWarnings, Is.True);
        });
    }

    [Test]
    public void IsValid_WithError_ReturnsFalse()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error"));

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void HasWarnings_WithWarning_ReturnsTrue()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));

        Assert.That(result.HasWarnings, Is.True);
    }

    [Test]
    public void HasWarnings_WithoutWarnings_ReturnsFalse()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error"));
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info"));

        Assert.That(result.HasWarnings, Is.False);
    }

    [Test]
    public void Errors_FiltersCorrectly()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error 1"));
        result.Add(ValidationIssue.Error(ValidationCodes.InvalidSequenceStructure, TestTag, "Error 2"));

        var errors = result.Errors.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors.All(e => e.Severity == ValidationSeverity.Error), Is.True);
        });
    }

    [Test]
    public void Warnings_FiltersCorrectly()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning 1"));
        result.Add(ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Warning 2"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error"));

        var warnings = result.Warnings.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(warnings.All(w => w.Severity == ValidationSeverity.Warning), Is.True);
        });
    }

    [Test]
    public void Infos_FiltersCorrectly()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 1"));
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 2"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));

        var infos = result.Infos.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(infos, Has.Count.EqualTo(2));
            Assert.That(infos.All(i => i.Severity == ValidationSeverity.Info), Is.True);
        });
    }

    [Test]
    public void Add_IncreasesCount()
    {
        var result = new ValidationResult();
        Assert.That(result.Count, Is.EqualTo(0));

        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Issue 1"));
        Assert.That(result.Count, Is.EqualTo(1));

        result.Add(ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Issue 2"));
        Assert.That(result.Count, Is.EqualTo(2));
    }

    [Test]
    public void AddRange_AddsMultipleIssues()
    {
        var result = new ValidationResult();
        var issues = new[]
        {
            ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Issue 1"),
            ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Issue 2"),
            ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Issue 3")
        };

        result.AddRange(issues);

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.Issues, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Clear_RemovesAllIssues()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Issue 1"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Issue 2"));
        Assert.That(result.Count, Is.EqualTo(2));

        result.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(0));
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.HasWarnings, Is.False);
            Assert.That(result.HasIssues, Is.False);
        });
    }

    [Test]
    public void Issues_ReturnsReadOnlyList()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Issue"));

        var issues = result.Issues;

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Code, Is.EqualTo(ValidationCodes.ValueTooLong));
        });
    }

    [Test]
    public void MultipleSeverities_AreHandledCorrectly()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 1"));
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info 2"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning 1"));
        result.Add(ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Warning 2"));
        result.Add(ValidationIssue.Warning(ValidationCodes.InvalidCharacter, TestTag, "Warning 3"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error 1"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Count, Is.EqualTo(6));
            Assert.That(result.Infos.Count(), Is.EqualTo(2));
            Assert.That(result.Warnings.Count(), Is.EqualTo(3));
            Assert.That(result.Errors.Count(), Is.EqualTo(1));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.HasWarnings, Is.True);
            Assert.That(result.HasInfos, Is.True);
            Assert.That(result.HasIssues, Is.True);
        });
    }

    [Test]
    public void GetByCode_FiltersCorrectly()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Too long 1"));
        result.Add(ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "VR mismatch"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Too long 2"));

        var byCode = result.GetByCode(ValidationCodes.ValueTooLong).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(byCode, Has.Count.EqualTo(2));
            Assert.That(byCode.All(i => i.Code == ValidationCodes.ValueTooLong), Is.True);
        });
    }

    [Test]
    public void GetByCode_NonExistentCode_ReturnsEmpty()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Issue"));

        var byCode = result.GetByCode(ValidationCodes.TruncatedElement).ToList();

        Assert.That(byCode, Is.Empty);
    }

    [Test]
    public void ToString_EmptyResult_ShowsNoIssues()
    {
        var result = new ValidationResult();

        var str = result.ToString();

        Assert.That(str, Does.Contain("no issues"));
    }

    [Test]
    public void ToString_WithIssues_ShowsCounts()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info"));
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));
        result.Add(ValidationIssue.Error(ValidationCodes.TruncatedElement, TestTag, "Error"));

        var str = result.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(str, Does.Contain("1 errors"));
            Assert.That(str, Does.Contain("1 warnings"));
            Assert.That(str, Does.Contain("1 infos"));
            Assert.That(str, Does.Contain("failed"));
        });
    }

    [Test]
    public void ToString_ValidWithWarnings_ShowsPassed()
    {
        var result = new ValidationResult();
        result.Add(ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning"));

        var str = result.ToString();

        Assert.That(str, Does.Contain("passed"));
    }
}
