using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

[TestFixture]
public class DummyValueGeneratorTests
{
    [Test]
    public void GetDummy_PersonName_ReturnsAnonymous()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.PN);
        var str = System.Text.Encoding.ASCII.GetString(dummy);
        Assert.That(str, Is.EqualTo("ANONYMOUS"));
    }

    [Test]
    public void GetDummy_Date_ReturnsValidDate()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.DA);
        var str = System.Text.Encoding.ASCII.GetString(dummy);
        Assert.That(str, Is.EqualTo("19000101"));
        Assert.That(str, Has.Length.EqualTo(8));
    }

    [Test]
    public void GetDummy_Time_ReturnsValidTime()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.TM);
        var str = System.Text.Encoding.ASCII.GetString(dummy);
        Assert.That(str, Does.StartWith("000000"));
    }

    [Test]
    public void GetDummy_AgeString_ReturnsValidAge()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.AS);
        var str = System.Text.Encoding.ASCII.GetString(dummy);
        Assert.That(str, Has.Length.EqualTo(4));
        Assert.That(str, Does.EndWith("Y").Or.EndWith("M").Or.EndWith("W").Or.EndWith("D"));
    }

    [Test]
    public void GetDummy_UniqueIdentifier_ReturnsValidUid()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.UI);
        var str = System.Text.Encoding.ASCII.GetString(dummy);
        Assert.That(str, Does.StartWith("2.25."));
    }

    [Test]
    public void GetDummy_UnsignedShort_ReturnsZero()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.US);
        Assert.That(dummy, Has.Length.EqualTo(2));
        var value = System.BitConverter.ToUInt16(dummy, 0);
        Assert.That(value, Is.EqualTo(0));
    }

    [Test]
    public void GetDummy_Float_ReturnsZero()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.FL);
        Assert.That(dummy, Has.Length.EqualTo(4));
        var value = System.BitConverter.ToSingle(dummy, 0);
        Assert.That(value, Is.EqualTo(0.0f));
    }

    [Test]
    public void GetDummy_OtherByte_ReturnsEmpty()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.OB);
        Assert.That(dummy, Is.Empty);
    }

    [Test]
    public void GetDummy_Sequence_ReturnsEmpty()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.SQ);
        Assert.That(dummy, Is.Empty);
    }

    [Test]
    public void GetDummy_Unknown_ReturnsEmpty()
    {
        var dummy = DummyValueGenerator.GetDummy(DicomVR.UN);
        Assert.That(dummy, Is.Empty);
    }

    [Test]
    public void GetDummyString_StringVR_ReturnsString()
    {
        var dummy = DummyValueGenerator.GetDummyString(DicomVR.LO);
        Assert.That(dummy, Is.Not.Null);
        Assert.That(dummy, Is.EqualTo("ANONYMIZED"));
    }

    [Test]
    public void GetDummyString_NumericVR_ReturnsNull()
    {
        var dummy = DummyValueGenerator.GetDummyString(DicomVR.US);
        Assert.That(dummy, Is.Null);
    }

    [Test]
    public void GetDummyDate_ReturnsExpectedFormat()
    {
        var date = DummyValueGenerator.GetDummyDate();
        Assert.That(date, Is.EqualTo("19000101"));
    }

    [Test]
    public void GetDummyPersonName_ReturnsAnonymous()
    {
        var name = DummyValueGenerator.GetDummyPersonName();
        Assert.That(name, Is.EqualTo("ANONYMOUS"));
    }

    [Test]
    public void GetDummyUid_Returns225Prefix()
    {
        var uid = DummyValueGenerator.GetDummyUid();
        Assert.That(uid, Does.StartWith("2.25."));
    }
}
