using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Validation;
using System.Collections.Generic;

namespace SharpDicom.Tests.Validation;

[TestFixture]
public class ValidationProfileTests
{
    [Test]
    public void Strict_HasAllRules()
    {
        var profile = ValidationProfile.Strict;

        Assert.That(profile.Name, Is.EqualTo("Strict"));
        Assert.That(profile.DefaultBehavior, Is.EqualTo(ValidationBehavior.Validate));
        Assert.That(profile.Rules, Is.Not.Empty);
        Assert.That(profile.Rules.Count, Is.EqualTo(StandardRules.All.Count));
    }

    [Test]
    public void Lenient_HasAllRulesWithWarnBehavior()
    {
        var profile = ValidationProfile.Lenient;

        Assert.That(profile.Name, Is.EqualTo("Lenient"));
        Assert.That(profile.DefaultBehavior, Is.EqualTo(ValidationBehavior.Warn));
        Assert.That(profile.Rules, Is.Not.Empty);
        Assert.That(profile.Rules.Count, Is.EqualTo(StandardRules.All.Count));
    }

    [Test]
    public void Permissive_HasStructuralRulesOnly()
    {
        var profile = ValidationProfile.Permissive;

        Assert.That(profile.Name, Is.EqualTo("Permissive"));
        Assert.That(profile.DefaultBehavior, Is.EqualTo(ValidationBehavior.Warn));
        Assert.That(profile.Rules, Is.Not.Empty);
        Assert.That(profile.Rules.Count, Is.EqualTo(StandardRules.StructuralOnly.Count));
    }

    [Test]
    public void None_HasNoRulesAndSkipBehavior()
    {
        var profile = ValidationProfile.None;

        Assert.That(profile.Name, Is.EqualTo("None"));
        Assert.That(profile.DefaultBehavior, Is.EqualTo(ValidationBehavior.Skip));
        Assert.That(profile.Rules, Is.Empty);
    }

    [Test]
    public void GetBehavior_NoOverride_ReturnsDefaultBehavior()
    {
        var profile = new ValidationProfile
        {
            DefaultBehavior = ValidationBehavior.Validate
        };

        var behavior = profile.GetBehavior(new DicomTag(0x0008, 0x0018));

        Assert.That(behavior, Is.EqualTo(ValidationBehavior.Validate));
    }

    [Test]
    public void GetBehavior_WithOverride_ReturnsOverrideBehavior()
    {
        var tag = new DicomTag(0x0008, 0x0018);
        var profile = new ValidationProfile
        {
            DefaultBehavior = ValidationBehavior.Validate,
            TagOverrides = new Dictionary<DicomTag, ValidationBehavior>
            {
                { tag, ValidationBehavior.Skip }
            }
        };

        var behavior = profile.GetBehavior(tag);

        Assert.That(behavior, Is.EqualTo(ValidationBehavior.Skip));
    }

    [Test]
    public void GetBehavior_TagNotInOverride_ReturnsDefaultBehavior()
    {
        var overrideTag = new DicomTag(0x0008, 0x0018);
        var otherTag = new DicomTag(0x0010, 0x0010);
        var profile = new ValidationProfile
        {
            DefaultBehavior = ValidationBehavior.Validate,
            TagOverrides = new Dictionary<DicomTag, ValidationBehavior>
            {
                { overrideTag, ValidationBehavior.Skip }
            }
        };

        var behavior = profile.GetBehavior(otherTag);

        Assert.That(behavior, Is.EqualTo(ValidationBehavior.Validate));
    }

    [Test]
    public void CustomProfile_CanBeCreated()
    {
        var customProfile = new ValidationProfile
        {
            Name = "Custom",
            Rules = StandardRules.StructuralOnly,
            DefaultBehavior = ValidationBehavior.Validate,
            TagOverrides = new Dictionary<DicomTag, ValidationBehavior>
            {
                { new DicomTag(0x0010, 0x0020), ValidationBehavior.Skip } // Skip Patient ID validation
            }
        };

        Assert.That(customProfile.Name, Is.EqualTo("Custom"));
        Assert.That(customProfile.Rules.Count, Is.EqualTo(StandardRules.StructuralOnly.Count));
        Assert.That(customProfile.GetBehavior(new DicomTag(0x0010, 0x0020)), Is.EqualTo(ValidationBehavior.Skip));
        Assert.That(customProfile.GetBehavior(new DicomTag(0x0008, 0x0018)), Is.EqualTo(ValidationBehavior.Validate));
    }
}
