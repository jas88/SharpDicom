using System.Text;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class DicomDictionaryTests
{
    [Test]
    public void CanLookupCommonTagInDictionary()
    {
        // Patient ID (0010,0020)
        var patientIdTag = new DicomTag(0x0010, 0x0020);
        var entry = DicomDictionary.Default.GetEntry(patientIdTag);

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Value.Keyword, Is.EqualTo("PatientID"));
        Assert.That(entry.Value.DefaultVR, Is.EqualTo(DicomVR.LO));
    }

    [Test]
    public void DictionaryContainsCommonTags()
    {
        var patientName = DicomDictionary.Default.GetEntry(new DicomTag(0x0010, 0x0010));
        var studyDate = DicomDictionary.Default.GetEntry(new DicomTag(0x0008, 0x0020));
        var modality = DicomDictionary.Default.GetEntry(new DicomTag(0x0008, 0x0060));

        Assert.That(patientName, Is.Not.Null);
        Assert.That(studyDate, Is.Not.Null);
        Assert.That(modality, Is.Not.Null);
    }

    [Test]
    public void GetEntryByKeyword_ReturnsCorrectTag()
    {
        var entry = DicomDictionary.Default.GetEntryByKeyword("PatientID");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Value.Tag, Is.EqualTo(new DicomTag(0x0010, 0x0020)));
    }

    [Test]
    public void UnknownTag_ReturnsNull()
    {
        var unknownTag = new DicomTag(0xFFFF, 0xFFFF);
        var entry = DicomDictionary.Default.GetEntry(unknownTag);
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void PrivateCreatorDictionary_RegisterAndRetrieve()
    {
        var dict = new PrivateCreatorDictionary();
        var creatorTag = new DicomTag(0x0009, 0x0010);  // Private creator

        dict.Register(creatorTag, "ACME_CORP");

        var creator = dict.GetCreator(new DicomTag(0x0009, 0x1001));
        Assert.That(creator, Is.EqualTo("ACME_CORP"));
    }

    [Test]
    public void PrivateCreatorDictionary_HasCreator()
    {
        var dict = new PrivateCreatorDictionary();
        var creatorTag = new DicomTag(0x0009, 0x0010);
        dict.Register(creatorTag, "VENDOR");

        Assert.That(dict.HasCreator(new DicomTag(0x0009, 0x1020)), Is.True);
        Assert.That(dict.HasCreator(new DicomTag(0x0009, 0x2020)), Is.False);
    }

    [Test]
    public void PrivateCreatorDictionary_Clear()
    {
        var dict = new PrivateCreatorDictionary();
        dict.Register(new DicomTag(0x0009, 0x0010), "VENDOR");

        dict.Clear();

        Assert.That(dict.HasCreator(new DicomTag(0x0009, 0x1001)), Is.False);
    }
}
