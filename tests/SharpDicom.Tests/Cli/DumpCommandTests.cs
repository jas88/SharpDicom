using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpDicom.Cli.Output;
using SharpDicom.Data;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class DumpCommandTests
{
    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    private static DicomDataset CreateTestDataset()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Smith^John"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
        return dataset;
    }

    #region TextFormatter Tests

    [Test]
    public void TextFormatter_WritesExpectedFormat()
    {
        var formatter = new TextFormatter(useColor: false);
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        foreach (var element in dataset)
        {
            formatter.WriteElement(element, dictionary, dataset, 0, sw);
        }

        var output = sw.ToString();
        Assert.That(output, Does.Contain("(0010,0010)"));
        Assert.That(output, Does.Contain("PN"));
        Assert.That(output, Does.Contain("[Smith^John]"));
        Assert.That(output, Does.Contain("PatientName"));
    }

    [Test]
    public void TextFormatter_IncludesTagVrValueKeyword()
    {
        var formatter = new TextFormatter(useColor: false);
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        var dateElement = dataset[DicomTag.StudyDate]!;
        formatter.WriteElement(dateElement, dictionary, dataset, 0, sw);

        var line = sw.ToString().TrimEnd();
        // Expected format: (0008,0020) DA [20240115] # StudyDate
        Assert.That(line, Does.Contain("(0008,0020)"));
        Assert.That(line, Does.Contain("DA"));
        Assert.That(line, Does.Contain("[20240115]"));
        Assert.That(line, Does.Contain("# StudyDate"));
    }

    [Test]
    public void TextFormatter_PixelData_ShowsLengthNotData()
    {
        var formatter = new TextFormatter(useColor: false);
        var dataset = new DicomDataset();
        var pixelBytes = new byte[1024];
        var pixelElement = new DicomBinaryElement(DicomTag.PixelData, DicomVR.OW, pixelBytes);
        dataset.Add(pixelElement);

        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();
        formatter.WriteElement(pixelElement, dictionary, dataset, 0, sw);

        var output = sw.ToString();
        Assert.That(output, Does.Contain("(7FE0,0010)"));
        Assert.That(output, Does.Contain("pixel data"));
        Assert.That(output, Does.Contain("length=1024"));
    }

    [Test]
    public void TextFormatter_Sequence_WritesNestedFormat()
    {
        var formatter = new TextFormatter(useColor: false);
        var sw = new StringWriter();

        // Sequence with tag (0008,1115) = ReferencedSeriesSequence
        var seqTag = new DicomTag(0x0008, 0x1115);
        formatter.WriteSequenceStart(seqTag, "ReferencedSeriesSequence", 0, sw);
        formatter.WriteSequenceItemStart(0, 1, sw);
        formatter.WriteSequenceItemEnd(1, sw);
        formatter.WriteSequenceEnd(0, sw);

        var output = sw.ToString();
        Assert.That(output, Does.Contain("(0008,1115)"));
        Assert.That(output, Does.Contain("SQ"));
        Assert.That(output, Does.Contain("ReferencedSeriesSequence"));
        Assert.That(output, Does.Contain("(FFFE,E000)"));
        Assert.That(output, Does.Contain("(FFFE,E00D)"));
        Assert.That(output, Does.Contain("(FFFE,E0DD)"));
    }

    [Test]
    public void TextFormatter_Depth_AddsIndentation()
    {
        var formatter = new TextFormatter(useColor: false);
        var sw = new StringWriter();

        var seqTag = new DicomTag(0x0008, 0x1115);
        formatter.WriteSequenceStart(seqTag, "ReferencedSeriesSequence", 2, sw);

        var output = sw.ToString();
        Assert.That(output, Does.StartWith(">>"));
    }

    #endregion

    #region JsonFormatter Tests

    [Test]
    public void JsonFormatter_WritesValidJson()
    {
        using var formatter = new JsonFormatter();
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        foreach (var element in dataset)
        {
            formatter.WriteElement(element, dictionary, dataset, 0, sw);
        }

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var jsonStr = sw.ToString();
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(root.GetArrayLength(), Is.EqualTo(1));

        var fileObj = root[0];
        Assert.That(fileObj.GetProperty("file").GetString(), Is.EqualTo("test.dcm"));
        Assert.That(fileObj.GetProperty("elements").GetArrayLength(), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void JsonFormatter_ElementHasTagVrKeywordValue()
    {
        using var formatter = new JsonFormatter();
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        var nameElement = dataset[DicomTag.PatientName]!;
        formatter.WriteElement(nameElement, dictionary, dataset, 0, sw);

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var jsonStr = sw.ToString();
        using var doc = JsonDocument.Parse(jsonStr);
        var elements = doc.RootElement[0].GetProperty("elements");
        var first = elements[0];

        Assert.That(first.GetProperty("tag").GetString(), Is.EqualTo("00100010"));
        Assert.That(first.GetProperty("vr").GetString(), Is.EqualTo("PN"));
        Assert.That(first.GetProperty("keyword").GetString(), Is.EqualTo("PatientName"));
        Assert.That(first.GetProperty("value").GetString(), Is.EqualTo("Smith^John"));
    }

    [Test]
    public void JsonFormatter_Sequence_HasItemsArray()
    {
        using var formatter = new JsonFormatter();
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        var seqTag = new DicomTag(0x0008, 0x1115);
        formatter.WriteSequenceStart(seqTag, "ReferencedSeriesSequence", 0, sw);
        formatter.WriteSequenceItemStart(0, 1, sw);
        formatter.WriteSequenceItemEnd(1, sw);
        formatter.WriteSequenceEnd(0, sw);

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var jsonStr = sw.ToString();
        using var doc = JsonDocument.Parse(jsonStr);
        var elements = doc.RootElement[0].GetProperty("elements");
        var seqElement = elements[0];

        Assert.That(seqElement.GetProperty("vr").GetString(), Is.EqualTo("SQ"));
        Assert.That(seqElement.GetProperty("keyword").GetString(), Is.EqualTo("ReferencedSeriesSequence"));
        Assert.That(seqElement.TryGetProperty("items", out var items), Is.True);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
    }

    #endregion

    #region XmlFormatter Tests

    [Test]
    public void XmlFormatter_WritesWellFormedXml()
    {
        using var formatter = new XmlFormatter();
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        foreach (var element in dataset)
        {
            formatter.WriteElement(element, dictionary, dataset, 0, sw);
        }

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var xmlStr = sw.ToString();
        var doc = XDocument.Parse(xmlStr);

        Assert.That(doc.Root, Is.Not.Null);
        Assert.That(doc.Root!.Name.LocalName, Is.EqualTo("DicomFiles"));
    }

    [Test]
    public void XmlFormatter_ElementHasAttributes()
    {
        using var formatter = new XmlFormatter();
        var dataset = CreateTestDataset();
        var dictionary = DicomDictionary.Default;
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        var nameElement = dataset[DicomTag.PatientName]!;
        formatter.WriteElement(nameElement, dictionary, dataset, 0, sw);

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var xmlStr = sw.ToString();
        var doc = XDocument.Parse(xmlStr);
        var dicomFile = doc.Root!.Element("DicomFile");
        Assert.That(dicomFile, Is.Not.Null);
        Assert.That(dicomFile!.Attribute("path")?.Value, Is.EqualTo("test.dcm"));

        var element = dicomFile.Element("Element");
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.Attribute("tag")?.Value, Is.EqualTo("00100010"));
        Assert.That(element.Attribute("vr")?.Value, Is.EqualTo("PN"));
        Assert.That(element.Attribute("keyword")?.Value, Is.EqualTo("PatientName"));
        Assert.That(element.Value, Is.EqualTo("Smith^John"));
    }

    [Test]
    public void XmlFormatter_Sequence_HasNestedElements()
    {
        using var formatter = new XmlFormatter();
        var sw = new StringWriter();

        formatter.WriteBatchHeader(sw);
        formatter.WriteFileHeader("test.dcm", sw);

        var seqTag = new DicomTag(0x0008, 0x1115);
        formatter.WriteSequenceStart(seqTag, "ReferencedSeriesSequence", 0, sw);
        formatter.WriteSequenceItemStart(0, 1, sw);
        formatter.WriteSequenceItemEnd(1, sw);
        formatter.WriteSequenceEnd(0, sw);

        formatter.WriteFileFooter(sw);
        formatter.WriteBatchFooter(sw);

        var xmlStr = sw.ToString();
        var doc = XDocument.Parse(xmlStr);
        var dicomFile = doc.Root!.Element("DicomFile");
        var seq = dicomFile!.Element("Sequence");

        Assert.That(seq, Is.Not.Null);
        Assert.That(seq!.Attribute("keyword")?.Value, Is.EqualTo("ReferencedSeriesSequence"));
        Assert.That(seq.Elements("Item").Count(), Is.EqualTo(1));
    }

    #endregion
}
