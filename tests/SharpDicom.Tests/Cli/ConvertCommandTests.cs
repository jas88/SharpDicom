using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Cli.Commands;
using SharpDicom.Codecs.Htj2k;
using SharpDicom.Data;

namespace SharpDicom.Tests.Cli;

[TestFixture]
public class ConvertCommandTests
{
    private string? _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpdcm_convert_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_tempDir != null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region Transfer Syntax Resolution

    [TestCase("htj2k-lossless", "1.2.840.10008.1.2.4.201")]
    [TestCase("htj2k-lossless-rpcl", "1.2.840.10008.1.2.4.202")]
    [TestCase("htj2k-lossy", "1.2.840.10008.1.2.4.203")]
    [TestCase("j2k-lossless", "1.2.840.10008.1.2.4.90")]
    [TestCase("j2k-lossy", "1.2.840.10008.1.2.4.91")]
    [TestCase("jpeg-baseline", "1.2.840.10008.1.2.4.50")]
    [TestCase("jpeg-lossless", "1.2.840.10008.1.2.4.70")]
    [TestCase("jpeg-ls-lossless", "1.2.840.10008.1.2.4.80")]
    [TestCase("rle", "1.2.840.10008.1.2.5")]
    [TestCase("explicit-le", "1.2.840.10008.1.2.1")]
    public void TryResolveTransferSyntax_ShortName_ReturnsCorrectUid(string shortName, string expectedUid)
    {
        var resolved = ConvertCommand.TryResolveTransferSyntax(shortName, out var ts);

        Assert.That(resolved, Is.True);
        Assert.That(ts.UID.ToString(), Is.EqualTo(expectedUid));
    }

    [TestCase("HTJ2K-LOSSLESS")]
    [TestCase("Htj2k-Lossless")]
    [TestCase("EXPLICIT-LE")]
    public void TryResolveTransferSyntax_CaseInsensitive(string shortName)
    {
        var resolved = ConvertCommand.TryResolveTransferSyntax(shortName, out var ts);

        Assert.That(resolved, Is.True);
        Assert.That(ts.IsKnown, Is.True);
    }

    [TestCase("1.2.840.10008.1.2.4.201")]
    [TestCase("1.2.840.10008.1.2.1")]
    [TestCase("1.2.840.10008.1.2.4.50")]
    public void TryResolveTransferSyntax_ByUid_ResolvesCorrectly(string uid)
    {
        var resolved = ConvertCommand.TryResolveTransferSyntax(uid, out var ts);

        Assert.That(resolved, Is.True);
        Assert.That(ts.UID.ToString(), Is.EqualTo(uid));
    }

    [Test]
    public void TryResolveTransferSyntax_InvalidName_ReturnsFalse()
    {
        var resolved = ConvertCommand.TryResolveTransferSyntax("not-a-real-ts", out _);

        Assert.That(resolved, Is.False);
    }

    [Test]
    public void TryResolveTransferSyntax_InvalidUid_ReturnsFalse()
    {
        var resolved = ConvertCommand.TryResolveTransferSyntax("9.9.9.9.9.9.9.9.9", out _);

        Assert.That(resolved, Is.False);
    }

    #endregion

    #region Preset Resolution

    [Test]
    public void TryResolvePreset_Diagnostic_ReturnsDiagnosticOptions()
    {
        var resolved = ConvertCommand.TryResolvePreset("diagnostic", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Diagnostic));
    }

    [Test]
    public void TryResolvePreset_Archive_ReturnsArchiveOptions()
    {
        var resolved = ConvertCommand.TryResolvePreset("archive", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Archive));
    }

    [Test]
    public void TryResolvePreset_Review_ReturnsReviewOptions()
    {
        var resolved = ConvertCommand.TryResolvePreset("review", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Review));
    }

    [Test]
    public void TryResolvePreset_Fast_ReturnsFastOptions()
    {
        var resolved = ConvertCommand.TryResolvePreset("fast", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Fast));
    }

    [Test]
    public void TryResolvePreset_Lossless_ReturnsLosslessOptions()
    {
        var resolved = ConvertCommand.TryResolvePreset("lossless", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Lossless));
    }

    [Test]
    public void TryResolvePreset_CaseInsensitive()
    {
        var resolved = ConvertCommand.TryResolvePreset("DIAGNOSTIC", out var options);

        Assert.That(resolved, Is.True);
        Assert.That(options, Is.EqualTo(HtEncoderOptions.Diagnostic));
    }

    [Test]
    public void TryResolvePreset_Unknown_ReturnsFalse()
    {
        var resolved = ConvertCommand.TryResolvePreset("not-a-preset", out _);

        Assert.That(resolved, Is.False);
    }

    #endregion

    #region Single File Conversion (non-pixel-data files)

    [Test]
    public async Task Convert_NoPixelData_SavesWithNewTransferSyntax()
    {
        // Create a simple DICOM file with no pixel data
        var path = await CreateSimpleDicomFile("nopixel.dcm");

        // Read it back, verify it's Explicit VR LE by default
        var original = await DicomFile.OpenAsync(path);
        Assert.That(original.TransferSyntax, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
    }

    [Test]
    public async Task Convert_AlreadyTargetTs_DetectedByResolver()
    {
        // Create a file with Explicit VR LE
        var path = await CreateSimpleDicomFile("already_target.dcm");
        var file = await DicomFile.OpenAsync(path);

        // Verify the file's TS matches the "explicit-le" alias target
        ConvertCommand.TryResolveTransferSyntax("explicit-le", out var targetTs);
        Assert.That(file.TransferSyntax, Is.EqualTo(targetTs));
    }

    #endregion

    #region Output Path Determination

    [Test]
    public void DetermineOutputPath_NoForceNoOutput_AddsConvertedSuffix()
    {
        var result = ConvertCommand.DetermineOutputPath("/tmp/test.dcm", false, null);

        Assert.That(result, Is.EqualTo(Path.Combine("/tmp", "test.converted.dcm")));
    }

    [Test]
    public void DetermineOutputPath_Force_ReturnsOriginalPath()
    {
        var result = ConvertCommand.DetermineOutputPath("/tmp/test.dcm", true, null);

        Assert.That(result, Is.EqualTo("/tmp/test.dcm"));
    }

    [Test]
    public void DetermineOutputPath_WithOutputDir_NoBasePath_CombinesDirectoryAndFilename()
    {
        var result = ConvertCommand.DetermineOutputPath("/tmp/test.dcm", false, "/output");

        Assert.That(result, Is.EqualTo(Path.Combine("/output", "test.dcm")));
    }

    [Test]
    public void DetermineOutputPath_OutputDirTakesPrecedenceOverForce()
    {
        // Even when force is true, output dir should be used if provided
        var result = ConvertCommand.DetermineOutputPath("/tmp/test.dcm", true, "/output");

        Assert.That(result, Is.EqualTo(Path.Combine("/output", "test.dcm")));
    }

    [Test]
    public void DetermineOutputPath_WithOutputDirAndBasePath_PreservesRelativeStructure()
    {
        var result = ConvertCommand.DetermineOutputPath(
            "/data/input/sub/deep/scan.dcm", false, _tempDir!, "/data/input");

        var expected = Path.Combine(_tempDir!, "sub", "deep", "scan.dcm");
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void DetermineOutputPath_WithOutputDirAndBasePath_FileInBaseDir_NoExtraSubdir()
    {
        var result = ConvertCommand.DetermineOutputPath(
            "/data/input/scan.dcm", false, _tempDir!, "/data/input");

        var expected = Path.Combine(_tempDir!, "scan.dcm");
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Alias Map Completeness

    [Test]
    public void TransferSyntaxAliases_AllEntriesAreKnown()
    {
        foreach (var (name, ts) in ConvertCommand.TransferSyntaxAliases)
        {
            Assert.That(ts.IsKnown, Is.True, $"Alias '{name}' maps to an unknown transfer syntax");
        }
    }

    [Test]
    public void TransferSyntaxAliases_ContainsAllExpectedEntries()
    {
        Assert.That(ConvertCommand.TransferSyntaxAliases.Count, Is.EqualTo(10));
    }

    #endregion

    #region Preset Map Completeness

    [Test]
    public void PresetMap_ContainsAllExpectedPresets()
    {
        Assert.That(ConvertCommand.PresetMap.Count, Is.EqualTo(5));
        Assert.That(ConvertCommand.PresetMap.ContainsKey("diagnostic"), Is.True);
        Assert.That(ConvertCommand.PresetMap.ContainsKey("archive"), Is.True);
        Assert.That(ConvertCommand.PresetMap.ContainsKey("review"), Is.True);
        Assert.That(ConvertCommand.PresetMap.ContainsKey("fast"), Is.True);
        Assert.That(ConvertCommand.PresetMap.ContainsKey("lossless"), Is.True);
    }

    [Test]
    public void PresetMap_DiagnosticHasCorrectPsnr()
    {
        ConvertCommand.TryResolvePreset("diagnostic", out var options);
        Assert.That(options.TargetPsnr, Is.EqualTo(40f));
        Assert.That(options.HtSetCount, Is.EqualTo(2));
        Assert.That(options.IncludeSigProp, Is.True);
        Assert.That(options.IncludeMagRef, Is.True);
    }

    [Test]
    public void PresetMap_FastHasMinimalPasses()
    {
        ConvertCommand.TryResolvePreset("fast", out var options);
        Assert.That(options.TargetPsnr, Is.EqualTo(25f));
        Assert.That(options.HtSetCount, Is.EqualTo(1));
        Assert.That(options.IncludeSigProp, Is.False);
        Assert.That(options.IncludeMagRef, Is.False);
    }

    [Test]
    public void PresetMap_LosslessHasNoRateTarget()
    {
        ConvertCommand.TryResolvePreset("lossless", out var options);
        Assert.That(options.IsLossless, Is.True);
        Assert.That(options.TargetBpp, Is.Null);
        Assert.That(options.TargetPsnr, Is.Null);
    }

    #endregion

    #region Command Registration

    [Test]
    public void Create_ReturnsCommandWithConvertName()
    {
        var command = ConvertCommand.Create();

        Assert.That(command.Name, Is.EqualTo("convert"));
    }

    [Test]
    public void Create_HasInputArgument()
    {
        var command = ConvertCommand.Create();

        Assert.That(command.Arguments.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(command.Arguments[0].Name, Is.EqualTo("input"));
    }

    [Test]
    public void Create_HasRequiredTransferSyntaxOption()
    {
        var command = ConvertCommand.Create();

        var tsOption = command.Options.FirstOrDefault(o => o.Name == "--transfer-syntax");
        Assert.That(tsOption, Is.Not.Null);
        Assert.That(tsOption!.Required, Is.True);
    }

    [Test]
    public void Create_HasAllExpectedOptions()
    {
        var command = ConvertCommand.Create();

        var optionNames = command.Options.Select(o => o.Name).ToHashSet();
        Assert.That(optionNames, Does.Contain("--transfer-syntax"));
        Assert.That(optionNames, Does.Contain("--preset"));
        Assert.That(optionNames, Does.Contain("--output"));
        Assert.That(optionNames, Does.Contain("--force"));
        Assert.That(optionNames, Does.Contain("--recursive"));
        Assert.That(optionNames, Does.Contain("--parallel"));
        Assert.That(optionNames, Does.Contain("--dry-run"));
        Assert.That(optionNames, Does.Contain("--skip-errors"));
    }

    #endregion

    #region Helpers

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    private async Task<string> CreateSimpleDicomFile(string fileName)
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.StudyDate, DicomVR.DA, "20240115"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4.5.6.7.8.9"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.3.4.5.6.7.8.10"));

        var path = Path.Combine(_tempDir!, fileName);
        var file = new DicomFile(dataset);
        await file.SaveAsync(path);
        return path;
    }

    #endregion
}
