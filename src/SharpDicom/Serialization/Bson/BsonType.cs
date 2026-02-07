namespace SharpDicom.Serialization.Bson;

/// <summary>
/// BSON type code constants per bsonspec.org v1.1.
/// </summary>
internal static class BsonType
{
    /// <summary>64-bit IEEE 754 floating point.</summary>
    internal const byte Double = 0x01;

    /// <summary>UTF-8 string (int32 length + bytes + null terminator).</summary>
    internal const byte String = 0x02;

    /// <summary>Embedded document (length-prefixed key-value pairs).</summary>
    internal const byte Document = 0x03;

    /// <summary>Array (document with "0", "1", ... as keys).</summary>
    internal const byte Array = 0x04;

    /// <summary>Binary data (int32 length + subtype byte + bytes).</summary>
    internal const byte Binary = 0x05;

    /// <summary>Boolean value (single byte: 0x00 or 0x01).</summary>
    internal const byte Boolean = 0x08;

    /// <summary>UTC datetime (int64 milliseconds since Unix epoch).</summary>
    internal const byte DateTime = 0x09;

    /// <summary>Null value.</summary>
    internal const byte Null = 0x0A;

    /// <summary>32-bit integer.</summary>
    internal const byte Int32 = 0x10;

    /// <summary>64-bit integer.</summary>
    internal const byte Int64 = 0x12;
}
