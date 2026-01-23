using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data;

[TestFixture]
public class DicomDatasetTests
{
    [Test]
    public void Add_Element_CanRetrieveByTag()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0020);  // Patient ID
        var element = new DicomStringElement(tag, DicomVR.LO,
            Encoding.ASCII.GetBytes("12345678"));

        dataset.Add(element);

        Assert.That(dataset[tag], Is.Not.Null);
        Assert.That(dataset[tag]!.Tag, Is.EqualTo(tag));
    }

    [Test]
    public void Contains_ExistingTag_ReturnsTrue()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0010);
        dataset.Add(new DicomStringElement(tag, DicomVR.PN, Encoding.ASCII.GetBytes("Doe^John")));

        Assert.That(dataset.Contains(tag), Is.True);
    }

    [Test]
    public void Contains_MissingTag_ReturnsFalse()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0020);

        Assert.That(dataset.Contains(tag), Is.False);
    }

    [Test]
    public void Remove_ExistingElement_ReturnsTrue()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0020);
        dataset.Add(new DicomStringElement(tag, DicomVR.LO, Encoding.ASCII.GetBytes("123")));

        var removed = dataset.Remove(tag);

        Assert.That(removed, Is.True);
        Assert.That(dataset.Contains(tag), Is.False);
    }

    [Test]
    public void Clear_RemovesAllElements()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
            Encoding.ASCII.GetBytes("Test")));
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO,
            Encoding.ASCII.GetBytes("123")));

        dataset.Clear();

        Assert.That(dataset.Count, Is.EqualTo(0));
    }

    [Test]
    public void Count_ReflectsElementCount()
    {
        var dataset = new DicomDataset();
        Assert.That(dataset.Count, Is.EqualTo(0));

        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
            Encoding.ASCII.GetBytes("Test")));
        Assert.That(dataset.Count, Is.EqualTo(1));

        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO,
            Encoding.ASCII.GetBytes("123")));
        Assert.That(dataset.Count, Is.EqualTo(2));
    }

    [Test]
    public void GetString_ReturnsValue()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0008, 0x0060); // Modality
        dataset.Add(new DicomStringElement(tag, DicomVR.CS, Encoding.ASCII.GetBytes("CT")));

        var value = dataset.GetString(tag);

        Assert.That(value, Is.EqualTo("CT"));
    }

    [Test]
    public void GetString_MissingTag_ReturnsNull()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0020);

        var value = dataset.GetString(tag);

        Assert.That(value, Is.Null);
    }

    [Test]
    public void GetInt32_NumericElement_ReturnsValue()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0028, 0x0010); // Rows
        var bytes = new byte[] { 0x00, 0x02, 0x00, 0x00 }; // 512 in little endian
        dataset.Add(new DicomNumericElement(tag, DicomVR.US, bytes));

        var value = dataset.GetInt32(tag);

        Assert.That(value, Is.EqualTo(512));
    }

    [Test]
    public void GetInt32_StringISElement_ReturnsValue()
    {
        var dataset = new DicomDataset();
        var tag = new DicomTag(0x0020, 0x0013); // Instance Number (IS)
        dataset.Add(new DicomStringElement(tag, DicomVR.IS, Encoding.ASCII.GetBytes("42")));

        var value = dataset.GetInt32(tag);

        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void Enumeration_ReturnsSortedByTag()
    {
        var dataset = new DicomDataset();
        // Add in reverse order
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO,
            Encoding.ASCII.GetBytes("ID2")));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0060), DicomVR.CS,
            Encoding.ASCII.GetBytes("CT")));
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
            Encoding.ASCII.GetBytes("Test")));

        var tags = dataset.Select(e => e.Tag).ToArray();

        // Should be sorted: (0008,0060), (0010,0010), (0010,0020)
        Assert.That(tags[0], Is.EqualTo(new DicomTag(0x0008, 0x0060)));
        Assert.That(tags[1], Is.EqualTo(new DicomTag(0x0010, 0x0010)));
        Assert.That(tags[2], Is.EqualTo(new DicomTag(0x0010, 0x0020)));
    }

    [Test]
    public void ToOwned_CreatesIndependentCopy()
    {
        var original = new DicomDataset();
        var tag = new DicomTag(0x0010, 0x0020);
        original.Add(new DicomStringElement(tag, DicomVR.LO, Encoding.ASCII.GetBytes("123")));

        var copy = original.ToOwned();
        copy.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
            Encoding.ASCII.GetBytes("Test")));

        Assert.That(original.Count, Is.EqualTo(1));
        Assert.That(copy.Count, Is.EqualTo(2));
    }

    [Test]
    public void WithElement_ReturnsDatasetForChaining()
    {
        var dataset = new DicomDataset()
            .WithElement(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
                Encoding.ASCII.GetBytes("Doe^John")))
            .WithElement(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO,
                Encoding.ASCII.GetBytes("12345")));

        Assert.That(dataset.Count, Is.EqualTo(2));
    }

    [Test]
    public void PrivateCreatorDictionary_IsAccessible()
    {
        var dataset = new DicomDataset();
        Assert.That(dataset.PrivateCreators, Is.Not.Null);
    }
}
