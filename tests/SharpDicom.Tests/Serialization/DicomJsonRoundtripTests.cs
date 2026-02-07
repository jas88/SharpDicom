using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class DicomJsonRoundtripTests
{
    [Test]
    public void Roundtrip_AllStringVRs()
    {
        var dataset = new DicomDataset();

        // AE
        dataset.Add(new DicomStringElement(new DicomTag(0x0040, 0x0001), DicomVR.AE,
            Encoding.UTF8.GetBytes("MY_AET")));
        // CS
        dataset.Add(new DicomStringElement(DicomTag.Modality, DicomVR.CS,
            Encoding.UTF8.GetBytes("CT")));
        // LO
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        // SH
        dataset.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH,
            Encoding.UTF8.GetBytes("ACC123")));
        // UI
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.840.113619.2.1")));
        // PN
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John")));
        // LT
        var ltTag = new DicomTag(0x0010, 0x21B0);
        dataset.Add(new DicomStringElement(ltTag, DicomVR.LT,
            Encoding.UTF8.GetBytes("Patient history text")));

        AssertJsonRoundtrip(dataset);
    }

    [Test]
    public void Roundtrip_AllNumericVRs()
    {
        var dataset = new DicomDataset();

        // US - Rows
        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 512);
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, usBytes));

        // SS - SmallestImagePixelValue (multi-VR)
        var ssBytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(ssBytes, -100);
        dataset.Add(new DicomNumericElement(DicomTag.SmallestImagePixelValue, DicomVR.SS, ssBytes));

        // UL
        var ulTag = new DicomTag(0x0008, 0x1161);
        var ulBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ulBytes, 50000);
        dataset.Add(new DicomNumericElement(ulTag, DicomVR.UL, ulBytes));

        // SL
        var slTag = new DicomTag(0x0018, 0x1310);
        var slBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(slBytes, -12345);
        dataset.Add(new DicomNumericElement(slTag, DicomVR.SL, slBytes));

        // FL
        var flTag = new DicomTag(0x0018, 0x1151);
        var flBytes = new byte[4];
#if NETSTANDARD2_0
        Buffer.BlockCopy(BitConverter.GetBytes(2.5f), 0, flBytes, 0, 4);
#else
        BinaryPrimitives.WriteSingleLittleEndian(flBytes, 2.5f);
#endif
        dataset.Add(new DicomNumericElement(flTag, DicomVR.FL, flBytes));

        // FD
        var fdTag = new DicomTag(0x0018, 0x0088);
        var fdBytes = new byte[8];
        long fdBits = BitConverter.DoubleToInt64Bits(3.14159);
        BinaryPrimitives.WriteInt64LittleEndian(fdBytes, fdBits);
        dataset.Add(new DicomNumericElement(fdTag, DicomVR.FD, fdBytes));

        AssertJsonRoundtrip(dataset);
    }

    [Test]
    public void Roundtrip_BinaryVRs()
    {
        var dataset = new DicomDataset();
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OB, data));

        AssertJsonRoundtrip(dataset);
    }

    [Test]
    public void Roundtrip_Sequence()
    {
        var dataset = new DicomDataset();
        var item = new DicomDataset();
        item.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("SEQ_PAT")));
        item.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Smith^Jane")));

        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, item));

        var json = DicomJsonWriter.SerializeToString(dataset);
        var restored = DicomJsonReader.Deserialize(json);

        var seq = restored[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(seq, Is.Not.Null);
        Assert.That(seq!.Items.Count, Is.EqualTo(1));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("SEQ_PAT"));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Smith^Jane"));
    }

    [Test]
    public void Roundtrip_PersonName()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John^M")));

        var json = DicomJsonWriter.SerializeToString(dataset);
        var restored = DicomJsonReader.Deserialize(json);

        Assert.That(restored.GetString(DicomTag.PatientName), Is.EqualTo("Doe^John^M"));
    }

    [Test]
    public void Roundtrip_MultiValuedElements()
    {
        var dataset = new DicomDataset();

        // Multi-valued CS
        dataset.Add(new DicomStringElement(DicomTag.ImageType, DicomVR.CS,
            Encoding.UTF8.GetBytes("ORIGINAL\\PRIMARY")));

        var json = DicomJsonWriter.SerializeToString(dataset);
        var restored = DicomJsonReader.Deserialize(json);

        var element = restored[DicomTag.ImageType] as DicomStringElement;
        Assert.That(element, Is.Not.Null);
        var strings = element!.GetStrings();
        Assert.That(strings, Is.Not.Null);
        Assert.That(strings!.Length, Is.EqualTo(2));
        Assert.That(strings[0], Is.EqualTo("ORIGINAL"));
        Assert.That(strings[1], Is.EqualTo("PRIMARY"));
    }

    [Test]
    public void Roundtrip_EmptyElements()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            ReadOnlyMemory<byte>.Empty));
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US,
            ReadOnlyMemory<byte>.Empty));

        var json = DicomJsonWriter.SerializeToString(dataset);
        var restored = DicomJsonReader.Deserialize(json);

        var strEl = restored[DicomTag.PatientID];
        Assert.That(strEl, Is.Not.Null);
        Assert.That(strEl!.IsEmpty, Is.True);

        var numEl = restored[DicomTag.Rows];
        Assert.That(numEl, Is.Not.Null);
        Assert.That(numEl!.IsEmpty, Is.True);
    }

    [Test]
    public void Roundtrip_LargeDataset_AllTypesPresent()
    {
        var dataset = new DicomDataset();

        // String VRs
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Smith^Alice")));
        dataset.Add(new DicomStringElement(DicomTag.Modality, DicomVR.CS,
            Encoding.UTF8.GetBytes("CT")));
        dataset.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH,
            Encoding.UTF8.GetBytes("ACC001")));
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.3.4.5")));
        dataset.Add(new DicomStringElement(DicomTag.SOPClassUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.840.10008.5.1.4.1.1.2")));
        dataset.Add(new DicomStringElement(DicomTag.StudyDescription, DicomVR.LO,
            Encoding.UTF8.GetBytes("Brain MRI")));

        // IS
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("3")));

        // DS
        var windowCenter = new DicomTag(0x0028, 0x1050);
        dataset.Add(new DicomStringElement(windowCenter, DicomVR.DS,
            Encoding.UTF8.GetBytes("40.0")));

        // Numeric
        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 256);
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, usBytes));

        var usBytes2 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes2, 256);
        dataset.Add(new DicomNumericElement(DicomTag.Columns, DicomVR.US, usBytes2));

        // Binary
        var pixelData = new byte[32];
        for (int i = 0; i < pixelData.Length; i++)
            pixelData[i] = (byte)(i & 0xFF);
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OB, pixelData));

        // Sequence
        var seqItem = new DicomDataset();
        seqItem.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("REF001")));
        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, seqItem));

        AssertJsonRoundtrip(dataset);
    }

    /// <summary>
    /// Verifies DICOM-JSON roundtrip: serialize to JSON, validate JSON structure, deserialize, compare.
    /// </summary>
    private static void AssertJsonRoundtrip(DicomDataset original)
    {
        var json = DicomJsonWriter.SerializeToString(original);

        // Validate JSON structure
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));

        foreach (var property in root.EnumerateObject())
        {
            // Each key should be 8-char hex
            Assert.That(property.Name.Length, Is.EqualTo(8),
                $"Key '{property.Name}' is not 8-char hex");

            // Each value should be an object
            Assert.That(property.Value.ValueKind, Is.EqualTo(JsonValueKind.Object),
                $"Value for key '{property.Name}' is not an object");

            // Each element object should have a "vr" field
            Assert.That(property.Value.TryGetProperty("vr", out var vrEl), Is.True,
                $"Element '{property.Name}' missing 'vr' field");
            Assert.That(vrEl.ValueKind, Is.EqualTo(JsonValueKind.String));

            // If Value present, it should be an array
            if (property.Value.TryGetProperty("Value", out var valueEl))
            {
                Assert.That(valueEl.ValueKind, Is.EqualTo(JsonValueKind.Array),
                    $"'Value' for element '{property.Name}' is not an array");
            }
        }

        // Deserialize and compare
        var restored = DicomJsonReader.Deserialize(json);

        foreach (var origElement in original)
        {
            if (origElement.Tag.IsPrivateCreator) continue;

            var restoredElement = restored[origElement.Tag];
            Assert.That(restoredElement, Is.Not.Null,
                $"Tag {origElement.Tag} not found in restored dataset");

            if (origElement is DicomStringElement origStr && restoredElement is DicomStringElement restoredStr)
            {
                // PN values are reconstructed from component objects -- compare reconstructed
                // IS/DS values lose their original string form through JSON number encoding
                if (origStr.VR != DicomVR.IS && origStr.VR != DicomVR.DS)
                {
                    Assert.That(restoredStr.GetString(), Is.EqualTo(origStr.GetString()),
                        $"String value mismatch for tag {origElement.Tag}");
                }
            }
            else if (origElement is DicomNumericElement origNum && restoredElement is DicomNumericElement restoredNum)
            {
                Assert.That(restoredNum.RawValue.Span.SequenceEqual(origNum.RawValue.Span), Is.True,
                    $"Numeric raw value mismatch for tag {origElement.Tag}");
            }
            else if (origElement is DicomBinaryElement origBin && restoredElement is DicomBinaryElement restoredBin)
            {
                Assert.That(restoredBin.RawValue.Span.SequenceEqual(origBin.RawValue.Span), Is.True,
                    $"Binary raw value mismatch for tag {origElement.Tag}");
            }
            else if (origElement is DicomSequence origSeq && restoredElement is DicomSequence restoredSeq)
            {
                Assert.That(restoredSeq.Items.Count, Is.EqualTo(origSeq.Items.Count),
                    $"Sequence item count mismatch for tag {origElement.Tag}");
            }
        }
    }
}
