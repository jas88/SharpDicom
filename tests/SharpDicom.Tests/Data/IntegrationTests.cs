using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Data;

/// <summary>
/// Integration tests verifying generated code works with the data model.
/// </summary>
[TestFixture]
public class IntegrationTests
{
    [Test]
    public void GeneratedDictionaryContainsExpectedTags()
    {
        // Verify dictionary has common DICOM tags
        var patientId = DicomDictionary.Default.GetEntry(new DicomTag(0x0010, 0x0020));
        Assert.That(patientId, Is.Not.Null, "PatientID should be in dictionary");
        Assert.That(patientId!.Value.Keyword, Is.EqualTo("PatientID"));

        var studyDate = DicomDictionary.Default.GetEntry(new DicomTag(0x0008, 0x0020));
        Assert.That(studyDate, Is.Not.Null, "StudyDate should be in dictionary");
        Assert.That(studyDate!.Value.Keyword, Is.EqualTo("StudyDate"));

        var modality = DicomDictionary.Default.GetEntry(new DicomTag(0x0008, 0x0060));
        Assert.That(modality, Is.Not.Null, "Modality should be in dictionary");
        Assert.That(modality!.Value.Keyword, Is.EqualTo("Modality"));
    }

    [Test]
    public void GeneratedUIDsAreAccessible()
    {
        // Verify well-known UIDs from generated DicomUIDs class are accessible
        Assert.That(DicomUIDs.ImplicitVRLittleEndian.ToString(),
            Is.EqualTo("1.2.840.10008.1.2"));
        Assert.That(DicomUIDs.ExplicitVRLittleEndian.ToString(),
            Is.EqualTo("1.2.840.10008.1.2.1"));
        Assert.That(DicomUIDs.CTImageStorage.ToString(),
            Is.EqualTo("1.2.840.10008.5.1.4.1.1.2"));
    }

    [Test]
    public void TransferSyntaxFromGeneratedUID()
    {
        // Use TransferSyntax constants which have the UIDs
        var ts = TransferSyntax.ImplicitVRLittleEndian;
        Assert.That(ts.IsKnown, Is.True);
        Assert.That(ts.IsExplicitVR, Is.False);
        Assert.That(ts.IsLittleEndian, Is.True);

        var ts2 = TransferSyntax.ExplicitVRLittleEndian;
        Assert.That(ts2.IsKnown, Is.True);
        Assert.That(ts2.IsExplicitVR, Is.True);
        Assert.That(ts2.IsLittleEndian, Is.True);
    }

    [Test]
    public void DatasetWithGeneratedTags()
    {
        var dataset = new DicomDataset();

        // Build a typical DICOM dataset using generated tags
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO,
            "PATIENT001"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN,
            "Doe^John"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0020), DicomVR.DA,
            "20240115"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0060), DicomVR.CS,
            "CT"u8.ToArray()));

        // Verify data retrieval
        Assert.That(dataset.GetString(new DicomTag(0x0010, 0x0020)), Is.EqualTo("PATIENT001"));
#if TESTING_NETSTANDARD_POLYFILLS
        // netstandard2.0 returns DateTime
        Assert.That(dataset.GetDate(new DicomTag(0x0008, 0x0020)), Is.EqualTo(new DateTime(2024, 1, 15)));
#else
        // net6.0+ returns DateOnly
        Assert.That(dataset.GetDate(new DicomTag(0x0008, 0x0020)), Is.EqualTo(new DateOnly(2024, 1, 15)));
#endif
        Assert.That(dataset.Count, Is.EqualTo(4));

        // Verify ToOwned creates independent copy
        var owned = dataset.ToOwned();
        Assert.That(owned.Count, Is.EqualTo(dataset.Count));

        // Verify sorted enumeration
        var tags = dataset.Select(e => e.Tag).ToList();
        Assert.That(tags, Is.Ordered);
    }

    [Test]
    public void DictionaryLookupPerformance()
    {
        // Simple performance sanity check (not a benchmark)
        var sw = Stopwatch.StartNew();
        var tag = new DicomTag(0x0010, 0x0020);

        for (int i = 0; i < 100_000; i++)
        {
            _ = DicomDictionary.Default.GetEntry(tag);
        }

        sw.Stop();

        // Should be very fast (< 100ms for 100K lookups on a modern machine)
        // Being generous with 500ms to account for CI variability
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500),
            $"Dictionary lookup should be fast. Took {sw.ElapsedMilliseconds}ms for 100K lookups");
    }

    [Test]
    public void DictionaryContainsMultiVRTag()
    {
        // Pixel Data can be OB or OW
        var pixelData = DicomDictionary.Default.GetEntry(new DicomTag(0x7FE0, 0x0010));
        Assert.That(pixelData, Is.Not.Null);
        Assert.That(pixelData!.Value.HasMultipleVRs, Is.True);
    }

    [Test]
    public void DictionaryKeywordLookupCaseInsensitive()
    {
        var entry1 = DicomDictionary.Default.GetEntryByKeyword("PatientID");
        var entry2 = DicomDictionary.Default.GetEntryByKeyword("patientid");
        var entry3 = DicomDictionary.Default.GetEntryByKeyword("PATIENTID");

        Assert.That(entry1, Is.Not.Null);
        Assert.That(entry2, Is.Not.Null);
        Assert.That(entry3, Is.Not.Null);
        Assert.That(entry1!.Value.Tag, Is.EqualTo(entry2!.Value.Tag));
        Assert.That(entry2.Value.Tag, Is.EqualTo(entry3!.Value.Tag));
    }

    [Test]
    public void DicomTagStaticMembersAreAccessible()
    {
        // Generated static tag members should be accessible from DicomTags class
        Assert.That(DicomTags.PatientID.Group, Is.EqualTo(0x0010));
        Assert.That(DicomTags.PatientID.Element, Is.EqualTo(0x0020));
        Assert.That(DicomTags.StudyDate.Group, Is.EqualTo(0x0008));
        Assert.That(DicomTags.StudyDate.Element, Is.EqualTo(0x0020));
    }

    [Test]
    public void DicomUIDsContainsExpectedUIDs()
    {
        // Verify CT Image Storage UID is correctly defined
        var ctUid = DicomUIDs.CTImageStorage;
        Assert.That(ctUid.ToString(), Is.EqualTo("1.2.840.10008.5.1.4.1.1.2"));

        // Verify MR Image Storage UID
        var mrUid = DicomUIDs.MRImageStorage;
        Assert.That(mrUid.ToString(), Is.EqualTo("1.2.840.10008.5.1.4.1.1.4"));
    }

    [Test]
    public void ValueMultiplicityParseVariants()
    {
        // Test various VM formats from the standard
        Assert.That(ValueMultiplicity.Parse("1").Min, Is.EqualTo(1));
        Assert.That(ValueMultiplicity.Parse("1").Max, Is.EqualTo(1));

        Assert.That(ValueMultiplicity.Parse("1-n").Min, Is.EqualTo(1));
        Assert.That(ValueMultiplicity.Parse("1-n").IsUnlimited, Is.True);

        Assert.That(ValueMultiplicity.Parse("2-2n").Min, Is.EqualTo(2));
        Assert.That(ValueMultiplicity.Parse("2-2n").IsUnlimited, Is.True);

        Assert.That(ValueMultiplicity.Parse("1-3").Min, Is.EqualTo(1));
        Assert.That(ValueMultiplicity.Parse("1-3").Max, Is.EqualTo(3));
    }

    [Test]
    public void CompleteDatasetRoundtrip()
    {
        // Create a complete dataset and verify all parts work together
        var dataset = new DicomDataset();

        // Add string elements
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0005), DicomVR.CS, "ISO_IR 100"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0016), DicomVR.UI,
            "1.2.840.10008.5.1.4.1.1.2"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0018), DicomVR.UI,
            "1.2.3.4.5.6.7.8.9"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0020), DicomVR.DA, "20240115"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0030), DicomVR.TM, "143052"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0008, 0x0060), DicomVR.CS, "CT"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0010), DicomVR.PN, "Doe^John"u8.ToArray()));
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO, "PATIENT001"u8.ToArray()));

        // Add numeric elements
        dataset.Add(new DicomNumericElement(new DicomTag(0x0028, 0x0010), DicomVR.US,
            new byte[] { 0x00, 0x02 })); // Rows = 512
        dataset.Add(new DicomNumericElement(new DicomTag(0x0028, 0x0011), DicomVR.US,
            new byte[] { 0x00, 0x02 })); // Columns = 512

        // Verify element count
        Assert.That(dataset.Count, Is.EqualTo(10));

        // Verify sorted enumeration
        var elements = dataset.ToList();
        for (int i = 1; i < elements.Count; i++)
        {
            Assert.That(elements[i].Tag.CompareTo(elements[i - 1].Tag), Is.GreaterThan(0),
                "Elements should be sorted by tag");
        }

        // Verify dictionary lookups
        var sopClassEntry = DicomDictionary.Default.GetEntry(new DicomTag(0x0008, 0x0016));
        Assert.That(sopClassEntry, Is.Not.Null);
        Assert.That(sopClassEntry!.Value.Keyword, Is.EqualTo("SOPClassUID"));

        // Verify SOP Class UID matches expected value
        var sopClassUid = dataset.GetString(new DicomTag(0x0008, 0x0016));
        Assert.That(sopClassUid, Is.EqualTo("1.2.840.10008.5.1.4.1.1.2")); // CT Image Storage
        Assert.That(sopClassUid, Is.EqualTo(DicomUIDs.CTImageStorage.ToString()));
    }
}
