using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using FellowOakDicom;

// Alias required because the test namespace SharpDicom.Migration.Integration causes
// C# namespace resolution to find SharpDicom.DicomFile before FellowOakDicom.DicomFile.
// This is the exact scenario a real migrating project would face if nested under SharpDicom.
using CompatDicomFile = FellowOakDicom.DicomFile;

namespace SharpDicom.Migration.Integration;

/// <summary>
/// Integration tests that prove dcm2csv's core logic (Entry.ProcessTag) works
/// correctly when compiled against SharpDicom.FoDicom5.Compat instead of fo-dicom.
///
/// These tests exercise the exact fo-dicom API surface used by dcm2csv:
/// - DicomFile.Open(path) opening real DICOM files
/// - Dataset enumeration (IEnumerable&lt;DicomItem&gt;)
/// - Pattern matching: DicomStringElement, DicomSequence, DicomAttributeTag
/// - DicomStringElement.Count and Get&lt;string&gt;(index)
/// - DicomSequence.Items with nested dataset enumeration
/// - DicomAttributeTag.Values (DicomTag[])
/// - DicomTag.DictionaryEntry.Name
/// - DicomItem.Tag and DicomItem.ToString()
/// </summary>
[TestFixture]
public class Dcm2CsvCompatTests
{
    [Test]
    public void ProcessTag_StringElement_ReturnsCorrectCsvRows()
    {
        var path = CreateTestFile();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("test.dcm", item))
                .ToList();

            Assert.That(entries, Is.Not.Empty, "Should produce CSV entries");

            // Check that PatientID is captured
            var patientIdEntries = entries
                .Where(e => e.Name == "Patient ID")
                .ToList();
            Assert.That(patientIdEntries, Has.Count.EqualTo(1));
            Assert.That(patientIdEntries[0].Value, Is.EqualTo("PATIENT001"));
            Assert.That(patientIdEntries[0].Id, Is.EqualTo("test.dcm"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_StringElement_PatientName_Resolved()
    {
        var path = CreateTestFile();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("test.dcm", item))
                .ToList();

            // Patient's Name should be resolved from dictionary
            var nameEntries = entries
                .Where(e => e.Name == "Patient's Name")
                .ToList();
            Assert.That(nameEntries, Has.Count.EqualTo(1));
            Assert.That(nameEntries[0].Value, Is.EqualTo("Doe^John"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_SequenceElement_RecursesIntoNestedDatasets()
    {
        var path = CreateTestFileWithSequence();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("seq.dcm", item))
                .ToList();

            Assert.That(entries, Is.Not.Empty, "Should produce entries including from sequence");

            // The sequence contains a Referenced SOP Class UID in its item
            var refSopEntries = entries
                .Where(e => e.Name == "Referenced SOP Class UID")
                .ToList();
            Assert.That(refSopEntries, Has.Count.GreaterThanOrEqualTo(1),
                "Sequence item should produce Referenced SOP Class UID entry");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_MultiValuedStringElement_ProducesMultipleEntries()
    {
        var path = CreateTestFileWithMultiValue();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("multi.dcm", item))
                .ToList();

            // ImageType (0008,0008) has 2 values: "ORIGINAL\PRIMARY"
            var imageTypeEntries = entries
                .Where(e => e.Name == "Image Type")
                .ToList();
            Assert.That(imageTypeEntries, Has.Count.EqualTo(2),
                "Multi-valued element should produce one entry per value");
            Assert.That(imageTypeEntries[0].Value, Is.EqualTo("ORIGINAL"));
            Assert.That(imageTypeEntries[1].Value, Is.EqualTo("PRIMARY"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_AttributeTagElement_FormatsTagValueNames()
    {
        var path = CreateTestFileWithAttributeTag();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("at.dcm", item))
                .ToList();

            // The AT element references tag (0010,0020) = PatientID
            // The entry name should be the AT tag's dictionary name: "Dimension Index Pointer"
            var atEntries = entries
                .Where(e => e.Name == "Dimension Index Pointer")
                .ToList();
            // The AT element value should be the name of the referenced tag
            Assert.That(atEntries, Has.Count.GreaterThanOrEqualTo(1),
                "AT element should produce entries for each referenced tag");
            Assert.That(atEntries[0].Value, Is.EqualTo("Patient ID"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_FallbackItem_UsesToString()
    {
        var path = CreateTestFileWithNumericElement();
        try
        {
            var file = CompatDicomFile.Open(path);
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("num.dcm", item))
                .ToList();

            Assert.That(entries, Is.Not.Empty, "Should produce entries even for non-string elements");

            // Numeric elements (US/UL etc.) fall through to the default branch
            // which uses item.ToString()
            var allIds = entries.Select(e => e.Id).Distinct().ToList();
            Assert.That(allIds, Does.Contain("num.dcm"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_EmptyStringElement_HandledGracefully()
    {
        var path = CreateTestFileWithEmptyElement();
        try
        {
            var file = CompatDicomFile.Open(path);

            // Should not throw
            var entries = file.Dataset
                .SelectMany(item => Entry.ProcessTag("empty.dcm", item))
                .ToList();

            Assert.That(entries, Is.Not.Empty, "Should produce entries even with empty elements");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ProcessTag_DicomTagDictionaryEntry_NeverNull()
    {
        var path = CreateTestFile();
        try
        {
            var file = CompatDicomFile.Open(path);

            // All items should have non-null DictionaryEntry (matching fo-dicom behavior)
            foreach (var item in file.Dataset)
            {
                Assert.That(item.Tag.DictionaryEntry, Is.Not.Null,
                    $"DictionaryEntry should never be null for tag {item.Tag}");
                Assert.That(item.Tag.DictionaryEntry.Name, Is.Not.Null.And.Not.Empty,
                    $"DictionaryEntry.Name should never be null/empty for tag {item.Tag}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void RealDicomFile_ProcessTag_ProducesExpectedOutput()
    {
        // Test with the actual dcm2csv sample file if it exists
        var samplePath = Path.Combine(
            "/Users/jas88/Developer/Github/dcm2csv",
            "20230724-001-lesion1-srdocument-medical_4314837306079021011jpg.dcm");

        if (!File.Exists(samplePath))
        {
            Assert.Ignore("Sample DICOM file not available at expected path");
            return;
        }

        var file = CompatDicomFile.Open(samplePath);
        var entries = file.Dataset
            .SelectMany(item => Entry.ProcessTag(samplePath, item))
            .ToList();

        Assert.That(entries, Is.Not.Empty, "Real DICOM file should produce entries");

        // All entries should have non-empty Id, Name, and Value
        foreach (var entry in entries)
        {
            Assert.That(entry.Id, Is.Not.Null.And.Not.Empty);
            Assert.That(entry.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(entry.Value, Is.Not.Null);
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

    private static string CreateTestFileWithMultiValue()
    {
        var data = CreateTestDicomBytesWithMultiValue();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string CreateTestFileWithAttributeTag()
    {
        // Create a DICOM file with an AT (Attribute Tag) element
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));

        // Transfer Syntax UID
        WriteElement(ms, 0x0002, 0x0010, "UI",
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));

        // Standard string elements
        WriteElement(ms, 0x0008, 0x0060, "CS",
            System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0020, "LO",
            System.Text.Encoding.ASCII.GetBytes("PATIENT001"));

        // AT element: Dimension Index Pointer (0020,9165) is AT
        // Value: PatientID tag (0010,0020) as raw bytes (little-endian: 10 00 20 00)
        WriteElement(ms, 0x0020, 0x9165, "AT",
            new byte[] { 0x10, 0x00, 0x20, 0x00 });

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static string CreateTestFileWithNumericElement()
    {
        // Create a DICOM file with a US (Unsigned Short) numeric element
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI",
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
        WriteElement(ms, 0x0010, 0x0010, "PN",
            System.Text.Encoding.ASCII.GetBytes("Doe^John"));

        // Rows (0028,0010) is US
        WriteElement(ms, 0x0028, 0x0010, "US",
            BitConverter.GetBytes((ushort)512));

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static string CreateTestFileWithEmptyElement()
    {
        // Create a DICOM file with an empty string element
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI",
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
        WriteElement(ms, 0x0010, 0x0010, "PN",
            System.Text.Encoding.ASCII.GetBytes("Doe^John"));

        // Empty Patient ID
        WriteElement(ms, 0x0010, 0x0020, "LO", Array.Empty<byte>());

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static byte[] CreateTestDicomBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI",
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));
        WriteElement(ms, 0x0008, 0x0060, "CS",
            System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0010, "PN",
            System.Text.Encoding.ASCII.GetBytes("Doe^John"));
        WriteElement(ms, 0x0010, 0x0020, "LO",
            System.Text.Encoding.ASCII.GetBytes("PATIENT001"));
        return ms.ToArray();
    }

    private static byte[] CreateTestDicomBytesWithMultiValue()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[128]);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("DICM"));
        WriteElement(ms, 0x0002, 0x0010, "UI",
            System.Text.Encoding.ASCII.GetBytes("1.2.840.10008.1.2.1\0"));

        // Multi-valued CS element: ImageType (0008,0008) = "ORIGINAL\PRIMARY"
        WriteElement(ms, 0x0008, 0x0008, "CS",
            System.Text.Encoding.ASCII.GetBytes("ORIGINAL\\PRIMARY"));
        WriteElement(ms, 0x0008, 0x0060, "CS",
            System.Text.Encoding.ASCII.GetBytes("CT "));
        WriteElement(ms, 0x0010, 0x0010, "PN",
            System.Text.Encoding.ASCII.GetBytes("Doe^John"));
        WriteElement(ms, 0x0010, 0x0020, "LO",
            System.Text.Encoding.ASCII.GetBytes("PATIENT001"));
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
