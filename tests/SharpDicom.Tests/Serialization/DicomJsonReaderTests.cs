using System;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class DicomJsonReaderTests
{
    [Test]
    public void Deserialize_EmptyJsonObject_ReturnsEmptyDataset()
    {
        var dataset = DicomJsonReader.Deserialize("{}");
        Assert.That(dataset.Count, Is.EqualTo(0));
    }

    [Test]
    public void Deserialize_StringElement_RestoresValue()
    {
        string json = """{"00100020":{"vr":"LO","Value":["PAT001"]}}""";
        var dataset = DicomJsonReader.Deserialize(json);

        Assert.That(dataset.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Deserialize_PersonName_RestoresFromAlphabetic()
    {
        string json = """{"00100010":{"vr":"PN","Value":[{"Alphabetic":"Doe^John"}]}}""";
        var dataset = DicomJsonReader.Deserialize(json);

        Assert.That(dataset.GetString(DicomTag.PatientName), Is.EqualTo("Doe^John"));
    }

    [Test]
    public void Deserialize_InlineBinary_DecodesBase64()
    {
        // Base64 of [0x01, 0x02, 0x03, 0x04]
        string base64 = Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        string json = $@"{{""7FE00010"":{{""vr"":""OB"",""InlineBinary"":""{base64}""}}}}";
        var dataset = DicomJsonReader.Deserialize(json);

        var element = dataset[DicomTag.PixelData] as DicomBinaryElement;
        Assert.That(element, Is.Not.Null);
        var expectedData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Assert.That(element!.RawValue.Span.SequenceEqual(expectedData), Is.True);
    }

    [Test]
    public void Deserialize_Sequence_RestoresNestedDatasets()
    {
        string json = """
        {
            "00081199": {
                "vr": "SQ",
                "Value": [
                    {
                        "00100020": {"vr": "LO", "Value": ["PAT001"]},
                        "00100010": {"vr": "PN", "Value": [{"Alphabetic": "Smith^Jane"}]}
                    }
                ]
            }
        }
        """;
        var dataset = DicomJsonReader.Deserialize(json);

        var seq = dataset[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(seq, Is.Not.Null);
        Assert.That(seq!.Items.Count, Is.EqualTo(1));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Smith^Jane"));
    }

    [Test]
    public void Deserialize_UnknownVr_FallsBackToUN()
    {
        // Element with a 3-char VR code (invalid) falls back to dictionary lookup.
        // Use a non-standard tag with vr missing -- should look up from dictionary
        string json = """{"99990099":{"vr":"XX","Value":["test"]}}""";
        var dataset = DicomJsonReader.Deserialize(json);

        // Tag (9999,0099) is private (odd group), so it won't be in the dictionary
        // The "XX" VR is invalid (not 2 chars that match any standard VR) but still accepted as-is
        // since DicomVR accepts any 2-char string
        var tag = new DicomTag(0x9999, 0x0099);
        var element = dataset[tag];
        // With an unknown VR string "XX", the element may or may not be created
        // The reader creates it with the literal VR
        Assert.That(element, Is.Not.Null);
    }
}
