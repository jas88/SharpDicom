using System;
using System.Buffers.Binary;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Data.Exceptions;
using SharpDicom.IO;

namespace SharpDicom.Tests.IO;

[TestFixture]
public class FragmentParserTests
{
    [Test]
    public void ParseEncapsulated_SingleFragmentEmptyBOT_ParsesCorrectly()
    {
        // Create: BOT (empty) + one fragment + sequence delimiter
        var data = new byte[]
        {
            // BOT Item header (tag FFFE,E000 + length 0)
            0xFE, 0xFF, 0x00, 0xE0,  // Item tag (little endian: E000FFFE written as FFFE E000)
            0x00, 0x00, 0x00, 0x00,  // Length: 0

            // Fragment Item (tag FFFE,E000 + length 4)
            0xFE, 0xFF, 0x00, 0xE0,  // Item tag
            0x04, 0x00, 0x00, 0x00,  // Length: 4
            0xAA, 0xBB, 0xCC, 0xDD,  // Fragment data

            // Sequence Delimiter (tag FFFE,E0DD + length 0)
            0xFE, 0xFF, 0xDD, 0xE0,  // Sequence Delimitation tag
            0x00, 0x00, 0x00, 0x00   // Length: 0
        };

        var result = FragmentParser.ParseEncapsulated(
            data, DicomTag.PixelData, DicomVR.OB, littleEndian: true);

        Assert.That(result.Tag, Is.EqualTo(DicomTag.PixelData));
        Assert.That(result.VR, Is.EqualTo(DicomVR.OB));
        Assert.That(result.OffsetTable.IsEmpty, Is.True);
        Assert.That(result.FragmentCount, Is.EqualTo(1));
        Assert.That(result.Fragments[0].ToArray(), Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }));
    }

    [Test]
    public void ParseEncapsulated_MultipleFragmentsWithBOT_ParsesCorrectly()
    {
        // Create: BOT with 3 offsets + three fragments + sequence delimiter
        var data = new byte[]
        {
            // BOT Item header (tag FFFE,E000 + length 12 for 3 uint32 offsets)
            0xFE, 0xFF, 0x00, 0xE0,
            0x0C, 0x00, 0x00, 0x00,  // Length: 12 (3 * 4 bytes)
            // Offset table: 0, 12, 24 (relative to first fragment)
            0x00, 0x00, 0x00, 0x00,  // Offset 0
            0x0C, 0x00, 0x00, 0x00,  // Offset 12
            0x18, 0x00, 0x00, 0x00,  // Offset 24

            // Fragment 1 (tag + length 4 + data)
            0xFE, 0xFF, 0x00, 0xE0,
            0x04, 0x00, 0x00, 0x00,
            0x11, 0x11, 0x11, 0x11,

            // Fragment 2 (tag + length 4 + data)
            0xFE, 0xFF, 0x00, 0xE0,
            0x04, 0x00, 0x00, 0x00,
            0x22, 0x22, 0x22, 0x22,

            // Fragment 3 (tag + length 4 + data)
            0xFE, 0xFF, 0x00, 0xE0,
            0x04, 0x00, 0x00, 0x00,
            0x33, 0x33, 0x33, 0x33,

            // Sequence Delimiter
            0xFE, 0xFF, 0xDD, 0xE0,
            0x00, 0x00, 0x00, 0x00
        };

        var result = FragmentParser.ParseEncapsulated(
            data, DicomTag.PixelData, DicomVR.OB, littleEndian: true);

        Assert.That(result.FragmentCount, Is.EqualTo(3));
        Assert.That(result.OffsetTable.Length, Is.EqualTo(12));

        // Verify parsed offsets
        var offsets = result.ParsedBasicOffsets;
        Assert.That(offsets.Count, Is.EqualTo(3));
        Assert.That(offsets[0], Is.EqualTo(0u));
        Assert.That(offsets[1], Is.EqualTo(12u));
        Assert.That(offsets[2], Is.EqualTo(24u));

        // Verify fragments
        Assert.That(result.Fragments[0].ToArray(), Is.EqualTo(new byte[] { 0x11, 0x11, 0x11, 0x11 }));
        Assert.That(result.Fragments[1].ToArray(), Is.EqualTo(new byte[] { 0x22, 0x22, 0x22, 0x22 }));
        Assert.That(result.Fragments[2].ToArray(), Is.EqualTo(new byte[] { 0x33, 0x33, 0x33, 0x33 }));
    }

    [Test]
    public void ParseEncapsulated_EmptyBOT_IsValid()
    {
        // Empty BOT (length 0) is common and valid
        var data = new byte[]
        {
            // BOT Item (empty)
            0xFE, 0xFF, 0x00, 0xE0,
            0x00, 0x00, 0x00, 0x00,

            // Sequence Delimiter
            0xFE, 0xFF, 0xDD, 0xE0,
            0x00, 0x00, 0x00, 0x00
        };

        var result = FragmentParser.ParseEncapsulated(
            data, DicomTag.PixelData, DicomVR.OB, littleEndian: true);

        Assert.That(result.OffsetTable.IsEmpty, Is.True);
        Assert.That(result.ParsedBasicOffsets.Count, Is.EqualTo(0));
        Assert.That(result.FragmentCount, Is.EqualTo(0));
    }

    [Test]
    public void ParseBasicOffsetTable_FourOffsets_ParsesCorrectly()
    {
        // Create BOT bytes: 0, 1000, 2000, 3000
        var bot = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bot.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bot.AsSpan(4), 1000);
        BinaryPrimitives.WriteUInt32LittleEndian(bot.AsSpan(8), 2000);
        BinaryPrimitives.WriteUInt32LittleEndian(bot.AsSpan(12), 3000);

        var offsets = DicomFragmentSequence.ParseBasicOffsetTable(bot);

        Assert.That(offsets.Length, Is.EqualTo(4));
        Assert.That(offsets[0], Is.EqualTo(0u));
        Assert.That(offsets[1], Is.EqualTo(1000u));
        Assert.That(offsets[2], Is.EqualTo(2000u));
        Assert.That(offsets[3], Is.EqualTo(3000u));
    }

    [Test]
    public void ParseBasicOffsetTable_Empty_ReturnsEmptyArray()
    {
        var offsets = DicomFragmentSequence.ParseBasicOffsetTable(ReadOnlySpan<byte>.Empty);

        Assert.That(offsets, Is.Empty);
    }

    [Test]
    public void ParseExtendedOffsetTable_64BitOffsets_ParsesCorrectly()
    {
        // Create extended offset table with 64-bit values
        var eot = new byte[24];  // 3 * 8 bytes
        BinaryPrimitives.WriteUInt64LittleEndian(eot.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(eot.AsSpan(8), 5_000_000_000UL);  // > 4GB
        BinaryPrimitives.WriteUInt64LittleEndian(eot.AsSpan(16), 10_000_000_000UL);

        var offsets = DicomFragmentSequence.ParseExtendedOffsetTable(eot);

        Assert.That(offsets.Length, Is.EqualTo(3));
        Assert.That(offsets[0], Is.EqualTo(0UL));
        Assert.That(offsets[1], Is.EqualTo(5_000_000_000UL));
        Assert.That(offsets[2], Is.EqualTo(10_000_000_000UL));
    }

    [Test]
    public void ParseExtendedOffsetTable_Empty_ReturnsEmptyArray()
    {
        var offsets = DicomFragmentSequence.ParseExtendedOffsetTable(ReadOnlySpan<byte>.Empty);

        Assert.That(offsets, Is.Empty);
    }

    [Test]
    public void ParseEncapsulated_MissingSequenceDelimiter_StopsGracefully()
    {
        // Data without sequence delimiter - parser should handle gracefully
        var data = new byte[]
        {
            // BOT Item (empty)
            0xFE, 0xFF, 0x00, 0xE0,
            0x00, 0x00, 0x00, 0x00,

            // Fragment
            0xFE, 0xFF, 0x00, 0xE0,
            0x04, 0x00, 0x00, 0x00,
            0xAA, 0xBB, 0xCC, 0xDD
            // No sequence delimiter
        };

        // Should parse without throwing (lenient mode)
        var result = FragmentParser.ParseEncapsulated(
            data, DicomTag.PixelData, DicomVR.OB, littleEndian: true);

        Assert.That(result.FragmentCount, Is.EqualTo(1));
    }

    [Test]
    public void ParseEncapsulated_UnexpectedTag_ThrowsException()
    {
        // Data with unexpected tag instead of Item/Delimiter
        var data = new byte[]
        {
            // BOT Item (empty)
            0xFE, 0xFF, 0x00, 0xE0,
            0x00, 0x00, 0x00, 0x00,

            // Unexpected tag (0010,0010 PatientName instead of Item)
            0x10, 0x00, 0x10, 0x00,
            0x04, 0x00, 0x00, 0x00,
            0x41, 0x42, 0x43, 0x44
        };

        var ex = Assert.Throws<DicomDataException>(() =>
            FragmentParser.ParseEncapsulated(
                data, DicomTag.PixelData, DicomVR.OB, littleEndian: true));

        Assert.That(ex!.Message, Does.Contain("Expected Item tag"));
    }

    [Test]
    public void ParseEncapsulated_MissingBOT_ThrowsException()
    {
        // Data starting with wrong tag (not Item for BOT)
        var data = new byte[]
        {
            // Wrong tag instead of BOT Item
            0xFE, 0xFF, 0xDD, 0xE0,  // Sequence Delimiter instead of Item
            0x00, 0x00, 0x00, 0x00
        };

        var ex = Assert.Throws<DicomDataException>(() =>
            FragmentParser.ParseEncapsulated(
                data, DicomTag.PixelData, DicomVR.OB, littleEndian: true));

        Assert.That(ex!.Message, Does.Contain("Basic Offset Table"));
    }

    [Test]
    public void ParseEncapsulated_FragmentLengthExceedsData_ThrowsException()
    {
        var data = new byte[]
        {
            // BOT Item (empty)
            0xFE, 0xFF, 0x00, 0xE0,
            0x00, 0x00, 0x00, 0x00,

            // Fragment with length exceeding available data
            0xFE, 0xFF, 0x00, 0xE0,
            0xFF, 0xFF, 0x00, 0x00,  // Length: 65535 (way more than available)
            0xAA, 0xBB
        };

        var ex = Assert.Throws<DicomDataException>(() =>
            FragmentParser.ParseEncapsulated(
                data, DicomTag.PixelData, DicomVR.OB, littleEndian: true));

        Assert.That(ex!.Message, Does.Contain("exceeds available data"));
    }

    [Test]
    public void ParseEncapsulated_TooShortForBOT_ThrowsException()
    {
        var data = new byte[] { 0xFE, 0xFF, 0x00 };  // Only 3 bytes, need at least 8

        var ex = Assert.Throws<DicomDataException>(() =>
            FragmentParser.ParseEncapsulated(
                data, DicomTag.PixelData, DicomVR.OB, littleEndian: true));

        Assert.That(ex!.Message, Does.Contain("too short"));
    }

    [Test]
    public void ParseEncapsulated_BOTLengthExceedsData_ThrowsException()
    {
        var data = new byte[]
        {
            // BOT Item with length exceeding data
            0xFE, 0xFF, 0x00, 0xE0,
            0xFF, 0x00, 0x00, 0x00,  // Length: 255 (more than available)
            0x00, 0x00, 0x00, 0x00   // Only 4 bytes of BOT data
        };

        var ex = Assert.Throws<DicomDataException>(() =>
            FragmentParser.ParseEncapsulated(
                data, DicomTag.PixelData, DicomVR.OB, littleEndian: true));

        Assert.That(ex!.Message, Does.Contain("Basic Offset Table length"));
    }

    [Test]
    public void DicomFragmentSequence_TotalSize_SumsAllFragments()
    {
        var fragments = new[]
        {
            (ReadOnlyMemory<byte>)new byte[100],
            (ReadOnlyMemory<byte>)new byte[200],
            (ReadOnlyMemory<byte>)new byte[300]
        };

        var seq = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            ReadOnlyMemory<byte>.Empty,
            fragments);

        Assert.That(seq.TotalSize, Is.EqualTo(600));
    }

    [Test]
    public void DicomFragmentSequence_FragmentCount_ReturnsCorrectCount()
    {
        var fragments = new[]
        {
            (ReadOnlyMemory<byte>)new byte[10],
            (ReadOnlyMemory<byte>)new byte[20],
            (ReadOnlyMemory<byte>)new byte[30],
            (ReadOnlyMemory<byte>)new byte[40]
        };

        var seq = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            ReadOnlyMemory<byte>.Empty,
            fragments);

        Assert.That(seq.FragmentCount, Is.EqualTo(4));
    }

    [Test]
    public void DicomFragmentSequence_ToOwned_CopiesAllData()
    {
        var botData = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00 };
        var extOffsets = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var extLengths = new byte[] { 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var fragment1 = new byte[] { 0xAA, 0xBB };
        var fragment2 = new byte[] { 0xCC, 0xDD };

        var seq = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            botData,
            new[] { (ReadOnlyMemory<byte>)fragment1, (ReadOnlyMemory<byte>)fragment2 },
            extOffsets,
            extLengths);

        var owned = (DicomFragmentSequence)seq.ToOwned();

        // Verify all data is copied
        Assert.That(owned.Tag, Is.EqualTo(seq.Tag));
        Assert.That(owned.VR, Is.EqualTo(seq.VR));
        Assert.That(owned.OffsetTable.ToArray(), Is.EqualTo(botData));
        Assert.That(owned.ExtendedOffsetTable.ToArray(), Is.EqualTo(extOffsets));
        Assert.That(owned.ExtendedOffsetTableLengths.ToArray(), Is.EqualTo(extLengths));
        Assert.That(owned.FragmentCount, Is.EqualTo(2));
        Assert.That(owned.Fragments[0].ToArray(), Is.EqualTo(fragment1));
        Assert.That(owned.Fragments[1].ToArray(), Is.EqualTo(fragment2));
    }

    [Test]
    public void DicomFragmentSequence_ParsedExtendedOffsets_ReturnsNullWhenEmpty()
    {
        var seq = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            ReadOnlyMemory<byte>.Empty,
            Array.Empty<ReadOnlyMemory<byte>>());

        Assert.That(seq.ParsedExtendedOffsets, Is.Null);
        Assert.That(seq.ParsedExtendedLengths, Is.Null);
    }

    [Test]
    public void DicomFragmentSequence_ParsedExtendedOffsets_ParsesWhenPresent()
    {
        var extOffsets = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(extOffsets.AsSpan(0), 100);
        BinaryPrimitives.WriteUInt64LittleEndian(extOffsets.AsSpan(8), 200);

        var seq = new DicomFragmentSequence(
            DicomTag.PixelData,
            DicomVR.OB,
            ReadOnlyMemory<byte>.Empty,
            Array.Empty<ReadOnlyMemory<byte>>(),
            extOffsets,
            ReadOnlyMemory<byte>.Empty);

        var offsets = seq.ParsedExtendedOffsets;
        Assert.That(offsets, Is.Not.Null);
        Assert.That(offsets!.Count, Is.EqualTo(2));
        Assert.That(offsets[0], Is.EqualTo(100UL));
        Assert.That(offsets[1], Is.EqualTo(200UL));
    }
}
