using System;
using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using SharpDicom.Serialization.Bson;

namespace SharpDicom.Tests.Serialization;

[TestFixture]
public class BsonDocumentBufferTests
{
    [Test]
    public void WriteInt32_WritesLittleEndian()
    {
        using var buffer = new BsonDocumentBuffer(64);
        buffer.WriteInt32(0x04030201);

        var span = buffer.WrittenSpan;
        Assert.That(span.Length, Is.EqualTo(4));
        Assert.That(span[0], Is.EqualTo(0x01));
        Assert.That(span[1], Is.EqualTo(0x02));
        Assert.That(span[2], Is.EqualTo(0x03));
        Assert.That(span[3], Is.EqualTo(0x04));
    }

    [Test]
    public void WriteInt64_WritesLittleEndian()
    {
        using var buffer = new BsonDocumentBuffer(64);
        buffer.WriteInt64(0x0807060504030201L);

        var span = buffer.WrittenSpan;
        Assert.That(span.Length, Is.EqualTo(8));
        Assert.That(span[0], Is.EqualTo(0x01));
        Assert.That(span[1], Is.EqualTo(0x02));
        Assert.That(span[2], Is.EqualTo(0x03));
        Assert.That(span[3], Is.EqualTo(0x04));
        Assert.That(span[4], Is.EqualTo(0x05));
        Assert.That(span[5], Is.EqualTo(0x06));
        Assert.That(span[6], Is.EqualTo(0x07));
        Assert.That(span[7], Is.EqualTo(0x08));
    }

    [Test]
    public void WriteDouble_WritesCorrectBits()
    {
        using var buffer = new BsonDocumentBuffer(64);
        double value = 3.14159265358979;
        buffer.WriteDouble(value);

        var span = buffer.WrittenSpan;
        Assert.That(span.Length, Is.EqualTo(8));

        // Roundtrip via BitConverter
        long bits = BinaryPrimitives.ReadInt64LittleEndian(span);
        double restored = BitConverter.Int64BitsToDouble(bits);
        Assert.That(restored, Is.EqualTo(value));
    }

    [Test]
    public void WriteCString_NullTerminated()
    {
        using var buffer = new BsonDocumentBuffer(64);
        buffer.WriteCString("hello");

        var span = buffer.WrittenSpan;
        // "hello" = 5 bytes UTF-8 + 1 null terminator
        Assert.That(span.Length, Is.EqualTo(6));

        // UTF-8 bytes
        Assert.That(span[0], Is.EqualTo((byte)'h'));
        Assert.That(span[1], Is.EqualTo((byte)'e'));
        Assert.That(span[2], Is.EqualTo((byte)'l'));
        Assert.That(span[3], Is.EqualTo((byte)'l'));
        Assert.That(span[4], Is.EqualTo((byte)'o'));
        // null terminator
        Assert.That(span[5], Is.EqualTo(0x00));
    }

    [Test]
    public void WriteBsonString_LengthPrefixedNullTerminated()
    {
        using var buffer = new BsonDocumentBuffer(64);
        buffer.WriteBsonString("test");

        var span = buffer.WrittenSpan;
        // int32 length (4 bytes) + "test" (4 bytes) + null (1 byte) = 9 bytes
        Assert.That(span.Length, Is.EqualTo(9));

        // Length field: includes null terminator so 5
        int length = BinaryPrimitives.ReadInt32LittleEndian(span);
        Assert.That(length, Is.EqualTo(5));

        // String bytes
        Assert.That(span[4], Is.EqualTo((byte)'t'));
        Assert.That(span[5], Is.EqualTo((byte)'e'));
        Assert.That(span[6], Is.EqualTo((byte)'s'));
        Assert.That(span[7], Is.EqualTo((byte)'t'));
        // null terminator
        Assert.That(span[8], Is.EqualTo(0x00));
    }

    [Test]
    public void BeginEndDocument_PatchesSize()
    {
        using var buffer = new BsonDocumentBuffer(64);
        int offset = buffer.BeginDocument();

        // Write a simple field inside
        buffer.WriteByte(0x10); // Int32 type
        buffer.WriteCString("x");
        buffer.WriteInt32(42);

        buffer.EndDocument(offset);

        var span = buffer.WrittenSpan;
        // Read the patched size at the beginning
        int docSize = BinaryPrimitives.ReadInt32LittleEndian(span);

        // Size should equal total written bytes (4 size + 1 type + 2 key + 4 value + 1 terminator = 12)
        Assert.That(docSize, Is.EqualTo(span.Length));
        // Terminator should be the last byte
        Assert.That(span[span.Length - 1], Is.EqualTo(0x00));
    }

    [Test]
    public void NestedDocuments_CorrectSizes()
    {
        using var buffer = new BsonDocumentBuffer(256);

        int outerOffset = buffer.BeginDocument();

        // Outer field: a nested document
        buffer.WriteByte(0x03); // Document type
        buffer.WriteCString("inner");
        int innerOffset = buffer.BeginDocument();

        buffer.WriteByte(0x10); // Int32 type
        buffer.WriteCString("val");
        buffer.WriteInt32(99);

        buffer.EndDocument(innerOffset);
        buffer.EndDocument(outerOffset);

        var span = buffer.WrittenSpan;

        // Outer doc size
        int outerSize = BinaryPrimitives.ReadInt32LittleEndian(span);
        Assert.That(outerSize, Is.EqualTo(span.Length));

        // Inner doc starts after: 4 (outer size) + 1 (type) + 6 (cstring "inner\0")
        int innerStart = 4 + 1 + 6;
        int innerSize = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(innerStart));
        // Inner doc: 4 (size) + 1 (type) + 4 (cstring "val\0") + 4 (int32) + 1 (terminator) = 14
        Assert.That(innerSize, Is.EqualTo(14));
    }

    [Test]
    public void EnsureCapacity_GrowsBuffer()
    {
        using var buffer = new BsonDocumentBuffer(16);
        int initialCapacity = buffer.Capacity;

        // Write enough to trigger resize
        var data = new byte[initialCapacity + 1];
        buffer.WriteBytes(data);

        Assert.That(buffer.Position, Is.EqualTo(data.Length));
        Assert.That(buffer.Capacity, Is.GreaterThan(initialCapacity));
    }

    [Test]
    public void Reset_ClearsPosition()
    {
        using var buffer = new BsonDocumentBuffer(64);
        buffer.WriteInt32(42);
        Assert.That(buffer.Position, Is.GreaterThan(0));

        buffer.Reset();
        Assert.That(buffer.Position, Is.EqualTo(0));
    }

    [Test]
    public void ToArray_CopiesWrittenBytes()
    {
        using var buffer = new BsonDocumentBuffer(256);
        buffer.WriteInt32(0x01020304);
        buffer.WriteByte(0xFF);

        var array = buffer.ToArray();
        Assert.That(array.Length, Is.EqualTo(5));
        Assert.That(array[0], Is.EqualTo(0x04));
        Assert.That(array[1], Is.EqualTo(0x03));
        Assert.That(array[2], Is.EqualTo(0x02));
        Assert.That(array[3], Is.EqualTo(0x01));
        Assert.That(array[4], Is.EqualTo(0xFF));
    }
}
