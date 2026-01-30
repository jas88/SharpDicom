using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Smoke tests exercising the full de-identification API surface.
/// These tests verify that all public APIs are accessible and work without error.
/// </summary>
[TestFixture]
public class DeidentificationApiSmokeTests
{
    #region DicomDeidentifier API

    [Test]
    public void DicomDeidentifier_DefaultConstructor_Works()
    {
        using var deid = new DicomDeidentifier();
        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void DicomDeidentifier_Deidentify_Works()
    {
        using var deid = new DicomDeidentifier();
        var dataset = CreateMinimalDataset();

        var result = deid.Deidentify(dataset);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void DicomDeidentifier_DisposeTwice_DoesNotThrow()
    {
        var deid = new DicomDeidentifier();
        deid.Dispose();
        deid.Dispose(); // Should not throw
        Assert.Pass();
    }

    #endregion

    #region DicomDeidentifierBuilder API

    [Test]
    public void Builder_AllMethods_Chainable()
    {
        using var remapper = new UidRemapper();
        using var deid = new DicomDeidentifierBuilder()
            .WithBasicProfile()
            .RetainSafePrivate()
            .RetainUIDs()
            .RetainDeviceIdentity()
            .RetainInstitutionIdentity()
            .RetainPatientCharacteristics()
            .RetainLongitudinalFullDates()
            .RetainLongitudinalModifiedDates()
            .CleanDescriptors()
            .CleanGraphics()
            .WithDateShift(TimeSpan.FromDays(-30))
            .WithSafePrivateCreators("TEST CREATOR")
            .WithUidRemapper(remapper)
            .WithOverride(new DicomTag(0x0008, 0x0080), DeidentificationAction.Keep)
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Builder_RandomDateShift_Works()
    {
        using var deid = new DicomDeidentifierBuilder()
            .WithRandomDateShift(-365, -30, seed: 12345)
            .Build();

        Assert.That(deid, Is.Not.Null);
    }

    [Test]
    public void Builder_MinimalConfig_Works()
    {
        using var deid = new DicomDeidentifierBuilder().Build();
        Assert.That(deid, Is.Not.Null);
    }

    #endregion

    #region UidRemapper API

    [Test]
    public void UidRemapper_Remap_GeneratesNewUid()
    {
        using var remapper = new UidRemapper();
        var original = "1.2.3.4.5";

        var remapped = remapper.Remap(original, null);

        Assert.That(remapped, Is.Not.EqualTo(original));
        Assert.That(remapped, Does.StartWith("2.25."));
    }

    [Test]
    public void UidRemapper_Remap_ConsistentForSameInput()
    {
        using var remapper = new UidRemapper();
        var original = "1.2.3.4.5";

        var first = remapper.Remap(original, null);
        var second = remapper.Remap(original, null);

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void UidRemapper_GetMappingCount_Works()
    {
        using var remapper = new UidRemapper();
        remapper.Remap("1.2.3", null);
        remapper.Remap("4.5.6", null);

        Assert.That(remapper.MappingCount, Is.EqualTo(2));
    }

    #endregion

    #region DateShifter API

    [Test]
    public void DateShifter_Fixed_Works()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);
        var result = shifter.ShiftDate("20240115", null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.EqualTo("20240115"));
    }

    [Test]
    public void DateShifter_Random_Works()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            MinOffsetDays = -365,
            MaxOffsetDays = -30,
            RandomSeed = 12345
        };
        var shifter = new DateShifter(config);
        var result = shifter.ShiftDate("20240115", "patient123");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.EqualTo("20240115"));
    }

    [Test]
    public void DateShifter_InvalidDate_ReturnsAsIs()
    {
        var config = new DateShiftConfig
        {
            Strategy = DateShiftStrategy.Fixed,
            FixedOffset = TimeSpan.FromDays(-100)
        };
        var shifter = new DateShifter(config);
        var result = shifter.ShiftDate("notadate", null);

        // Invalid dates are returned as-is
        Assert.That(result, Is.EqualTo("notadate"));
    }

    [Test]
    public void DateShiftConfig_Presets_Work()
    {
        Assert.That(DateShiftConfig.Default, Is.Not.Null);
        Assert.That(DateShiftConfig.None, Is.Not.Null);
        Assert.That(DateShiftConfig.Research, Is.Not.Null);
        Assert.That(DateShiftConfig.ClinicalTrial, Is.Not.Null);
    }

    #endregion

    #region IUidStore implementations

    [Test]
    public void InMemoryUidStore_AllMethods_Work()
    {
        using var store = new InMemoryUidStore();

        // GetOrCreateMapping
        var mapped = store.GetOrCreateMapping("1.2.3", "context");
        Assert.That(mapped, Is.Not.Null);

        // Count
        Assert.That(store.Count, Is.EqualTo(1));

        // TryGetMapped
        Assert.That(store.TryGetMapped("1.2.3", out var result), Is.True);
        Assert.That(result, Is.EqualTo(mapped));

        // TryGetOriginal
        Assert.That(store.TryGetOriginal(mapped, out var original), Is.True);
        Assert.That(original, Is.EqualTo("1.2.3"));

        // Clear
        store.Clear();
        Assert.That(store.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task InMemoryUidStore_ExportToJsonAsync_Works()
    {
        using var store = new InMemoryUidStore();
        store.GetOrCreateMapping("1.2.3", "scope1");
        store.GetOrCreateMapping("4.5.6", "scope2");

        using var stream = new MemoryStream();
        await store.ExportToJsonAsync(stream);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        Assert.That(json, Does.Contain("mappings"));
        Assert.That(json, Does.Contain("1.2.3"));
    }

    [Test]
    public void SqliteUidStore_AllMethods_Work()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-uid-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteUidStore(tempPath);

            // GetOrCreateMapping
            var mapped = store.GetOrCreateMapping("1.2.3", "context");
            Assert.That(mapped, Is.Not.Null);

            // Count
            Assert.That(store.Count, Is.EqualTo(1));

            // TryGetMapped
            Assert.That(store.TryGetMapped("1.2.3", out var result), Is.True);
            Assert.That(result, Is.EqualTo(mapped));

            // TryGetOriginal
            Assert.That(store.TryGetOriginal(mapped, out var original), Is.True);
            Assert.That(original, Is.EqualTo("1.2.3"));

            // GetMappingsByScope
            var scopeMappings = store.GetMappingsByScope("context");
            Assert.That(scopeMappings, Is.Not.Empty);

            // Clear
            store.Clear();
            Assert.That(store.Count, Is.EqualTo(0));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var walPath = tempPath + "-wal";
            var shmPath = tempPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
    }

    #endregion

    #region DeidentificationContext API

    [Test]
    public void DeidentificationContext_CreateBuilder_Works()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-ctx-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new DeidentificationContext(tempPath);
            var builder = context.CreateBuilder();
            using var deid = builder.Build();

            Assert.That(deid, Is.Not.Null);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var walPath = tempPath + "-wal";
            var shmPath = tempPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
    }

    [Test]
    public void DeidentificationContext_UidRemapper_Works()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-ctx-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new DeidentificationContext(tempPath);
            var remapper = context.UidRemapper;

            var remapped = remapper.Remap("1.2.3", null);
            Assert.That(remapped, Does.StartWith("2.25."));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var walPath = tempPath + "-wal";
            var shmPath = tempPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
    }

    [Test]
    public void DeidentificationContext_UidStore_Works()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-ctx-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new DeidentificationContext(tempPath);
            var store = context.UidStore;

            var mapped = store.GetOrCreateMapping("test.uid", "scope");
            Assert.That(store.TryGetMapped("test.uid", out _), Is.True);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            var walPath = tempPath + "-wal";
            var shmPath = tempPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
    }

    #endregion

    #region DeidentificationOptions API

    [Test]
    public void DeidentificationOptions_Presets_Work()
    {
        var basic = DeidentificationOptions.BasicProfile;
        var research = DeidentificationOptions.Research;
        var trial = DeidentificationOptions.ClinicalTrial;
        var teaching = DeidentificationOptions.Teaching;

        Assert.That(basic, Is.Not.Null);
        Assert.That(research, Is.Not.Null);
        Assert.That(trial, Is.Not.Null);
        Assert.That(teaching, Is.Not.Null);
    }

    [Test]
    public void DeidentificationOptions_AllProperties_Accessible()
    {
        var options = new DeidentificationOptions
        {
            RetainSafePrivate = true,
            RetainUIDs = true,
            RetainDeviceIdentity = true,
            RetainInstitutionIdentity = true,
            RetainPatientCharacteristics = true,
            RetainLongitudinalFullDates = true,
            RetainLongitudinalModifiedDates = true,
            CleanDescriptors = true,
            CleanStructuredContent = true,
            CleanGraphics = true,
            DefaultPrivateTagAction = PrivateTagAction.Remove,
            RemoveUnknownTags = true
        };

        Assert.That(options.RetainSafePrivate, Is.True);
        Assert.That(options.RetainUIDs, Is.True);
    }

    #endregion

    #region DeidentificationResult API

    [Test]
    public void DeidentificationResult_AllProperties_Accessible()
    {
        using var deid = new DicomDeidentifier();
        var dataset = CreateMinimalDataset();

        var result = deid.Deidentify(dataset);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Summary, Is.Not.Null);
        Assert.That(result.Warnings, Is.Not.Null);
        Assert.That(result.Errors, Is.Not.Null);
        Assert.That(result.UidRemappings, Is.Not.Null);
    }

    [Test]
    public void DeidentificationSummary_AllProperties_Accessible()
    {
        using var deid = new DicomDeidentifier();
        var dataset = CreateMinimalDataset();

        var result = deid.Deidentify(dataset);
        var summary = result.Summary;

        var removed = summary.AttributesRemoved;
        var replaced = summary.AttributesReplaced;
        var emptied = summary.AttributesEmptied;
        var uids = summary.UidsRemapped;
        var dates = summary.DatesShifted;
        var privates = summary.PrivateTagsProcessed;
        var sequences = summary.SequenceItemsProcessed;
        var total = summary.TotalModifications;

        // Just verify they're accessible
        Assert.That(total, Is.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region BurnedInAnnotationDetector API

    [Test]
    public void BurnedInAnnotationDetector_DetectRisk_Works()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "US"));

        var risk = BurnedInAnnotationDetector.DetectRisk(dataset);

        Assert.That(risk, Is.EqualTo(BurnedInAnnotationRisk.High));
    }

    [Test]
    public void BurnedInAnnotationDetector_GetWarningMessage_Works()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "US"));

        var message = BurnedInAnnotationDetector.GetWarningMessage(BurnedInAnnotationRisk.High, dataset);

        Assert.That(message, Is.Not.Empty);
        Assert.That(message, Does.Contain("US"));
    }

    [Test]
    public void BurnedInAnnotationDetector_SuggestRedactionOptions_Works()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "US"));

        var options = BurnedInAnnotationDetector.SuggestRedactionOptions(dataset, 800, 600);

        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Regions.Count, Is.GreaterThan(0));
    }

    #endregion

    #region RedactionOptions API

    [Test]
    public void RedactionOptions_Presets_Work()
    {
        var empty = RedactionOptions.Empty;
        var us = RedactionOptions.UltrasoundDefault(800, 600);
        var sc = RedactionOptions.SecondaryCapture(1024, 768);
        var endo = RedactionOptions.Endoscopy(640, 480);
        var full = RedactionOptions.FullImage(512, 512);

        Assert.That(empty.Regions.Count, Is.EqualTo(0));
        Assert.That(us.Regions.Count, Is.GreaterThan(0));
        Assert.That(sc.Regions.Count, Is.GreaterThan(0));
        Assert.That(endo.Regions.Count, Is.GreaterThan(0));
        Assert.That(full.Regions.Count, Is.EqualTo(1));
    }

    [Test]
    public void RedactionOptions_AllProperties_Accessible()
    {
        var options = new RedactionOptions
        {
            Regions = new[] { new RedactionRegion(0, 0, 100, 100) },
            FillValue = 0xFF,
            UpdateBurnedInAnnotationTag = true,
            SkipCompressed = false
        };

        Assert.That(options.Regions.Count, Is.EqualTo(1));
        Assert.That(options.FillValue, Is.EqualTo(0xFF));
        Assert.That(options.UpdateBurnedInAnnotationTag, Is.True);
        Assert.That(options.SkipCompressed, Is.False);
    }

    #endregion

    #region RedactionRegion API

    [Test]
    public void RedactionRegion_Constructor_Works()
    {
        var region = new RedactionRegion(10, 20, 100, 200, frame: 0);

        Assert.That(region.X, Is.EqualTo(10));
        Assert.That(region.Y, Is.EqualTo(20));
        Assert.That(region.Width, Is.EqualTo(100));
        Assert.That(region.Height, Is.EqualTo(200));
        Assert.That(region.Frame, Is.EqualTo(0));
    }

    [Test]
    public void RedactionRegion_StaticFactories_Work()
    {
        var top = RedactionRegion.TopBar(50, 800);
        var bottom = RedactionRegion.BottomBar(50, 800, 600);
        var left = RedactionRegion.LeftBar(50, 600);
        var right = RedactionRegion.RightBar(50, 800, 600);
        var corners = RedactionRegion.FromCorners(10, 10, 100, 100);

        Assert.That(top.Y, Is.EqualTo(0));
        Assert.That(bottom.Y, Is.EqualTo(550));
        Assert.That(left.X, Is.EqualTo(0));
        Assert.That(right.X, Is.EqualTo(750));
        Assert.That(corners.Width, Is.EqualTo(90));
    }

    [Test]
    public void RedactionRegion_IntersectsImage_Works()
    {
        var region = new RedactionRegion(10, 10, 100, 100);

        Assert.That(region.IntersectsImage(800, 600), Is.True);
        Assert.That(region.IntersectsImage(5, 5), Is.False);
    }

    [Test]
    public void RedactionRegion_Area_Works()
    {
        var region = new RedactionRegion(0, 0, 100, 200);
        Assert.That(region.Area, Is.EqualTo(20000));
    }

    [Test]
    public void RedactionRegion_Equality_Works()
    {
        var a = new RedactionRegion(10, 20, 100, 200);
        var b = new RedactionRegion(10, 20, 100, 200);
        var c = new RedactionRegion(10, 20, 100, 201);

        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void RedactionRegion_ToString_Works()
    {
        var region = new RedactionRegion(10, 20, 100, 200);
        var str = region.ToString();

        Assert.That(str, Does.Contain("10"));
        Assert.That(str, Does.Contain("20"));
        Assert.That(str, Does.Contain("100"));
    }

    #endregion

    #region ActionResolver API

    [Test]
    public void ActionResolver_Resolve_Works()
    {
        var action = ActionResolver.Resolve(DeidentificationAction.Remove);
        Assert.That(action, Is.EqualTo(ResolvedAction.Remove));

        action = ActionResolver.Resolve(DeidentificationAction.Keep);
        Assert.That(action, Is.EqualTo(ResolvedAction.Keep));

        action = ActionResolver.Resolve(DeidentificationAction.RemapUid);
        Assert.That(action, Is.EqualTo(ResolvedAction.RemapUid));
    }

    [Test]
    public void ActionResolver_ResolveConditional_Works()
    {
        // Z/D conditional: Type1 = Dummy, Type2/3 = Zero
        var type1 = ActionResolver.Resolve(DeidentificationAction.ZeroOrDummyConditional, DicomAttributeType.Type1);
        Assert.That(type1, Is.EqualTo(ResolvedAction.ReplaceWithDummy));

        var type3 = ActionResolver.Resolve(DeidentificationAction.ZeroOrDummyConditional, DicomAttributeType.Type3);
        Assert.That(type3, Is.EqualTo(ResolvedAction.ReplaceWithEmpty));
    }

    #endregion

    #region DummyValueGenerator API

    [Test]
    public void DummyValueGenerator_GetDummy_Works()
    {
        var bytes = DummyValueGenerator.GetDummy(DicomVR.PN);
        Assert.That(bytes, Is.Not.Empty);

        bytes = DummyValueGenerator.GetDummy(DicomVR.DA);
        Assert.That(bytes, Is.Not.Empty);

        bytes = DummyValueGenerator.GetDummy(DicomVR.UI);
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public void DummyValueGenerator_GetDummyString_Works()
    {
        var str = DummyValueGenerator.GetDummyString(DicomVR.PN);
        Assert.That(str, Is.EqualTo("ANONYMOUS"));

        str = DummyValueGenerator.GetDummyString(DicomVR.DA);
        Assert.That(str, Is.EqualTo("19000101"));
    }

    [Test]
    public void DummyValueGenerator_SpecificMethods_Work()
    {
        Assert.That(DummyValueGenerator.GetDummyDate(), Is.EqualTo("19000101"));
        Assert.That(DummyValueGenerator.GetDummyTime(), Is.EqualTo("000000.000000"));
        Assert.That(DummyValueGenerator.GetDummyDateTime(), Is.EqualTo("19000101000000.000000"));
        Assert.That(DummyValueGenerator.GetDummyPersonName(), Is.EqualTo("ANONYMOUS"));
        Assert.That(DummyValueGenerator.GetDummyUid(), Is.EqualTo("2.25.0"));
    }

    #endregion

#if NET6_0_OR_GREATER
    #region BatchDeidentifier API

    [Test]
    public async Task BatchDeidentifier_ProcessFilesAsync_Works()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"batch-test-{Guid.NewGuid():N}");
        var outputDir = Path.Combine(Path.GetTempPath(), $"batch-out-{Guid.NewGuid():N}");
        var dbPath = Path.Combine(tempDir, "mappings.db");

        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(outputDir);

            // Create a test file
            var dataset = CreateMinimalDataset();
            var file = new DicomFile(dataset);
            var filePath = Path.Combine(tempDir, "test.dcm");
            await file.SaveAsync(filePath);

            var config = DeidentificationConfigLoader.GetPreset("basic-profile");
            await using var batch = new BatchDeidentifier(dbPath, config);

            var result = await batch.ProcessFilesAsync(new[] { filePath }, outputDir);

            Assert.That(result.TotalFiles, Is.EqualTo(1));
            Assert.That(result.ProcessedFiles, Is.EqualTo(1));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Test]
    public void BatchDeidentificationOptions_AllProperties_Work()
    {
        var options = new BatchDeidentificationOptions
        {
            SearchPattern = "*.dcm",
            Recursive = true,
            PreserveDirectoryStructure = true,
            MaxParallelism = 4,
            ContinueOnError = true,
            Progress = null,
            ExportMappings = true,
            MappingsFileName = "mappings.json"
        };

        Assert.That(options.SearchPattern, Is.EqualTo("*.dcm"));
        Assert.That(options.Recursive, Is.True);
        Assert.That(options.MaxParallelism, Is.EqualTo(4));
    }

    [Test]
    public void BatchDeidentificationResult_AllProperties_Work()
    {
        var result = new BatchDeidentificationResult
        {
            TotalFiles = 10,
            MappingsExportPath = "/path/to/mappings.json"
        };

        Assert.That(result.TotalFiles, Is.EqualTo(10));
        Assert.That(result.ProcessedFiles, Is.EqualTo(0));
        Assert.That(result.SuccessCount, Is.EqualTo(0));
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(result.SuccessRate, Is.EqualTo(0));
        Assert.That(result.AllSucceeded, Is.False);
        Assert.That(result.MappingsExportPath, Is.EqualTo("/path/to/mappings.json"));
    }

    #endregion

    #region DeidentificationConfigLoader API

    [Test]
    public void DeidentificationConfigLoader_GetPreset_Works()
    {
        var basic = DeidentificationConfigLoader.GetPreset("basic-profile");
        var research = DeidentificationConfigLoader.GetPreset("research");
        var trial = DeidentificationConfigLoader.GetPreset("clinical-trial");
        var teaching = DeidentificationConfigLoader.GetPreset("teaching");

        Assert.That(basic, Is.Not.Null);
        Assert.That(research, Is.Not.Null);
        Assert.That(trial, Is.Not.Null);
        Assert.That(teaching, Is.Not.Null);
    }

    [Test]
    public void DeidentificationConfigLoader_PresetNames_Works()
    {
        var names = DeidentificationConfigLoader.PresetNames;

        Assert.That(names, Contains.Item("basic-profile"));
        Assert.That(names, Contains.Item("research"));
    }

    [Test]
    public void DeidentificationConfigLoader_CreateBuilder_Works()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var builder = DeidentificationConfigLoader.CreateBuilder(config);

        using var deid = builder.Build();
        Assert.That(deid, Is.Not.Null);
    }

    #endregion
#endif

    #region Helper Methods

    private static DicomDataset CreateMinimalDataset()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.5"));
        dataset.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.3.4.5.1"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4.5.1.1"));
        dataset.Add(CreateStringElement(new DicomTag(0x0008, 0x0016), DicomVR.UI, DicomUID.SecondaryCaptureImageStorage.ToString())); // SOPClassUID
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "OT"));
        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    #endregion
}
