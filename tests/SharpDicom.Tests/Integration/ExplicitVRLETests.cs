using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;

namespace SharpDicom.Tests.Integration;

/// <summary>
/// Integration tests for parsing Explicit VR Little Endian DICOM files.
/// </summary>
[TestFixture]
public class ExplicitVRLETests
{
    #region Complete File Parsing Tests

    [Test]
    public async Task Parse_CompleteFile_AllElementsPresent()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // Verify all element types parsed correctly
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0005)), Is.True, "SpecificCharacterSet");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0016)), Is.True, "SOPClassUID");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0018)), Is.True, "SOPInstanceUID");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0020)), Is.True, "StudyDate");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0030)), Is.True, "StudyTime");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0008, 0x0060)), Is.True, "Modality");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0010, 0x0010)), Is.True, "PatientName");
        Assert.That(file.Dataset.Contains(new DicomTag(0x0010, 0x0020)), Is.True, "PatientID");
    }

    [Test]
    public async Task Parse_StringValues_DecodedCorrectly()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.GetString(new DicomTag(0x0010, 0x0010)), Is.EqualTo("Doe^John"));
        Assert.That(file.GetString(new DicomTag(0x0010, 0x0020)), Is.EqualTo("PATIENT001"));
        Assert.That(file.GetString(new DicomTag(0x0008, 0x0060)), Is.EqualTo("CT"));
    }

    #endregion

    #region Date/Time Parsing Tests

    [Test]
    public async Task Parse_DateValue_ParsedCorrectly()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var studyDateElement = file.Dataset[new DicomTag(0x0008, 0x0020)] as DicomStringElement;
        Assert.That(studyDateElement, Is.Not.Null);

        var date = studyDateElement!.GetDate();
        Assert.That(date, Is.EqualTo(new DateOnly(2024, 1, 15)));
    }

    [Test]
    public async Task Parse_TimeValue_ParsedCorrectly()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var studyTimeElement = file.Dataset[new DicomTag(0x0008, 0x0030)] as DicomStringElement;
        Assert.That(studyTimeElement, Is.Not.Null);

        var time = studyTimeElement!.GetTime();
        Assert.That(time, Is.EqualTo(new TimeOnly(14, 30, 00)));
    }

    #endregion

    #region Numeric Value Parsing Tests

    [Test]
    public async Task Parse_USValue_ParsedCorrectly()
    {
        var data = CreateFileWithNumericElements();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // Rows (US)
        var rowsElement = file.Dataset[new DicomTag(0x0028, 0x0010)] as DicomNumericElement;
        Assert.That(rowsElement, Is.Not.Null);
        Assert.That(rowsElement!.GetUInt16(), Is.EqualTo(512));

        // Columns (US)
        var colsElement = file.Dataset[new DicomTag(0x0028, 0x0011)] as DicomNumericElement;
        Assert.That(colsElement, Is.Not.Null);
        Assert.That(colsElement!.GetUInt16(), Is.EqualTo(512));
    }

    [Test]
    public async Task Parse_BitsAllocated_ParsedCorrectly()
    {
        var data = CreateFileWithNumericElements();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // BitsAllocated (US)
        var bitsElement = file.Dataset[new DicomTag(0x0028, 0x0100)] as DicomNumericElement;
        Assert.That(bitsElement, Is.Not.Null);
        Assert.That(bitsElement!.GetUInt16(), Is.EqualTo(16));
    }

    #endregion

    #region Long VR Element Tests

    [Test]
    public async Task Parse_LongVRElement_ParsedCorrectly()
    {
        var data = CreateFileWithLongVRElement();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // UT (Unlimited Text) is a long VR
        var element = file.Dataset[new DicomTag(0x0008, 0x0115)]; // CodingSchemeURL
        Assert.That(element, Is.Not.Null);

        var stringElement = element as DicomStringElement;
        Assert.That(stringElement, Is.Not.Null);
        Assert.That(stringElement!.GetString(), Is.EqualTo("http://example.com/codingscheme"));
    }

    [Test]
    public async Task Parse_OBElement_ParsedAsLongVR()
    {
        var data = CreateFileWithBinaryElement();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // OB element - PixelPaddingValue (binary data)
        var element = file.Dataset[new DicomTag(0x0028, 0x0120)];
        Assert.That(element, Is.Not.Null);
        Assert.That(element!.VR, Is.EqualTo(DicomVR.OB));
    }

    #endregion

    #region Transfer Syntax Tests

    [Test]
    public async Task Parse_TransferSyntax_ExtractedCorrectly()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        Assert.That(file.TransferSyntax.IsExplicitVR, Is.True);
        Assert.That(file.TransferSyntax.IsLittleEndian, Is.True);
        Assert.That(file.TransferSyntax.IsEncapsulated, Is.False);
    }

    [Test]
    public async Task Parse_TransferSyntaxUID_MatchesExplicitVRLE()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // Verify FMI contains Transfer Syntax UID
        var tsElement = file.FileMetaInfo[DicomTag.TransferSyntaxUID];
        Assert.That(tsElement, Is.Not.Null);

        // Extract UID value
        var tsString = (tsElement as DicomStringElement)?.GetString();
        Assert.That(tsString, Is.EqualTo("1.2.840.10008.1.2.1"));
    }

    #endregion

    #region Element Enumeration Tests

    [Test]
    public async Task Parse_ElementEnumeration_SortedByTag()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);
        var tags = file.Dataset.Select(e => e.Tag).ToList();

        // Should be sorted
        var sortedTags = tags.OrderBy(t => t).ToList();
        Assert.That(tags, Is.EqualTo(sortedTags));
    }

    [Test]
    public async Task Parse_ElementEnumeration_ContainsAllElements()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // File has 8 elements in dataset
        Assert.That(file.Dataset.Count, Is.EqualTo(8));
    }

    #endregion

    #region UID Value Tests

    [Test]
    public async Task Parse_UIDValues_PreservedCorrectly()
    {
        var data = CreateComprehensiveTestFile();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        var sopClassUID = file.Dataset.GetUID(new DicomTag(0x0008, 0x0016));
        Assert.That(sopClassUID, Is.Not.Null);
        Assert.That(sopClassUID!.Value.ToString(), Is.EqualTo("1.2.840.10008.5.1.4.1.1.2")); // CT Image Storage

        var sopInstanceUID = file.Dataset.GetUID(new DicomTag(0x0008, 0x0018));
        Assert.That(sopInstanceUID, Is.Not.Null);
        Assert.That(sopInstanceUID!.Value.ToString(), Is.EqualTo("1.2.3.4.5.6.7.8.9"));
    }

    #endregion

    #region Multi-Valued String Tests

    [Test]
    public async Task Parse_MultiValuedString_SplitsCorrectly()
    {
        var data = CreateFileWithMultiValuedElement();
        using var stream = new MemoryStream(data);

        var file = await DicomFile.OpenAsync(stream);

        // ImageType with multiple values
        var imageTypeElement = file.Dataset[new DicomTag(0x0008, 0x0008)] as DicomStringElement;
        Assert.That(imageTypeElement, Is.Not.Null);

        var values = imageTypeElement!.GetStrings();
        Assert.That(values, Is.Not.Null);
        Assert.That(values, Has.Length.EqualTo(3));
        Assert.That(values![0], Is.EqualTo("ORIGINAL"));
        Assert.That(values[1], Is.EqualTo("PRIMARY"));
        Assert.That(values[2], Is.EqualTo("AXIAL"));
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateComprehensiveTestFile()
    {
        using var ms = new MemoryStream();

        // Preamble + DICM
        ms.Write(new byte[128]);
        ms.Write("DICM"u8);

        // FMI
        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // Dataset - various VRs
        WriteElement(ms, 0x0008, 0x0005, "CS", PadToEven("ISO_IR 100"u8.ToArray())); // SpecificCharacterSet
        WriteElement(ms, 0x0008, 0x0016, "UI", "1.2.840.10008.5.1.4.1.1.2\0"u8.ToArray()); // SOPClassUID
        WriteElement(ms, 0x0008, 0x0018, "UI", "1.2.3.4.5.6.7.8.9\0"u8.ToArray()); // SOPInstanceUID
        WriteElement(ms, 0x0008, 0x0020, "DA", "20240115"u8.ToArray()); // StudyDate
        WriteElement(ms, 0x0008, 0x0030, "TM", "143000"u8.ToArray()); // StudyTime
        WriteElement(ms, 0x0008, 0x0060, "CS", PadToEven("CT"u8.ToArray())); // Modality
        WriteElement(ms, 0x0010, 0x0010, "PN", PadToEven("Doe^John"u8.ToArray())); // PatientName
        WriteElement(ms, 0x0010, 0x0020, "LO", PadToEven("PATIENT001"u8.ToArray())); // PatientID

        return ms.ToArray();
    }

    private static byte[] CreateFileWithNumericElements()
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[128]);
        ms.Write("DICM"u8);

        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // Rows (US) = 512
        WriteElement(ms, 0x0028, 0x0010, "US", BitConverter.GetBytes((ushort)512));
        // Columns (US) = 512
        WriteElement(ms, 0x0028, 0x0011, "US", BitConverter.GetBytes((ushort)512));
        // BitsAllocated (US) = 16
        WriteElement(ms, 0x0028, 0x0100, "US", BitConverter.GetBytes((ushort)16));

        return ms.ToArray();
    }

    private static byte[] CreateFileWithLongVRElement()
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[128]);
        ms.Write("DICM"u8);

        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // CodingSchemeURL (UT - long VR)
        WriteElementLong(ms, 0x0008, 0x0115, "UT", PadToEven("http://example.com/codingscheme"u8.ToArray()));

        return ms.ToArray();
    }

    private static byte[] CreateFileWithBinaryElement()
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[128]);
        ms.Write("DICM"u8);

        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // PixelPaddingValue (OB) - binary data
        WriteElementLong(ms, 0x0028, 0x0120, "OB", new byte[] { 0x00, 0x00 });

        return ms.ToArray();
    }

    private static byte[] CreateFileWithMultiValuedElement()
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[128]);
        ms.Write("DICM"u8);

        WriteElement(ms, 0x0002, 0x0010, "UI", "1.2.840.10008.1.2.1\0"u8.ToArray());

        // ImageType (CS) with multiple values separated by backslash
        WriteElement(ms, 0x0008, 0x0008, "CS", PadToEven(@"ORIGINAL\PRIMARY\AXIAL"u8.ToArray()));

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
            ms.Write(new byte[2]); // Reserved
            ms.Write(BitConverter.GetBytes((uint)value.Length));
        }
        else
        {
            ms.Write(BitConverter.GetBytes((ushort)value.Length));
        }

        ms.Write(value);
    }

    private static void WriteElementLong(MemoryStream ms, ushort group, ushort element,
        string vr, byte[] value)
    {
        ms.Write(BitConverter.GetBytes(group));
        ms.Write(BitConverter.GetBytes(element));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(vr));
        ms.Write(new byte[2]); // Reserved
        ms.Write(BitConverter.GetBytes((uint)value.Length));
        ms.Write(value);
    }

    private static byte[] PadToEven(byte[] value)
    {
        if (value.Length % 2 == 0)
            return value;

        var padded = new byte[value.Length + 1];
        Array.Copy(value, padded, value.Length);
        padded[padded.Length - 1] = (byte)' ';
        return padded;
    }

    #endregion
}
