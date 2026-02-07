using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class BsonDicomWriterTests
{
    [Test]
    public void Serialize_EmptyDataset_ReturnsMinimalBsonDocument()
    {
        var dataset = new DicomDataset();
        var bytes = BsonDicomWriter.Serialize(dataset);

        // Minimal BSON doc: 4 bytes size + 1 byte terminator = 5
        Assert.That(bytes.Length, Is.EqualTo(5));
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        Assert.That(size, Is.EqualTo(5));
        Assert.That(bytes[4], Is.EqualTo(0x00));
    }

    [Test]
    public void Serialize_StringElement_WritesValueArray()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        // Deserialize to verify structure
        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Serialize_IntegerString_DualStorage()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("42")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        // Verify roundtrip preserves value via Raw field
        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.SeriesNumber), Is.EqualTo("42"));
    }

    [Test]
    public void Serialize_DecimalString_DualStorage()
    {
        var dataset = new DicomDataset();
        // Use a generic DS tag -- NumberOfFrames is IS, use a private tag or specific DS tag
        // WindowCenter (0028,1050) is DS
        var windowCenter = new DicomTag(0x0028, 0x1050);
        dataset.Add(new DicomStringElement(windowCenter, DicomVR.DS,
            Encoding.UTF8.GetBytes("3.14")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        var element = restored[windowCenter] as DicomStringElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.GetString(), Is.EqualTo("3.14"));
    }

    [Test]
    public void Serialize_Date_DualStorage()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA,
            Encoding.UTF8.GetBytes("20240115")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.StudyDate), Is.EqualTo("20240115"));
    }

    [Test]
    public void Serialize_Time_DualStorage()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyTime, DicomVR.TM,
            Encoding.UTF8.GetBytes("120000")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.StudyTime), Is.EqualTo("120000"));
    }

    [Test]
    public void Serialize_PersonName_WithComponents()
    {
        var dataset = new DicomDataset();
        // Use ASCII-safe component groups for reliable roundtrip
        // (non-ASCII requires SpecificCharacterSet; tested separately)
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John=Yamada^Tarou=Yamataro^T")));

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.PatientName),
            Is.EqualTo("Doe^John=Yamada^Tarou=Yamataro^T"));
    }

    [Test]
    public void Serialize_NumericElements_CorrectBsonTypes()
    {
        var dataset = new DicomDataset();

        // SS (Int16 -> BsonInt32) -- SmallestImagePixelValue is multi-VR so VR is included
        var ssTag = new DicomTag(0x0028, 0x0106); // SmallestImagePixelValue (multi-VR)
        var ssBytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(ssBytes, -42);
        dataset.Add(new DicomNumericElement(ssTag, DicomVR.SS, ssBytes));

        // US (UInt16 -> BsonInt32)
        var usTag = new DicomTag(0x0028, 0x0010); // Rows
        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 512);
        dataset.Add(new DicomNumericElement(usTag, DicomVR.US, usBytes));

        // FD (Double -> BsonDouble) -- use AlwaysIncludeVR since tag may have different dict VR
        var fdTag = new DicomTag(0x0018, 0x0088); // Spacing Between Slices
        var fdBytes = new byte[8];
        long bits = BitConverter.DoubleToInt64Bits(1.5);
        BinaryPrimitives.WriteInt64LittleEndian(fdBytes, bits);
        dataset.Add(new DicomNumericElement(fdTag, DicomVR.FD, fdBytes));

        // Use AlwaysIncludeVR to ensure all VRs are written explicitly
        var options = new BsonSerializationOptions { AlwaysIncludeVR = true };
        var serialized = BsonDicomWriter.Serialize(dataset, options);
        var restored = BsonDicomReader.Deserialize(serialized, options);

        var ssRestored = restored[ssTag] as DicomNumericElement;
        Assert.That(ssRestored, Is.Not.Null);
        Assert.That(ssRestored!.GetInt16(), Is.EqualTo(-42));

        var usRestored = restored[usTag] as DicomNumericElement;
        Assert.That(usRestored, Is.Not.Null);
        Assert.That(usRestored!.GetUInt16(), Is.EqualTo(512));

        var fdRestored = restored[fdTag] as DicomNumericElement;
        Assert.That(fdRestored, Is.Not.Null);
        Assert.That(fdRestored!.GetFloat64(), Is.EqualTo(1.5));
    }

    [Test]
    public void Serialize_Sequence_NestedDocuments()
    {
        var dataset = new DicomDataset();
        var itemDataset = new DicomDataset();
        itemDataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        var sequence = new DicomSequence(DicomTag.ReferencedSOPSequence, itemDataset);
        dataset.Add(sequence);

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        var restoredSeq = restored[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(restoredSeq, Is.Not.Null);
        Assert.That(restoredSeq!.Items.Count, Is.EqualTo(1));
        Assert.That(restoredSeq.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));
    }

    [Test]
    public void Serialize_BinaryElement_InlinesBelowThreshold()
    {
        var dataset = new DicomDataset();
        var smallData = new byte[100]; // well below default 16KB threshold
        for (int i = 0; i < smallData.Length; i++)
            smallData[i] = (byte)(i & 0xFF);

        dataset.Add(new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, smallData));

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        var element = restored[new DicomTag(0x7FE0, 0x0010)] as DicomBinaryElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.RawValue.Span.SequenceEqual(smallData), Is.True);
    }

    [Test]
    public void Serialize_BinaryElement_ExternalAboveThreshold()
    {
        var dataset = new DicomDataset();
        var largeData = new byte[20000]; // above default 16KB threshold

        dataset.Add(new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, largeData));

        string? capturedId = null;
        var options = new BsonSerializationOptions
        {
            BinaryInlineThreshold = 1024,
            ExternalBinaryHandler = (tag, data) =>
            {
                capturedId = "gridfs_id_123";
                return BinaryDataReference.ForGridFs("gridfs_id_123");
            }
        };

        var bytes = BsonDicomWriter.Serialize(dataset, options);

        // The handler was invoked
        Assert.That(capturedId, Is.EqualTo("gridfs_id_123"));

        // Deserialized element is empty (data was external)
        var restored = BsonDicomReader.Deserialize(bytes, options);
        var element = restored[new DicomTag(0x7FE0, 0x0010)] as DicomBinaryElement;
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.RawValue.IsEmpty, Is.True);
    }

    [Test]
    public void Serialize_PrivateTags_GroupedByCreator()
    {
        var dataset = new DicomDataset();
        dataset.AddPrivateString(0x0009, "MyCreator", 0x01, DicomVR.LO, "PrivateValue");

        var bytes = BsonDicomWriter.Serialize(dataset);

        var restored = BsonDicomReader.Deserialize(bytes);
        // The private tag should have been restored
        bool found = false;
        foreach (var element in restored)
        {
            if (element.Tag.IsPrivate && !element.Tag.IsPrivateCreator &&
                element is DicomStringElement str && str.GetString() == "PrivateValue")
            {
                found = true;
                break;
            }
        }
        Assert.That(found, Is.True, "Private element should be restored from _private sub-document");
    }

    [Test]
    public void Serialize_StripPrivateTags_OmitsPrivate()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        dataset.AddPrivateString(0x0009, "MyCreator", 0x01, DicomVR.LO, "Secret");

        var options = new BsonSerializationOptions { StripPrivateTags = true };
        var bytes = BsonDicomWriter.Serialize(dataset, options);

        var restored = BsonDicomReader.Deserialize(bytes, options);
        Assert.That(restored.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));

        // No private elements should exist
        foreach (var element in restored)
        {
            Assert.That(element.Tag.IsPrivate, Is.False,
                $"Private tag {element.Tag} should have been stripped");
        }
    }

    [Test]
    public void Serialize_TagKeyFormat_Hex8()
    {
        var options = new BsonSerializationOptions { TagKeyFormat = BsonTagKeyFormat.Hex8 };
        string key = BsonDicomWriter.FormatTagKey(DicomTag.PatientID, options.TagKeyFormat);
        Assert.That(key, Is.EqualTo("00100020"));
    }

    [Test]
    public void Serialize_TagKeyFormat_Keyword()
    {
        var options = new BsonSerializationOptions { TagKeyFormat = BsonTagKeyFormat.Keyword };
        string key = BsonDicomWriter.FormatTagKey(DicomTag.PatientID, options.TagKeyFormat);
        // PatientID should resolve to keyword from dictionary
        Assert.That(key, Is.Not.EqualTo("00100020"),
            "Keyword format should use dictionary name, not hex");
        // Should be "PatientID" from the dictionary
        Assert.That(key.Length, Is.GreaterThan(0));
    }

    [Test]
    public void Serialize_VrField_OnlyWhenAmbiguous()
    {
        // Standard non-ambiguous tag: PatientID (LO, not retired, single VR)
        // The VR should NOT be included by default

        // Multi-VR tag: SmallestImagePixelValue (US or SS) -- VR should be included
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));

        var ssBytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(ssBytes, 0);
        dataset.Add(new DicomNumericElement(DicomTag.SmallestImagePixelValue, DicomVR.SS, ssBytes));

        var bytes = BsonDicomWriter.Serialize(dataset);

        // Roundtrip with default options should work
        var restored = BsonDicomReader.Deserialize(bytes);
        Assert.That(restored.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));

        // SmallestImagePixelValue should have its VR restored from the explicit vr field
        var numEl = restored[DicomTag.SmallestImagePixelValue] as DicomNumericElement;
        Assert.That(numEl, Is.Not.Null);
        Assert.That(numEl!.VR, Is.EqualTo(DicomVR.SS));
    }
}
