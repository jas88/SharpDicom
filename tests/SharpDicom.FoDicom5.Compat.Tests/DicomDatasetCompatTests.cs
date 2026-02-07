using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using FellowOakDicom;
using SharpDicom.Data;

namespace SharpDicom.FoDicom5.Compat.Tests;

/// <summary>
/// Tests for DicomDataset compat wrapper.
/// </summary>
[TestFixture]
public class DicomDatasetCompatTests
{
    [Test]
    public void GetSingleValue_String_ReturnsCorrectValue()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var value = file.Dataset.GetSingleValue<string>(FellowOakDicom.DicomTag.PatientID);
            Assert.That(value, Is.EqualTo("PATIENT001"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void GetSingleValue_String_ReturnsModality()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var value = file.Dataset.GetSingleValue<string>(FellowOakDicom.DicomTag.Modality);
            Assert.That(value, Is.EqualTo("CT"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void GetSingleValue_Int_ReturnsCorrectValue()
    {
        // Create a dataset with an IS element
        var dataset = CreateDatasetWithIntegerElement();
        var value = dataset.GetSingleValue<int>(new FellowOakDicom.DicomTag(0x0020, 0x0011));
        Assert.That(value, Is.EqualTo(3));
    }

    [Test]
    public void GetValues_String_ReturnsAllVMValues()
    {
        var path = CreateTestFileWithMultiValue();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var values = file.Dataset.GetValues<string>(FellowOakDicom.DicomTag.ImageType);

            Assert.That(values, Is.Not.Null);
            Assert.That(values.Length, Is.EqualTo(2));
            Assert.That(values[0], Is.EqualTo("ORIGINAL"));
            Assert.That(values[1], Is.EqualTo("PRIMARY"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void GetValues_String_ReturnsSingleValueAsArray()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var values = file.Dataset.GetValues<string>(FellowOakDicom.DicomTag.Modality);

            Assert.That(values, Is.Not.Null);
            Assert.That(values.Length, Is.EqualTo(1));
            Assert.That(values[0], Is.EqualTo("CT"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void GetValues_String_ReturnsEmptyForMissingTag()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        var values = dataset.GetValues<string>(new FellowOakDicom.DicomTag(0x9999, 0x9999));

        Assert.That(values, Is.Not.Null);
        Assert.That(values.Length, Is.EqualTo(0));
    }

    [Test]
    public void AddOrUpdate_CreatesNewElement()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "TEST123");

        Assert.That(dataset.Contains(FellowOakDicom.DicomTag.PatientID), Is.True);
        var value = dataset.GetSingleValue<string>(FellowOakDicom.DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("TEST123"));
    }

    [Test]
    public void AddOrUpdate_ReplacesExistingElement()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "FIRST");
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "SECOND");

        var value = dataset.GetSingleValue<string>(FellowOakDicom.DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("SECOND"));
    }

    [Test]
    public void AddOrUpdate_MultipleValues_JoinsWithBackslash()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.ImageType, "ORIGINAL", "PRIMARY");

        var values = dataset.GetValues<string>(FellowOakDicom.DicomTag.ImageType);
        Assert.That(values.Length, Is.EqualTo(2));
        Assert.That(values[0], Is.EqualTo("ORIGINAL"));
        Assert.That(values[1], Is.EqualTo("PRIMARY"));
    }

    [Test]
    public void Contains_ReturnsTrue_ForExistingTag()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            Assert.That(file.Dataset.Contains(FellowOakDicom.DicomTag.PatientID), Is.True);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Contains_ReturnsFalse_ForMissingTag()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        Assert.That(dataset.Contains(FellowOakDicom.DicomTag.PatientID), Is.False);
    }

    [Test]
    public void TryGetSingleValue_ReturnsFalse_ForMissingTag()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        var result = dataset.TryGetSingleValue<string>(FellowOakDicom.DicomTag.PatientID, out var value);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TryGetSingleValue_ReturnsTrue_ForExistingTag()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "TEST");

        var result = dataset.TryGetSingleValue<string>(FellowOakDicom.DicomTag.PatientID, out var value);

        Assert.That(result, Is.True);
        Assert.That(value, Is.EqualTo("TEST"));
    }

    [Test]
    public void Enumeration_ReturnsCorrectDicomItemSubtypes()
    {
        var path = CreateTestFile();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var items = file.Dataset.ToList();

            Assert.That(items, Is.Not.Empty);

            // All items in this test file should be DicomStringElement
            // (Modality=CS, PatientName=PN, PatientID=LO)
            foreach (var item in items)
            {
                Assert.That(item, Is.InstanceOf<FellowOakDicom.DicomStringElement>(),
                    $"Expected DicomStringElement but got {item.GetType().Name} for tag {item.Tag}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void EmptyDataset_HasNoElements()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        var items = dataset.ToList();

        Assert.That(items, Is.Empty);
    }

    [Test]
    public void Unwrap_ReturnsUnderlyingSharpDicomDataset()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "TEST");

        var unwrapped = dataset.Unwrap();

        Assert.That(unwrapped, Is.Not.Null);
        Assert.That(unwrapped, Is.InstanceOf<SharpDicom.Data.DicomDataset>());
        Assert.That(unwrapped.GetString(new SharpDicom.Data.DicomTag(0x0010, 0x0020)), Is.EqualTo("TEST"));
    }

    [Test]
    public void Remove_RemovesElement()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "TEST");

        Assert.That(dataset.Contains(FellowOakDicom.DicomTag.PatientID), Is.True);
        dataset.Remove(FellowOakDicom.DicomTag.PatientID);
        Assert.That(dataset.Contains(FellowOakDicom.DicomTag.PatientID), Is.False);
    }

    [Test]
    public void GetString_ReturnsValue()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        dataset.AddOrUpdate(FellowOakDicom.DicomTag.PatientID, "HELLO");

        var value = dataset.GetString(FellowOakDicom.DicomTag.PatientID);
        Assert.That(value, Is.EqualTo("HELLO"));
    }

    [Test]
    public void GetString_ReturnsNull_ForMissingTag()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        var value = dataset.GetString(FellowOakDicom.DicomTag.PatientID);
        Assert.That(value, Is.Null);
    }

    [Test]
    public void TryGetSequence_ReturnsFalse_WhenNoSequence()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        var result = dataset.TryGetSequence(new FellowOakDicom.DicomTag(0x0008, 0x1110), out _);
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetSequence_Throws_WhenMissing()
    {
        var dataset = new FellowOakDicom.DicomDataset();
        Assert.Throws<FellowOakDicom.DicomDataException>(() =>
            dataset.GetSequence(new FellowOakDicom.DicomTag(0x0008, 0x1110)));
    }

    [Test]
    public void GetValue_WithIndex_ReturnsSpecificValue()
    {
        var path = CreateTestFileWithMultiValue();
        try
        {
            var file = FellowOakDicom.DicomFile.Open(path);
            var val0 = file.Dataset.GetValue<string>(FellowOakDicom.DicomTag.ImageType, 0);
            var val1 = file.Dataset.GetValue<string>(FellowOakDicom.DicomTag.ImageType, 1);

            Assert.That(val0, Is.EqualTo("ORIGINAL"));
            Assert.That(val1, Is.EqualTo("PRIMARY"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DicomTag_Equality_Works()
    {
        var tag1 = new FellowOakDicom.DicomTag(0x0010, 0x0020);
        var tag2 = new FellowOakDicom.DicomTag(0x0010, 0x0020);
        var tag3 = new FellowOakDicom.DicomTag(0x0010, 0x0010);

        Assert.That(tag1, Is.EqualTo(tag2));
        Assert.That(tag1, Is.Not.EqualTo(tag3));
        Assert.That(tag1 == tag2, Is.True);
        Assert.That(tag1 != tag3, Is.True);
    }

    [Test]
    public void DicomTag_ToString_ReturnsFormattedString()
    {
        var tag = new FellowOakDicom.DicomTag(0x0010, 0x0020);
        Assert.That(tag.ToString(), Is.EqualTo("(0010,0020)"));
    }

    #region Helper Methods

    private static FellowOakDicom.DicomDataset CreateDatasetWithIntegerElement()
    {
        var inner = new SharpDicom.Data.DicomDataset();
        // Series Number (0020,0011) is IS VR
        inner.Add(new SharpDicom.Data.DicomStringElement(
            new SharpDicom.Data.DicomTag(0x0020, 0x0011), SharpDicom.Data.DicomVR.IS,
            System.Text.Encoding.ASCII.GetBytes("3 ")));
        return new FellowOakDicom.DicomDataset(inner);
    }

    private static string CreateTestFile()
    {
        var data = CreateTestDicomBytes();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string CreateTestFileWithMultiValue()
    {
        var data = CreateTestDicomBytesWithMultiValue();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
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

    private static byte[] CreateTestDicomBytesWithMultiValue()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI", System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
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
