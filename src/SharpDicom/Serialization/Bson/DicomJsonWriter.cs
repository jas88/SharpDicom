using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Serializes a <see cref="DicomDataset"/> to DICOM-JSON per PS3.18 Annex F using <see cref="Utf8JsonWriter"/>.
/// </summary>
/// <remarks>
/// Produces strict DICOM JSON Model objects suitable for DICOMweb STOW/WADO-RS.
/// Every element key is an 8-character uppercase hex tag (GGGGEEEE).
/// Every element object contains a "vr" field.
/// </remarks>
public static class DicomJsonWriter
{
    /// <summary>
    /// Serializes a DICOM dataset to UTF-8 JSON bytes in PS3.18 Annex F format.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A UTF-8 JSON byte array.</returns>
    public static byte[] Serialize(DicomDataset dataset, BsonSerializationOptions? options = null)
    {
        options ??= BsonSerializationOptions.Default;
        using var ms = new MemoryStream(4096);
        using (var writer = CreateWriter(ms))
        {
            WriteDataset(writer, dataset, options, depth: 0);
            writer.Flush();
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Serializes a DICOM dataset to PS3.18 Annex F JSON and writes it to the specified stream.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="stream">The target stream.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    public static void Serialize(DicomDataset dataset, Stream stream, BsonSerializationOptions? options = null)
    {
        options ??= BsonSerializationOptions.Default;
        using var writer = CreateWriter(stream);
        WriteDataset(writer, dataset, options, depth: 0);
        writer.Flush();
    }

    /// <summary>
    /// Serializes a DICOM dataset to a PS3.18 Annex F JSON string.
    /// </summary>
    /// <param name="dataset">The dataset to serialize.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A JSON string.</returns>
    public static string SerializeToString(DicomDataset dataset, BsonSerializationOptions? options = null)
    {
        var bytes = Serialize(dataset, options);
        return Encoding.UTF8.GetString(bytes);
    }

    private static Utf8JsonWriter CreateWriter(Stream stream)
    {
        return new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        });
    }

    private static void WriteDataset(
        Utf8JsonWriter writer,
        DicomDataset dataset,
        BsonSerializationOptions options,
        int depth)
    {
        writer.WriteStartObject();

        foreach (var element in dataset)
        {
            if (element.Tag.IsPrivate)
            {
                if (options.StripPrivateTags)
                    continue;

                // Skip private creator elements (they are metadata, not data)
                if (element.Tag.IsPrivateCreator)
                    continue;
            }

            // DICOM-JSON keys are always 8-char uppercase hex
            string key = $"{element.Tag.Group:X4}{element.Tag.Element:X4}";
            WriteElement(writer, key, element, options, depth);
        }

        writer.WriteEndObject();
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        string key,
        IDicomElement element,
        BsonSerializationOptions options,
        int depth)
    {
        if (element is DicomSequence seq)
        {
            WriteSequenceElement(writer, key, seq, options, depth);
            return;
        }

        if (element is DicomStringElement str)
        {
            WriteStringElement(writer, key, str, options);
            return;
        }

        if (element is DicomNumericElement num)
        {
            WriteNumericElement(writer, key, num);
            return;
        }

        // DicomBinaryElement, DicomPixelDataElement, DicomFragmentSequence: binary
        WriteBinaryElement(writer, key, element, options);
    }

    #region String Element Writing

    private static void WriteStringElement(
        Utf8JsonWriter writer,
        string key,
        DicomStringElement element,
        BsonSerializationOptions options)
    {
        var vr = element.VR;

        writer.WriteStartObject(key);
        writer.WriteString("vr", vr.ToString());

        if (element.IsEmpty)
        {
            // PS3.18 F.2.5: empty elements omit Value entirely
            writer.WriteEndObject();
            return;
        }

        if (vr == DicomVR.PN)
        {
            WritePersonNameValues(writer, element);
        }
        else if (vr == DicomVR.IS)
        {
            WriteIntegerStringValues(writer, element);
        }
        else if (vr == DicomVR.DS)
        {
            WriteDecimalStringValues(writer, element);
        }
        else
        {
            // All other string VRs: Value array of strings
            WriteSimpleStringValues(writer, element);
        }

        writer.WriteEndObject();
    }

    private static void WriteSimpleStringValues(Utf8JsonWriter writer, DicomStringElement element)
    {
        var strings = element.GetStrings() ?? Array.Empty<string>();

        writer.WriteStartArray("Value");
        for (int i = 0; i < strings.Length; i++)
        {
            writer.WriteStringValue(strings[i]);
        }
        writer.WriteEndArray();
    }

    private static void WriteIntegerStringValues(Utf8JsonWriter writer, DicomStringElement element)
    {
        // PS3.18 F.2.3: IS → JSON number array
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        writer.WriteStartArray("Value");
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            {
                writer.WriteNumberValue(longVal);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteDecimalStringValues(Utf8JsonWriter writer, DicomStringElement element)
    {
        // PS3.18 F.2.3: DS → JSON number array
        string rawValue = element.GetString() ?? "";
        var parts = rawValue.Split('\\');

        writer.WriteStartArray("Value");
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
            {
                writer.WriteNumberValue(dblVal);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
        writer.WriteEndArray();
    }

    private static void WritePersonNameValues(Utf8JsonWriter writer, DicomStringElement element)
    {
        // PS3.18 F.2.2: PN → Value array of objects with Alphabetic/Ideographic/Phonetic
        string rawValue = element.GetString() ?? "";
        var pnValues = rawValue.Split('\\');

        writer.WriteStartArray("Value");
        for (int v = 0; v < pnValues.Length; v++)
        {
            writer.WriteStartObject();
            var componentGroups = pnValues[v].Split('=');
            string[] groupNames = { "Alphabetic", "Ideographic", "Phonetic" };

            for (int g = 0; g < componentGroups.Length && g < groupNames.Length; g++)
            {
                if (!string.IsNullOrEmpty(componentGroups[g]))
                {
                    writer.WriteString(groupNames[g], componentGroups[g]);
                }
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    #endregion

    #region Numeric Element Writing

    private static void WriteNumericElement(
        Utf8JsonWriter writer,
        string key,
        DicomNumericElement element)
    {
        var vr = element.VR;

        writer.WriteStartObject(key);
        writer.WriteString("vr", vr.ToString());

        if (element.IsEmpty)
        {
            writer.WriteEndObject();
            return;
        }

        if (vr == DicomVR.AT)
        {
            WriteATValues(writer, element);
        }
        else if (vr == DicomVR.SS)
        {
            WriteInt16Values(writer, element);
        }
        else if (vr == DicomVR.US)
        {
            WriteUInt16Values(writer, element);
        }
        else if (vr == DicomVR.SL)
        {
            WriteInt32Values(writer, element);
        }
        else if (vr == DicomVR.UL)
        {
            WriteUInt32Values(writer, element);
        }
        else if (vr == DicomVR.FL)
        {
            WriteFloat32Values(writer, element);
        }
        else if (vr == DicomVR.FD)
        {
            WriteFloat64Values(writer, element);
        }
        else if (vr == DicomVR.SV)
        {
            WriteInt64Values(writer, element);
        }
        else if (vr == DicomVR.UV)
        {
            WriteUInt64Values(writer, element);
        }
        else
        {
            // Unknown numeric VR: attempt Int32 array
            var values = element.GetInt32Array();
            if (values != null)
            {
                writer.WriteStartArray("Value");
                for (int i = 0; i < values.Length; i++)
                    writer.WriteNumberValue(values[i]);
                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteATValues(Utf8JsonWriter writer, DicomNumericElement element)
    {
        // AT → Value array of 8-char hex strings
        var raw = element.RawValue;
        int count = raw.Length / 4;

        writer.WriteStartArray("Value");
        for (int i = 0; i < count; i++)
        {
            var group = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4));
            var elem = BinaryPrimitives.ReadUInt16LittleEndian(raw.Span.Slice(i * 4 + 2));
            writer.WriteStringValue($"{group:X4}{elem:X4}");
        }
        writer.WriteEndArray();
    }

    private static void WriteInt16Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetInt16Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
            writer.WriteNumberValue(values[i]);
        writer.WriteEndArray();
    }

    private static void WriteUInt16Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetUInt16Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
            writer.WriteNumberValue(values[i]);
        writer.WriteEndArray();
    }

    private static void WriteInt32Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetInt32Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
            writer.WriteNumberValue(values[i]);
        writer.WriteEndArray();
    }

    private static void WriteUInt32Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetUInt32Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
            writer.WriteNumberValue(values[i]);
        writer.WriteEndArray();
    }

    private static void WriteFloat32Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetFloat32Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
        {
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(values[i]);
        }
        writer.WriteEndArray();
    }

    private static void WriteFloat64Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        var values = element.GetFloat64Array();
        if (values == null) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(values[i]);
        }
        writer.WriteEndArray();
    }

    private static void WriteInt64Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        // SV: 64-bit signed integers
        var raw = element.RawValue;
        int count = raw.Length / 8;
        if (count == 0) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < count; i++)
        {
            long val = BinaryPrimitives.ReadInt64LittleEndian(raw.Span.Slice(i * 8));
            writer.WriteNumberValue(val);
        }
        writer.WriteEndArray();
    }

    private static void WriteUInt64Values(Utf8JsonWriter writer, DicomNumericElement element)
    {
        // UV: 64-bit unsigned integers
        // Per PS3.18 F.2.3: values > Int64.Max are encoded as strings
        var raw = element.RawValue;
        int count = raw.Length / 8;
        if (count == 0) return;

        writer.WriteStartArray("Value");
        for (int i = 0; i < count; i++)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(raw.Span.Slice(i * 8));
            if (val > long.MaxValue)
            {
                // Exceeds JSON number range: write as string
                writer.WriteStringValue(val.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue((long)val);
            }
        }
        writer.WriteEndArray();
    }

    #endregion

    #region Binary Element Writing

    private static void WriteBinaryElement(
        Utf8JsonWriter writer,
        string key,
        IDicomElement element,
        BsonSerializationOptions options)
    {
        var rawData = element.RawValue;

        writer.WriteStartObject(key);
        writer.WriteString("vr", element.VR.ToString());

        if (element.IsEmpty)
        {
            writer.WriteEndObject();
            return;
        }

        if (rawData.Length >= options.BinaryInlineThreshold && options.ExternalBinaryHandler != null)
        {
            // External reference → BulkDataURI
            var reference = options.ExternalBinaryHandler(element.Tag, rawData);

            // Construct URI from reference
            string uri;
            if (reference.Path != null)
                uri = reference.Path;
            else if (reference.Id != null)
                uri = $"{reference.ReferenceType}://{reference.Id}";
            else
                uri = reference.ReferenceType;

            writer.WriteString("BulkDataURI", uri);
        }
        else
        {
            // InlineBinary: base64 encoded
#if NETSTANDARD2_0
            writer.WriteString("InlineBinary", Convert.ToBase64String(rawData.ToArray()));
#else
            writer.WriteString("InlineBinary", Convert.ToBase64String(rawData.Span));
#endif
        }

        writer.WriteEndObject();
    }

    #endregion

    #region Sequence Element Writing

    private static void WriteSequenceElement(
        Utf8JsonWriter writer,
        string key,
        DicomSequence sequence,
        BsonSerializationOptions options,
        int depth)
    {
        writer.WriteStartObject(key);
        writer.WriteString("vr", "SQ");

        if (depth >= options.MaxSequenceDepth)
        {
            // Exceeded max depth: omit Value
            writer.WriteEndObject();
            return;
        }

        if (sequence.IsEmpty)
        {
            writer.WriteEndObject();
            return;
        }

        writer.WriteStartArray("Value");
        for (int i = 0; i < sequence.Items.Count; i++)
        {
            WriteDataset(writer, sequence.Items[i], options, depth + 1);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    #endregion
}
