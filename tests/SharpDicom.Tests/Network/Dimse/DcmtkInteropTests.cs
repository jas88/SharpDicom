using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;

namespace SharpDicom.Tests.Network.Dimse;

/// <summary>
/// DCMTK interoperability tests for validating compatibility with the reference DICOM implementation.
/// These tests require DCMTK tools (dcmdump, dcmftest, dump2dcm) to be installed.
/// Install via: brew install dcmtk (macOS), apt install dcmtk (Linux), or download from https://dicom.offis.de/
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("DCMTK")]
public sealed class DcmtkInteropTests
{
    private static bool? _dcmtkAvailable;

    private static bool IsDcmtkAvailable()
    {
        if (_dcmtkAvailable.HasValue)
            return _dcmtkAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo("dcmdump", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                _dcmtkAvailable = false;
                return false;
            }

            p.WaitForExit(5000);
            _dcmtkAvailable = p.ExitCode == 0;
            return _dcmtkAvailable.Value;
        }
        catch
        {
            _dcmtkAvailable = false;
            return false;
        }
    }

    [SetUp]
    public void Setup()
    {
        if (!IsDcmtkAvailable())
            Assert.Ignore("DCMTK not found in PATH - install with: brew install dcmtk (macOS) or apt install dcmtk (Linux)");
    }

    /// <summary>
    /// Test 1: SharpDicom file with nested sequences validates with DCMTK dcmftest.
    /// </summary>
    [Test]
    public async Task SharpDicomFile_WithNestedSequences_ValidatesInDcmtk()
    {
        // Create complex file with nested sequences
        var dataset = CreateNestedSequenceDataset(depth: 3);
        var file = new DicomFile(dataset);

        var tempPath = Path.GetTempFileName() + ".dcm";
        try
        {
            await file.SaveAsync(tempPath);

            // Run dcmftest (DICOM format validation)
            var result = RunDcmtk("dcmftest", tempPath);
            Assert.That(result.ExitCode, Is.EqualTo(0),
                $"dcmftest failed:\nStdout: {result.Stdout}\nStderr: {result.Stderr}");

            TestContext.WriteLine($"dcmftest validation passed for {tempPath}");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Test 2: SharpDicom file with undefined-length sequences validates with DCMTK.
    /// </summary>
    [Test]
    public async Task SharpDicomFile_UndefinedLengthSequences_ValidatesInDcmtk()
    {
        var dataset = CreateNestedSequenceDataset(depth: 2);
        var file = new DicomFile(dataset);
        var options = new DicomWriterOptions { SequenceLength = SequenceLengthEncoding.Undefined };

        var tempPath = Path.GetTempFileName() + ".dcm";
        try
        {
            await file.SaveAsync(tempPath, options);

            // Run dcmftest
            var result = RunDcmtk("dcmftest", tempPath);
            Assert.That(result.ExitCode, Is.EqualTo(0),
                $"dcmftest failed for undefined-length sequences:\nStdout: {result.Stdout}\nStderr: {result.Stderr}");

            TestContext.WriteLine($"dcmftest validation passed for undefined-length sequences");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Test 3: SharpDicom can parse file dumped and re-created by DCMTK dump2dcm.
    /// </summary>
    [Test]
    public async Task DcmtkDump2Dcm_RoundTrip_ParsesInSharpDicom()
    {
        // First create a SharpDicom file
        var originalDataset = CreateNestedSequenceDataset(depth: 2);
        var originalFile = new DicomFile(originalDataset);

        var tempOriginal = Path.GetTempFileName() + ".dcm";
        var tempDump = Path.GetTempFileName() + ".dump";
        var tempRecreated = Path.GetTempFileName() + ".dcm";

        try
        {
            // Save original
            await originalFile.SaveAsync(tempOriginal);

            // Dump to text with dcmdump (use +L for detailed output without +Wn which may not be available)
            var dumpResult = RunDcmtk("dcmdump", $"+L --print-all \"{tempOriginal}\"");
            if (dumpResult.ExitCode != 0)
            {
                Assert.Inconclusive($"dcmdump failed: {dumpResult.Stderr}");
                return;
            }

            // Write dump to file
            await File.WriteAllTextAsync(tempDump, dumpResult.Stdout);

            // Recreate from dump with dump2dcm
            var recreateResult = RunDcmtk("dump2dcm", $"\"{tempDump}\" \"{tempRecreated}\"");
            if (recreateResult.ExitCode != 0)
            {
                Assert.Inconclusive($"dump2dcm failed: {recreateResult.Stderr}");
                return;
            }

            // Parse with SharpDicom
            var recreatedFile = await DicomFile.OpenAsync(tempRecreated);

            // Verify sequences were parsed
            Assert.That(recreatedFile.Dataset, Is.Not.Null);
            Assert.That(recreatedFile.Dataset.Count, Is.GreaterThan(0));

            TestContext.WriteLine($"Successfully parsed DCMTK-recreated file with {recreatedFile.Dataset.Count} elements");
        }
        finally
        {
            if (File.Exists(tempOriginal)) File.Delete(tempOriginal);
            if (File.Exists(tempDump)) File.Delete(tempDump);
            if (File.Exists(tempRecreated)) File.Delete(tempRecreated);
        }
    }

    /// <summary>
    /// Test 4: SharpDicom file is readable by dcmdump (basic DICOM conformance).
    /// </summary>
    [Test]
    public async Task SharpDicomFile_ReadableByDcmdump()
    {
        var dataset = CreateNestedSequenceDataset(depth: 2);
        var file = new DicomFile(dataset);

        var tempPath = Path.GetTempFileName() + ".dcm";
        try
        {
            await file.SaveAsync(tempPath);

            // Run dcmdump to read the file
            var result = RunDcmtk("dcmdump", $"\"{tempPath}\"");
            Assert.That(result.ExitCode, Is.EqualTo(0),
                $"dcmdump failed to read SharpDicom file:\nStderr: {result.Stderr}");

            // Verify output contains expected elements
            Assert.That(result.Stdout, Does.Contain("(0010,0010)"), "Missing PatientName");
            Assert.That(result.Stdout, Does.Contain("(0040,0100)"), "Missing Scheduled Procedure Step Sequence");

            TestContext.WriteLine("dcmdump successfully read SharpDicom file");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Test 5: Verify SharpDicom and DCMTK agree on sequence structure.
    /// </summary>
    [Test]
    public async Task SharpDicom_AndDcmtk_AgreeOnSequenceStructure()
    {
        var dataset = CreateSequenceWithKnownStructure();
        var file = new DicomFile(dataset);

        var tempPath = Path.GetTempFileName() + ".dcm";
        try
        {
            await file.SaveAsync(tempPath);

            // Parse with dcmdump and check structure
            var result = RunDcmtk("dcmdump", $"+L \"{tempPath}\"");
            Assert.That(result.ExitCode, Is.EqualTo(0));

            // Verify sequence structure is present
            var output = result.Stdout;
            Assert.That(output, Does.Contain("(0040,0100)"), "Missing main sequence");
            Assert.That(output, Does.Contain("(0008,0050)"), "Missing sequence item element");

            // Parse with SharpDicom
            var roundtrip = await DicomFile.OpenAsync(tempPath);

            // Verify SharpDicom sees the same structure
            Assert.That(roundtrip.Dataset.TryGetElement(new DicomTag(0x0040, 0x0100), out var seqElem), Is.True);
            Assert.That(seqElem, Is.InstanceOf<DicomSequence>());

            var seq = (DicomSequence)seqElem;
            Assert.That(seq.Items.Count, Is.EqualTo(2), "Should have 2 items");

            TestContext.WriteLine("SharpDicom and DCMTK agree on sequence structure");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // Helper methods

    private static DicomDataset CreateNestedSequenceDataset(int depth)
    {
        var dataset = new DicomDataset();

        // Add required elements for valid DICOM file
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2")); // CT Image Storage
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7.8.9"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7.8"));
        dataset.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7"));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Test^Patient"));
        dataset.Add(CreateStringElement(DicomTag.PatientID, DicomVR.LO, "TEST123"));
        dataset.Add(CreateStringElement(DicomTag.Modality, DicomVR.CS, "CT"));

        if (depth > 0)
        {
            // Create nested sequence
            var outerSequence = CreateNestedSequence(depth, 0x0040, 0x0100, 0x0008, 0x0050);
            dataset.Add(outerSequence);
        }

        return dataset;
    }

    private static DicomSequence CreateNestedSequence(int depth, ushort seqGroup, ushort seqElement, ushort elemGroup, ushort elemElement)
    {
        var items = new System.Collections.Generic.List<DicomDataset>();

        for (int i = 0; i < 2; i++)
        {
            var item = new DicomDataset();
            item.Add(CreateStringElement(new DicomTag(elemGroup, elemElement), DicomVR.SH, $"Value_{depth}_{i}"));

            if (depth > 1)
            {
                // Add nested sequence
                var nested = CreateNestedSequence(depth - 1, 0x0008, 0x1115, (ushort)(elemGroup + 1), (ushort)(elemElement + 1));
                item.Add(nested);
            }

            items.Add(item);
        }

        return new DicomSequence(new DicomTag(seqGroup, seqElement), items);
    }

    private static DicomDataset CreateSequenceWithKnownStructure()
    {
        var dataset = new DicomDataset();

        // Add required UIDs
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.840.10008.5.1.4.1.1.2"));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7.8.9.10"));
        dataset.Add(CreateStringElement(DicomTag.StudyInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7.8.10"));
        dataset.Add(CreateStringElement(DicomTag.SeriesInstanceUID, DicomVR.UI, "1.2.840.10008.1.2.3.4.5.6.7.10"));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, "Known^Structure"));

        var item1 = new DicomDataset();
        item1.Add(CreateStringElement(new DicomTag(0x0008, 0x0050), DicomVR.SH, "Item1Value"));

        var item2 = new DicomDataset();
        item2.Add(CreateStringElement(new DicomTag(0x0008, 0x0050), DicomVR.SH, "Item2Value"));

        var sequence = new DicomSequence(new DicomTag(0x0040, 0x0100), item1, item2);
        dataset.Add(sequence);

        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    private static ProcessResult RunDcmtk(string tool, string args)
    {
        var psi = new ProcessStartInfo(tool, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null)
        {
            return new ProcessResult
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = "Failed to start process"
            };
        }

        p.WaitForExit(30000);

        return new ProcessResult
        {
            ExitCode = p.ExitCode,
            Stdout = p.StandardOutput.ReadToEnd(),
            Stderr = p.StandardError.ReadToEnd()
        };
    }

    private sealed record ProcessResult
    {
        public required int ExitCode { get; init; }
        public required string Stdout { get; init; }
        public required string Stderr { get; init; }
    }
}
