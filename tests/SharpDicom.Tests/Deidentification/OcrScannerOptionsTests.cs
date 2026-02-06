using System;
using NUnit.Framework;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
[Category("Deidentification")]
public class OcrScannerOptionsTests
{
    [Test]
    public void DefaultNonPhiAllowlist_ContainsOrientationMarkers()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        Assert.That(allowlist, Does.Contain("L"));
        Assert.That(allowlist, Does.Contain("R"));
        Assert.That(allowlist, Does.Contain("P"));
        Assert.That(allowlist, Does.Contain("A"));
        Assert.That(allowlist, Does.Contain("S"));
        Assert.That(allowlist, Does.Contain("I"));
        Assert.That(allowlist, Does.Contain("H"));
        Assert.That(allowlist, Does.Contain("F"));
    }

    [Test]
    public void DefaultNonPhiAllowlist_ContainsMeasurementUnits()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        Assert.That(allowlist, Does.Contain("cm"));
        Assert.That(allowlist, Does.Contain("mm"));
        Assert.That(allowlist, Does.Contain("Hz"));
        Assert.That(allowlist, Does.Contain("bpm"));
        Assert.That(allowlist, Does.Contain("dB"));
        Assert.That(allowlist, Does.Contain("ms"));
    }

    [Test]
    public void DefaultNonPhiAllowlist_DoesNotContainNames()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        Assert.That(allowlist, Does.Not.Contain("John"));
        Assert.That(allowlist, Does.Not.Contain("Smith"));
        Assert.That(allowlist, Does.Not.Contain("Jane"));
        Assert.That(allowlist, Does.Not.Contain("Doe"));
    }

    [Test]
    public void ScanModalities_Default_IsHighRiskAndModerateRisk()
    {
        var options = new OcrScannerOptions();

        var expected = OcrScanModality.HighRisk | OcrScanModality.ModerateRisk;
        Assert.That(options.ScanModalities, Is.EqualTo(expected));
    }

    [Test]
    public void ConfidenceThreshold_DefaultIs0Point6()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.ConfidenceThreshold, Is.EqualTo(0.6f));
    }

    [Test]
    public void EdgeConfidenceThreshold_DefaultIs0Point4()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.EdgeConfidenceThreshold, Is.EqualTo(0.4f));
    }

    [Test]
    public void PageSegMode_DefaultIs11()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.PageSegMode, Is.EqualTo(11));
    }

    [Test]
    public void MaxDetectionsPerFrame_DefaultIs200()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.MaxDetectionsPerFrame, Is.EqualTo(200));
    }

    [Test]
    public void DecompressForOcr_DefaultIsTrue()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.DecompressForOcr, Is.True);
    }

    [Test]
    public void DefaultNonPhiAllowlist_IsCaseInsensitive()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        // The default allowlist uses OrdinalIgnoreCase comparer
        Assert.That(allowlist.Contains("l"), Is.True, "Should match 'l' (lowercase of 'L')");
        Assert.That(allowlist.Contains("CM"), Is.True, "Should match 'CM' (uppercase of 'cm')");
        Assert.That(allowlist.Contains("gain"), Is.True, "Should match 'gain' (lowercase of 'GAIN')");
    }

    [Test]
    public void DefaultNonPhiAllowlist_ContainsMedicalAbbreviations()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        Assert.That(allowlist, Does.Contain("HR"));
        Assert.That(allowlist, Does.Contain("BP"));
        Assert.That(allowlist, Does.Contain("SpO2"));
        Assert.That(allowlist, Does.Contain("ECG"));
    }

    [Test]
    public void DefaultNonPhiAllowlist_ContainsImagingLabels()
    {
        var allowlist = OcrScannerOptions.DefaultNonPhiAllowlist;

        Assert.That(allowlist, Does.Contain("GAIN"));
        Assert.That(allowlist, Does.Contain("DEPTH"));
        Assert.That(allowlist, Does.Contain("FREQ"));
    }

    [Test]
    public void Allowlist_DefaultIsNotNull()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.Allowlist, Is.Not.Null);
        Assert.That(options.Allowlist.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Denylist_DefaultIsNull()
    {
        var options = new OcrScannerOptions();

        Assert.That(options.Denylist, Is.Null);
    }
}
