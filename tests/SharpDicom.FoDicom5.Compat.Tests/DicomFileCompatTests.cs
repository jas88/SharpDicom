using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using FellowOakDicom;
using SharpDicom.Data;

namespace SharpDicom.FoDicom5.Compat.Tests;

/// <summary>
/// Tests for DicomFile compat wrapper and element type dispatch.
/// </summary>
[TestFixture]
public class DicomFileCompatTests
{
    [Test]
    public void Open_ReturnsNonNullFile()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            Assert.That(file, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Open_DatasetIsNotNull()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            Assert.That(file.Dataset, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Open_FileMetaInfoIsNotNull()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            Assert.That(file.FileMetaInfo, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Dataset_Enumeration_YieldsDicomItemSubtypes()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            Assert.That(items, Is.Not.Empty, "Dataset should contain elements");

            // All items should be DicomItem subtypes
            foreach (var item in items)
            {
                Assert.That(item, Is.InstanceOf<FellowOakDicom.DicomItem>());
            }

            // Should have at least one DicomStringElement (PatientName, PatientID, Modality)
            var stringElements = items.OfType<FellowOakDicom.DicomStringElement>().ToList();
            Assert.That(stringElements, Is.Not.Empty, "Should contain string elements");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DicomStringElement_Get_ReturnsCorrectValue()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            // Find PatientID element (0010,0020)
            var patientIdElement = items
                .OfType<FellowOakDicom.DicomStringElement>()
                .FirstOrDefault(e => e.Tag.Group == 0x0010 && e.Tag.Element == 0x0020);

            Assert.That(patientIdElement, Is.Not.Null, "PatientID element should exist");
            var value = patientIdElement!.Get<string>(0);
            Assert.That(value, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DicomTag_DictionaryEntry_ResolvesName()
    {
        var tag = new FellowOakDicom.DicomTag(0x0010, 0x0010);
        Assert.That(tag.DictionaryEntry, Is.Not.Null);
        Assert.That(tag.DictionaryEntry!.Name, Is.EqualTo("Patient's Name"));
    }

    [Test]
    public void DicomTag_DictionaryEntry_ResolvesKeyword()
    {
        var tag = new FellowOakDicom.DicomTag(0x0008, 0x0020);
        Assert.That(tag.DictionaryEntry, Is.Not.Null);
        Assert.That(tag.DictionaryEntry!.Keyword, Is.EqualTo("StudyDate"));
    }

    [Test]
    public void DicomTag_WellKnownTags_MatchExpectedValues()
    {
        Assert.That(FellowOakDicom.DicomTag.PatientName.Group, Is.EqualTo(0x0010));
        Assert.That(FellowOakDicom.DicomTag.PatientName.Element, Is.EqualTo(0x0010));
        Assert.That(FellowOakDicom.DicomTag.StudyDate.Group, Is.EqualTo(0x0008));
        Assert.That(FellowOakDicom.DicomTag.StudyDate.Element, Is.EqualTo(0x0020));
        Assert.That(FellowOakDicom.DicomTag.PatientID.Group, Is.EqualTo(0x0010));
        Assert.That(FellowOakDicom.DicomTag.PatientID.Element, Is.EqualTo(0x0020));
    }

    [Test]
    public void DicomSequence_Items_ReturnsNestedDatasets()
    {
        var path = CreateTestFileWithSequence();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            var seqItems = items.OfType<FellowOakDicom.DicomSequence>().ToList();
            Assert.That(seqItems, Is.Not.Empty, "Should have a sequence element");

            var seq = seqItems.First();
            Assert.That(seq.Items, Is.Not.Null);
            Assert.That(seq.Items.Count, Is.GreaterThan(0), "Sequence should have items");

            // Each sequence item should be a DicomDataset
            foreach (var item in seq.Items)
            {
                Assert.That(item, Is.InstanceOf<FellowOakDicom.DicomDataset>());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Unwrap_ReturnsUnderlyingSharpDicomFile()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var unwrapped = file.Unwrap();

            Assert.That(unwrapped, Is.Not.Null);
            Assert.That(unwrapped, Is.InstanceOf<SharpDicom.DicomFile>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Save_RoundtripsCorrectly()
    {
        var path = CreateTestFileWithSopUids();
        var savedPath = Path.GetTempFileName();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            file.Save(savedPath);

            var reopened = FellowOakDicom.DicomFile.Open(savedPath);
            var patientId = reopened.Dataset.GetSingleValue<string>(FellowOakDicom.DicomTag.PatientID);
            Assert.That(patientId, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(savedPath);
        }
    }

    [Test]
    public void DicomVR_StaticInstances_HaveCorrectCodes()
    {
        Assert.That(FellowOakDicom.DicomVR.LO.Code, Is.EqualTo("LO"));
        Assert.That(FellowOakDicom.DicomVR.CS.Code, Is.EqualTo("CS"));
        Assert.That(FellowOakDicom.DicomVR.SQ.Code, Is.EqualTo("SQ"));
        Assert.That(FellowOakDicom.DicomVR.AT.Code, Is.EqualTo("AT"));
        Assert.That(FellowOakDicom.DicomVR.PN.Code, Is.EqualTo("PN"));
    }

    [Test]
    public void DicomUID_Equality_Works()
    {
        var uid1 = new FellowOakDicom.DicomUID("1.2.3.4");
        var uid2 = new FellowOakDicom.DicomUID("1.2.3.4");
        var uid3 = new FellowOakDicom.DicomUID("1.2.3.5");

        Assert.That(uid1, Is.EqualTo(uid2));
        Assert.That(uid1, Is.Not.EqualTo(uid3));
    }

    [Test]
    public void DicomStringElement_Count_ReturnsNumberOfValues()
    {
        var path = CreateTestFileWithMultiValuedElement();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            // ImageType is multi-valued CS: "ORIGINAL\PRIMARY"
            var imageType = items
                .OfType<FellowOakDicom.DicomStringElement>()
                .FirstOrDefault(e => e.Tag.Group == 0x0008 && e.Tag.Element == 0x0008);

            Assert.That(imageType, Is.Not.Null, "ImageType element should exist");
            Assert.That(imageType!.Count, Is.EqualTo(2), "Should have 2 values");
            Assert.That(imageType.Get<string>(0), Is.EqualTo("ORIGINAL"));
            Assert.That(imageType.Get<string>(1), Is.EqualTo("PRIMARY"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    #region Helper Methods

    private static string CreateTestFile()
    {
        var data = CreateTestDicomBytes();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string CreateTestFileWithSopUids()
    {
        var data = CreateTestDicomBytesWithSopUids();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] CreateTestDicomBytesWithSopUids()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
        WriteElement(ms, 0x0008, 0x0016, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.5.1.4.1.1.2\0"));
        WriteElement(ms, 0x0008, 0x0018, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.3.4.5.6.7.8.9\0"));
        WriteElement(ms, 0x0008, 0x0060, "CS", System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0010, "PN", System.Text.Encoding.ASCII.GetBytes("Doe^John"));
        WriteElement(ms, 0x0010, 0x0020, "LO", System.Text.Encoding.ASCII.GetBytes("PATIENT001"));
        return ms.ToArray();
    }

    private static string CreateTestFileWithSequence()
    {
        // Create a DICOM file with a sequence using SharpDicom directly
        var dataset = new SharpDicom.Data.DicomDataset();
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x0016), SharpDicom.Data.DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.5.1.4.1.1.2\0")));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x0018), SharpDicom.Data.DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.3.4.5.6.7.8.9\0")));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x0060), SharpDicom.Data.DicomVR.CS,
            System.Text.Encoding.ASCII.GetBytes("CT")));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0010, 0x0010), SharpDicom.Data.DicomVR.PN,
            System.Text.Encoding.ASCII.GetBytes("Doe^John")));
        dataset.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0010, 0x0020), SharpDicom.Data.DicomVR.LO,
            System.Text.Encoding.ASCII.GetBytes("PATIENT001")));

        // Add a sequence (Referenced Study Sequence 0008,1110)
        var seqItem = new SharpDicom.Data.DicomDataset();
        seqItem.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x1150), SharpDicom.Data.DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.5.1.4.1.1.2\0")));
        seqItem.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0008, 0x1155), SharpDicom.Data.DicomVR.UI,
            System.Text.Encoding.ASCII.GetBytes("1.2.3.4.5.6.7.8.9.10\0\0")));

        dataset.Add(new SharpDicom.Data.DicomSequence(
            new SharpDicom.Data.DicomTag(0x0008, 0x1110), seqItem));

        var file = new SharpDicom.DicomFile(dataset);
        var path = Path.GetTempFileName();
        file.Save(path);
        return path;
    }

    private static string CreateTestFileWithMultiValuedElement()
    {
        var data = CreateTestDicomBytesWithMultiValue();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] CreateTestDicomBytes()
    {
        using var ms = new MemoryStream();

        // 128 byte preamble
        ms.Write(new byte[128]);

        // DICM prefix
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));

        // File Meta Information (Explicit VR LE)
        WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));

        // Dataset elements
        WriteElement(ms, 0x0008, 0x0060, "CS", System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0010, "PN", System.Text.Encoding.ASCII.GetBytes("Doe^John"));
        WriteElement(ms, 0x0010, 0x0020, "LO", System.Text.Encoding.ASCII.GetBytes("PATIENT001"));

        return ms.ToArray();
    }

    private static byte[] CreateTestDicomBytesWithMultiValue()
    {
        using var ms = new MemoryStream();

        // 128 byte preamble
        ms.Write(new byte[128]);

        // DICM prefix
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));

        // File Meta Information (Explicit VR LE)
        WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));

        // Multi-valued CS element: ImageType (0008,0008) = "ORIGINAL\PRIMARY"
        WriteElement(ms, 0x0008, 0x0008, "CS", System.Text.Encoding.ASCII.GetBytes("ORIGINAL\\PRIMARY"));
        WriteElement(ms, 0x0008, 0x0060, "CS", System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0010, "PN", System.Text.Encoding.ASCII.GetBytes("Doe^John"));
        WriteElement(ms, 0x0010, 0x0020, "LO", System.Text.Encoding.ASCII.GetBytes("PATIENT001"));

        return ms.ToArray();
    }

    private static void WriteElement(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));

        var vrCode = new SharpDicom.Data.DicomVR(vr);
        if (vrCode.Is32BitLength)
        {
            ms.Write(new byte[2]);
            ms.Write(BitConverter.GetBytes((uint)value.Length));
        }
        else
        {
            ms.Write(BitConverter.GetBytes((ushort)value.Length));
        }

        ms.Write(value);
    }

    #endregion
}
