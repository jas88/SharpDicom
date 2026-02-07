using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class DicomJsonWriterTests
{
    [Test]
    public void Serialize_EmptyDataset_ReturnsEmptyJsonObject()
    {
        var dataset = new DicomDataset();
        string json = DicomJsonWriter.SerializeToString(dataset);
        Assert.That(json, Is.EqualTo("{}"));
    }

    [Test]
    public void Serialize_StringElement_VrAlwaysPresent()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));

        // Key should be 8-char hex
        Assert.That(root.TryGetProperty("00100020", out var element), Is.True);

        // vr field should always be present in DICOM-JSON
        Assert.That(element.TryGetProperty("vr", out var vrEl), Is.True);
        Assert.That(vrEl.GetString(), Is.EqualTo("LO"));

        // Value array
        Assert.That(element.TryGetProperty("Value", out var valueArr), Is.True);
        Assert.That(valueArr.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(valueArr.GetArrayLength(), Is.EqualTo(1));
        Assert.That(valueArr[0].GetString(), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Serialize_PersonName_AlphabeticObject()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John")));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var pnElement = doc.RootElement.GetProperty("00100010");

        Assert.That(pnElement.GetProperty("vr").GetString(), Is.EqualTo("PN"));

        var valueArr = pnElement.GetProperty("Value");
        Assert.That(valueArr.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(valueArr.GetArrayLength(), Is.EqualTo(1));

        var pnObj = valueArr[0];
        Assert.That(pnObj.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(pnObj.GetProperty("Alphabetic").GetString(), Is.EqualTo("Doe^John"));
    }

    [Test]
    public void Serialize_IntegerString_AsJsonNumber()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("42")));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var isElement = doc.RootElement.GetProperty("00200011");

        Assert.That(isElement.GetProperty("vr").GetString(), Is.EqualTo("IS"));

        var valueArr = isElement.GetProperty("Value");
        Assert.That(valueArr[0].ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(valueArr[0].GetInt64(), Is.EqualTo(42));
    }

    [Test]
    public void Serialize_Sequence_NestedJsonObjects()
    {
        var dataset = new DicomDataset();
        var item = new DicomDataset();
        item.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, item));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var seqElement = doc.RootElement.GetProperty("00081199");

        Assert.That(seqElement.GetProperty("vr").GetString(), Is.EqualTo("SQ"));

        var valueArr = seqElement.GetProperty("Value");
        Assert.That(valueArr.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(valueArr.GetArrayLength(), Is.EqualTo(1));

        // Nested item is a JSON object with DICOM elements
        var itemObj = valueArr[0];
        Assert.That(itemObj.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(itemObj.TryGetProperty("00100020", out _), Is.True);
    }

    [Test]
    public void Serialize_BinaryElement_InlineBinaryBase64()
    {
        var dataset = new DicomDataset();
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OB, data));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("7FE00010");

        Assert.That(element.GetProperty("vr").GetString(), Is.EqualTo("OB"));
        Assert.That(element.TryGetProperty("InlineBinary", out var inlineBin), Is.True);
        Assert.That(inlineBin.ValueKind, Is.EqualTo(JsonValueKind.String));

        // Verify base64 decodes to original data
        byte[] decoded = Convert.FromBase64String(inlineBin.GetString()!);
        Assert.That(decoded, Is.EqualTo(data));
    }

    [Test]
    public void Serialize_BinaryElement_BulkDataURI()
    {
        var dataset = new DicomDataset();
        var largeData = new byte[20000];
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OB, largeData));

        var options = new BsonSerializationOptions
        {
            BinaryInlineThreshold = 1024,
            ExternalBinaryHandler = (tag, data) =>
                BinaryDataReference.ForFile("/dicom/pixeldata/abc123.bin")
        };

        string json = DicomJsonWriter.SerializeToString(dataset, options);

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("7FE00010");

        Assert.That(element.GetProperty("vr").GetString(), Is.EqualTo("OB"));
        Assert.That(element.TryGetProperty("BulkDataURI", out var bulkUri), Is.True);
        Assert.That(bulkUri.GetString(), Is.EqualTo("/dicom/pixeldata/abc123.bin"));
    }

    [Test]
    public void Serialize_EmptyElement_NoValueField()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            ReadOnlyMemory<byte>.Empty));

        string json = DicomJsonWriter.SerializeToString(dataset);

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("00100020");

        // PS3.18 F.2.5: empty elements have vr but omit Value
        Assert.That(element.GetProperty("vr").GetString(), Is.EqualTo("LO"));
        Assert.That(element.TryGetProperty("Value", out _), Is.False,
            "Empty element should not have a Value field");
    }
}
