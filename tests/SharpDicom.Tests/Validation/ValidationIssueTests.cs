using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;

namespace SharpDicom.Tests.Validation;

/// <summary>
/// Unit tests for ValidationIssue record struct.
/// </summary>
[TestFixture]
public class ValidationIssueTests
{
    private static readonly DicomTag TestTag = new(0x0008, 0x0010);

    [Test]
    public void ErrorFactory_CreatesErrorSeverityIssue()
    {
        var issue = ValidationIssue.Error(
            ValidationCodes.InvalidTagFormat,
            TestTag,
            "Test error message");

        Assert.Multiple(() =>
        {
            Assert.That(issue.Code, Is.EqualTo(ValidationCodes.InvalidTagFormat));
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Tag, Is.EqualTo(TestTag));
            Assert.That(issue.Message, Is.EqualTo("Test error message"));
            Assert.That(issue.SuggestedFix, Is.Null);
            Assert.That(issue.DeclaredVR, Is.Null);
            Assert.That(issue.ExpectedVR, Is.Null);
            Assert.That(issue.Position, Is.Null);
            Assert.That(issue.RawValue.IsEmpty, Is.True);
        });
    }

    [Test]
    public void ErrorFactory_WithSuggestedFix_IncludesFix()
    {
        var issue = ValidationIssue.Error(
            ValidationCodes.VRMismatch,
            TestTag,
            "VR mismatch detected",
            "Use correct VR from dictionary");

        Assert.That(issue.SuggestedFix, Is.EqualTo("Use correct VR from dictionary"));
    }

    [Test]
    public void WarningFactory_CreatesWarningSeverityIssue()
    {
        var issue = ValidationIssue.Warning(
            ValidationCodes.ValueTooLong,
            TestTag,
            "Value exceeds maximum length");

        Assert.Multiple(() =>
        {
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Warning));
            Assert.That(issue.Code, Is.EqualTo(ValidationCodes.ValueTooLong));
            Assert.That(issue.Tag, Is.EqualTo(TestTag));
        });
    }

    [Test]
    public void InfoFactory_CreatesInfoSeverityIssue()
    {
        var issue = ValidationIssue.Info(
            ValidationCodes.DeprecatedAttribute,
            TestTag,
            "Retired attribute present");

        Assert.Multiple(() =>
        {
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Info));
            Assert.That(issue.Code, Is.EqualTo(ValidationCodes.DeprecatedAttribute));
        });
    }

    [Test]
    public void Create_WithFullContext_PopulatesAllProperties()
    {
        var rawValue = new byte[] { 0x41, 0x42, 0x43 };

        var issue = ValidationIssue.Create(
            code: ValidationCodes.VRMismatch,
            severity: ValidationSeverity.Warning,
            tag: TestTag,
            declaredVR: DicomVR.LO,
            expectedVR: DicomVR.SH,
            position: 1234,
            message: "VR mismatch: LO vs SH",
            suggestedFix: "Use SH",
            rawValue: rawValue);

        Assert.Multiple(() =>
        {
            Assert.That(issue.Code, Is.EqualTo(ValidationCodes.VRMismatch));
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Warning));
            Assert.That(issue.Tag, Is.EqualTo(TestTag));
            Assert.That(issue.DeclaredVR, Is.EqualTo(DicomVR.LO));
            Assert.That(issue.ExpectedVR, Is.EqualTo(DicomVR.SH));
            Assert.That(issue.Position, Is.EqualTo(1234));
            Assert.That(issue.Message, Is.EqualTo("VR mismatch: LO vs SH"));
            Assert.That(issue.SuggestedFix, Is.EqualTo("Use SH"));
            Assert.That(issue.RawValue.ToArray(), Is.EqualTo(rawValue));
        });
    }

    [Test]
    public void Create_WithNullTag_AcceptsNull()
    {
        var issue = ValidationIssue.Create(
            code: ValidationCodes.TruncatedElement,
            severity: ValidationSeverity.Error,
            tag: null,
            declaredVR: null,
            expectedVR: null,
            position: 100,
            message: "Truncated data",
            suggestedFix: null,
            rawValue: default);

        Assert.Multiple(() =>
        {
            Assert.That(issue.Tag, Is.Null);
            Assert.That(issue.DeclaredVR, Is.Null);
            Assert.That(issue.ExpectedVR, Is.Null);
        });
    }

    [Test]
    public void RawValue_EmptyByDefault_InFactoryMethods()
    {
        var error = ValidationIssue.Error(ValidationCodes.InvalidTagFormat, TestTag, "Error");
        var warning = ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Warning");
        var info = ValidationIssue.Info(ValidationCodes.DeprecatedAttribute, TestTag, "Info");

        Assert.Multiple(() =>
        {
            Assert.That(error.RawValue.IsEmpty, Is.True);
            Assert.That(warning.RawValue.IsEmpty, Is.True);
            Assert.That(info.RawValue.IsEmpty, Is.True);
        });
    }

    [Test]
    public void RawValue_CanBePopulated_InCreate()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        var issue = ValidationIssue.Create(
            ValidationCodes.InvalidCharacter,
            ValidationSeverity.Warning,
            TestTag,
            DicomVR.CS,
            DicomVR.CS,
            0,
            "Invalid character",
            null,
            bytes);

        Assert.Multiple(() =>
        {
            Assert.That(issue.RawValue.Length, Is.EqualTo(5));
            Assert.That(issue.RawValue.ToArray(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void ToString_IncludesAllRelevantInfo()
    {
        var issue = ValidationIssue.Create(
            ValidationCodes.VRMismatch,
            ValidationSeverity.Warning,
            TestTag,
            DicomVR.LO,
            DicomVR.SH,
            1234,
            "VR mismatch detected",
            null,
            default);

        var str = issue.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(str, Does.Contain("Warning"));
            Assert.That(str, Does.Contain(ValidationCodes.VRMismatch));
            Assert.That(str, Does.Contain("(0008,0010)"));
            Assert.That(str, Does.Contain("VR mismatch detected"));
            Assert.That(str, Does.Contain("1234"));
        });
    }

    [Test]
    public void ToString_WithoutPosition_OmitsPosition()
    {
        var issue = ValidationIssue.Warning(ValidationCodes.ValueTooLong, TestTag, "Value too long");

        var str = issue.ToString();

        Assert.That(str, Does.Not.Contain("position"));
    }

    [Test]
    public void ToString_WithoutTag_OmitsTag()
    {
        var issue = ValidationIssue.Create(
            ValidationCodes.TruncatedElement,
            ValidationSeverity.Error,
            null,
            null,
            null,
            100,
            "Data truncated",
            null,
            default);

        var str = issue.ToString();

        Assert.That(str, Does.Not.Contain(" at "));
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        var issue1 = ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Same message");
        var issue2 = ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Same message");

        Assert.That(issue1, Is.EqualTo(issue2));
    }

    [Test]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var issue1 = ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Message 1");
        var issue2 = ValidationIssue.Warning(ValidationCodes.VRMismatch, TestTag, "Message 2");

        Assert.That(issue1, Is.Not.EqualTo(issue2));
    }
}
