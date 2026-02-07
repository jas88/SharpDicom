using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Deserializes raw BSON bytes back to a <see cref="DicomDataset"/> with zero external dependencies.
/// </summary>
/// <remarks>
/// Reconstructs DICOM elements from the BSON format produced by <see cref="BsonDicomWriter"/>.
/// Supports dual-storage VRs (IS/DS/DA/TM/DT) via the "Raw" field, Person Name component groups,
/// private tag re-registration, and recursive sequence deserialization.
/// </remarks>
public static class BsonDicomReader
{
    /// <summary>
    /// Deserializes a BSON document byte array to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="bson">The BSON document bytes.</param>
    /// <param name="options">Deserialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bson"/> is null.</exception>
    public static DicomDataset Deserialize(byte[] bson, BsonSerializationOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(bson);
#else
        if (bson == null)
            throw new ArgumentNullException(nameof(bson));
#endif

        return DeserializeCore(bson, options ?? BsonSerializationOptions.Default);
    }

    /// <summary>
    /// Deserializes a BSON document from a <see cref="ReadOnlyMemory{T}"/> to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="bson">The BSON document memory.</param>
    /// <param name="options">Deserialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset Deserialize(ReadOnlyMemory<byte> bson, BsonSerializationOptions? options = null)
    {
        return DeserializeCore(bson.ToArray(), options ?? BsonSerializationOptions.Default);
    }

    #region Core Document Parsing

    private static DicomDataset DeserializeCore(byte[] data, BsonSerializationOptions options)
    {
        var dataset = new DicomDataset();
        int offset = 0;

        if (data.Length < 5) // minimum BSON doc: 4 bytes size + 1 byte terminator
            return dataset;

        int docSize = ReadInt32(data, ref offset);
        if (docSize > data.Length)
            return dataset;

        int docEnd = docSize; // offset started at 0

        // Collect private elements to process after main elements
        Dictionary<string, List<(DicomTag Tag, IDicomElement Element)>>? privateElements = null;

        while (offset < docEnd - 1) // -1 for terminator
        {
            byte typeByte = data[offset++];
            if (typeByte == 0x00)
                break; // document terminator

            string key = ReadCString(data, ref offset);

            if (key == "_private" && typeByte == BsonType.Document)
            {
                privateElements = ReadPrivateSection(data, ref offset, options);
                continue;
            }

            // Skip flattened fields (concatenated keys from FlattenProfile)
            // These are informational and do not correspond to actual DICOM tags
            if (typeByte == BsonType.String && IsLikelyFlattenedKey(key))
            {
                SkipValue(data, ref offset, typeByte);
                continue;
            }

            if (typeByte != BsonType.Document)
            {
                // Top-level entries should be BSON documents (element sub-docs)
                SkipValue(data, ref offset, typeByte);
                continue;
            }

            if (!TryParseTagKey(key, out var tag))
            {
                SkipValue(data, ref offset, BsonType.Document);
                continue;
            }

            var element = ReadElementSubDocument(data, ref offset, tag, options);
            if (element != null)
            {
                dataset.Add(element);
            }
        }

        // Process private elements
        if (privateElements != null)
        {
            foreach (var kvp in privateElements)
            {
                string creatorName = kvp.Key;

                foreach (var (privateTag, element) in kvp.Value)
                {
                    if (creatorName != "_unknown")
                    {
                        // Register the private creator if not already registered
                        byte slot = privateTag.PrivateCreatorSlot;
                        if (slot > 0)
                        {
                            var creatorTag = new DicomTag(privateTag.Group, slot);
                            if (!dataset.PrivateCreators.HasCreator(privateTag))
                            {
                                // Register via the private creator tag
                                var creatorElement = new DicomStringElement(
                                    creatorTag, DicomVR.LO,
                                    Encoding.UTF8.GetBytes(creatorName));
                                dataset.Add(creatorElement);
                            }
                        }
                    }

                    dataset.Add(element);
                }
            }
        }

        return dataset;
    }

    /// <summary>
    /// Checks if a key looks like a flattened field (concatenation of two tag keys).
    /// Flattened keys are longer than standard tag keys (8-char hex = 8, but flattened = 16).
    /// </summary>
    private static bool IsLikelyFlattenedKey(string key)
    {
        // Hex8 flattened: 16 hex chars (e.g. "0008111000080018")
        if (key.Length == 16)
        {
            for (int i = 0; i < 16; i++)
            {
                char c = key[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    return false;
            }
            return true;
        }

        // Dotted flattened: "0008.11100008.0018" (19 chars with two dots)
#if NETSTANDARD2_0
        if (key.Length > 9 && key.IndexOf('.') >= 0)
#else
        if (key.Length > 9 && key.Contains('.'))
#endif
        {
            int dotCount = 0;
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] == '.')
                    dotCount++;
            }
            return dotCount >= 2;
        }

        return false;
    }

    #endregion

    #region Element Sub-Document Parsing

    private static IDicomElement? ReadElementSubDocument(
        byte[] data, ref int offset, DicomTag tag, BsonSerializationOptions options)
    {
        int docSize = ReadInt32(data, ref offset);
        int docEnd = offset + docSize - 4; // -4 because size includes itself

        DicomVR? vr = null;
        string? rawString = null;
        List<object>? valueArray = null;
        byte[]? binaryValue = null;
        bool hasExternalRef = false;

        while (offset < docEnd - 1)
        {
            byte typeByte = data[offset++];
            if (typeByte == 0x00)
                break;

            string fieldKey = ReadCString(data, ref offset);

            if (fieldKey == "vr" && typeByte == BsonType.String)
            {
                string vrStr = ReadBsonString(data, ref offset);
                if (vrStr.Length == 2)
                    vr = new DicomVR(vrStr);
                continue;
            }

            if (fieldKey == "Raw" && typeByte == BsonType.String)
            {
                rawString = ReadBsonString(data, ref offset);
                continue;
            }

            if (fieldKey == "Value")
            {
                if (typeByte == BsonType.Array)
                {
                    valueArray = ReadBsonArray(data, ref offset);
                }
                else if (typeByte == BsonType.Binary)
                {
                    binaryValue = ReadBsonBinary(data, ref offset);
                }
                else
                {
                    SkipValue(data, ref offset, typeByte);
                }
                continue;
            }

            if (fieldKey == "$ref" && typeByte == BsonType.String)
            {
                hasExternalRef = true;
                SkipValue(data, ref offset, typeByte);
                continue;
            }

            // Skip Alphabetic, Ideographic, Phonetic sub-documents for PN
            // These are informational only; round-trip comes from Value array
            SkipValue(data, ref offset, typeByte);
        }

        // Advance past document terminator if needed
        if (offset < docEnd)
            offset = docEnd;

        // Resolve VR from dictionary if not explicitly stated
        if (!vr.HasValue)
        {
            var entry = DicomDictionary.Default.GetEntry(tag);
            vr = entry.HasValue ? entry.Value.DefaultVR : DicomVR.UN;
        }

        return ReconstructElement(tag, vr.Value, rawString, valueArray, binaryValue, hasExternalRef, options);
    }

    #endregion

    #region Element Reconstruction

    private static IDicomElement? ReconstructElement(
        DicomTag tag,
        DicomVR vr,
        string? rawString,
        List<object>? valueArray,
        byte[]? binaryValue,
        bool hasExternalRef,
        BsonSerializationOptions options)
    {
        // SQ (Sequence)
        if (vr == DicomVR.SQ)
        {
            return ReconstructSequence(tag, valueArray, options);
        }

        // Binary VRs (OB, OW, OD, OF, OL, OV, UN)
        if (IsBinaryVR(vr))
        {
            if (hasExternalRef)
            {
                return new DicomBinaryElement(tag, vr, ReadOnlyMemory<byte>.Empty);
            }

            if (binaryValue != null)
            {
                return new DicomBinaryElement(tag, vr, binaryValue);
            }

            return new DicomBinaryElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        // Dual-storage string VRs (IS, DS, DA, TM, DT): prefer Raw field
        if (rawString != null && IsDualStorageVR(vr))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(rawString);
            return new DicomStringElement(tag, vr, bytes);
        }

        // Numeric VRs
        if (vr.IsNumericVR)
        {
            return ReconstructNumericElement(tag, vr, valueArray);
        }

        // String VRs (including PN)
        if (vr.IsStringVR)
        {
            return ReconstructStringElement(tag, vr, valueArray);
        }

        // Fallback: treat as string if there are values, binary otherwise
        if (valueArray != null && valueArray.Count > 0)
        {
            return ReconstructStringElement(tag, vr, valueArray);
        }

        if (binaryValue != null)
        {
            return new DicomBinaryElement(tag, vr, binaryValue);
        }

        // Empty element
        return new DicomStringElement(tag, vr, ReadOnlyMemory<byte>.Empty);
    }

    private static DicomSequence ReconstructSequence(
        DicomTag tag,
        List<object>? valueArray,
        BsonSerializationOptions options)
    {
        var items = new List<DicomDataset>();

        if (valueArray != null)
        {
            foreach (var item in valueArray)
            {
                if (item is byte[] itemBytes)
                {
                    var itemDataset = DeserializeCore(itemBytes, options);
                    items.Add(itemDataset);
                }
            }
        }

        return new DicomSequence(tag, items);
    }

    private static DicomNumericElement ReconstructNumericElement(
        DicomTag tag,
        DicomVR vr,
        List<object>? valueArray)
    {
        if (valueArray == null || valueArray.Count == 0)
        {
            return new DicomNumericElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        // AT: string hex values
        if (vr == DicomVR.AT)
        {
            return ReconstructATElement(tag, valueArray);
        }

        int valueSize = GetNumericVRByteSize(vr);
        byte[] rawBytes = new byte[valueArray.Count * valueSize];

        for (int i = 0; i < valueArray.Count; i++)
        {
            int byteOffset = i * valueSize;
            var val = valueArray[i];

            if (val == null)
                continue; // leave zeros

            WriteNumericValue(rawBytes, byteOffset, vr, val);
        }

        return new DicomNumericElement(tag, vr, rawBytes);
    }

    private static DicomNumericElement ReconstructATElement(DicomTag tag, List<object> valueArray)
    {
        byte[] rawBytes = new byte[valueArray.Count * 4];

        for (int i = 0; i < valueArray.Count; i++)
        {
            if (valueArray[i] is string hexStr && hexStr.Length == 8)
            {
                if (uint.TryParse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint tagVal))
                {
                    ushort group = (ushort)(tagVal >> 16);
                    ushort element = (ushort)(tagVal & 0xFFFF);
                    BinaryPrimitives.WriteUInt16LittleEndian(rawBytes.AsSpan(i * 4), group);
                    BinaryPrimitives.WriteUInt16LittleEndian(rawBytes.AsSpan(i * 4 + 2), element);
                }
            }
        }

        return new DicomNumericElement(tag, DicomVR.AT, rawBytes);
    }

    private static DicomStringElement ReconstructStringElement(
        DicomTag tag,
        DicomVR vr,
        List<object>? valueArray)
    {
        if (valueArray == null || valueArray.Count == 0)
        {
            return new DicomStringElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        // Join multiple string values with backslash
        var sb = new StringBuilder();
        for (int i = 0; i < valueArray.Count; i++)
        {
            if (i > 0)
                sb.Append('\\');

            if (valueArray[i] is string s)
                sb.Append(s);
            else if (valueArray[i] != null)
                sb.Append(Convert.ToString(valueArray[i], CultureInfo.InvariantCulture));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new DicomStringElement(tag, vr, bytes);
    }

    #endregion

    #region Private Tag Handling

    private static Dictionary<string, List<(DicomTag Tag, IDicomElement Element)>> ReadPrivateSection(
        byte[] data, ref int offset, BsonSerializationOptions options)
    {
        var result = new Dictionary<string, List<(DicomTag, IDicomElement)>>();

        int docSize = ReadInt32(data, ref offset);
        int docEnd = offset + docSize - 4;

        while (offset < docEnd - 1)
        {
            byte typeByte = data[offset++];
            if (typeByte == 0x00)
                break;

            string creatorName = ReadCString(data, ref offset);

            if (typeByte != BsonType.Document)
            {
                SkipValue(data, ref offset, typeByte);
                continue;
            }

            var elements = ReadCreatorSubDocument(data, ref offset, options);
            if (elements.Count > 0)
            {
                result[creatorName] = elements;
            }
        }

        if (offset < docEnd)
            offset = docEnd;

        return result;
    }

    private static List<(DicomTag Tag, IDicomElement Element)> ReadCreatorSubDocument(
        byte[] data, ref int offset, BsonSerializationOptions options)
    {
        var result = new List<(DicomTag, IDicomElement)>();

        int docSize = ReadInt32(data, ref offset);
        int docEnd = offset + docSize - 4;

        while (offset < docEnd - 1)
        {
            byte typeByte = data[offset++];
            if (typeByte == 0x00)
                break;

            string key = ReadCString(data, ref offset);

            if (typeByte != BsonType.Document)
            {
                SkipValue(data, ref offset, typeByte);
                continue;
            }

            if (!TryParseTagKey(key, out var tag))
            {
                SkipValue(data, ref offset, BsonType.Document);
                continue;
            }

            var element = ReadElementSubDocument(data, ref offset, tag, options);
            if (element != null)
            {
                result.Add((tag, element));
            }
        }

        if (offset < docEnd)
            offset = docEnd;

        return result;
    }

    #endregion

    #region Tag Key Parsing

    private static bool TryParseTagKey(string key, out DicomTag tag)
    {
        tag = default;

        if (string.IsNullOrEmpty(key))
            return false;

        // 8-char hex format: "00100010"
        if (key.Length == 8 && IsHexString(key))
        {
            if (uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint val))
            {
                tag = new DicomTag(val);
                return true;
            }
        }

        // Dotted format: "0010.0010"
        if (key.Length == 9 && key[4] == '.')
        {
            string groupStr = key.Substring(0, 4);
            string elemStr = key.Substring(5, 4);

            if (ushort.TryParse(groupStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort group) &&
                ushort.TryParse(elemStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort element))
            {
                tag = new DicomTag(group, element);
                return true;
            }
        }

        // Keyword format: "PatientName"
        var entry = DicomDictionary.Default.GetEntryByKeyword(key);
        if (entry.HasValue)
        {
            tag = entry.Value.Tag;
            return true;
        }

        return false;
    }

    private static bool IsHexString(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    #endregion

    #region VR Classification Helpers

    private static bool IsBinaryVR(DicomVR vr)
    {
        return vr == DicomVR.OB || vr == DicomVR.OW || vr == DicomVR.OD ||
               vr == DicomVR.OF || vr == DicomVR.OL || vr == DicomVR.OV ||
               vr == DicomVR.UN;
    }

    private static bool IsDualStorageVR(DicomVR vr)
    {
        return vr == DicomVR.IS || vr == DicomVR.DS ||
               vr == DicomVR.DA || vr == DicomVR.TM || vr == DicomVR.DT;
    }

    private static int GetNumericVRByteSize(DicomVR vr)
    {
        if (vr == DicomVR.SS || vr == DicomVR.US)
            return 2;
        if (vr == DicomVR.SL || vr == DicomVR.UL || vr == DicomVR.FL || vr == DicomVR.AT)
            return 4;
        if (vr == DicomVR.FD || vr == DicomVR.SV || vr == DicomVR.UV)
            return 8;
        return 4; // default
    }

    private static void WriteNumericValue(byte[] target, int offset, DicomVR vr, object value)
    {
        if (vr == DicomVR.SS)
        {
            short v = ConvertToInt16(value);
            BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.US)
        {
            ushort v = ConvertToUInt16(value);
            BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.SL)
        {
            int v = ConvertToInt32(value);
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.UL)
        {
            uint v = ConvertToUInt32(value);
            BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.FL)
        {
            float v = ConvertToFloat(value);
#if NETSTANDARD2_0
            byte[] bytes = BitConverter.GetBytes(v);
            Buffer.BlockCopy(bytes, 0, target, offset, 4);
#else
            BinaryPrimitives.WriteSingleLittleEndian(target.AsSpan(offset), v);
#endif
        }
        else if (vr == DicomVR.FD)
        {
            double v = ConvertToDouble(value);
            long bits = BitConverter.DoubleToInt64Bits(v);
            BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset), bits);
        }
        else if (vr == DicomVR.SV)
        {
            long v = ConvertToInt64(value);
            BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.UV)
        {
            ulong v = ConvertToUInt64(value);
            BinaryPrimitives.WriteUInt64LittleEndian(target.AsSpan(offset), v);
        }
    }

    #endregion

    #region Numeric Conversions

    private static short ConvertToInt16(object value)
    {
        return value switch
        {
            int i => (short)i,
            long l => (short)l,
            double d => (short)d,
            _ => Convert.ToInt16(value, CultureInfo.InvariantCulture)
        };
    }

    private static ushort ConvertToUInt16(object value)
    {
        return value switch
        {
            int i => (ushort)i,
            long l => (ushort)l,
            double d => (ushort)d,
            _ => Convert.ToUInt16(value, CultureInfo.InvariantCulture)
        };
    }

    private static int ConvertToInt32(object value)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
        };
    }

    private static uint ConvertToUInt32(object value)
    {
        return value switch
        {
            int i => (uint)i,
            long l => (uint)l,
            double d => (uint)d,
            _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture)
        };
    }

    private static float ConvertToFloat(object value)
    {
        return value switch
        {
            double d => (float)d,
            int i => i,
            long l => l,
            _ => Convert.ToSingle(value, CultureInfo.InvariantCulture)
        };
    }

    private static double ConvertToDouble(object value)
    {
        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
        };
    }

    private static long ConvertToInt64(object value)
    {
        return value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static ulong ConvertToUInt64(object value)
    {
        return value switch
        {
            long l => (ulong)l,
            int i => (ulong)i,
            double d => (ulong)d,
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region BSON Primitive Readers

    private static int ReadInt32(byte[] data, ref int offset)
    {
        int val = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        offset += 4;
        return val;
    }

    private static long ReadInt64(byte[] data, ref int offset)
    {
        long val = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
        offset += 8;
        return val;
    }

    private static double ReadDouble(byte[] data, ref int offset)
    {
        long bits = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
        offset += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static string ReadCString(byte[] data, ref int offset)
    {
        int start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        int length = offset - start;
#if NETSTANDARD2_0
        string result = Encoding.UTF8.GetString(data, start, length);
#else
        string result = Encoding.UTF8.GetString(data.AsSpan(start, length));
#endif

        if (offset < data.Length)
            offset++; // skip null terminator

        return result;
    }

    private static string ReadBsonString(byte[] data, ref int offset)
    {
        int length = ReadInt32(data, ref offset); // includes null terminator
        int byteLength = length - 1; // exclude null

        if (byteLength <= 0)
        {
            offset++; // skip null terminator
            return string.Empty;
        }

#if NETSTANDARD2_0
        string result = Encoding.UTF8.GetString(data, offset, byteLength);
#else
        string result = Encoding.UTF8.GetString(data.AsSpan(offset, byteLength));
#endif
        offset += length; // advance past string bytes + null terminator
        return result;
    }

    private static byte[] ReadBsonBinary(byte[] data, ref int offset)
    {
        int length = ReadInt32(data, ref offset);
        byte _subtype = data[offset++]; // subtype byte (unused)
        byte[] result = new byte[length];
        Buffer.BlockCopy(data, offset, result, 0, length);
        offset += length;
        return result;
    }

    /// <summary>
    /// Reads a BSON array and returns its elements as a list of boxed values.
    /// Arrays containing BSON Documents are returned as byte[] (raw doc bytes) for recursive parsing.
    /// </summary>
    private static List<object> ReadBsonArray(byte[] data, ref int offset)
    {
        var result = new List<object>();

        int docSize = ReadInt32(data, ref offset);
        int docEnd = offset + docSize - 4;

        while (offset < docEnd - 1)
        {
            byte typeByte = data[offset++];
            if (typeByte == 0x00)
                break;

            // Skip array index key (CString)
            ReadCString(data, ref offset);

            object? value = ReadTypedValue(data, ref offset, typeByte);
            if (value != null)
            {
                result.Add(value);
            }
        }

        if (offset < docEnd)
            offset = docEnd;

        return result;
    }

    /// <summary>
    /// Reads a typed value from BSON data based on the type byte.
    /// </summary>
    private static object? ReadTypedValue(byte[] data, ref int offset, byte typeByte)
    {
        switch (typeByte)
        {
            case BsonType.Double:
                return ReadDouble(data, ref offset);

            case BsonType.String:
                return ReadBsonString(data, ref offset);

            case BsonType.Document:
            {
                // Return raw document bytes for recursive parsing (sequences)
                int docStart = offset - 0; // offset currently points to first byte of doc size
                int docSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
                byte[] docBytes = new byte[docSize];
                Buffer.BlockCopy(data, offset, docBytes, 0, docSize);
                offset += docSize;
                return docBytes;
            }

            case BsonType.Array:
                // Nested arrays (rare in DICOM BSON)
                return ReadBsonArray(data, ref offset);

            case BsonType.Binary:
                return ReadBsonBinary(data, ref offset);

            case BsonType.Boolean:
            {
                bool val = data[offset] != 0;
                offset++;
                return val;
            }

            case BsonType.DateTime:
            {
                long ms = ReadInt64(data, ref offset);
                return ms;
            }

            case BsonType.Null:
                return null;

            case BsonType.Int32:
            {
                int val = ReadInt32(data, ref offset);
                return val;
            }

            case BsonType.Int64:
            {
                long val = ReadInt64(data, ref offset);
                return val;
            }

            default:
                // Unknown type: cannot reliably skip without knowing size
                return null;
        }
    }

    /// <summary>
    /// Skips over a BSON value based on its type byte without parsing it.
    /// </summary>
    private static void SkipValue(byte[] data, ref int offset, byte typeByte)
    {
        switch (typeByte)
        {
            case BsonType.Double:
                offset += 8;
                break;

            case BsonType.String:
            {
                int len = ReadInt32(data, ref offset);
                offset += len; // length includes null
                break;
            }

            case BsonType.Document:
            case BsonType.Array:
            {
                int docSize = ReadInt32(data, ref offset);
                offset += docSize - 4; // -4 because we already read size
                break;
            }

            case BsonType.Binary:
            {
                int len = ReadInt32(data, ref offset);
                offset++; // subtype
                offset += len;
                break;
            }

            case BsonType.Boolean:
                offset++;
                break;

            case BsonType.DateTime:
            case BsonType.Int64:
                offset += 8;
                break;

            case BsonType.Null:
                break;

            case BsonType.Int32:
                offset += 4;
                break;
        }
    }

    #endregion
}
