using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO;

[TestFixture]
public class DicomFileWriterTests
{
    private const string TestSopClassUid = "1.2.840.10008.5.1.4.1.1.2"; // CT Image Storage
    private const string TestSopInstanceUid = "1.2.3.4.5.6.7.8.9";
    private const string TestPatientName = "Test^Patient";

    #region File Structure Tests

    [Test]
    public void Write_ProducesPreambleOf128Bytes()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        stream.Position = 0;
        var preamble = new byte[128];
        stream.Read(preamble, 0, 128);

        // Default preamble should be all zeros
        Assert.That(preamble, Is.All.EqualTo(0));
    }

    [Test]
    public void Write_DicmPrefixPresentAtOffset128()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        stream.Position = 128;
        var prefix = new byte[4];
        stream.Read(prefix, 0, 4);

        Assert.That(Encoding.ASCII.GetString(prefix), Is.EqualTo("DICM"));
    }

    [Test]
    public void Write_CustomPreambleIsWrittenCorrectly()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        // Create custom preamble with identifiable pattern
        var customPreamble = new byte[128];
        for (int i = 0; i < 128; i++)
            customPreamble[i] = (byte)(i % 256);

        var options = new DicomWriterOptions { Preamble = customPreamble };

        using (var writer = new DicomFileWriter(stream, options, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        stream.Position = 0;
        var writtenPreamble = new byte[128];
        stream.Read(writtenPreamble, 0, 128);

        Assert.That(writtenPreamble, Is.EqualTo(customPreamble));
    }

    [Test]
    public void Write_FmiElementsPresentAfterDicm()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        // FMI starts at offset 132 (128 preamble + 4 DICM)
        stream.Position = 132;
        var tagBytes = new byte[4];
        stream.Read(tagBytes, 0, 4);

        // First FMI element should be (0002,0000) FileMetaInformationGroupLength
        ushort group = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(0, 2));
        ushort element = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(group, Is.EqualTo(0x0002));
            Assert.That(element, Is.EqualTo(0x0000));
        });
    }

    #endregion

    #region FMI Generation Tests

    [Test]
    public void FileMetaInfoGenerator_VersionIs0x00_0x01()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var versionElement = fmi[DicomTag.FileMetaInformationVersion];
        Assert.That(versionElement, Is.Not.Null);
        Assert.That(versionElement!.RawValue.Span.ToArray(), Is.EqualTo(new byte[] { 0x00, 0x01 }));
    }

    [Test]
    public void FileMetaInfoGenerator_MediaStorageSOPClassUIDMatchesDataset()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var uidElement = fmi[DicomTag.MediaStorageSOPClassUID] as DicomStringElement;
        Assert.That(uidElement, Is.Not.Null);

        var uidValue = uidElement!.GetString()!.TrimEnd('\0');
        Assert.That(uidValue, Is.EqualTo(TestSopClassUid));
    }

    [Test]
    public void FileMetaInfoGenerator_MediaStorageSOPInstanceUIDMatchesDataset()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var uidElement = fmi[DicomTag.MediaStorageSOPInstanceUID] as DicomStringElement;
        Assert.That(uidElement, Is.Not.Null);

        var uidValue = uidElement!.GetString()!.TrimEnd('\0');
        Assert.That(uidValue, Is.EqualTo(TestSopInstanceUid));
    }

    [Test]
    public void FileMetaInfoGenerator_TransferSyntaxUIDMatchesSpecified()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ImplicitVRLittleEndian);

        var uidElement = fmi[DicomTag.TransferSyntaxUID] as DicomStringElement;
        Assert.That(uidElement, Is.Not.Null);

        var uidValue = uidElement!.GetString()!.TrimEnd('\0');
        Assert.That(uidValue, Is.EqualTo(TransferSyntax.ImplicitVRLittleEndian.UID.ToString()));
    }

    [Test]
    public void FileMetaInfoGenerator_UsesSharpDicomImplementationClassUID()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var uidElement = fmi[DicomTag.ImplementationClassUID] as DicomStringElement;
        Assert.That(uidElement, Is.Not.Null);

        var uidValue = uidElement!.GetString()!.TrimEnd('\0');
        Assert.That(uidValue, Is.EqualTo(SharpDicomInfo.ImplementationClassUID.ToString()));
    }

    [Test]
    public void FileMetaInfoGenerator_UsesSharpDicomImplementationVersionName()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var versionElement = fmi[DicomTag.ImplementationVersionName] as DicomStringElement;
        Assert.That(versionElement, Is.Not.Null);

        var versionValue = versionElement!.GetString()!.TrimEnd('\0', ' ');
        Assert.That(versionValue, Is.EqualTo(SharpDicomInfo.ImplementationVersionName));
    }

    [Test]
    public void FileMetaInfoGenerator_CustomImplementationClassUIDIsUsed()
    {
        var dataset = CreateMinimalDataset();
        var customUid = new DicomUID("1.2.3.4.5.6.7.8.9.10");
        var options = new DicomWriterOptions { ImplementationClassUID = customUid };

        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian, options);

        var uidElement = fmi[DicomTag.ImplementationClassUID] as DicomStringElement;
        Assert.That(uidElement, Is.Not.Null);

        var uidValue = uidElement!.GetString()!.TrimEnd('\0');
        Assert.That(uidValue, Is.EqualTo(customUid.ToString()));
    }

    #endregion

    #region Group Length Tests

    [Test]
    public void FileMetaInfoGenerator_GroupLengthIsPresent()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var groupLengthElement = fmi[DicomTag.FileMetaInformationGroupLength];
        Assert.That(groupLengthElement, Is.Not.Null);
        Assert.That(groupLengthElement!.VR, Is.EqualTo(DicomVR.UL));
    }

    [Test]
    public void FileMetaInfoGenerator_GroupLengthValueIsCorrect()
    {
        var dataset = CreateMinimalDataset();
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        // Get the group length value
        var groupLengthElement = fmi[DicomTag.FileMetaInformationGroupLength] as DicomNumericElement;
        Assert.That(groupLengthElement, Is.Not.Null);
        var groupLengthValue = groupLengthElement!.GetUInt32();
        Assert.That(groupLengthValue, Is.Not.Null);

        // Calculate expected length by summing encoded lengths of all elements after (0002,0000)
        uint calculatedLength = 0;
        foreach (var element in fmi)
        {
            if (element.Tag == DicomTag.FileMetaInformationGroupLength)
                continue;

            calculatedLength += FileMetaInfoGenerator.GetEncodedLength(element);
        }

        Assert.That(groupLengthValue.GetValueOrDefault(), Is.EqualTo(calculatedLength));
    }

    [Test]
    public void FileMetaInfoGenerator_GroupLengthAccurateWithOptionalElements()
    {
        var dataset = CreateMinimalDataset();
        var options = new DicomWriterOptions
        {
            ImplementationVersionName = "CUSTOM_VERSION"
        };

        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian, options);

        var groupLengthElement = fmi[DicomTag.FileMetaInformationGroupLength] as DicomNumericElement;
        var groupLengthValue = groupLengthElement!.GetUInt32()!.Value;

        uint calculatedLength = 0;
        foreach (var element in fmi)
        {
            if (element.Tag == DicomTag.FileMetaInformationGroupLength)
                continue;

            calculatedLength += FileMetaInfoGenerator.GetEncodedLength(element);
        }

        Assert.That(groupLengthValue, Is.EqualTo(calculatedLength));
    }

    #endregion

    #region Transfer Syntax Tests

    [Test]
    public void Write_FmiIsAlwaysExplicitVRLittleEndian()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        // Request Implicit VR LE for the dataset
        var options = new DicomWriterOptions { TransferSyntax = TransferSyntax.ImplicitVRLittleEndian };

        using (var writer = new DicomFileWriter(stream, options, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        // Read FMI and verify it's Explicit VR (VR bytes present)
        stream.Position = 132; // Skip preamble + DICM

        // First element: (0002,0000) FileMetaInformationGroupLength
        var buffer = new byte[8];
        stream.Read(buffer, 0, 8);

        // In Explicit VR, bytes 4-5 are the VR ("UL")
        var vr = Encoding.ASCII.GetString(buffer, 4, 2);
        Assert.That(vr, Is.EqualTo("UL"));
    }

    [Test]
    public void Write_DatasetUsesSpecifiedTransferSyntax_ImplicitVR()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        var options = new DicomWriterOptions { TransferSyntax = TransferSyntax.ImplicitVRLittleEndian };

        using (var writer = new DicomFileWriter(stream, options, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        // Find start of dataset (after FMI)
        // Read the group length to know where FMI ends
        stream.Position = 132;

        // Skip to group length value (UL, 16-bit length format)
        // Tag(4) + VR(2) + Length(2) = 8 bytes header
        stream.Position = 132 + 8;
        var lengthBytes = new byte[4];
        stream.Read(lengthBytes, 0, 4);
        uint fmiLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

        // Dataset starts at: 132 (preamble+DICM) + 12 (GroupLength element) + fmiLength
        long datasetStart = 132 + 12 + fmiLength;
        stream.Position = datasetStart;

        // Read first dataset element - should be implicit VR (no VR bytes)
        // In implicit VR: Tag(4) + Length(4) = 8 bytes before value
        var tagBytes = new byte[4];
        stream.Read(tagBytes, 0, 4);

        ushort group = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(0, 2));
        ushort element = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(2, 2));

        // Should be SpecificCharacterSet or SOPClassUID (first dataset element)
        Assert.That(group, Is.EqualTo(0x0008));
    }

    #endregion

    #region Validation Tests

    [Test]
    public void FileMetaInfoGenerator_ThrowsWhenMissingSOPClassUID()
    {
        var dataset = new DicomDataset();
        // Only add SOPInstanceUID, not SOPClassUID
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, TestSopInstanceUid));

        Assert.Throws<DicomDataException>(() =>
            FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian));
    }

    [Test]
    public void FileMetaInfoGenerator_ThrowsWhenMissingSOPInstanceUID()
    {
        var dataset = new DicomDataset();
        // Only add SOPClassUID, not SOPInstanceUID
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, TestSopClassUid));

        Assert.Throws<DicomDataException>(() =>
            FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian));
    }

    [Test]
    public void FileMetaInfoGenerator_SucceedsWhenValidationDisabled()
    {
        var dataset = new DicomDataset();
        // No SOPClassUID or SOPInstanceUID
        var options = new DicomWriterOptions { ValidateFmiUids = false };

        // Should not throw
        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian, options);

        Assert.That(fmi.Count, Is.GreaterThan(0));
    }

    #endregion

    #region Roundtrip Preparation Tests

    [Test]
    public void Write_OutputStartsWithExpectedBytes()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        stream.Position = 0;
        var bytes = stream.ToArray();

        Assert.Multiple(() =>
        {
            // Should have preamble (128 zeros) + DICM
            Assert.That(bytes.Length, Is.GreaterThan(132));
            Assert.That(bytes.Take(128), Is.All.EqualTo(0));
            Assert.That(Encoding.ASCII.GetString(bytes, 128, 4), Is.EqualTo("DICM"));
        });
    }

    [Test]
    public void Write_FmiElementsInCorrectOrder()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            writer.Write(dataset);
        }

        // Parse FMI elements and verify order
        stream.Position = 132;

        var tags = new System.Collections.Generic.List<DicomTag>();

        // Read tags until we hit group > 0002
        while (stream.Position < stream.Length)
        {
            var tagBytes = new byte[4];
            if (stream.Read(tagBytes, 0, 4) < 4) break;

            ushort group = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(0, 2));
            ushort element = BinaryPrimitives.ReadUInt16LittleEndian(tagBytes.AsSpan(2, 2));

            if (group > 0x0002) break;

            tags.Add(new DicomTag(group, element));

            // Skip VR + length + value
            var vrBytes = new byte[2];
            stream.Read(vrBytes, 0, 2);
            var vr = Encoding.ASCII.GetString(vrBytes);

            int lengthSize;
            if (vr == "OB" || vr == "OD" || vr == "OF" || vr == "OL" || vr == "OW" ||
                vr == "SQ" || vr == "UC" || vr == "UN" || vr == "UR" || vr == "UT")
            {
                stream.Position += 2; // Skip reserved
                lengthSize = 4;
            }
            else
            {
                lengthSize = 2;
            }

            var lengthBytes = new byte[4];
            stream.Read(lengthBytes, 0, lengthSize);

            uint length = lengthSize == 2
                ? BinaryPrimitives.ReadUInt16LittleEndian(lengthBytes)
                : BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

            stream.Position += length;
        }

        // Verify sorted order
        for (int i = 1; i < tags.Count; i++)
        {
            Assert.That(tags[i], Is.GreaterThan(tags[i - 1]),
                $"FMI elements not sorted: {tags[i - 1]} before {tags[i]}");
        }
    }

    #endregion

    #region Async Tests

    [Test]
    public async Task WriteAsync_ProducesValidOutput()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();

        await using (var writer = new DicomFileWriter(stream, leaveOpen: true))
        {
            await writer.WriteAsync(dataset);
        }

        stream.Position = 128;
        var prefix = new byte[4];
        await stream.ReadAsync(prefix.AsMemory(0, 4));

        Assert.That(Encoding.ASCII.GetString(prefix), Is.EqualTo("DICM"));
    }

    [Test]
    public async Task DicomFile_SaveAsync_Works()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();
        var file = new DicomFile(dataset);

        await file.SaveAsync(stream);

        Assert.That(stream.Length, Is.GreaterThan(132));
    }

    #endregion

    #region DicomFile.Save Tests

    [Test]
    public void DicomFile_Save_ToStream_Works()
    {
        using var stream = new MemoryStream();
        var dataset = CreateMinimalDataset();
        var file = new DicomFile(dataset);

        file.Save(stream);

        stream.Position = 128;
        var prefix = new byte[4];
        stream.Read(prefix, 0, 4);

        Assert.That(Encoding.ASCII.GetString(prefix), Is.EqualTo("DICM"));
    }

    [Test]
    public void DicomFile_Save_ToFile_Works()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var dataset = CreateMinimalDataset();
            var file = new DicomFile(dataset);

            file.Save(tempPath);

            Assert.That(File.Exists(tempPath));
            var bytes = File.ReadAllBytes(tempPath);
            Assert.Multiple(() =>
            {
                Assert.That(bytes.Length, Is.GreaterThan(132));
                Assert.That(Encoding.ASCII.GetString(bytes, 128, 4), Is.EqualTo("DICM"));
            });
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    #endregion

    #region UID Padding Tests

    [Test]
    public void FileMetaInfoGenerator_UidPaddedWithNull()
    {
        // Odd-length UID should be padded with null byte
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.3")); // 5 chars - odd
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.4")); // 7 chars - odd

        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var classUidElement = fmi[DicomTag.MediaStorageSOPClassUID];
        Assert.That(classUidElement, Is.Not.Null);
        Assert.That(classUidElement!.RawValue.Length % 2, Is.EqualTo(0), "UID length should be even (padded)");
        Assert.That(classUidElement.RawValue.Span[classUidElement.RawValue.Length - 1], Is.EqualTo(0x00),
            "UID should be padded with null byte");
    }

    [Test]
    public void FileMetaInfoGenerator_EvenLengthUidNotPadded()
    {
        // Even-length UID should not have extra padding
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, "1.2.34")); // 6 chars - even
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, "1.2.3.45")); // 8 chars - even

        var fmi = FileMetaInfoGenerator.Generate(dataset, TransferSyntax.ExplicitVRLittleEndian);

        var classUidElement = fmi[DicomTag.MediaStorageSOPClassUID];
        Assert.That(classUidElement, Is.Not.Null);
        Assert.That(classUidElement!.RawValue.Length, Is.EqualTo(6));
    }

    #endregion

    #region Helper Methods

    private static DicomDataset CreateMinimalDataset()
    {
        var dataset = new DicomDataset();
        dataset.Add(CreateStringElement(DicomTag.SOPClassUID, DicomVR.UI, TestSopClassUid));
        dataset.Add(CreateStringElement(DicomTag.SOPInstanceUID, DicomVR.UI, TestSopInstanceUid));
        dataset.Add(CreateStringElement(DicomTag.PatientName, DicomVR.PN, TestPatientName));
        return dataset;
    }

    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        return new DicomStringElement(tag, vr, bytes);
    }

    #endregion
}
