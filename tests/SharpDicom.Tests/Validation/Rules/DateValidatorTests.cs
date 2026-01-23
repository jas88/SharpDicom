using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;
using SharpDicom.Validation.Rules;
using System;
using System.Text;

namespace SharpDicom.Tests.Validation.Rules;

[TestFixture]
public class DateValidatorTests
{
    private DateValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new DateValidator();
    }

    private ElementValidationContext CreateContext(string value, DicomVR vr)
    {
        return new ElementValidationContext
        {
            Tag = new DicomTag(0x0008, 0x0020),
            DeclaredVR = vr,
            RawValue = Encoding.ASCII.GetBytes(value),
            Dataset = new DicomDataset(),
            Encoding = DicomEncoding.Default
        };
    }

    [Test]
    public void RuleId_ReturnsExpectedValue()
    {
        Assert.That(_validator.RuleId, Is.EqualTo("VR-DA-FORMAT"));
    }

    [Test]
    public void Validate_NonDAVR_ReturnsNull()
    {
        var context = CreateContext("20240115", DicomVR.CS);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_EmptyValue_ReturnsNull()
    {
        var context = CreateContext("", DicomVR.DA);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [TestCase("20240115")]  // Standard date
    [TestCase("20240229")]  // Leap year
    [TestCase("19000101")]  // Old date
    [TestCase("99991231")]  // Max date
    [TestCase("202401")]    // Partial: YYYYMM
    [TestCase("2024")]      // Partial: YYYY
    public void Validate_ValidDates_ReturnsNull(string date)
    {
        var context = CreateContext(date, DicomVR.DA);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_ValidDateWithSpacePadding_ReturnsNull()
    {
        var context = CreateContext("20240115 ", DicomVR.DA);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_InvalidMonth_ReturnsError()
    {
        var context = CreateContext("20241301", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Severity, Is.EqualTo(ValidationSeverity.Error));
        Assert.That(result.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
        Assert.That(result.Value.Message, Does.Contain("Month"));
    }

    [Test]
    public void Validate_InvalidMonthZero_ReturnsError()
    {
        var context = CreateContext("20240001", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
    }

    [Test]
    public void Validate_InvalidDay_Feb30_ReturnsError()
    {
        var context = CreateContext("20240230", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Severity, Is.EqualTo(ValidationSeverity.Error));
        Assert.That(result.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
        Assert.That(result.Value.Message, Does.Contain("Day 30"));
    }

    [Test]
    public void Validate_InvalidDay_April31_ReturnsError()
    {
        var context = CreateContext("20240431", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
        Assert.That(result.Value.Message, Does.Contain("Day 31"));
    }

    [Test]
    public void Validate_NonLeapYear_Feb29_ReturnsError()
    {
        var context = CreateContext("20230229", DicomVR.DA);  // 2023 is not a leap year
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
    }

    [Test]
    public void Validate_LeapYear_Feb29_ReturnsNull()
    {
        var context = CreateContext("20240229", DicomVR.DA);  // 2024 is a leap year
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_InvalidFormat_Dashes_ReturnsError()
    {
        var context = CreateContext("2024-01-15", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        // 10 characters with dashes fails length check before character check
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateFormat));
    }

    [Test]
    public void Validate_InvalidFormat_Slashes_ReturnsError()
    {
        var context = CreateContext("01/15/24", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void Validate_NonDigitCharacters_ReturnsError()
    {
        var context = CreateContext("2024O115", DicomVR.DA);  // O instead of 0
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void Validate_WrongLength_ReturnsError()
    {
        var context = CreateContext("20240", DicomVR.DA);  // 5 chars - not 4, 6, or 8
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateFormat));
    }

    [Test]
    public void Validate_TooLong_ReturnsError()
    {
        var context = CreateContext("202401151", DicomVR.DA);  // 9 chars
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateFormat));
    }

    [Test]
    public void Validate_InvalidDayZero_ReturnsError()
    {
        var context = CreateContext("20240100", DicomVR.DA);  // Day 00 is invalid
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDateValue));
    }

    [Test]
    public void Validate_ErrorHasSuggestedFix()
    {
        var context = CreateContext("20241301", DicomVR.DA);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SuggestedFix, Is.Not.Null.And.Not.Empty);
    }
}
