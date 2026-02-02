using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for DeidentificationContext UID mapping and date offset tracking.
/// </summary>
[TestFixture]
public class DeidentificationContextTests
{
    [Test]
    public void RemapUID_SameInput_ReturnsSameOutput()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");
        var first = context.RemapUID(original);
        var second = context.RemapUID(original);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void RemapUID_DifferentInputs_ReturnsDifferentOutputs()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var uid1 = new DicomUID("1.2.3.4.5");
        var uid2 = new DicomUID("1.2.3.4.6");

        var mapped1 = context.RemapUID(uid1);
        var mapped2 = context.RemapUID(uid2);

        Assert.That(mapped2, Is.Not.EqualTo(mapped1));
    }

    [Test]
    public void RemapUID_CreatesNewUID_DifferentFromOriginal()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");
        var mapped = context.RemapUID(original);

        Assert.That(mapped.ToString(), Is.Not.EqualTo(original.ToString()));
    }

    [Test]
    public void GetDateOffset_SamePatient_ReturnsSameOffset()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var offset1 = context.GetDateOffset("PATIENT001");
        var offset2 = context.GetDateOffset("PATIENT001");

        Assert.That(offset2, Is.EqualTo(offset1));
    }

    [Test]
    public void GetDateOffset_DifferentPatients_MayReturnDifferentOffsets()
    {
        // Note: With random range, different patients get different random offsets
        var options = new DeidentificationOptions { DateShiftRange = (-365, 365) };
        using var context = new DeidentificationContext(options);

        // Note: Could theoretically return same offset by chance,
        // but with 730-day range this is extremely unlikely
        var offsets = Enumerable.Range(0, 10)
            .Select(i => context.GetDateOffset($"PATIENT{i:D3}"))
            .ToList();

        // At least some should be different
        Assert.That(offsets.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void GetStudyDateOffset_SameStudy_ReturnsSameOffset()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var studyUid = new DicomUID("1.2.3.4.5.6.7.8.9");
        var offset1 = context.GetStudyDateOffset(studyUid);
        var offset2 = context.GetStudyDateOffset(studyUid);

        Assert.That(offset2, Is.EqualTo(offset1));
    }

    [Test]
    public void GetStudyDateOffset_DifferentStudies_MayReturnDifferentOffsets()
    {
        var options = new DeidentificationOptions { DateShiftRange = (-365, 365) };
        using var context = new DeidentificationContext(options);

        var offsets = Enumerable.Range(0, 10)
            .Select(i => context.GetStudyDateOffset(new DicomUID($"1.2.3.{i}")))
            .ToList();

        Assert.That(offsets.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public async Task SaveAndLoad_PreservesUidMappings()
    {
        var options = new DeidentificationOptions();
        var original = new DicomUID("1.2.3.4.5");
        DicomUID mappedUid;

        using var stream = new MemoryStream();

        using (var context = new DeidentificationContext(options))
        {
            mappedUid = context.RemapUID(original);
            await context.SaveAsync(stream);
        }

        stream.Position = 0;
        var loaded = await DeidentificationContext.LoadAsync(stream, options);
        var remapped = loaded.RemapUID(original);

        Assert.That(remapped, Is.EqualTo(mappedUid));
    }

    [Test]
    public async Task SaveAndLoad_PreservesPatientDateOffsets()
    {
        var options = new DeidentificationOptions();
        TimeSpan originalOffset;

        using var stream = new MemoryStream();

        using (var context = new DeidentificationContext(options))
        {
            originalOffset = context.GetDateOffset("PATIENT001");
            await context.SaveAsync(stream);
        }

        stream.Position = 0;
        var loaded = await DeidentificationContext.LoadAsync(stream, options);
        var loadedOffset = loaded.GetDateOffset("PATIENT001");

        Assert.That(loadedOffset, Is.EqualTo(originalOffset));
    }

    [Test]
    public async Task SaveAndLoad_PreservesStudyDateOffsets()
    {
        var options = new DeidentificationOptions();
        TimeSpan originalOffset;
        var studyUid = new DicomUID("1.2.3.4.5.6.7.8.9");

        using var stream = new MemoryStream();

        using (var context = new DeidentificationContext(options))
        {
            originalOffset = context.GetStudyDateOffset(studyUid);
            await context.SaveAsync(stream);
        }

        stream.Position = 0;
        var loaded = await DeidentificationContext.LoadAsync(stream, options);
        var loadedOffset = loaded.GetStudyDateOffset(studyUid);

        Assert.That(loadedOffset, Is.EqualTo(originalOffset));
    }

    [Test]
    public void GetUidMappings_ReturnsAllMappings()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        context.RemapUID(new DicomUID("1.2.3"));
        context.RemapUID(new DicomUID("4.5.6"));

        var mappings = context.GetUidMappings();

        Assert.That(mappings.Count, Is.EqualTo(2));
        Assert.That(mappings.ContainsKey("1.2.3"), Is.True);
        Assert.That(mappings.ContainsKey("4.5.6"), Is.True);
    }

    [Test]
    public void GetPatientDateOffsets_ReturnsAllOffsets()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        context.GetDateOffset("PATIENT001");
        context.GetDateOffset("PATIENT002");

        var offsets = context.GetPatientDateOffsets();

        Assert.That(offsets.Count, Is.EqualTo(2));
        Assert.That(offsets.ContainsKey("PATIENT001"), Is.True);
        Assert.That(offsets.ContainsKey("PATIENT002"), Is.True);
    }

    [Test]
    public void GetStudyDateOffsets_ReturnsAllOffsets()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        context.GetStudyDateOffset(new DicomUID("1.2.3"));
        context.GetStudyDateOffset(new DicomUID("4.5.6"));

        var offsets = context.GetStudyDateOffsets();

        Assert.That(offsets.Count, Is.EqualTo(2));
        Assert.That(offsets.ContainsKey("1.2.3"), Is.True);
        Assert.That(offsets.ContainsKey("4.5.6"), Is.True);
    }

    [Test]
    public void HasUidMapping_AfterRemap_ReturnsTrue()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");
        context.RemapUID(original);

        Assert.That(context.HasUidMapping(original), Is.True);
    }

    [Test]
    public void HasUidMapping_BeforeRemap_ReturnsFalse()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");

        Assert.That(context.HasUidMapping(original), Is.False);
    }

    [Test]
    public void TryGetRemappedUID_AfterRemap_ReturnsTrue()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");
        var expected = context.RemapUID(original);

        var found = context.TryGetRemappedUID(original, out var actual);

        Assert.That(found, Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void TryGetRemappedUID_BeforeRemap_ReturnsFalse()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        var original = new DicomUID("1.2.3.4.5");

        var found = context.TryGetRemappedUID(original, out _);

        Assert.That(found, Is.False);
    }

    [Test]
    public void UidMappingCount_ReflectsActualCount()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        Assert.That(context.UidMappingCount, Is.EqualTo(0));

        context.RemapUID(new DicomUID("1.2.3"));
        Assert.That(context.UidMappingCount, Is.EqualTo(1));

        context.RemapUID(new DicomUID("4.5.6"));
        Assert.That(context.UidMappingCount, Is.EqualTo(2));

        // Remap same UID doesn't increase count
        context.RemapUID(new DicomUID("1.2.3"));
        Assert.That(context.UidMappingCount, Is.EqualTo(2));
    }

    [Test]
    public void Clear_RemovesAllMappings()
    {
        var options = new DeidentificationOptions();
        using var context = new DeidentificationContext(options);

        context.RemapUID(new DicomUID("1.2.3"));
        context.GetDateOffset("PATIENT001");
        context.GetStudyDateOffset(new DicomUID("4.5.6"));

        context.Clear();

        Assert.That(context.UidMappingCount, Is.EqualTo(0));
        Assert.That(context.GetUidMappings().Count, Is.EqualTo(0));
        Assert.That(context.GetPatientDateOffsets().Count, Is.EqualTo(0));
        Assert.That(context.GetStudyDateOffsets().Count, Is.EqualTo(0));
    }

    [Test]
    public void UidPrefix_ReturnsConfiguredPrefix()
    {
        var options = new DeidentificationOptions { UidPrefix = "1.2.826.0.1.3680043" };
        using var context = new DeidentificationContext(options);

        Assert.That(context.UidPrefix, Is.EqualTo("1.2.826.0.1.3680043"));
    }

    [Test]
    public void DateShiftStrategy_ReturnsConfiguredStrategy()
    {
        var options = new DeidentificationOptions { DateShiftStrategy = DateShiftStrategy.PerStudy };
        using var context = new DeidentificationContext(options);

        Assert.That(context.DateShiftStrategy, Is.EqualTo(DateShiftStrategy.PerStudy));
    }

    [Test]
    public void GetDateOffsetForStrategy_PerPatient_UsesPatientOffset()
    {
        var options = new DeidentificationOptions { DateShiftStrategy = DateShiftStrategy.PerPatient };
        using var context = new DeidentificationContext(options);

        var patientId = "PATIENT001";
        var studyUid = new DicomUID("1.2.3.4.5");

        // Get offset using strategy
        var offset1 = context.GetDateOffsetForStrategy(patientId, studyUid);

        // Get again - should be same
        var offset2 = context.GetDateOffsetForStrategy(patientId, studyUid);

        // Also should match direct patient lookup
        var patientOffset = context.GetDateOffset(patientId);

        Assert.That(offset1, Is.EqualTo(offset2));
        Assert.That(offset1, Is.EqualTo(patientOffset));
    }

    [Test]
    public void GetDateOffsetForStrategy_PerStudy_UsesStudyOffset()
    {
        var options = new DeidentificationOptions { DateShiftStrategy = DateShiftStrategy.PerStudy };
        using var context = new DeidentificationContext(options);

        var patientId = "PATIENT001";
        var studyUid = new DicomUID("1.2.3.4.5");

        var offset1 = context.GetDateOffsetForStrategy(patientId, studyUid);
        var offset2 = context.GetDateOffsetForStrategy(patientId, studyUid);
        var studyOffset = context.GetStudyDateOffset(studyUid);

        Assert.That(offset1, Is.EqualTo(offset2));
        Assert.That(offset1, Is.EqualTo(studyOffset));
    }

    [Test]
    public void GetDateOffsetForStrategy_PerElement_CreatesNewEachTime()
    {
        var options = new DeidentificationOptions
        {
            DateShiftStrategy = DateShiftStrategy.PerElement,
            DateShiftRange = (-365, 365)
        };
        using var context = new DeidentificationContext(options);

        var patientId = "PATIENT001";
        var studyUid = new DicomUID("1.2.3.4.5");

        // Get multiple offsets - they should potentially be different
        var offsets = Enumerable.Range(0, 20)
            .Select(_ => context.GetDateOffsetForStrategy(patientId, studyUid))
            .ToList();

        // With a 730-day range and 20 samples, extremely unlikely all are the same
        Assert.That(offsets.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void CreateRandomOffset_ReturnsOffsetWithinRange()
    {
        var options = new DeidentificationOptions { DateShiftRange = (-30, 30) };
        using var context = new DeidentificationContext(options);

        for (int i = 0; i < 100; i++)
        {
            var offset = context.CreateRandomOffset();
            Assert.That(offset.TotalDays, Is.GreaterThanOrEqualTo(-30));
            Assert.That(offset.TotalDays, Is.LessThanOrEqualTo(30));
        }
    }

    [Test]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeidentificationContext(null!));
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var options = new DeidentificationOptions();
        var context = new DeidentificationContext(options);

        Assert.DoesNotThrow(() =>
        {
            context.Dispose();
            context.Dispose();
            context.Dispose();
        });
    }
}
