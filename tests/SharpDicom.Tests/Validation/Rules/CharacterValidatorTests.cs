using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;
using SharpDicom.Validation.Rules;
using System;
using System.Linq;
using System.Text;

namespace SharpDicom.Tests.Validation.Rules;

[TestFixture]
public class CharacterValidatorTests
{
    private CodeStringValidator _csValidator = null!;
    private CharacterRepertoireValidator _charValidator = null!;
    private TimeValidator _tmValidator = null!;
    private AgeStringValidator _asValidator = null!;

    [SetUp]
    public void SetUp()
    {
        _csValidator = new CodeStringValidator();
        _charValidator = new CharacterRepertoireValidator();
        _tmValidator = new TimeValidator();
        _asValidator = new AgeStringValidator();
    }

    private static ElementValidationContext CreateContext(string value, DicomVR vr, DicomTag? tag = null)
    {
        return new ElementValidationContext
        {
            Tag = tag ?? new DicomTag(0x0008, 0x0060),  // Modality (CS)
            DeclaredVR = vr,
            RawValue = Encoding.ASCII.GetBytes(value),
            Dataset = new DicomDataset(),
            Encoding = DicomEncoding.Default
        };
    }

    #region CodeStringValidator Tests

    [Test]
    public void CS_Lowercase_ReturnsWarning()
    {
        var context = CreateContext("abc", DicomVR.CS);
        var result = _csValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Severity, Is.EqualTo(ValidationSeverity.Warning));
        Assert.That(result.Value.Code, Is.EqualTo(ValidationCodes.InvalidCodeString));
        Assert.That(result.Value.Message, Does.Contain("lowercase"));
    }

    [Test]
    public void CS_ValidUppercase_ReturnsNull()
    {
        var context = CreateContext("CT", DicomVR.CS);
        var result = _csValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [TestCase("MR")]
    [TestCase("CT")]
    [TestCase("PT")]
    [TestCase("US")]
    [TestCase("DERIVED")]
    [TestCase("PRIMARY")]
    [TestCase("VALUE_1")]
    [TestCase("A B C")]
    public void CS_ValidValues_ReturnsNull(string value)
    {
        var context = CreateContext(value, DicomVR.CS);
        var result = _csValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void CS_InvalidCharacter_AtSign_ReturnsWarning()
    {
        var context = CreateContext("TEST@VALUE", DicomVR.CS);
        var result = _csValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCodeString));
    }

    [Test]
    public void CS_TooLong_ReturnsWarning()
    {
        var context = CreateContext("12345678901234567", DicomVR.CS);  // 17 chars
        var result = _csValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.ValueTooLong));
    }

    [Test]
    public void CS_MultiValued_WithLowercase_ReturnsWarning()
    {
        var context = CreateContext("CT\\mr", DicomVR.CS);
        var result = _csValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCodeString));
    }

    [Test]
    public void CS_Empty_ReturnsNull()
    {
        var context = CreateContext("", DicomVR.CS);
        var result = _csValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region CharacterRepertoireValidator Tests (DS, IS, AE)

    [Test]
    public void DS_ValidScientificNotation_ReturnsNull()
    {
        var context = CreateContext("1.23E-4", DicomVR.DS);
        var result = _charValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [TestCase("123.456")]
    [TestCase("-123.456")]
    [TestCase("+123.456")]
    [TestCase("1e10")]
    [TestCase("1E10")]
    [TestCase("1.5e-3")]
    [TestCase(" 123 ")]  // Leading/trailing spaces allowed
    public void DS_ValidValues_ReturnsNull(string value)
    {
        var context = CreateContext(value, DicomVR.DS);
        var result = _charValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DS_InvalidCharacter_ReturnsWarning()
    {
        var context = CreateContext("12.3A", DicomVR.DS);
        var result = _charValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidDecimalString));
    }

    [Test]
    public void IS_ValidWithLeadingPlus_ReturnsNull()
    {
        var context = CreateContext("+123", DicomVR.IS);
        var result = _charValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [TestCase("123")]
    [TestCase("-123")]
    [TestCase("+456")]
    [TestCase(" 789 ")]
    [TestCase("0")]
    public void IS_ValidValues_ReturnsNull(string value)
    {
        var context = CreateContext(value, DicomVR.IS);
        var result = _charValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void IS_InvalidCharacter_Decimal_ReturnsWarning()
    {
        var context = CreateContext("12.3", DicomVR.IS);
        var result = _charValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidIntegerString));
    }

    [Test]
    public void AE_Backslash_ReturnsWarning()
    {
        var context = CreateContext("AE\\TITLE", DicomVR.AE);
        var result = _charValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
        Assert.That(result.Value.Message, Does.Contain("backslash"));
    }

    [Test]
    public void AE_ValidTitle_ReturnsNull()
    {
        var context = CreateContext("MY_AE_TITLE", DicomVR.AE);
        var result = _charValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void AE_ControlCharacter_ReturnsWarning()
    {
        var bytes = new byte[] { (byte)'A', 0x01, (byte)'E' };
        var context = new ElementValidationContext
        {
            Tag = new DicomTag(0x0008, 0x0054),
            DeclaredVR = DicomVR.AE,
            RawValue = bytes,
            Dataset = new DicomDataset(),
            Encoding = DicomEncoding.Default
        };

        var result = _charValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
        Assert.That(result.Value.Message, Does.Contain("control character"));
    }

    [Test]
    public void AE_SpaceOnly_ReturnsWarning()
    {
        var context = CreateContext("   ", DicomVR.AE);
        var result = _charValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Message, Does.Contain("only spaces"));
    }

    #endregion

    #region TimeValidator Tests

    [TestCase("12")]         // HH
    [TestCase("1234")]       // HHMM
    [TestCase("123456")]     // HHMMSS
    [TestCase("123456.1")]   // with fractional
    [TestCase("123456.123456")]  // max fractional
    public void TM_ValidTimes_ReturnsNull(string value)
    {
        var context = CreateContext(value, DicomVR.TM);
        var result = _tmValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TM_InvalidHour_ReturnsError()
    {
        var context = CreateContext("250000", DicomVR.TM);
        var result = _tmValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidTimeValue));
    }

    [Test]
    public void TM_InvalidMinute_ReturnsError()
    {
        var context = CreateContext("126000", DicomVR.TM);
        var result = _tmValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidTimeValue));
    }

    [Test]
    public void TM_InvalidSecond_ReturnsError()
    {
        var context = CreateContext("125960", DicomVR.TM);
        var result = _tmValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidTimeValue));
    }

    [Test]
    public void TM_Empty_ReturnsNull()
    {
        var context = CreateContext("", DicomVR.TM);
        var result = _tmValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region AgeStringValidator Tests

    [TestCase("045Y")]  // 45 years
    [TestCase("012M")]  // 12 months
    [TestCase("003W")]  // 3 weeks
    [TestCase("001D")]  // 1 day
    [TestCase("000Y")]  // 0 years (newborn)
    [TestCase("999Y")]  // max
    public void AS_ValidAges_ReturnsNull(string value)
    {
        var context = CreateContext(value, DicomVR.AS);
        var result = _asValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void AS_InvalidUnit_Lowercase_ReturnsError()
    {
        var context = CreateContext("045y", DicomVR.AS);  // lowercase 'y'
        var result = _asValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidAgeString));
    }

    [Test]
    public void AS_InvalidUnit_X_ReturnsError()
    {
        var context = CreateContext("045X", DicomVR.AS);
        var result = _asValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidAgeString));
    }

    [Test]
    public void AS_WrongLength_ReturnsError()
    {
        var context = CreateContext("45Y", DicomVR.AS);  // 3 chars, needs 4
        var result = _asValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidAgeString));
    }

    [Test]
    public void AS_NonDigitInNumber_ReturnsError()
    {
        var context = CreateContext("04AY", DicomVR.AS);
        var result = _asValidator.Validate(in context);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Code, Is.EqualTo(ValidationCodes.InvalidCharacter));
    }

    [Test]
    public void AS_Empty_ReturnsNull()
    {
        var context = CreateContext("", DicomVR.AS);
        var result = _asValidator.Validate(in context);
        Assert.That(result, Is.Null);
    }

    #endregion

    #region StandardRules Tests

    [Test]
    public void StandardRules_All_Contains9Validators()
    {
        Assert.That(StandardRules.All.Count, Is.EqualTo(9));
    }

    [Test]
    public void StandardRules_All_AllImplementIValidationRule()
    {
        foreach (var rule in StandardRules.All)
        {
            Assert.That(rule, Is.InstanceOf<IValidationRule>());
        }
    }

    [Test]
    public void StandardRules_All_HasUniqueRuleIds()
    {
        var ruleIds = StandardRules.All.Select(r => r.RuleId).ToList();
        Assert.That(ruleIds.Distinct().Count(), Is.EqualTo(ruleIds.Count));
    }

    [Test]
    public void StandardRules_StructuralOnly_ContainsStringLengthValidator()
    {
        Assert.That(StandardRules.StructuralOnly, Has.Count.EqualTo(1));
        Assert.That(StandardRules.StructuralOnly[0], Is.InstanceOf<StringLengthValidator>());
    }

    [Test]
    public void StandardRules_DateTimeRules_Contains4Validators()
    {
        Assert.That(StandardRules.DateTimeRules.Count, Is.EqualTo(4));
    }

    [Test]
    public void StandardRules_IdentifierRules_Contains2Validators()
    {
        Assert.That(StandardRules.IdentifierRules.Count, Is.EqualTo(2));
    }

    #endregion
}
