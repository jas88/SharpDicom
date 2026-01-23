using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests;

/// <summary>
/// Unit tests for <see cref="DicomFile"/>.
/// </summary>
[TestFixture]
public class DicomFileTests
{
    #region OpenAsync Tests

    [Test]
    public async Task OpenAsync_FromStream_ReturnsValidFile()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file, Is.Not.Null);
        Assert.That(file.Dataset, Is.Not.Null);
        Assert.That(file.FileMetaInfo, Is.Not.Null);
    }

    [Test]
    public async Task OpenAsync_ParsesFileMetaInfo()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.FileMetaInfo.Contains(DicomTag.TransferSyntaxUID), Is.True);
    }

    [Test]
    public async Task OpenAsync_ParsesDataset()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.Dataset.Contains(new DicomTag(0x0010, 0x0010)), Is.True, "Should contain PatientName");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0010, 0x0020)), Is.True, "Should contain PatientID");
    }

    [Test]
    public async Task OpenAsync_SetsTransferSyntax()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.TransferSyntax.IsExplicitVR, Is.True);
        Assert.That(file.TransferSyntax.IsLittleEndian, Is.True);
    }

    [Test]
    public async Task OpenAsync_PreservesPreamble()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.Preamble.Length, Is.EqualTo(128));
    }

    #endregion

    #region Open (Synchronous) Tests

    [Test]
    public void Open_Synchronous_Works()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = DicomFile.Open(stream);

        Assert.That(file, Is.Not.Null);
        Assert.That(file.Dataset, Is.Not.Null);
    }

    [Test]
    public void Open_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DicomFile.Open((Stream)null!));
    }

    [Test]
    public void Open_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DicomFile.Open((string)null!));
    }

    #endregion

    #region GetString Tests

    [Test]
    public async Task GetString_ReturnsValue()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var patientId = file.GetString(new DicomTag(0x0010, 0x0020));
        Assert.That(patientId, Is.EqualTo("PATIENT001"));
    }

    [Test]
    public async Task GetString_ReturnsNullForMissingTag()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var value = file.GetString(new DicomTag(0x9999, 0x9999));
        Assert.That(value, Is.Null);
    }

    #endregion

    #region Contains Tests

    [Test]
    public async Task Contains_ReturnsTrue_WhenTagExists()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.Contains(new DicomTag(0x0010, 0x0020)), Is.True);
    }

    [Test]
    public async Task Contains_ReturnsFalse_WhenTagMissing()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.Contains(new DicomTag(0x9999, 0x9999)), Is.False);
    }

    #endregion

    #region Indexer Tests

    [Test]
    public async Task Indexer_ReturnsElement()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var element = file[new DicomTag(0x0010, 0x0020)];
        Assert.That(element, Is.Not.Null);
    }

    [Test]
    public async Task Indexer_ReturnsNull_WhenMissing()
    {
        var data = CreateTestDicomFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var element = file[new DicomTag(0x9999, 0x9999)];
        Assert.That(element, Is.Null);
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void Constructor_FromDataset_Works()
    {
        var dataset = new DicomDataset();
        dataset.Add(new DicomStringElement(new DicomTag(0x0010, 0x0020), DicomVR.LO, "TEST"u8.ToArray()));

        var file = new DicomFile(dataset);

        Assert.That(file.Dataset, Is.SameAs(dataset));
        Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.ExplicitVRLittleEndian));
    }

    [Test]
    public void Constructor_WithTransferSyntax_SetsCorrectly()
    {
        var dataset = new DicomDataset();

        var file = new DicomFile(dataset, TransferSyntax.ImplicitVRLittleEndian);

        Assert.That(file.TransferSyntax, Is.EqualTo(TransferSyntax.ImplicitVRLittleEndian));
    }

    [Test]
    public void Constructor_NullDataset_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DicomFile(null!));
    }

    [Test]
    public void Constructor_InitializesEmptyFileMetaInfo()
    {
        var dataset = new DicomDataset();

        var file = new DicomFile(dataset);

        Assert.That(file.FileMetaInfo, Is.Not.Null);
        Assert.That(file.FileMetaInfo.Count, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_InitializesPreamble()
    {
        var dataset = new DicomDataset();

        var file = new DicomFile(dataset);

        Assert.That(file.Preamble.Length, Is.EqualTo(128));
        Assert.That(file.Preamble.ToArray(), Is.All.EqualTo(0));
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateTestDicomFile()
    {
        using var ms = new MemoryStream();

        // 128 byte preamble
        ms.Write(new byte[128]);

        // DICM prefix
        ms.Write("DICM"u8);

        // File Meta Information (Explicit VR LE)
        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // Dataset elements
        WriteElement(ms, 0x0008, 0x0060, "CS", "CT "u8.ToArray());
        WriteElement(ms, 0x0010, 0x0010, "PN", "Doe^John"u8.ToArray());
        WriteElement(ms, 0x0010, 0x0020, "LO", "PATIENT001"u8.ToArray());

        return ms.ToArray();
    }

    private static void WriteElement(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));

        var vrCode = new DicomVR(vr);
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
