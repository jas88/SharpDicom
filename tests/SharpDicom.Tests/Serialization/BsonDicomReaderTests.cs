using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class BsonDicomReaderTests
{
    [Test]
    public void Deserialize_MinimalDocument_ReturnsEmptyDataset()
    {
        // Minimal BSON: size=5 + terminator
        var bson = new byte[] { 5, 0, 0, 0, 0 };
        var dataset = BsonDicomReader.Deserialize(bson);
        Assert.That(dataset.Count, Is.EqualTo(0));
    }

    [Test]
    public void Deserialize_StringElement_RestoresValue()
    {
        var original = new DicomDataset();
        original.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Deserialize_DualStorageIS_RestoresFromRaw()
    {
        var original = new DicomDataset();
        original.Add(new DicomStringElement(DicomTag.InstanceNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("042")));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        // Should restore from Raw field, preserving leading zero
        Assert.That(restored.GetString(DicomTag.InstanceNumber), Is.EqualTo("042"));
    }

    [Test]
    public void Deserialize_PersonName_RestoresOriginalString()
    {
        var original = new DicomDataset();
        original.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Smith^John^Q")));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.PatientName), Is.EqualTo("Smith^John^Q"));
    }

    [Test]
    public void Deserialize_NumericElement_RestoresValue()
    {
        var original = new DicomDataset();

        var slBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(slBytes, -12345);
        // Use Rows (0028,0010) as a US tag
        original.Add(new DicomNumericElement(new DicomTag(0x0028, 0x0010), DicomVR.US,
            BitConverter.GetBytes((ushort)512)));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        var element = restored[new DicomTag(0x0028, 0x0010)] as DicomNumericElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.GetUInt16(), Is.EqualTo(512));
    }

    [Test]
    public void Deserialize_Sequence_RestoresNestedDatasets()
    {
        var original = new DicomDataset();
        var item1 = new DicomDataset();
        item1.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        var item2 = new DicomDataset();
        item2.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT002")));

        original.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, item1, item2));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        var seq = restored[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(seq, Is.Not.Null);
        Assert.That(seq!.Items.Count, Is.EqualTo(2));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
        Assert.That(seq.Items[1].GetString(DicomTag.PatientID), Is.EqualTo("PAT002"));
    }

    [Test]
    public void Deserialize_BinaryElement_RestoresBytes()
    {
        var original = new DicomDataset();
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
        original.Add(new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, data));

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        var element = restored[new DicomTag(0x7FE0, 0x0010)] as DicomBinaryElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.RawValue.Span.SequenceEqual(data), Is.True);
    }

    [Test]
    public void Deserialize_PrivateTags_RestoresWithCreator()
    {
        var original = new DicomDataset();
        original.AddPrivateString(0x0009, "TestCreator", 0x01, DicomVR.LO, "PrivateData");

        var bson = BsonDicomWriter.Serialize(original);
        var restored = BsonDicomReader.Deserialize(bson);

        // Find the private data element
        bool found = false;
        foreach (var element in restored)
        {
            if (element.Tag.IsPrivate && !element.Tag.IsPrivateCreator &&
                element is DicomStringElement str && str.GetString() == "PrivateData")
            {
                found = true;
                // Verify creator was registered
                string? creator = restored.PrivateCreators.GetCreator(element.Tag);
                Assert.That(creator, Is.EqualTo("TestCreator"));
                break;
            }
        }
        Assert.That(found, Is.True, "Private element should be restored with creator");
    }

    [Test]
    public void Deserialize_AllTagKeyFormats()
    {
        // Hex8 format
        var options1 = new BsonSerializationOptions { TagKeyFormat = BsonTagKeyFormat.Hex8 };
        var dataset = CreateSimpleDataset();
        var bson1 = BsonDicomWriter.Serialize(dataset, options1);
        var restored1 = BsonDicomReader.Deserialize(bson1, options1);
        Assert.That(restored1.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));

        // Dotted format
        var options2 = new BsonSerializationOptions { TagKeyFormat = BsonTagKeyFormat.Dotted };
        var bson2 = BsonDicomWriter.Serialize(dataset, options2);
        var restored2 = BsonDicomReader.Deserialize(bson2, options2);
        Assert.That(restored2.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));

        // Keyword format
        var options3 = new BsonSerializationOptions { TagKeyFormat = BsonTagKeyFormat.Keyword };
        var bson3 = BsonDicomWriter.Serialize(dataset, options3);
        var restored3 = BsonDicomReader.Deserialize(bson3, options3);
        Assert.That(restored3.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Deserialize_MissingVr_LooksUpInDictionary()
    {
        // When AlwaysIncludeVR is false and the tag is standard non-ambiguous,
        // the VR field is omitted. Reader must look it up from the dictionary.
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        // Default options: VR is not included for PatientID (standard, non-ambiguous)
        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        var element = restored[DicomTag.PatientID] as DicomStringElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.VR, Is.EqualTo(DicomVR.LO));
        Assert.That(element.GetString(), Is.EqualTo("PAT001"));
    }

    private static DicomDataset CreateSimpleDataset()
    {
        var ds = new DicomDataset();
        ds.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        return ds;
    }
}
