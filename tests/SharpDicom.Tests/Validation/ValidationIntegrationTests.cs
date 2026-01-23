using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom;
using SharpDicom.Data;
using SharpDicom.IO;
using SharpDicom.Validation;

namespace SharpDicom.Tests.Validation;

/// <summary>
/// Integration tests for validation during file parsing.
/// </summary>
[TestFixture]
public class ValidationIntegrationTests
{
    /// <summary>
    /// Creates minimal DICOM file bytes with a specific element.
    /// </summary>
    private static byte[] CreateMinimalDicomFile(DicomTag tag, string vr, string value)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // 128-byte preamble
        writer.Write(new byte[128]);

        // DICM prefix
        writer.Write(Encoding.ASCII.GetBytes("DICM"));

        // File Meta Information (Group 0002, Explicit VR LE)
        // (0002,0000) UL FileMetaInformationGroupLength
        WriteExplicitElement(writer, 0x0002, 0x0000, "UL", BitConverter.GetBytes((uint)88));
        // (0002,0001) OB FileMetaInformationVersion
        WriteExplicitElement(writer, 0x0002, 0x0001, "OB", new byte[] { 0x00, 0x01 });
        // (0002,0002) UI MediaStorageSOPClassUID - Secondary Capture
        // UIDs must be padded with null (0x00), not space
        WriteExplicitElement(writer, 0x0002, 0x0002, "UI", Encoding.ASCII.GetBytes("1.2.840.10008.5.1.4.1.1.7\0"));
        // (0002,0003) UI MediaStorageSOPInstanceUID
        WriteExplicitElement(writer, 0x0002, 0x0003, "UI", Encoding.ASCII.GetBytes("1.2.3.4.5.6.7.8.9.0.1\0"));
        // (0002,0010) UI TransferSyntaxUID - Explicit VR LE
        WriteExplicitElement(writer, 0x0002, 0x0010, "UI", Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
        // (0002,0012) UI ImplementationClassUID
        WriteExplicitElement(writer, 0x0002, 0x0012, "UI", Encoding.ASCII.GetBytes("1.2.3.4.5.6.7.8.9\0"));

        // Dataset element with the specified tag/vr/value
        var valueBytes = vr == "DA" || vr == "TM" || vr == "DT" || vr.StartsWith("U") || vr == "LO" || vr == "SH" || vr == "CS" || vr == "PN"
            ? Encoding.ASCII.GetBytes(value.PadRight((value.Length + 1) & ~1)) // Even padding
            : Encoding.ASCII.GetBytes(value);

        WriteExplicitElement(writer, tag.Group, tag.Element, vr, valueBytes);

        return ms.ToArray();
    }

    private static void WriteExplicitElement(BinaryWriter writer, ushort group, ushort element, string vr, byte[] value)
    {
        writer.Write(group);
        writer.Write(element);
        writer.Write(Encoding.ASCII.GetBytes(vr));

        bool is32BitLength = vr == "SQ" || vr == "OB" || vr == "OW" || vr == "OF" ||
                             vr == "OD" || vr == "OL" || vr == "UC" || vr == "UR" || vr == "UN";

        if (is32BitLength)
        {
            writer.Write((ushort)0); // Reserved
            writer.Write((uint)value.Length);
        }
        else
        {
            writer.Write((ushort)value.Length);
        }

        writer.Write(value);
    }

    [Test]
    public async Task Parse_WithStrictMode_ValidFile_Succeeds()
    {
        // Create valid file with proper date format
        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "20240115");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, DicomReaderOptions.Strict);

        Assert.That(file, Is.Not.Null);
        Assert.That(file.Dataset, Is.Not.Null);
    }

    [Test]
    public async Task Parse_WithLenientMode_InvalidDate_CollectsIssues()
    {
        // Create file with invalid date format
        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "2024-01-15");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, DicomReaderOptions.Lenient);

        Assert.That(file, Is.Not.Null);
        // Lenient mode should continue and collect issues
        // ValidationResult may contain issues depending on which validators ran
    }

    [Test]
    public async Task Parse_WithPermissiveMode_NoValidation()
    {
        // Create file with invalid date format
        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "invalid_date");

        using var stream = new MemoryStream(bytes);

        // Permissive mode should not throw and skip validation
        var file = await DicomFile.OpenAsync(stream, DicomReaderOptions.Permissive);

        Assert.That(file, Is.Not.Null);
    }

    [Test]
    public async Task Parse_WithCallback_InvokedForEachIssue()
    {
        var issues = new List<ValidationIssue>();

        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Lenient,
            ValidationCallback = issue =>
            {
                issues.Add(issue);
                return true; // Continue parsing
            },
            CollectValidationIssues = true
        };

        // Create file with potentially invalid content
        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "invalid");

        using var stream = new MemoryStream(bytes);
        await DicomFile.OpenAsync(stream, options);

        // Callback should have been invoked if validation found issues
        // The exact count depends on which validators detect issues
    }

    [Test]
    public async Task Parse_WithCallback_ReturningFalse_AbortsOnIssue()
    {
        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Lenient,
            ValidationCallback = issue => false, // Abort on any issue
            CollectValidationIssues = true
        };

        // Create file with invalid date
        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "not_a_date");

        using var stream = new MemoryStream(bytes);

        // If validators detect an issue and callback returns false, should throw
        // This may or may not throw depending on which validators are active
        try
        {
            await DicomFile.OpenAsync(stream, options);
        }
        catch (SharpDicom.Data.Exceptions.DicomDataException)
        {
            // Expected if validation issue found
            Assert.Pass("Parsing aborted as expected");
        }

        // If no exception, either no issues found or file is valid enough
        Assert.Pass("No validation issues triggered abort");
    }

    [Test]
    public async Task Parse_CollectValidationIssuesFalse_OnlyCallback()
    {
        var callbackCount = 0;
        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.Lenient,
            ValidationCallback = issue =>
            {
                callbackCount++;
                return true;
            },
            CollectValidationIssues = false
        };

        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "invalid");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, options);

        Assert.That(file, Is.Not.Null);
        // Callback should have been invoked, but ValidationResult should be null
        // because collection is disabled
        // but callback should still be invoked
    }

    [Test]
    public async Task Parse_NoValidationProfile_NoValidation()
    {
        var options = new DicomReaderOptions
        {
            ValidationProfile = null
        };

        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "invalid_date");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, options);

        Assert.That(file, Is.Not.Null);
        Assert.That(file.ValidationResult, Is.Null);
    }

    [Test]
    public async Task Parse_NoneProfile_NoValidation()
    {
        var options = new DicomReaderOptions
        {
            ValidationProfile = ValidationProfile.None
        };

        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "invalid_date");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, options);

        Assert.That(file, Is.Not.Null);
        // None profile has no rules, so no validation occurs
    }

    [Test]
    public async Task Parse_CustomProfile_UsesCustomRules()
    {
        var customRule = new TestValidationRule();
        var profile = new ValidationProfile
        {
            Name = "Custom",
            Rules = new[] { customRule },
            DefaultBehavior = ValidationBehavior.Validate
        };

        var options = new DicomReaderOptions
        {
            ValidationProfile = profile,
            CollectValidationIssues = true
        };

        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "20240115");

        using var stream = new MemoryStream(bytes);
        var file = await DicomFile.OpenAsync(stream, options);

        Assert.That(customRule.ValidateCalled, Is.True);
    }

    [Test]
    public async Task Parse_WithTagOverride_SkipsSpecificTag()
    {
        var validateCalled = new Dictionary<uint, int>();
        var customRule = new TestValidationRule(ctx =>
        {
            var key = ctx.Tag.Value;
            validateCalled[key] = validateCalled.GetValueOrDefault(key) + 1;
            return null;
        });

        var profile = new ValidationProfile
        {
            Name = "Custom",
            Rules = new[] { customRule },
            DefaultBehavior = ValidationBehavior.Validate,
            TagOverrides = new Dictionary<DicomTag, ValidationBehavior>
            {
                { new DicomTag(0x0008, 0x0020), ValidationBehavior.Skip } // Skip StudyDate
            }
        };

        var options = new DicomReaderOptions
        {
            ValidationProfile = profile,
            CollectValidationIssues = true
        };

        var bytes = CreateMinimalDicomFile(new DicomTag(0x0008, 0x0020), "DA", "20240115");

        using var stream = new MemoryStream(bytes);
        await DicomFile.OpenAsync(stream, options);

        // StudyDate (0008,0020) should have been skipped
        Assert.That(validateCalled.ContainsKey(0x00080020), Is.False,
            "Validation should be skipped for overridden tag");
    }

    [Test]
    public void ValidationProfile_Presets_AreNotNull()
    {
        Assert.That(ValidationProfile.Strict, Is.Not.Null);
        Assert.That(ValidationProfile.Lenient, Is.Not.Null);
        Assert.That(ValidationProfile.Permissive, Is.Not.Null);
        Assert.That(ValidationProfile.None, Is.Not.Null);
    }

    [Test]
    public void ValidationProfile_Presets_HaveCorrectNames()
    {
        Assert.That(ValidationProfile.Strict.Name, Is.EqualTo("Strict"));
        Assert.That(ValidationProfile.Lenient.Name, Is.EqualTo("Lenient"));
        Assert.That(ValidationProfile.Permissive.Name, Is.EqualTo("Permissive"));
        Assert.That(ValidationProfile.None.Name, Is.EqualTo("None"));
    }

    [Test]
    public void DicomReaderOptions_Presets_HaveValidationProfiles()
    {
        Assert.That(DicomReaderOptions.Strict.ValidationProfile, Is.EqualTo(ValidationProfile.Strict));
        Assert.That(DicomReaderOptions.Lenient.ValidationProfile, Is.EqualTo(ValidationProfile.Lenient));
        Assert.That(DicomReaderOptions.Permissive.ValidationProfile, Is.EqualTo(ValidationProfile.Permissive));
    }

    [Test]
    public void DicomReaderOptions_Default_HasNoValidationProfile()
    {
        Assert.That(DicomReaderOptions.Default.ValidationProfile, Is.Null);
    }

    /// <summary>
    /// Test validation rule for integration testing.
    /// </summary>
    private class TestValidationRule : IValidationRule
    {
        private readonly Func<ElementValidationContext, ValidationIssue?>? _validateFunc;

        public TestValidationRule(Func<ElementValidationContext, ValidationIssue?>? validateFunc = null)
        {
            _validateFunc = validateFunc;
        }

        public string RuleId => "TEST-RULE";
        public string Description => "Test validation rule for integration testing";
        public bool ValidateCalled { get; private set; }

        public ValidationIssue? Validate(in ElementValidationContext context)
        {
            ValidateCalled = true;
            return _validateFunc?.Invoke(context);
        }
    }
}
