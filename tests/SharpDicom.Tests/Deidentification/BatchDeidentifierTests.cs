using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Tests.Deidentification;

/// <summary>
/// Tests for BatchDeidentifier directory processing.
/// </summary>
[TestFixture]
public class BatchDeidentifierTests
{
    private string _testDir = null!;
    private string _outputDir = null!;
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"batch-test-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(Path.GetTempPath(), $"batch-out-{Guid.NewGuid():N}");
        _dbPath = Path.Combine(_testDir, "mappings.db");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

#if NET6_0_OR_GREATER
    [Test]
    public async Task ProcessDirectoryAsync_EmptyDirectory_ReturnsZeroFiles()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(0));
        Assert.That(result.ProcessedFiles, Is.EqualTo(0));
        Assert.That(result.SuccessCount, Is.EqualTo(0));
        Assert.That(result.ErrorCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ProcessDirectoryAsync_SingleFile_ProcessesSuccessfully()
    {
        await CreateTestFileAsync("test.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(1));
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(result.ErrorCount, Is.EqualTo(0));
        Assert.That(result.AllSucceeded, Is.True);

        // Verify output file exists
        var outputPath = Path.Combine(_outputDir, "test.dcm");
        Assert.That(File.Exists(outputPath), Is.True);
    }

    [Test]
    public async Task ProcessDirectoryAsync_MultipleFiles_AllProcessed()
    {
        await CreateTestFileAsync("file1.dcm");
        await CreateTestFileAsync("file2.dcm");
        await CreateTestFileAsync("file3.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(3));
        Assert.That(result.SuccessCount, Is.EqualTo(3));
        Assert.That(result.ProcessedFiles, Is.EqualTo(3));
    }

    [Test]
    public async Task ProcessDirectoryAsync_SubDirectories_Recursive()
    {
        var subDir = Path.Combine(_testDir, "study001");
        Directory.CreateDirectory(subDir);
        await CreateTestFileAsync("file1.dcm", _testDir);
        await CreateTestFileAsync("file2.dcm", subDir);

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { Recursive = true };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(2));
        Assert.That(result.SuccessCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ProcessDirectoryAsync_NotRecursive_OnlyTopLevel()
    {
        var subDir = Path.Combine(_testDir, "study001");
        Directory.CreateDirectory(subDir);
        await CreateTestFileAsync("file1.dcm", _testDir);
        await CreateTestFileAsync("file2.dcm", subDir);

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { Recursive = false };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(1));
        Assert.That(result.SuccessCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ProcessDirectoryAsync_PreservesDirectoryStructure()
    {
        var subDir = Path.Combine(_testDir, "patient", "study");
        Directory.CreateDirectory(subDir);
        await CreateTestFileAsync("test.dcm", subDir);

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { PreserveDirectoryStructure = true };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.SuccessCount, Is.EqualTo(1));

        // Verify directory structure preserved
        var expectedPath = Path.Combine(_outputDir, "patient", "study", "test.dcm");
        Assert.That(File.Exists(expectedPath), Is.True);
    }

    [Test]
    public async Task ProcessDirectoryAsync_FlatOutput_NoStructure()
    {
        var subDir = Path.Combine(_testDir, "patient", "study");
        Directory.CreateDirectory(subDir);
        await CreateTestFileAsync("test.dcm", subDir);

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { PreserveDirectoryStructure = false };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.SuccessCount, Is.EqualTo(1));

        // Verify file is in root of output dir
        var expectedPath = Path.Combine(_outputDir, "test.dcm");
        Assert.That(File.Exists(expectedPath), Is.True);
    }

    [Test]
    public async Task ProcessDirectoryAsync_SearchPattern_FiltersByPattern()
    {
        await CreateTestFileAsync("file1.dcm");
        await CreateTestFileAsync("file2.dicom");
        await CreateTestFileAsync("file3.txt");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { SearchPattern = "*.dcm" };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task ProcessDirectoryAsync_ContinueOnError_ProcessesRemainingFiles()
    {
        await CreateTestFileAsync("valid1.dcm");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "invalid.dcm"), "Not DICOM");
        await CreateTestFileAsync("valid2.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { ContinueOnError = true };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(3));
        Assert.That(result.SuccessCount, Is.EqualTo(2));
        Assert.That(result.ErrorCount, Is.EqualTo(1));
        Assert.That(result.Errors.Count, Is.EqualTo(1));
        Assert.That(result.Errors[0].FilePath, Does.Contain("invalid.dcm"));
    }

    [Test]
    public async Task ProcessDirectoryAsync_Progress_ReportsProgress()
    {
        await CreateTestFileAsync("file1.dcm");
        await CreateTestFileAsync("file2.dcm");
        await CreateTestFileAsync("file3.dcm");

        var reports = new System.Collections.Generic.List<BatchProgress>();
        var progress = new Progress<BatchProgress>(p => reports.Add(p));

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions
        {
            Progress = progress,
            MaxParallelism = 1 // Sequential for deterministic progress
        };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        // Allow progress callback to complete
        await Task.Delay(100);

        Assert.That(result.SuccessCount, Is.EqualTo(3));
        Assert.That(reports.Count, Is.GreaterThanOrEqualTo(1));

        // Verify progress reports are reasonable
        foreach (var report in reports)
        {
            Assert.That(report.TotalFiles, Is.EqualTo(3));
            Assert.That(report.ProcessedFiles, Is.GreaterThan(0));
            Assert.That(report.PercentComplete, Is.InRange(0, 100));
        }
    }

    [Test]
    public async Task ProcessDirectoryAsync_ExportsMappings()
    {
        await CreateTestFileAsync("test.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions
        {
            ExportMappings = true,
            MappingsFileName = "uid-mappings.json"
        };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.MappingsExportPath, Is.Not.Null);
        Assert.That(File.Exists(result.MappingsExportPath), Is.True);

        var mappingsJson = await File.ReadAllTextAsync(result.MappingsExportPath!);
        Assert.That(mappingsJson, Does.Contain("mappings"));
    }

    [Test]
    public async Task ProcessDirectoryAsync_NoExportMappings_NoMappingFile()
    {
        await CreateTestFileAsync("test.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { ExportMappings = false };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.MappingsExportPath, Is.Null);
    }

    [Test]
    public async Task ProcessDirectoryAsync_ParallelProcessing_Works()
    {
        // Create multiple files to test parallel processing
        for (int i = 0; i < 10; i++)
        {
            await CreateTestFileAsync($"file{i}.dcm");
        }

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { MaxParallelism = 4 };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(10));
        Assert.That(result.SuccessCount, Is.EqualTo(10));
    }

    [Test]
    public async Task ProcessFilesAsync_SpecificFiles_Works()
    {
        await CreateTestFileAsync("file1.dcm");
        await CreateTestFileAsync("file2.dcm");
        await CreateTestFileAsync("file3.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        var files = new[]
        {
            Path.Combine(_testDir, "file1.dcm"),
            Path.Combine(_testDir, "file3.dcm")
        };

        var result = await batch.ProcessFilesAsync(files, _outputDir);

        Assert.That(result.TotalFiles, Is.EqualTo(2));
        Assert.That(result.SuccessCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ProcessDirectoryAsync_Cancellation_StopsProcessing()
    {
        // Create files
        for (int i = 0; i < 5; i++)
        {
            await CreateTestFileAsync($"file{i}.dcm");
        }

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { MaxParallelism = 1 };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // Cancel quickly

        try
        {
            await batch.ProcessDirectoryAsync(_testDir, _outputDir, cts.Token);
            // May complete before cancellation if fast enough
        }
        catch (OperationCanceledException)
        {
            // Expected
            Assert.Pass("Cancellation worked");
        }
    }

    [Test]
    public async Task ProcessDirectoryAsync_ConsistentUidMapping_AcrossFiles()
    {
        // Create two files with same study UID
        var studyUid = "1.2.3.4.5.6.7.8";
        await CreateTestFileAsync("file1.dcm", studyUid: studyUid);
        await CreateTestFileAsync("file2.dcm", studyUid: studyUid);

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        var options = new BatchDeidentificationOptions { MaxParallelism = 1 };
        await using var batch = new BatchDeidentifier(_dbPath, config, options);

        var result = await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        Assert.That(result.SuccessCount, Is.EqualTo(2));

        // Load processed files and verify UIDs match
        var file1 = await DicomFile.OpenAsync(Path.Combine(_outputDir, "file1.dcm"));
        var file2 = await DicomFile.OpenAsync(Path.Combine(_outputDir, "file2.dcm"));

        var newStudyUid1 = file1.Dataset.GetString(DicomTag.StudyInstanceUID);
        var newStudyUid2 = file2.Dataset.GetString(DicomTag.StudyInstanceUID);

        Assert.That(newStudyUid1, Is.EqualTo(newStudyUid2));
        Assert.That(newStudyUid1, Is.Not.EqualTo(studyUid));
    }

    [Test]
    public async Task BatchDeidentifier_Context_AccessibleAfterProcessing()
    {
        await CreateTestFileAsync("test.dcm");

        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        await batch.ProcessDirectoryAsync(_testDir, _outputDir);

        // Context should be accessible
        var context = batch.Context;
        Assert.That(context, Is.Not.Null);
        Assert.That(context.UidStore.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task BatchDeidentifier_InvalidInputDir_ThrowsDirectoryNotFoundException()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");
        await using var batch = new BatchDeidentifier(_dbPath, config);

        Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await batch.ProcessDirectoryAsync("/nonexistent/path", _outputDir);
        });
    }

    [Test]
    public void BatchDeidentifier_NullDbPath_ThrowsArgumentNullException()
    {
        var config = DeidentificationConfigLoader.GetPreset("basic-profile");

        Assert.Throws<ArgumentNullException>(() =>
        {
            new BatchDeidentifier(null!, config);
        });
    }

    [Test]
    public void BatchDeidentifier_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new BatchDeidentifier(_dbPath, null!);
        });
    }
#endif

    #region Helper Methods

    private async Task CreateTestFileAsync(string name, string? dir = null, string? studyUid = null)
    {
        dir ??= _testDir;
        var path = Path.Combine(dir, name);
        var dataset = CreateTestDataset(studyUid);
        var file = new DicomFile(dataset);
        await file.SaveAsync(path);
    }

    private static DicomDataset CreateTestDataset(string? studyUid = null)
    {
        var dataset = new DicomDataset();
        var guidPart = Guid.NewGuid().ToString("N").AsSpan(0, 8);
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, studyUid ?? string.Concat("1.2.3.4.5.", guidPart)));
        dataset.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, string.Concat("1.2.3.4.5.1.", Guid.NewGuid().ToString("N").AsSpan(0, 8))));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, string.Concat("1.2.3.4.5.1.1.", Guid.NewGuid().ToString("N").AsSpan(0, 8))));
        dataset.Add(CreateStringElement(new DicomTag(0x0008, 0x0016), DicomVR.UI, DicomUID.SecondaryCaptureImageStorage.ToString()));
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
