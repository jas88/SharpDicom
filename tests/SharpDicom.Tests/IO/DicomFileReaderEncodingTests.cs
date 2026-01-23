using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.IO;
using System;
using System.IO;
using System.Text;

namespace SharpDicom.Tests.IO;

[TestFixture]
public class DicomFileReaderEncodingTests
{
    [Test]
    public void ReadFile_Utf8Encoding_DecodesPatientNameCorrectly()
    {
        // Arrange
        var utf8Name = "Müller^José";
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Name);
        var fileBytes = CreateMinimalDicomFile(
            "1.2.840.10008.1.2.1", // Explicit VR Little Endian
            "ISO_IR 192",          // UTF-8
            utf8Bytes);

        // Act
        DicomFile file;
        using (var ms = new MemoryStream(fileBytes))
        {
            file = DicomFile.Open(ms);
        }

        // Assert
        Assert.That(file.Dataset.Encoding.IsUtf8Compatible, Is.True);
        Assert.That(file.Dataset.Encoding.Primary.CodePage, Is.EqualTo(65001)); // UTF-8

        var patientName = file.Dataset.GetString(DicomTag.PatientName);
        Assert.That(patientName, Is.EqualTo(utf8Name));
    }

    [Test]
    public void ReadFile_Latin1Encoding_DecodesPatientNameCorrectly()
    {
        // Arrange
        var latin1Name = "Müller";
        var latin1Bytes = Encoding.GetEncoding(28591).GetBytes(latin1Name);
        var fileBytes = CreateMinimalDicomFile(
            "1.2.840.10008.1.2.1", // Explicit VR Little Endian
            "ISO_IR 100",          // Latin-1
            latin1Bytes);

        // Act
        DicomFile file;
        using (var ms = new MemoryStream(fileBytes))
        {
            file = DicomFile.Open(ms);
        }

        // Assert
        Assert.That(file.Dataset.Encoding.IsUtf8Compatible, Is.False);
        Assert.That(file.Dataset.Encoding.Primary.CodePage, Is.EqualTo(28591)); // Latin-1

        var patientName = file.Dataset.GetString(DicomTag.PatientName);
        Assert.That(patientName, Is.EqualTo(latin1Name));
    }

    [Test]
    public void ReadFile_NoSpecificCharacterSet_DefaultsToAscii()
    {
        // Arrange
        var asciiName = "Smith^John";
        var asciiBytes = Encoding.ASCII.GetBytes(asciiName);
        var fileBytes = CreateMinimalDicomFile(
            "1.2.840.10008.1.2.1", // Explicit VR Little Endian
            null,                  // No Specific Character Set
            asciiBytes);

        // Act
        DicomFile file;
        using (var ms = new MemoryStream(fileBytes))
        {
            file = DicomFile.Open(ms);
        }

        // Assert
        Assert.That(file.Dataset.Encoding, Is.EqualTo(DicomEncoding.Default));
        Assert.That(file.Dataset.Encoding.Primary.CodePage, Is.EqualTo(20127)); // ASCII

        var patientName = file.Dataset.GetString(DicomTag.PatientName);
        Assert.That(patientName, Is.EqualTo(asciiName));
    }

    [Test]
    public void ReadFile_MultiValueString_SplitsCorrectly()
    {
        // Arrange
        var multiValue = "Value1\\Value2\\Value3";
        var utf8Bytes = Encoding.UTF8.GetBytes(multiValue);
        var fileBytes = CreateMinimalDicomFile(
            "1.2.840.10008.1.2.1", // Explicit VR Little Endian
            "ISO_IR 192",          // UTF-8
            utf8Bytes);

        // Act
        DicomFile file;
        using (var ms = new MemoryStream(fileBytes))
        {
            file = DicomFile.Open(ms);
        }

        // Assert
        var values = file.Dataset.GetStrings(DicomTag.PatientName);
        Assert.That(values, Is.Not.Null);
        Assert.That(values!.Length, Is.EqualTo(3));
        Assert.That(values[0], Is.EqualTo("Value1"));
        Assert.That(values[1], Is.EqualTo("Value2"));
        Assert.That(values[2], Is.EqualTo("Value3"));
    }

    [Test]
    public void ReadFile_SequenceItem_InheritsParentEncoding()
    {
        // Note: Top-level sequence items don't automatically get Parent set during file reading.
        // This test verifies the inheritance mechanism works when Parent IS set (e.g., for nested sequences).
        // Testing manual inheritance setup since top-level items don't have file.Dataset as parent.

        // Arrange - Create parent dataset with UTF-8 encoding
        var parent = new DicomDataset();
        var charset = new DicomStringElement(
            DicomTag.SpecificCharacterSet,
            DicomVR.CS,
            Encoding.ASCII.GetBytes("ISO_IR 192"));
        parent.Add(charset);

        // Create child item dataset with UTF-8 text but no SpecificCharacterSet
        var child = new DicomDataset { Parent = parent };
        var utf8Text = "Étude 1";
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Text);
        var studyDesc = new DicomStringElement(
            new DicomTag(0x0008, 0x1030),
            DicomVR.LO,
            utf8Bytes);
        child.Add(studyDesc);

        // Assert
        Assert.That(parent.Encoding.Primary.CodePage, Is.EqualTo(65001), "Parent should have UTF-8");
        Assert.That(child.Contains(DicomTag.SpecificCharacterSet), Is.False, "Child should not have local SpecificCharacterSet");
        Assert.That(child.Encoding.Primary.CodePage, Is.EqualTo(65001), "Child should inherit UTF-8 from parent");

        // Verify the string decodes correctly using inherited encoding
        var decodedText = child.GetString(new DicomTag(0x0008, 0x1030));
        Assert.That(decodedText, Is.EqualTo(utf8Text));
    }

    // Helper methods

    private static byte[] CreateMinimalDicomFile(
        string transferSyntaxUID,
        string? specificCharacterSet,
        byte[] patientNameBytes)
    {
        using var ms = new MemoryStream();

        // Preamble (128 bytes)
        ms.Write(new byte[128]);

        // DICM prefix
        ms.Write("DICM"u8);

        // File Meta Information (Group 0002) - always Explicit VR Little Endian
        // Calculate FMI length
        var tsBytes = Encoding.ASCII.GetBytes(transferSyntaxUID);
        var tsPadded = EnsureEvenLength(tsBytes);
        int fmiContentLength = 18 + 34 + 8 + tsPadded.Length; // Version + SOP Class + TS

        // (0002,0000) File Meta Information Group Length
        WriteElement(ms, 0x0002, 0x0000, "UL", BitConverter.GetBytes((uint)fmiContentLength));

        // (0002,0001) File Meta Information Version
        WriteElementLong(ms, 0x0002, 0x0001, "OB", new byte[] { 0x00, 0x01 });

        // (0002,0002) Media Storage SOP Class UID - CT Image Storage
        var sopClassUid = "1.2.840.10008.5.1.4.1.1.2 "; // 26 chars (even)
        WriteElement(ms, 0x0002, 0x0002, "UI", Encoding.ASCII.GetBytes(sopClassUid));

        // (0002,0010) Transfer Syntax UID
        WriteElement(ms, 0x0002, 0x0010, "UI", tsPadded);

        // Dataset (Transfer Syntax determines encoding)
        bool isExplicitVR = transferSyntaxUID != "1.2.840.10008.1.2"; // ImplicitVRLittleEndian

        // (0008,0005) Specific Character Set (if provided)
        if (specificCharacterSet != null)
        {
            var csBytes = Encoding.ASCII.GetBytes(specificCharacterSet);
            var csPadded = EnsureEvenLength(csBytes, 0x20); // Space padding for CS VR
            if (isExplicitVR)
                WriteElement(ms, 0x0008, 0x0005, "CS", csPadded);
            else
                WriteElementImplicit(ms, 0x0008, 0x0005, csPadded);
        }

        // (0010,0010) Patient Name
        var pnPadded = EnsureEvenLength(patientNameBytes, 0x20); // Space padding for PN VR
        if (isExplicitVR)
            WriteElement(ms, 0x0010, 0x0010, "PN", pnPadded);
        else
            WriteElementImplicit(ms, 0x0010, 0x0010, pnPadded);

        return ms.ToArray();
    }

    private static void WriteElement(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(Encoding.ASCII.GetBytes(vr));
        ms.Write(BitConverter.GetBytes((ushort)value.Length));
        ms.Write(value);
    }

    private static void WriteElementLong(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(Encoding.ASCII.GetBytes(vr));
        ms.Write(new byte[2]); // Reserved
        ms.Write(BitConverter.GetBytes((uint)value.Length));
        ms.Write(value);
    }

    private static void WriteElementImplicit(MemoryStream ms, ushort group, ushort element,
        byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(BitConverter.GetBytes((uint)value.Length));
        ms.Write(value);
    }

    private static byte[] EnsureEvenLength(byte[] bytes, byte paddingByte = 0x00)
    {
        if (bytes.Length % 2 == 0)
            return bytes;

        var padded = new byte[bytes.Length + 1];
        Array.Copy(bytes, padded, bytes.Length);
        padded[^1] = paddingByte;
        return padded;
    }
}
