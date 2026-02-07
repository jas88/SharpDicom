using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class BsonRoundtripTests
{
    [Test]
    public void Roundtrip_AllStringVRs()
    {
        var dataset = new DicomDataset();

        // AE - Application Entity
        dataset.Add(new DicomStringElement(new DicomTag(0x0040, 0x0001), DicomVR.AE,
            Encoding.UTF8.GetBytes("MY_AET")));
        // AS - Age String
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x1010), DicomVR.AS,
            Encoding.UTF8.GetBytes("045Y")));
        // CS - Code String
        dataset.Add(new DicomStringElement(DicomTag.Modality, DicomVR.CS,
            Encoding.UTF8.GetBytes("CT")));
        // DA - Date
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA,
            Encoding.UTF8.GetBytes("20240115")));
        // DS - Decimal String
        var windowCenter = new DicomTag(0x0028, 0x1050);
        dataset.Add(new DicomStringElement(windowCenter, DicomVR.DS,
            Encoding.UTF8.GetBytes("40.0")));
        // DT - DateTime
        var dtTag = new DicomTag(0x0008, 0x002A); // Acquisition DateTime
        dataset.Add(new DicomStringElement(dtTag, DicomVR.DT,
            Encoding.UTF8.GetBytes("20240115120000")));
        // IS - Integer String
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("1")));
        // LO - Long String
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        // LT - Long Text
        var ltTag = new DicomTag(0x0010, 0x21B0); // Additional Patient History
        dataset.Add(new DicomStringElement(ltTag, DicomVR.LT,
            Encoding.UTF8.GetBytes("Patient has a history of cardiac issues.")));
        // PN - Person Name
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John")));
        // SH - Short String
        dataset.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH,
            Encoding.UTF8.GetBytes("ACC12345")));
        // ST - Short Text (use ImageComments 0020,4000 which is LT; use InstitutionalDepartmentName 0008,1040 LO)
        // Use a tag that is actually ST in the dictionary: (0032,4000) Study Comments
        var stTag = new DicomTag(0x0032, 0x4000); // Study Comments - ST VR (retired)
        dataset.Add(new DicomStringElement(stTag, DicomVR.ST,
            Encoding.UTF8.GetBytes("General Hospital")));
        // TM - Time
        dataset.Add(new DicomStringElement(DicomTag.StudyTime, DicomVR.TM,
            Encoding.UTF8.GetBytes("093000")));
        // UC - Unlimited Characters
        var ucTag = new DicomTag(0x0008, 0x0119); // Long Code Value
        dataset.Add(new DicomStringElement(ucTag, DicomVR.UC,
            Encoding.UTF8.GetBytes("SOME_LONG_CODE_VALUE")));
        // UI - Unique Identifier
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.840.113619.2.1")));
        // UR - URI/URL
        var urTag = new DicomTag(0x0040, 0xE010); // Retrieve URI
        dataset.Add(new DicomStringElement(urTag, DicomVR.UR,
            Encoding.UTF8.GetBytes("https://example.com/wado")));
        // UT - Unlimited Text (0040,A160 Text Value is UT)
        var utTag = new DicomTag(0x0040, 0xA160); // Text Value
        dataset.Add(new DicomStringElement(utTag, DicomVR.UT,
            Encoding.UTF8.GetBytes("This is some longer text for UT VR testing.")));

        // Use AlwaysIncludeVR to ensure all VRs roundtrip correctly
        var options = new BsonSerializationOptions { AlwaysIncludeVR = true };
        AssertBsonRoundtrip(dataset, options);
    }

    [Test]
    public void Roundtrip_AllNumericVRs()
    {
        var dataset = new DicomDataset();

        // SS (Int16) - SmallestImagePixelValue is multi-VR so VR is included
        var ssTag = DicomTag.SmallestImagePixelValue;
        var ssBytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(ssBytes, -100);
        dataset.Add(new DicomNumericElement(ssTag, DicomVR.SS, ssBytes));

        // US (UInt16) - Rows
        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 512);
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, usBytes));

        // SL (Int32) - use AlwaysIncludeVR to ensure VR is written for non-standard VR pairings
        var slTag = new DicomTag(0x0018, 0x1310); // Acquisition Matrix
        var slBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(slBytes, -99999);
        dataset.Add(new DicomNumericElement(slTag, DicomVR.SL, slBytes));

        // UL (UInt32)
        var ulTag = new DicomTag(0x0008, 0x1161); // SimpleFrameList
        var ulBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(ulBytes, 3000000);
        dataset.Add(new DicomNumericElement(ulTag, DicomVR.UL, ulBytes));

        // FL (Float32)
        var flTag = new DicomTag(0x0018, 0x1151); // X-Ray Tube Current
        var flBytes = new byte[4];
#if NETSTANDARD2_0
        Buffer.BlockCopy(BitConverter.GetBytes(1.5f), 0, flBytes, 0, 4);
#else
        BinaryPrimitives.WriteSingleLittleEndian(flBytes, 1.5f);
#endif
        dataset.Add(new DicomNumericElement(flTag, DicomVR.FL, flBytes));

        // FD (Float64)
        var fdTag = new DicomTag(0x0018, 0x0088); // Spacing Between Slices
        var fdBytes = new byte[8];
        long fdBits = BitConverter.DoubleToInt64Bits(3.14159);
        BinaryPrimitives.WriteInt64LittleEndian(fdBytes, fdBits);
        dataset.Add(new DicomNumericElement(fdTag, DicomVR.FD, fdBytes));

        // AT (Attribute Tag) - use AlwaysIncludeVR so AT VR is explicitly stored
        var atTag = DicomTag.OffendingElement;
        var atBytes = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(atBytes.AsSpan(0), 0x0010);
        BinaryPrimitives.WriteUInt16LittleEndian(atBytes.AsSpan(2), 0x0020);
        dataset.Add(new DicomNumericElement(atTag, DicomVR.AT, atBytes));

        // Use AlwaysIncludeVR to preserve exact VRs for all tags
        var options = new BsonSerializationOptions { AlwaysIncludeVR = true };
        AssertBsonRoundtrip(dataset, options);
    }

    [Test]
    public void Roundtrip_BinaryVRs()
    {
        var dataset = new DicomDataset();
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02 };

        // OB
        dataset.Add(new DicomBinaryElement(new DicomTag(0x7FE0, 0x0010), DicomVR.OB, data));

        // OW - use a different tag to avoid collision
        var owData = new byte[] { 0x01, 0x00, 0x02, 0x00 };
        dataset.Add(new DicomBinaryElement(new DicomTag(0x5400, 0x1010), DicomVR.OW, owData));

        AssertBsonRoundtrip(dataset);
    }

    [Test]
    public void Roundtrip_Sequence_WithNestedElements()
    {
        var dataset = new DicomDataset();
        var item = new DicomDataset();
        item.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("NESTED_PAT")));
        item.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Smith^Jane")));

        var ssBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(ssBytes, 256);
        item.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, ssBytes));

        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, item));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        var seq = restored[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(seq, Is.Not.Null);
        Assert.That(seq!.Items.Count, Is.EqualTo(1));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientID), Is.EqualTo("NESTED_PAT"));
        Assert.That(seq.Items[0].GetString(DicomTag.PatientName), Is.EqualTo("Smith^Jane"));

        var usEl = seq.Items[0][DicomTag.Rows] as DicomNumericElement;
        Assert.That(usEl!.GetUInt16(), Is.EqualTo(256));
    }

    [Test]
    public void Roundtrip_SequenceDepth_ExceedsLimit_FallsBackToEmpty()
    {
        // Create deeply nested sequence
        var innerItem = new DicomDataset();
        innerItem.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("DEEP")));

        var innerSeq = new DicomSequence(DicomTag.ReferencedSOPSequence, innerItem);

        var outerItem = new DicomDataset();
        outerItem.Add(innerSeq);

        var dataset = new DicomDataset();
        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, outerItem));

        // Set very low depth limit
        var options = new BsonSerializationOptions { MaxSequenceDepth = 1 };
        var bson = BsonDicomWriter.Serialize(dataset, options);
        var restored = BsonDicomReader.Deserialize(bson, options);

        // Top-level sequence should exist
        var seq = restored[DicomTag.ReferencedSOPSequence] as DicomSequence;
        Assert.That(seq, Is.Not.Null);
    }

    [Test]
    public void Roundtrip_PrivateTags_PreservesCreatorNames()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        dataset.AddPrivateString(0x0009, "TestCreator", 0x01, DicomVR.LO, "Value1");
        dataset.AddPrivateString(0x0009, "TestCreator", 0x02, DicomVR.LO, "Value2");

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.PatientID), Is.EqualTo("PAT001"));

        int privateDataCount = 0;
        foreach (var element in restored)
        {
            if (element.Tag.IsPrivate && !element.Tag.IsPrivateCreator)
            {
                privateDataCount++;
                string? creator = restored.PrivateCreators.GetCreator(element.Tag);
                Assert.That(creator, Is.EqualTo("TestCreator"));
            }
        }
        Assert.That(privateDataCount, Is.EqualTo(2));
    }

    [Test]
    public void Roundtrip_EmptyElements()
    {
        var dataset = new DicomDataset();
        // Empty string element
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            ReadOnlyMemory<byte>.Empty));
        // Empty numeric element
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US,
            ReadOnlyMemory<byte>.Empty));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        var strEl = restored[DicomTag.PatientID];
        Assert.That(strEl, Is.Not.Null);
        Assert.That(strEl!.IsEmpty, Is.True);

        var numEl = restored[DicomTag.Rows];
        Assert.That(numEl, Is.Not.Null);
        Assert.That(numEl!.IsEmpty, Is.True);
    }

    [Test]
    public void Roundtrip_MultiValuedElements()
    {
        var dataset = new DicomDataset();

        // Multi-valued string: CS with backslash
        dataset.Add(new DicomStringElement(DicomTag.ImageType, DicomVR.CS,
            Encoding.UTF8.GetBytes("ORIGINAL\\PRIMARY\\AXIAL")));

        // Multi-valued IS
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("1\\2\\3")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        var imageType = restored[DicomTag.ImageType] as DicomStringElement;
        var expectedValues = new[] { "ORIGINAL", "PRIMARY", "AXIAL" };
        Assert.That(imageType!.GetStrings(), Is.EqualTo(expectedValues));

        Assert.That(restored.GetString(DicomTag.SeriesNumber), Is.EqualTo("1\\2\\3"));
    }

    [Test]
    public void Roundtrip_IS_PreservesExactString()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.InstanceNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("042")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        // "042" should survive intact, not become "42"
        Assert.That(restored.GetString(DicomTag.InstanceNumber), Is.EqualTo("042"));
    }

    [Test]
    public void Roundtrip_DS_PreservesExactString()
    {
        var dataset = new DicomDataset();
        var windowCenter = new DicomTag(0x0028, 0x1050);
        dataset.Add(new DicomStringElement(windowCenter, DicomVR.DS,
            Encoding.UTF8.GetBytes("3.14000")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(windowCenter), Is.EqualTo("3.14000"));
    }

    [Test]
    public void Roundtrip_DA_PreservesExactString()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA,
            Encoding.UTF8.GetBytes("20240115")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.StudyDate), Is.EqualTo("20240115"));
    }

    [Test]
    public void Roundtrip_TM_PreservesExactString()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.StudyTime, DicomVR.TM,
            Encoding.UTF8.GetBytes("120000.000000")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.StudyTime), Is.EqualTo("120000.000000"));
    }

    [Test]
    public void Roundtrip_PN_PreservesComponentGroups()
    {
        var dataset = new DicomDataset();
        // Use ASCII-safe component groups (non-ASCII requires SpecificCharacterSet)
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John=Yamada^Tarou=Phonetic^Name")));

        var bson = BsonDicomWriter.Serialize(dataset);
        var restored = BsonDicomReader.Deserialize(bson);

        Assert.That(restored.GetString(DicomTag.PatientName),
            Is.EqualTo("Doe^John=Yamada^Tarou=Phonetic^Name"));
    }

    [Test]
    public void Roundtrip_LargeDataset_AllTypesPresent()
    {
        var dataset = new DicomDataset();

        // String VRs
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Smith^Alice^M")));
        dataset.Add(new DicomStringElement(DicomTag.Modality, DicomVR.CS,
            Encoding.UTF8.GetBytes("MR")));
        dataset.Add(new DicomStringElement(DicomTag.StudyDate, DicomVR.DA,
            Encoding.UTF8.GetBytes("20240101")));
        dataset.Add(new DicomStringElement(DicomTag.StudyTime, DicomVR.TM,
            Encoding.UTF8.GetBytes("080000")));
        dataset.Add(new DicomStringElement(DicomTag.AccessionNumber, DicomVR.SH,
            Encoding.UTF8.GetBytes("ACC001")));
        dataset.Add(new DicomStringElement(DicomTag.StudyDescription, DicomVR.LO,
            Encoding.UTF8.GetBytes("Brain MRI")));
        dataset.Add(new DicomStringElement(DicomTag.SeriesDescription, DicomVR.LO,
            Encoding.UTF8.GetBytes("T1 Weighted")));
        dataset.Add(new DicomStringElement(DicomTag.SOPInstanceUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.3.4.5.6.7.8.9")));
        dataset.Add(new DicomStringElement(DicomTag.SOPClassUID, DicomVR.UI,
            Encoding.UTF8.GetBytes("1.2.840.10008.5.1.4.1.1.2")));

        // IS - Integer String
        dataset.Add(new DicomStringElement(DicomTag.SeriesNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("3")));
        dataset.Add(new DicomStringElement(DicomTag.InstanceNumber, DicomVR.IS,
            Encoding.UTF8.GetBytes("7")));

        // DS - Decimal String
        var windowCenter = new DicomTag(0x0028, 0x1050);
        dataset.Add(new DicomStringElement(windowCenter, DicomVR.DS,
            Encoding.UTF8.GetBytes("40.0")));

        // Numeric VRs
        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 512);
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, usBytes));

        var usBytes2 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes2, 512);
        dataset.Add(new DicomNumericElement(DicomTag.Columns, DicomVR.US, usBytes2));

        var usBytes3 = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes3, 16);
        dataset.Add(new DicomNumericElement(DicomTag.BitsAllocated, DicomVR.US, usBytes3));

        // Binary VR
        var smallPixelData = new byte[64];
        for (int i = 0; i < smallPixelData.Length; i++)
            smallPixelData[i] = (byte)(i & 0xFF);
        dataset.Add(new DicomBinaryElement(DicomTag.PixelData, DicomVR.OB, smallPixelData));

        // Sequence
        var seqItem = new DicomDataset();
        seqItem.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("REF001")));
        dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, seqItem));

        // Private tags
        dataset.AddPrivateString(0x0009, "TestApp", 0x01, DicomVR.LO, "CustomData");

        AssertBsonRoundtrip(dataset);
    }

    [Test]
    public void Roundtrip_IBufferWriter_MatchesByteArrayOutput()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(DicomTag.PatientID, DicomVR.LO,
            Encoding.UTF8.GetBytes("PAT001")));
        dataset.Add(new DicomStringElement(DicomTag.PatientName, DicomVR.PN,
            Encoding.UTF8.GetBytes("Doe^John")));

        var usBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(usBytes, 256);
        dataset.Add(new DicomNumericElement(DicomTag.Rows, DicomVR.US, usBytes));

        // Serialize via byte[] overload
        var bytesResult = BsonDicomWriter.Serialize(dataset);

        // Serialize via IBufferWriter overload
        var bufferWriter = new ArrayBufferWriter<byte>(4096);
        BsonDicomWriter.Serialize(dataset, bufferWriter);
        var writerResult = bufferWriter.WrittenSpan.ToArray();

        Assert.That(writerResult.Length, Is.EqualTo(bytesResult.Length));
        Assert.That(writerResult.AsSpan().SequenceEqual(bytesResult), Is.True,
            "Both Serialize overloads should produce identical output");
    }

    /// <summary>
    /// Serializes and deserializes a dataset, then compares element-by-element.
    /// </summary>
    private static void AssertBsonRoundtrip(DicomDataset original)
        => AssertBsonRoundtrip(original, null);

    /// <summary>
    /// Serializes and deserializes a dataset with specified options, then compares element-by-element.
    /// </summary>
    private static void AssertBsonRoundtrip(DicomDataset original, BsonSerializationOptions? options)
    {
        var bson = BsonDicomWriter.Serialize(original, options);
        var restored = BsonDicomReader.Deserialize(bson, options);

        // Compare element counts (excluding private creators which are metadata)
        int origDataCount = 0;
        int restoredDataCount = 0;
        foreach (var el in original)
        {
            if (!el.Tag.IsPrivateCreator) origDataCount++;
        }
        foreach (var el in restored)
        {
            if (!el.Tag.IsPrivateCreator) restoredDataCount++;
        }

        Assert.That(restoredDataCount, Is.EqualTo(origDataCount),
            $"Element count mismatch. Expected {origDataCount}, got {restoredDataCount}");

        foreach (var origElement in original)
        {
            if (origElement.Tag.IsPrivateCreator) continue;

            var restoredElement = restored[origElement.Tag];

            // For private data elements, find by iterating
            if (origElement.Tag.IsPrivate && restoredElement == null)
            {
                foreach (var candidate in restored)
                {
                    if (candidate.Tag == origElement.Tag)
                    {
                        restoredElement = candidate;
                        break;
                    }
                }
            }

            Assert.That(restoredElement, Is.Not.Null,
                $"Tag {origElement.Tag} not found in restored dataset");

            Assert.That(restoredElement!.VR, Is.EqualTo(origElement.VR),
                $"VR mismatch for tag {origElement.Tag}: expected {origElement.VR}, got {restoredElement.VR}");

            if (origElement is DicomStringElement origStr && restoredElement is DicomStringElement restoredStr)
            {
                Assert.That(restoredStr.GetString(), Is.EqualTo(origStr.GetString()),
                    $"String value mismatch for tag {origElement.Tag}");
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
