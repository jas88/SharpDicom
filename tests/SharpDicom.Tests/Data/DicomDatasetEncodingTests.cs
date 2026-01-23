using NUnit.Framework;
using SharpDicom.Data;
using System.Text;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class DicomDatasetEncodingTests
{
    [Test]
    public void EmptyDataset_HasDefaultEncoding()
    {
        var ds = new DicomDataset();

        Assert.That(ds.Encoding, Is.EqualTo(DicomEncoding.Default));
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void AddSpecificCharacterSet_UpdatesEncoding_Utf8()
    {
        var ds = new DicomDataset();
        var element = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 192"));

        ds.Add(element);

        Assert.That(ds.Encoding.IsUtf8Compatible, Is.True);
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(65001)); // UTF-8
    }

    [Test]
    public void AddSpecificCharacterSet_UpdatesEncoding_Latin1()
    {
        var ds = new DicomDataset();
        var element = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));

        ds.Add(element);

        Assert.That(ds.Encoding.IsUtf8Compatible, Is.False);
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    [Test]
    public void RemoveSpecificCharacterSet_ReturnsToDefault()
    {
        var ds = new DicomDataset();
        var element = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));

        ds.Add(element);
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1

        ds.Remove(DicomTag.SpecificCharacterSet);

        Assert.That(ds.Encoding, Is.EqualTo(DicomEncoding.Default));
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }

    [Test]
    public void MultipleSpecificCharacterSet_CreatesEncodingWithExtensions()
    {
        var ds = new DicomDataset();
        // ISO 2022 with Japanese extension
        var element = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("\\ISO 2022 IR 13"));

        ds.Add(element);

        Assert.That(ds.Encoding.HasExtensions, Is.True);
        Assert.That(ds.Encoding.Extensions, Is.Not.Null);
        Assert.That(ds.Encoding.Extensions!.Count, Is.EqualTo(1));
    }

    [Test]
    public void ChildDataset_WithoutSpecificCharacterSet_InheritsParentEncoding()
    {
        var parent = new DicomDataset();
        var parentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        parent.Add(parentCharset);

        var child = new DicomDataset { Parent = parent };

        Assert.That(child.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1 from parent
    }

    [Test]
    public void ChildDataset_WithSpecificCharacterSet_OverridesParentEncoding()
    {
        var parent = new DicomDataset();
        var parentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        parent.Add(parentCharset);

        var child = new DicomDataset { Parent = parent };
        var childCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 192"));
        child.Add(childCharset);

        Assert.That(parent.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
        Assert.That(child.Encoding.Primary.CodePage, Is.EqualTo(65001));  // UTF-8
    }

    [Test]
    public void GrandchildDataset_InheritsFromParent_NotGrandparent()
    {
        var grandparent = new DicomDataset();
        var grandparentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        grandparent.Add(grandparentCharset);

        var parent = new DicomDataset { Parent = grandparent };
        var parentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 192"));
        parent.Add(parentCharset);

        var child = new DicomDataset { Parent = parent };

        Assert.That(child.Encoding.Primary.CodePage, Is.EqualTo(65001)); // UTF-8 from parent
    }

    [Test]
    public void GrandchildDataset_InheritsFromGrandparent_WhenParentHasNoEncoding()
    {
        var grandparent = new DicomDataset();
        var grandparentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        grandparent.Add(grandparentCharset);

        var parent = new DicomDataset { Parent = grandparent };
        // parent has no SpecificCharacterSet - should inherit from grandparent

        var child = new DicomDataset { Parent = parent };

        Assert.That(child.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1 from grandparent
    }

    [Test]
    public void GetString_UsesDatasetEncoding_WhenNoEncodingProvided()
    {
        var ds = new DicomDataset();
        var charset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        ds.Add(charset);

        // Add Latin-1 patient name with umlaut (Müller)
        var latin1Bytes = Encoding.GetEncoding(28591).GetBytes("Müller");
        var patientName = new DicomStringElement(
            DicomTag.PatientName,
            DicomVR.PN,
            latin1Bytes);
        ds.Add(patientName);

        var name = ds.GetString(DicomTag.PatientName);

        Assert.That(name, Is.EqualTo("Müller"));
    }

    [Test]
    public void GetString_ExplicitEncoding_OverridesDatasetEncoding()
    {
        var ds = new DicomDataset();
        var charset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        ds.Add(charset);

        // Add UTF-8 bytes for patient name
        var utf8Bytes = Encoding.UTF8.GetBytes("Müller");
        var patientName = new DicomStringElement(
            DicomTag.PatientName,
            DicomVR.PN,
            utf8Bytes);
        ds.Add(patientName);

        // Explicitly decode as UTF-8 (overriding dataset's Latin-1)
        var name = ds.GetString(DicomTag.PatientName, DicomEncoding.Utf8);

        Assert.That(name, Is.EqualTo("Müller"));
    }

    [Test]
    public void ToOwned_PreservesEncoding()
    {
        var ds = new DicomDataset();
        var charset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        ds.Add(charset);

        var copy = ds.ToOwned();

        Assert.That(copy.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1
    }

    [Test]
    public void ToOwned_CreatesIndependentCopy_NoParentReference()
    {
        var parent = new DicomDataset();
        var parentCharset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        parent.Add(parentCharset);

        var child = new DicomDataset { Parent = parent };

        var copy = child.ToOwned();

        Assert.That(copy.Parent, Is.Null);
        // Copy should have Default encoding (no SpecificCharacterSet, no Parent)
        Assert.That(copy.Encoding, Is.EqualTo(DicomEncoding.Default));
    }

    [Test]
    public void Clear_ResetsEncodingToDefault()
    {
        var ds = new DicomDataset();
        var charset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 100"));
        ds.Add(charset);

        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1

        ds.Clear();

        Assert.That(ds.Encoding, Is.EqualTo(DicomEncoding.Default));
        Assert.That(ds.Encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII
    }
}
