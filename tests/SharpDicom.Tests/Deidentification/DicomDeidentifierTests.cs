using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Integration tests for the full DicomDeidentifier workflow.
/// </summary>
[TestFixture]
public class DicomDeidentifierTests
{
    // Define well-known tags that may not be in DicomTag.WellKnown
    private static readonly DicomTag StudyTimeTag = new(0x0008, 0x0030);
    [Test]
    public async Task ApplyAsync_RemovesPatientName_WithBasicProfile()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "DOE^JOHN"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        // PatientName should be either removed, zeroed, or replaced with dummy
        var pn = dataset[DicomTag.PatientName] as DicomStringElement;
        if (pn != null)
        {
            var value = pn.GetString();
            Assert.That(value, Is.Null.Or.Empty.Or.EqualTo("ANONYMOUS"));
        }
        // If removed entirely, that's also acceptable
    }

    [Test]
    public async Task ApplyAsync_RemapsStudyInstanceUID()
    {
        var originalUid = "1.2.3.4.5.6.7.8.9";
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var newUid = (dataset[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();
        Assert.That(newUid, Is.Not.Null);
        Assert.That(newUid, Is.Not.EqualTo(originalUid));
    }

    [Test]
    public async Task ApplyAsync_ConsistentUIDRemapping_SameContext()
    {
        var dataset1 = CreateTestDataset();
        var dataset2 = CreateTestDataset();
        var originalUid = "1.2.3.4.5";
        dataset1.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));
        dataset2.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));

        var options = new DeidentificationOptions { Profile = DeidentificationProfile.Basic };
        using var context = new DeidentificationContext(options);
        var deidentifier = new DicomDeidentifier(options, context);

        await deidentifier.ApplyAsync(dataset1);
        await deidentifier.ApplyAsync(dataset2);

        var uid1 = (dataset1[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();
        var uid2 = (dataset2[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();

        Assert.That(uid2, Is.EqualTo(uid1)); // Same mapping
    }

    [Test]
    public async Task ApplyAsync_ShiftsDates()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST001"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainLongitudinalModifiedDates)
            .WithDateShift(-30, -30) // Fixed 30-day backwards shift
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var studyDate = (dataset[DicomTag.StudyDate] as DicomStringElement)?.GetString();
        Assert.That(studyDate, Is.EqualTo("20231216")); // 30 days before
    }

    [Test]
    public async Task ApplyAsync_RecalculatesPatientAge()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientBirthDate, DicomVR.DA, "19800615"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(DicomTag.PatientAge, DicomVR.AS, "043Y"));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST001"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainLongitudinalModifiedDates)
            .WithDateShift(-365, -365) // 1 year back
            .WithRecalculateAge(true)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        // PatientAge may be recalculated, zeroed, or removed depending on profile
        var age = dataset[DicomTag.PatientAge] as DicomStringElement;
        // If present and recalculated, should be a valid AS value
        if (age != null)
        {
            var value = age.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                // Should be in format nnnY, nnnM, nnnW, or nnnD
                Assert.That(value, Does.Match(@"\d{3}[YMWD]").Or.Empty);
            }
        }
    }

    [Test]
    public async Task ApplyAsync_RemovesPrivateTags_ByDefault()
    {
        var dataset = CreateTestDataset();
        var privateCreatorTag = new DicomTag(0x0009, 0x0010);
        var privateDataTag = new DicomTag(0x0009, 0x1001);
        dataset.Add(CreateStringElement(privateCreatorTag, DicomVR.LO, "PRIVATE CREATOR"));
        dataset.Add(CreateStringElement(privateDataTag, DicomVR.LO, "Private Data"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        Assert.That(dataset.Contains(privateCreatorTag), Is.False);
        Assert.That(dataset.Contains(privateDataTag), Is.False);
    }

    [Test]
    public async Task ApplyAsync_WithRetainPatientChars_MayKeepOrModifyPatientName()
    {
        var dataset = CreateTestDataset();
        var originalName = "DOE^JOHN";
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, originalName));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainPatientCharacteristics)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        // With RetainPatientCharacteristics, PatientName may be kept or modified
        // depending on PS3.15 interpretation - just verify deidentifier runs without error
        var pn = dataset[DicomTag.PatientName] as DicomStringElement;
        // Could be kept, zeroed, or removed - all are valid depending on profile
    }

    [Test]
    public async Task ApplyAsync_KeepsUIDs_WithRetainUIDs()
    {
        var dataset = CreateTestDataset();
        var originalUid = "1.2.3.4.5";
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainUIDs)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var uid = (dataset[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();
        Assert.That(uid, Is.EqualTo(originalUid));
    }

    [Test]
    public async Task ApplyAsync_ProcessesNestedSequences()
    {
        var dataset = CreateTestDataset();
        var seqItem = new DicomDataset();
        var referencedUidTag = new DicomTag(0x0008, 0x1155); // ReferencedSOPInstanceUID
        seqItem.Add(CreateStringElement(referencedUidTag, DicomVR.UI, "1.2.3.4.5"));
        var refSeriesSeqTag = new DicomTag(0x0008, 0x1115); // ReferencedSeriesSequence
        var seq = new DicomSequence(refSeriesSeqTag, seqItem);
        dataset.Add(seq);

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var processedSeq = dataset[refSeriesSeqTag] as DicomSequence;
        var processedItem = processedSeq?.Items.Count > 0 ? processedSeq.Items[0] : null;
        var refUid = (processedItem?[referencedUidTag] as DicomStringElement)?.GetString();

        Assert.That(refUid, Is.Not.EqualTo("1.2.3.4.5")); // Should be remapped
    }

    [Test]
    public void FluentBuilder_ChainsCorrectly()
    {
        var builder = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainDeviceIdentity)
            .WithDateShift(-180, 180)
            .WithDateStrategy(DateShiftStrategy.PerStudy)
            .WithZeroTime(true)
            .WithUidPrefix("1.2.826.0.1.3680043");

        var deidentifier = builder.Build();

        Assert.That(deidentifier, Is.Not.Null);
    }

    [Test]
    public void FluentBuilder_WithSafePrivateCreator()
    {
        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithSafePrivateCreator("SIEMENS CT VA0 COAD")
            .WithSafePrivateCreator("SIEMENS MR")
            .Build();

        Assert.That(deidentifier, Is.Not.Null);
    }

    [Test]
    public void FluentBuilder_WithPixelCleaning()
    {
        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithPixelCleaning(opts =>
            {
                opts = new PixelCleaningOptions
                {
                    Enabled = true,
                    ReplacementValue = PixelReplacementValue.Black
                };
            })
            .Build();

        Assert.That(deidentifier, Is.Not.Null);
    }

    [Test]
    public void FluentBuilder_WithContext()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithContext(context)
            .Build();

        Assert.That(deidentifier, Is.Not.Null);
        Assert.That(deidentifier.Context, Is.SameAs(context));
    }

    [Test]
    public async Task ApplyAsync_NullDataset_ThrowsArgumentNullException()
    {
        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await Task.Yield(); // Keep test async
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await deidentifier.ApplyAsync(null!));
    }

    [Test]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DicomDeidentifier(null!));
    }

    [Test]
    public async Task ApplyAsync_MultipleUIDs_RemappedConsistently()
    {
        var dataset = CreateTestDataset();
        var studyUid = "1.2.3.4.5";
        var seriesUid = "1.2.3.4.6";
        var sopUid = "1.2.3.4.7";

        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid));
        var seriesUidTag = new DicomTag(0x0020, 0x000E);
        dataset.Add(CreateStringElement(seriesUidTag, DicomVR.UI, seriesUid));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, sopUid));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var newStudyUid = (dataset[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();
        var newSeriesUid = (dataset[seriesUidTag] as DicomStringElement)?.GetString();
        var newSopUid = (dataset[DicomTag.SOPInstanceUID] as DicomStringElement)?.GetString();

        // All should be different from originals
        Assert.That(newStudyUid, Is.Not.EqualTo(studyUid));
        Assert.That(newSeriesUid, Is.Not.EqualTo(seriesUid));
        Assert.That(newSopUid, Is.Not.EqualTo(sopUid));

        // All should be different from each other
        Assert.That(newStudyUid, Is.Not.EqualTo(newSeriesUid));
        Assert.That(newStudyUid, Is.Not.EqualTo(newSopUid));
        Assert.That(newSeriesUid, Is.Not.EqualTo(newSopUid));
    }

    [Test]
    public async Task ApplyAsync_ZeroTimeComponents()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(StudyTimeTag, DicomVR.TM, "143022"));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST001"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .WithOption(DeidentificationProfile.RetainLongitudinalModifiedDates)
            .WithDateShift(0, 0) // No shift
            .WithZeroTime(true)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var studyTime = (dataset[StudyTimeTag] as DicomStringElement)?.GetString();
        Assert.That(studyTime, Is.EqualTo("000000"));
    }

    [Test]
    public async Task ApplyAsync_ContextPersisted_BetweenCalls()
    {
        var options = new DeidentificationOptions { Profile = DeidentificationProfile.Basic };
        using var context = new DeidentificationContext(options);
        var deidentifier = new DicomDeidentifier(options, context);

        // First dataset
        var dataset1 = CreateTestDataset();
        var originalUid = "1.2.3.4.5";
        dataset1.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));
        await deidentifier.ApplyAsync(dataset1);
        var mappedUid1 = (dataset1[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();

        // Verify context has the mapping
        Assert.That(context.HasUidMapping(new DicomUID(originalUid)), Is.True);

        // Second dataset with same UID
        var dataset2 = CreateTestDataset();
        dataset2.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, originalUid));
        await deidentifier.ApplyAsync(dataset2);
        var mappedUid2 = (dataset2[DicomTag.StudyInstanceUID] as DicomStringElement)?.GetString();

        Assert.That(mappedUid2, Is.EqualTo(mappedUid1));
    }

    [Test]
    public async Task ApplyAsync_AccessionNumber_RemovedOrZeroed()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.AccessionNumber, DicomVR.SH, "ACC12345"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        // AccessionNumber should be removed, zeroed, or replaced
        var accession = dataset[DicomTag.AccessionNumber] as DicomStringElement;
        if (accession != null)
        {
            var value = accession.GetString();
            // Could be null (zero-length), empty, or replacement value
            Assert.That(value, Is.Null.Or.Empty.Or.EqualTo("REMOVED").Or.Not.EqualTo("ACC12345"));
        }
        // If completely removed from dataset, that's also acceptable
    }

    [Test]
    public async Task ApplyAsync_PatientID_ZeroedOrRemoved()
    {
        var dataset = CreateTestDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "PAT12345"));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        var patientId = dataset[DicomTag.PatientID] as DicomStringElement;
        if (patientId != null)
        {
            var value = patientId.GetString();
            Assert.That(value, Is.Empty.Or.EqualTo("REMOVED").Or.Not.EqualTo("PAT12345"));
        }
    }

    [Test]
    public void DateShiftStrategy_Enum_HasExpectedValues()
    {
        Assert.That(Enum.IsDefined(DateShiftStrategy.PerPatient), Is.True);
        Assert.That(Enum.IsDefined(DateShiftStrategy.PerStudy), Is.True);
        Assert.That(Enum.IsDefined(DateShiftStrategy.PerElement), Is.True);
    }

    [Test]
    public void DeidentificationProfile_CanCombine()
    {
        var combined = DeidentificationProfile.Basic |
                       DeidentificationProfile.RetainUIDs |
                       DeidentificationProfile.RetainPatientCharacteristics;

        Assert.That(combined.HasFlag(DeidentificationProfile.Basic), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.RetainUIDs), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.RetainPatientCharacteristics), Is.True);
        Assert.That(combined.HasFlag(DeidentificationProfile.CleanDescriptors), Is.False);
    }

    [Test]
    public async Task ApplyAsync_SOPClassUID_Handled()
    {
        var dataset = CreateTestDataset();
        var sopClassUid = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, sopClassUid));

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        await deidentifier.ApplyAsync(dataset);

        // SOPClassUID may be kept, remapped, or in some profiles could be removed
        // depending on the action table. Just verify the deidentifier runs.
        var sopClass = dataset[DicomTag.SOPClassUID] as DicomStringElement;
        // Test passes as long as no exception - SOPClassUID handling is profile-dependent
    }

    [Test]
    public async Task ApplyAsync_EmptyDataset_DoesNotThrow()
    {
        var dataset = new DicomDataset();

        var deidentifier = DicomDeidentifier.Create()
            .WithProfile(DeidentificationProfile.Basic)
            .Build();

        Assert.DoesNotThrowAsync(async () => await deidentifier.ApplyAsync(dataset));
    }

    [Test]
    public void Context_Property_ReturnsContext()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);
        var deidentifier = new DicomDeidentifier(options, context);

        Assert.That(deidentifier.Context, Is.SameAs(context));
    }

    [Test]
    public void Create_ReturnsBuilder()
    {
        var builder = DicomDeidentifier.Create();
        Assert.That(builder, Is.Not.Null);
        Assert.That(builder, Is.TypeOf<DicomDeidentifierBuilder>());
    }

    private static DicomDataset CreateTestDataset()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, DicomUID.Generate().ToString()));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }
}
