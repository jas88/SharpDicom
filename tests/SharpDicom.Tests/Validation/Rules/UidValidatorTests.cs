using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;
using SharpDicom.Validation.Rules;
using System;
using System.Text;

namespace SharpDicom.Tests.Validation.Rules;

[TestFixture]
public class UidValidatorTests
{
    private UidValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new UidValidator();
    }

    private static ElementValidationContext CreateContext(string value, DicomVR vr)
    {
        return new ElementValidationContext
        {
            Tag = new DicomTag(0x0008, 0x0018),  // SOP Instance UID
            DeclaredVR = vr,
            RawValue = Encoding.ASCII.GetBytes(value),
            Dataset = new DicomDataset(),
            Encoding = DicomEncoding.Default
        };
    }

    private static ElementValidationContext CreateContextWithNullPadding(string value)
    {
        var bytes = new byte[value.Length + 1];
        Encoding.ASCII.GetBytes(value, 0, value.Length, bytes, 0);
        bytes[value.Length] = 0x00;  // Null padding

        return new ElementValidationContext
        {
            Tag = new DicomTag(0x0008, 0x0018),  // SOP Instance UID
            DeclaredVR = DicomVR.UI,
            RawValue = bytes,
            Dataset = new DicomDataset(),
            Encoding = DicomEncoding.Default
        };
    }

    [Test]
    public void RuleId_ReturnsExpectedValue()
    {
        Assert.That(_validator.RuleId, Is.EqualTo("VR-UI-FORMAT"));
    }

    [Test]
    public void Validate_NonUIVR_ReturnsNull()
    {
        var context = CreateContext("1.2.840.10008.1.2", DicomVR.CS);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_EmptyValue_ReturnsNull()
    {
        var context = CreateContext("", DicomVR.UI);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [TestCase("1.2.840.10008.1.2")]              // Implicit VR Little Endian
    [TestCase("1.2.840.10008.5.1.4.1.1.2")]     // CT Image Storage
    [TestCase("2.25.123456789")]                 // UUID-based
    [TestCase("1.0")]                            // Simple valid
    [TestCase("0.0")]                            // All zeros allowed
    [TestCase("1")]                              // Single component allowed
    public void Validate_ValidUIDs_ReturnsNull(string uid)
    {
        var context = CreateContext(uid, DicomVR.UI);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_ValidUIDWithNullPadding_ReturnsNull()
    {
        var context = CreateContextWithNullPadding("1.2.840.10008.1.2");
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Validate_TooLong_ReturnsError()
    {
        // Create a UID that's 65 characters
        var uid = "1." + new string('2', 62) + ".3";  // > 64 chars
        var context = CreateContext(uid, DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.ValueTooLong));
    }

    [Test]
    public void Validate_LeadingZeroInComponent_ReturnsError()
    {
        var context = CreateContext("1.02.3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidUidFormat));
        Assert.That(result.Value.Message, Does.Contain("leading zero"));
    }

    [Test]
    public void Validate_SingleZeroComponent_IsValid()
    {
        var context = CreateContext("1.0.3", DicomVR.UI);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);  // "0" by itself is allowed
    }

    [Test]
    public void Validate_EmptyComponent_ConsecutiveDots_ReturnsError()
    {
        var context = CreateContext("1..3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidUidFormat));
        Assert.That(result.Value.Message, Does.Contain("empty component"));
    }

    [Test]
    public void Validate_LeadingDot_ReturnsError()
    {
        var context = CreateContext(".1.2.3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidUidFormat));
        Assert.That(result.Value.Message, Does.Contain("start with"));
    }

    [Test]
    public void Validate_TrailingDot_ReturnsError()
    {
        var context = CreateContext("1.2.3.", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidUidFormat));
        Assert.That(result.Value.Message, Does.Contain("end with"));
    }

    [Test]
    public void Validate_InvalidCharacter_Letter_ReturnsError()
    {
        var context = CreateContext("1.2.A.3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void Validate_InvalidCharacter_Space_ReturnsError()
    {
        var context = CreateContext("1.2 .3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void Validate_InvalidCharacter_Dash_ReturnsError()
    {
        var context = CreateContext("1.2-3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void Validate_ErrorHasSuggestedFix()
    {
        var context = CreateContext("1.02.3", DicomVR.UI);
        var result = _validator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.SuggestedFix, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Validate_MaxLength64_IsValid()
    {
        // Exactly 64 characters
        var uid = "1.2." + new string('3', 60);  // 64 chars total
        var context = CreateContext(uid, DicomVR.UI);
        var result = _validator.Validate(in context);
        Assert.That(result, Is.Null);
    }
}
