using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Dicom;
using SharpDicom.Data;

namespace SharpDicom.FoDicom4.Compat.Tests;

/// <summary>
/// Tests for FoDicom4.Compat layer verifying Dicom namespace and fo-dicom 4.x API surface.
/// </summary>
[TestFixture]
public class FoDicom4CompatTests
{
    [Test]
    public void DicomFile_Open_ReturnsNonNullFile()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            Assert.That(file, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DicomFile_Open_DatasetIsNotNull()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            Assert.That(file.Dataset, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DicomFile_Open_FileMetaInfoIsNotNull()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            Assert.That(file.FileMetaInfo, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Get_String_ReturnsCorrectValue()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            var value = file.Dataset.Get<string>(Dicom.DicomTag.PatientID);
            Assert.That(value, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Get_String_ReturnsModality()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            var value = file.Dataset.Get<string>(Dicom.DicomTag.Modality);
            Assert.That(value, Is.EqualTo("CT"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Get_WithDefault_ReturnsDefaultForMissingTag()
    {
        var dataset = new Dicom.DicomDataset();
        var value = dataset.Get<string>(Dicom.DicomTag.PatientID, "default_value");
        Assert.That(value, Is.EqualTo("default_value"));
    }

    [Test]
    public void Get_WithDefault_ReturnsActualValueWhenPresent()
    {
        var dataset = new Dicom.DicomDataset();
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "ACTUAL");
        var value = dataset.Get<string>(Dicom.DicomTag.PatientID, "default_value");
        Assert.That(value, Is.EqualTo("ACTUAL"));
    }

    [Test]
    public void GetSingleValue_String_AlsoWorks()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            var value = file.Dataset.GetSingleValue<string>(Dicom.DicomTag.PatientID);
            Assert.That(value, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Get_Int_ReturnsCorrectValue()
    {
        var inner = new SharpDicom.Data.DicomDataset();
        inner.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0020, 0x0011), SharpDicom.Data.DicomVR.IS,
            System.Text.Encoding.ASCII.GetBytes("3 ")));
        var dataset = new Dicom.DicomDataset(inner);

        var value = dataset.Get<int>(Dicom.DicomTag.SeriesNumber);
        Assert.That(value, Is.EqualTo(3));
    }

    [Test]
    public void DicomTag_DictionaryEntry_ResolvesName()
    {
        var tag = new Dicom.DicomTag(0x0010, 0x0010);
        Assert.That(tag.DictionaryEntry, Is.Not.Null);
        Assert.That(tag.DictionaryEntry!.Name, Is.EqualTo("Patient's Name"));
    }

    [Test]
    public void DicomTag_DictionaryEntry_ResolvesKeyword()
    {
        var tag = new Dicom.DicomTag(0x0008, 0x0020);
        Assert.That(tag.DictionaryEntry, Is.Not.Null);
        Assert.That(tag.DictionaryEntry!.Keyword, Is.EqualTo("StudyDate"));
    }

    [Test]
    public void DicomTag_WellKnownTags_MatchExpectedValues()
    {
        Assert.That(Dicom.DicomTag.PatientName.Group, Is.EqualTo(0x0010));
        Assert.That(Dicom.DicomTag.PatientName.Element, Is.EqualTo(0x0010));
        Assert.That(Dicom.DicomTag.StudyDate.Group, Is.EqualTo(0x0008));
        Assert.That(Dicom.DicomTag.StudyDate.Element, Is.EqualTo(0x0020));
    }

    [Test]
    public void Dataset_Enumeration_YieldsDicomItemSubtypes()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            Assert.That(items, Is.Not.Empty, "Dataset should contain elements");

            // All items should be DicomItem subtypes
            foreach (var item in items)
            {
                Assert.That(item, Is.InstanceOf<Dicom.DicomItem>());
            }

            // Should have at least one DicomStringElement
            var stringElements = items.OfType<Dicom.DicomStringElement>().ToList();
            Assert.That(stringElements, Is.Not.Empty, "Should contain string elements");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void AddOrUpdate_CreatesAndRetrievesElement()
    {
        var dataset = new Dicom.DicomDataset();
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "TEST123");

        Assert.That(dataset.Contains(Dicom.DicomTag.PatientID), Is.True);
        var value = dataset.Get<string>(Dicom.DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("TEST123"));
    }

    [Test]
    public void AddOrUpdate_ReplacesExistingElement()
    {
        var dataset = new Dicom.DicomDataset();
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "FIRST");
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "SECOND");

        var value = dataset.Get<string>(Dicom.DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("SECOND"));
    }

    [Test]
    public void Contains_ReturnsFalse_ForMissingTag()
    {
        var dataset = new Dicom.DicomDataset();
        Assert.That(dataset.Contains(Dicom.DicomTag.PatientID), Is.False);
    }

    [Test]
    public void Remove_RemovesElement()
    {
        var dataset = new Dicom.DicomDataset();
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "TEST");

        Assert.That(dataset.Contains(Dicom.DicomTag.PatientID), Is.True);
        dataset.Remove(Dicom.DicomTag.PatientID);
        Assert.That(dataset.Contains(Dicom.DicomTag.PatientID), Is.False);
    }

    [Test]
    public void DicomVR_StaticInstances_HaveCorrectCodes()
    {
        Assert.That(Dicom.DicomVR.LO.Code, Is.EqualTo("LO"));
        Assert.That(Dicom.DicomVR.CS.Code, Is.EqualTo("CS"));
        Assert.That(Dicom.DicomVR.SQ.Code, Is.EqualTo("SQ"));
        Assert.That(Dicom.DicomVR.PN.Code, Is.EqualTo("PN"));
    }

    [Test]
    public void DicomUID_Equality_Works()
    {
        var uid1 = new Dicom.DicomUID("1.2.3.4");
        var uid2 = new Dicom.DicomUID("1.2.3.4");
        var uid3 = new Dicom.DicomUID("1.2.3.5");

        Assert.That(uid1, Is.EqualTo(uid2));
        Assert.That(uid1, Is.Not.EqualTo(uid3));
    }

    [Test]
    public void Save_RoundtripsCorrectly()
    {
        var path = CreateTestFileWithSopUids();
        var savedPath = Path.GetTempFileName();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            file.Save(savedPath);

            var reopened = Dicom.DicomFile.Open(savedPath);
            var patientId = reopened.Dataset.Get<string>(Dicom.DicomTag.PatientID);
            Assert.That(patientId, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(savedPath);
        }
    }

    [Test]
    public void Unwrap_ReturnsUnderlyingSharpDicomFile()
    {
        var path = CreateTestFile();
        try
        {
            var file = Dicom.DicomFile.Open(path);
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
    public void DicomTag_Equality_Works()
    {
        var tag1 = new Dicom.DicomTag(0x0010, 0x0020);
        var tag2 = new Dicom.DicomTag(0x0010, 0x0020);
        var tag3 = new Dicom.DicomTag(0x0010, 0x0010);

        Assert.That(tag1, Is.EqualTo(tag2));
        Assert.That(tag1, Is.Not.EqualTo(tag3));
        Assert.That(tag1 == tag2, Is.True);
        Assert.That(tag1 != tag3, Is.True);
    }

    [Test]
    public void TryGetSingleValue_ReturnsFalse_ForMissingTag()
    {
        var dataset = new Dicom.DicomDataset();
        var result = dataset.TryGetSingleValue<string>(Dicom.DicomTag.PatientID, out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryGetSingleValue_ReturnsTrue_ForExistingTag()
    {
        var dataset = new Dicom.DicomDataset();
        dataset.AddOrUpdate(Dicom.DicomTag.PatientID, "TEST");

        var result = dataset.TryGetSingleValue<string>(Dicom.DicomTag.PatientID, out var value);

        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo("TEST"));
    }

    [Test]
    public void DicomSequence_Items_ReturnsNestedDatasets()
    {
        var path = CreateTestFileWithSequence();
        try
        {
            var file = Dicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            var seqItems = items.OfType<Dicom.DicomSequence>().ToList();
            Assert.That(seqItems, Is.Not.Empty, "Should have a sequence element");

            var seq = seqItems.First();
            Assert.That(seq.Items, Is.Not.Null);
            Assert.That(seq.Items.Count, Is.GreaterThan(0), "Sequence should have items");

            foreach (var item in seq.Items)
            {
                Assert.That(item, Is.InstanceOf<Dicom.DicomDataset>());
            }
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

    private static byte[] CreateTestDicomBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
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
