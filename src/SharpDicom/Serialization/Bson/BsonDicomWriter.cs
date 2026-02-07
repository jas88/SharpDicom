using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Serializes a <see cref="DicomDataset"/> to raw BSON bytes with zero external dependencies.
/// </summary>
/// <remarks>
/// Produces MongoDB-native BSON documents with dual-storage for IS/DS/DA/TM/DT VRs,
/// Person Name component parsing, private tag grouping, sequence nesting,
/// and binary threshold handling.
/// </remarks>
public static class BsonDicomWriter
{
    // Unix epoch for DateTime-to-BSON conversion
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Serializes a DICOM dataset to a BSON document byte array.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A BSON document as a byte array.</returns>
    public static byte[] Serialize(DicomDataset dataset, BsonSerializationOptions? options = null)
    {
        options ??= BsonSerializationOptions.Default;

        using var buffer = new BsonDocumentBuffer(4096);
        WriteDataset(buffer, dataset, options, depth: 0, flattenTarget: null);
        return buffer.ToArray();
    }

    /// <summary>
    /// Serializes a DICOM dataset to BSON and writes it to the specified <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="writer">The target buffer writer.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    public static void Serialize(DicomDataset dataset, IBufferWriter<byte> writer, BsonSerializationOptions? options = null)
    {
        options ??= BsonSerializationOptions.Default;

        using var buffer = new BsonDocumentBuffer(4096);
        WriteDataset(buffer, dataset, options, depth: 0, flattenTarget: null);
        buffer.CopyTo(writer);
    }

    /// <summary>
    /// Formats a DICOM tag as a BSON key string per the specified format.
    /// </summary>
    internal static string FormatTagKey(DicomTag tag, BsonTagKeyFormat format)
    {
        switch (format)
        {
            case BsonTagKeyFormat.Dotted:
                return $"{tag.Group:X4}.{tag.Element:X4}";

            case BsonTagKeyFormat.Keyword:
                var entry = DicomDictionary.Default.GetEntry(tag);
                if (entry.HasValue && !string.IsNullOrEmpty(entry.Value.Keyword))
                    return entry.Value.Keyword;
                return $"{tag.Group:X4}{tag.Element:X4}";

            default: // Hex8
                return $"{tag.Group:X4}{tag.Element:X4}";
        }
    }

    private static void WriteDataset(
        BsonDocumentBuffer buffer,
        DicomDataset dataset,
        BsonSerializationOptions options,
        int depth,
        BsonDocumentBuffer? flattenTarget)
    {
        int docOffset = buffer.BeginDocument();

        // Separate standard and private elements
        var privateByCreator = options.StripPrivateTags
            ? null
            : new Dictionary<string, List<IDicomElement>>();

        foreach (var element in dataset)
        {
            if (element.Tag.IsPrivate)
            {
                if (options.StripPrivateTags)
                    continue;

                // Skip private creator elements themselves (they're metadata)
                if (element.Tag.IsPrivateCreator)
                    continue;

                string creator = dataset.PrivateCreators.GetCreator(element.Tag) ?? "_unknown";
                if (!privateByCreator!.TryGetValue(creator, out var list))
                {
                    list = new List<IDicomElement>();
                    privateByCreator[creator] = list;
                }
                list.Add(element);
                continue;
            }

            string key = FormatTagKey(element.Tag, options.TagKeyFormat);
            WriteElement(buffer, key, element, dataset, options, depth, flattenTarget);
        }

        // Write private tags under _private sub-document
        if (privateByCreator != null && privateByCreator.Count > 0)
        {
            buffer.WriteByte(BsonType.Document);
            buffer.WriteCString("_private");
            int privateDocOffset = buffer.BeginDocument();

            foreach (var kvp in privateByCreator)
            {
                buffer.WriteByte(BsonType.Document);
                buffer.WriteCString(kvp.Key);
                int creatorDocOffset = buffer.BeginDocument();

                foreach (var element in kvp.Value)
                {
                    // Private tags always use Hex8 format
                    string key = FormatTagKey(element.Tag, BsonTagKeyFormat.Hex8);
                    WriteElement(buffer, key, element, dataset, options, depth, flattenTarget: null);
                }

                buffer.EndDocument(creatorDocOffset);
            }

            buffer.EndDocument(privateDocOffset);
        }

        buffer.EndDocument(docOffset);
    }

    private static void WriteElement(
        BsonDocumentBuffer buffer,
        string key,
        IDicomElement element,
        DicomDataset dataset,
        BsonSerializationOptions options,
        int depth,
        BsonDocumentBuffer? flattenTarget)
    {
        // Determine if VR metadata should be included
        bool includeVR = ShouldIncludeVR(element.Tag, element.VR, options);

        if (element is DicomSequence seq)
        {
            WriteSequenceElement(buffer, key, seq, options, depth, includeVR, flattenTarget);
            return;
        }

        if (element is DicomStringElement str)
        {
            WriteStringElement(buffer, key, str, options, includeVR);
            return;
        }

        if (element is DicomNumericElement num)
        {
            WriteNumericElement(buffer, key, num, includeVR);
            return;
        }

        // DicomBinaryElement, DicomPixelDataElement, DicomFragmentSequence: all treated as binary
        WriteBinaryElement(buffer, key, element, options, includeVR);
    }

    private static bool ShouldIncludeVR(DicomTag tag, DicomVR vr, BsonSerializationOptions options)
    {
        if (options.AlwaysIncludeVR)
            return true;

        if (tag.IsPrivate)
            return true;

        var entry = DicomDictionary.Default.GetEntry(tag);
        if (entry.HasValue)
        {
            if (entry.Value.HasMultipleVRs)
                return true;
            if (entry.Value.IsRetired)
                return true;
        }

        return false;
    }

    #region String Element Writing

    private static void WriteStringElement(
        BsonDocumentBuffer buffer,
        string key,
        DicomStringElement element,
        BsonSerializationOptions options,
        bool includeVR)
    {
        var vr = element.VR;

        if (element.IsEmpty)
        {
            WriteEmptyElement(buffer, key, vr, includeVR);
            return;
        }

        if (vr == DicomVR.IS)
        {
            WriteIntegerStringElement(buffer, key, element, includeVR);
            return;
        }

        if (vr == DicomVR.DS)
        {
            WriteDecimalStringElement(buffer, key, element, includeVR);
            return;
        }

        if (vr == DicomVR.DA)
        {
            WriteDateElement(buffer, key, element, includeVR);
            return;
        }

        if (vr == DicomVR.TM)
        {
            WriteTimeElement(buffer, key, element, includeVR);
            return;
        }

        if (vr == DicomVR.DT)
        {
            WriteDateTimeElement(buffer, key, element, includeVR);
            return;
        }

        if (vr == DicomVR.PN)
        {
            WritePersonNameElement(buffer, key, element, includeVR);
            return;
        }

        // All other string VRs: simple Value array
        WriteSimpleStringElement(buffer, key, element, includeVR);
    }

    private static void WriteEmptyElement(BsonDocumentBuffer buffer, string key, DicomVR vr, bool includeVR)
    {
        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, vr);

        // Empty array for Value
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        buffer.EndDocument(arrOffset);

        buffer.EndDocument(docOffset);
    }

    private static void WriteSimpleStringElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        var strings = element.GetStrings() ?? Array.Empty<string>();

        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < strings.Length; i++)
        {
            buffer.WriteByte(BsonType.String);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteBsonString(strings[i]);
        }
        buffer.EndDocument(arrOffset);

        buffer.EndDocument(docOffset);
    }

    private static void WriteIntegerStringElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: parsed integers
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string idx = i.ToString(CultureInfo.InvariantCulture);

            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            {
                buffer.WriteByte(BsonType.Int32);
                buffer.WriteCString(idx);
                buffer.WriteInt32(intVal);
            }
            else if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            {
                buffer.WriteByte(BsonType.Int64);
                buffer.WriteCString(idx);
                buffer.WriteInt64(longVal);
            }
            else
            {
                // Unparseable: write null
                buffer.WriteByte(BsonType.Null);
                buffer.WriteCString(idx);
            }
        }
        buffer.EndDocument(arrOffset);

        // Raw field: original string
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("Raw");
        buffer.WriteBsonString(rawValue);

        buffer.EndDocument(docOffset);
    }

    private static void WriteDecimalStringElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: parsed doubles
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string idx = i.ToString(CultureInfo.InvariantCulture);

            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
            {
                buffer.WriteByte(BsonType.Double);
                buffer.WriteCString(idx);
                buffer.WriteDouble(dblVal);
            }
            else
            {
                buffer.WriteByte(BsonType.Null);
                buffer.WriteCString(idx);
            }
        }
        buffer.EndDocument(arrOffset);

        // Raw field
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("Raw");
        buffer.WriteBsonString(rawValue);

        buffer.EndDocument(docOffset);
    }

    private static void WriteDateElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: BSON DateTime (ms since epoch)
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string idx = i.ToString(CultureInfo.InvariantCulture);

            if (TryParseDicomDate(part, out DateTime dt))
            {
                long ms = (long)(dt.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
                buffer.WriteByte(BsonType.DateTime);
                buffer.WriteCString(idx);
                buffer.WriteInt64(ms);
            }
            else
            {
                buffer.WriteByte(BsonType.Null);
                buffer.WriteCString(idx);
            }
        }
        buffer.EndDocument(arrOffset);

        // Raw field
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("Raw");
        buffer.WriteBsonString(rawValue);

        buffer.EndDocument(docOffset);
    }

    private static void WriteTimeElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: BSON Int64 (milliseconds from midnight)
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string idx = i.ToString(CultureInfo.InvariantCulture);

            if (TryParseDicomTime(part, out long millisFromMidnight))
            {
                buffer.WriteByte(BsonType.Int64);
                buffer.WriteCString(idx);
                buffer.WriteInt64(millisFromMidnight);
            }
            else
            {
                buffer.WriteByte(BsonType.Null);
                buffer.WriteCString(idx);
            }
        }
        buffer.EndDocument(arrOffset);

        // Raw field
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("Raw");
        buffer.WriteBsonString(rawValue);

        buffer.EndDocument(docOffset);
    }

    private static void WriteDateTimeElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: BSON DateTime (ms since epoch)
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            string idx = i.ToString(CultureInfo.InvariantCulture);

            if (TryParseDicomDateTime(part, out DateTime dt))
            {
                long ms = (long)(dt.ToUniversalTime() - UnixEpoch).TotalMilliseconds;
                buffer.WriteByte(BsonType.DateTime);
                buffer.WriteCString(idx);
                buffer.WriteInt64(ms);
            }
            else
            {
                buffer.WriteByte(BsonType.Null);
                buffer.WriteCString(idx);
            }
        }
        buffer.EndDocument(arrOffset);

        // Raw field
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("Raw");
        buffer.WriteBsonString(rawValue);

        buffer.EndDocument(docOffset);
    }

    private static void WritePersonNameElement(
        BsonDocumentBuffer buffer, string key, DicomStringElement element, bool includeVR)
    {
        string rawValue = element.GetString() ?? "";

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        // Value array: original string(s) -- PN can be multi-valued with backslash
        var values = rawValue.Split('\\');
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.String);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteBsonString(values[i]);
        }
        buffer.EndDocument(arrOffset);

        // Parse component groups for the first value (primary PN representation)
        // PN format: Alphabetic=Ideographic=Phonetic, each: Family^Given^Middle^Prefix^Suffix
        if (values.Length > 0 && !string.IsNullOrEmpty(values[0]))
        {
            var componentGroups = values[0].Split('=');
            string[] groupNames = { "Alphabetic", "Ideographic", "Phonetic" };
            string[] fieldNames = { "FamilyName", "GivenName", "MiddleName", "NamePrefix", "NameSuffix" };

            for (int g = 0; g < componentGroups.Length && g < groupNames.Length; g++)
            {
                if (string.IsNullOrEmpty(componentGroups[g]))
                    continue;

                var components = componentGroups[g].Split('^');

                buffer.WriteByte(BsonType.Document);
                buffer.WriteCString(groupNames[g]);
                int groupDocOffset = buffer.BeginDocument();

                for (int c = 0; c < components.Length && c < fieldNames.Length; c++)
                {
                    if (!string.IsNullOrEmpty(components[c]))
                    {
                        buffer.WriteByte(BsonType.String);
                        buffer.WriteCString(fieldNames[c]);
                        buffer.WriteBsonString(components[c]);
                    }
                }

                buffer.EndDocument(groupDocOffset);
            }
        }

        buffer.EndDocument(docOffset);
    }

    #endregion

    #region Numeric Element Writing

    private static void WriteNumericElement(
        BsonDocumentBuffer buffer, string key, DicomNumericElement element, bool includeVR)
    {
        if (element.IsEmpty)
        {
            WriteEmptyElement(buffer, key, element.VR, includeVR);
            return;
        }

        var vr = element.VR;

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, vr);

        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();

        if (vr == DicomVR.SS)
        {
            WriteInt16ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.US)
        {
            WriteUInt16ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.SL)
        {
            WriteInt32ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.UL)
        {
            WriteUInt32ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.FL)
        {
            WriteFloat32ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.FD)
        {
            WriteFloat64ArrayValues(buffer, element);
        }
        else if (vr == DicomVR.AT)
        {
            WriteATArrayValues(buffer, element);
        }
        else
        {
            // Unknown numeric VR, write raw values as Int32 if possible
            var values = element.GetInt32Array();
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    buffer.WriteByte(BsonType.Int32);
                    buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
                    buffer.WriteInt32(values[i]);
                }
            }
        }

        buffer.EndDocument(arrOffset);
        buffer.EndDocument(docOffset);
    }

    private static void WriteInt16ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        var values = element.GetInt16Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Int32);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteInt32(values[i]);
        }
    }

    private static void WriteUInt16ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        var values = element.GetUInt16Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Int32);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteInt32(values[i]);
        }
    }

    private static void WriteInt32ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        var values = element.GetInt32Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Int32);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteInt32(values[i]);
        }
    }

    private static void WriteUInt32ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        // UL → BSON Int64 to avoid sign issues
        var values = element.GetUInt32Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Int64);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteInt64(values[i]);
        }
    }

    private static void WriteFloat32ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        var values = element.GetFloat32Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Double);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteDouble(values[i]);
        }
    }

    private static void WriteFloat64ArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        var values = element.GetFloat64Array();
        if (values == null) return;
        for (int i = 0; i < values.Length; i++)
        {
            buffer.WriteByte(BsonType.Double);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteDouble(values[i]);
        }
    }

    private static void WriteATArrayValues(BsonDocumentBuffer buffer, DicomNumericElement element)
    {
        // AT → BSON String array (formatted as 8-char hex)
        var raw = element.RawValue;
        int count = raw.Length / 4;
        for (int i = 0; i < count; i++)
        {
            var group = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4));
            var elem = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4 + 2));
            string tagStr = $"{group:X4}{elem:X4}";

            buffer.WriteByte(BsonType.String);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));
            buffer.WriteBsonString(tagStr);
        }
    }

    #endregion

    #region Binary Element Writing

    private static void WriteBinaryElement(
        BsonDocumentBuffer buffer, string key, IDicomElement element,
        BsonSerializationOptions options, bool includeVR)
    {
        var rawData = element.RawValue;

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, element.VR);

        if (rawData.Length >= options.BinaryInlineThreshold && options.ExternalBinaryHandler != null)
        {
            // External reference
            var reference = options.ExternalBinaryHandler(element.Tag, rawData);

            buffer.WriteByte(BsonType.String);
            buffer.WriteCString("$ref");
            buffer.WriteBsonString(reference.ReferenceType);

            if (reference.Id != null)
            {
                buffer.WriteByte(BsonType.String);
                buffer.WriteCString("id");
                buffer.WriteBsonString(reference.Id);
            }

            if (reference.Path != null)
            {
                buffer.WriteByte(BsonType.String);
                buffer.WriteCString("path");
                buffer.WriteBsonString(reference.Path);
            }
        }
        else
        {
            // Inline binary data
            WriteBsonBinary(buffer, "Value", rawData.Span);
        }

        buffer.EndDocument(docOffset);
    }

    private static void WriteBsonBinary(BsonDocumentBuffer buffer, string fieldName, ReadOnlySpan<byte> data)
    {
        // BSON Binary: type byte + cstring key + int32 length + byte subtype + bytes
        buffer.WriteByte(BsonType.Binary);
        buffer.WriteCString(fieldName);
        buffer.WriteInt32(data.Length);     // binary data length
        buffer.WriteByte(0x00);             // subtype: Generic
        buffer.WriteBytes(data);
    }

    #endregion

    #region Sequence Element Writing

    private static void WriteSequenceElement(
        BsonDocumentBuffer buffer,
        string key,
        DicomSequence sequence,
        BsonSerializationOptions options,
        int depth,
        bool includeVR,
        BsonDocumentBuffer? flattenTarget)
    {
        if (depth >= options.MaxSequenceDepth)
        {
            // Exceeded max depth: skip (write empty value)
            WriteEmptyElement(buffer, key, DicomVR.SQ, includeVR);
            return;
        }

        buffer.WriteByte(BsonType.Document);
        buffer.WriteCString(key);
        int docOffset = buffer.BeginDocument();

        if (includeVR)
            WriteVRField(buffer, DicomVR.SQ);

        // Value: BSON Array of Documents
        buffer.WriteByte(BsonType.Array);
        buffer.WriteCString("Value");
        int arrOffset = buffer.BeginDocument();

        for (int i = 0; i < sequence.Items.Count; i++)
        {
            var item = sequence.Items[i];

            buffer.WriteByte(BsonType.Document);
            buffer.WriteCString(i.ToString(CultureInfo.InvariantCulture));

            // Determine if this sequence should contribute flattened fields
            BsonDocumentBuffer? itemFlattenTarget = null;
            if (depth == 0 && flattenTarget == null
                && options.FlattenProfile != null
                && options.FlattenProfile.FlattenTags.Contains(sequence.Tag))
            {
                // The flattenTarget for items in this sequence is our parent buffer
                // We need a reference to the parent document buffer -- use the main buffer
                // Flattened fields will be written to the same buffer after the sequence
                itemFlattenTarget = buffer;
            }

            WriteDataset(buffer, item, options, depth + 1, itemFlattenTarget);
        }

        buffer.EndDocument(arrOffset);
        buffer.EndDocument(docOffset);

        // Write flattened dot-notation fields at the top level
        if (depth == 0 && options.FlattenProfile != null
            && options.FlattenProfile.FlattenTags.Contains(sequence.Tag)
            && flattenTarget == null)
        {
            WriteFlattenedFields(buffer, key, sequence, options);
        }
    }

    private static void WriteFlattenedFields(
        BsonDocumentBuffer buffer,
        string sequenceKey,
        DicomSequence sequence,
        BsonSerializationOptions options)
    {
        // For each item in the sequence, write flattened fields as sequenceKey + elementKey
        if (sequence.Items.Count == 0)
            return;

        // Use first item only for flattening (common pattern for single-item sequences)
        var item = sequence.Items[0];
        foreach (var element in item)
        {
            if (element.Tag.IsPrivate)
                continue;

            string elementKey = FormatTagKey(element.Tag, options.TagKeyFormat);
            string flatKey = sequenceKey + elementKey;

            if (element is DicomStringElement strEl)
            {
                var val = strEl.GetString();
                if (val != null)
                {
                    buffer.WriteByte(BsonType.String);
                    buffer.WriteCString(flatKey);
                    buffer.WriteBsonString(val);
                }
            }
        }
    }

    #endregion

    #region Helpers

    private static void WriteVRField(BsonDocumentBuffer buffer, DicomVR vr)
    {
        buffer.WriteByte(BsonType.String);
        buffer.WriteCString("vr");
        buffer.WriteBsonString(vr.ToString());
    }

    private static bool TryParseDicomDate(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
            return false;

        // DICOM DA format: YYYYMMDD
        if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
            return true;

        return false;
    }

    private static bool TryParseDicomTime(string value, out long millisFromMidnight)
    {
        millisFromMidnight = 0;
        if (string.IsNullOrEmpty(value))
            return false;

        // DICOM TM formats: HHMMSS.FFFFFF, HHMMSS, HHMM, HH
        var formats = new[]
        {
            "HHmmss.ffffff",
            "HHmmss.fffff",
            "HHmmss.ffff",
            "HHmmss.fff",
            "HHmmss.ff",
            "HHmmss.f",
            "HHmmss",
            "HHmm",
            "HH"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            {
                millisFromMidnight = (long)dt.TimeOfDay.TotalMilliseconds;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDicomDateTime(string value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
            return false;

        // DICOM DT formats
        var formats = new[]
        {
            "yyyyMMddHHmmss.ffffff",
            "yyyyMMddHHmmss.fffff",
            "yyyyMMddHHmmss.ffff",
            "yyyyMMddHHmmss.fff",
            "yyyyMMddHHmmss.ff",
            "yyyyMMddHHmmss.f",
            "yyyyMMddHHmmss",
            "yyyyMMddHHmm",
            "yyyyMMddHH",
            "yyyyMMdd"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return true;
        }

        return false;
    }

    #endregion
}
