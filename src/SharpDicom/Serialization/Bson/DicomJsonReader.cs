using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using SharpDicom.Data;

namespace SharpDicom.Serialization.Bson;

/// <summary>
/// Deserializes DICOM-JSON (PS3.18 Annex F) into a <see cref="DicomDataset"/> using <see cref="JsonDocument"/>.
/// </summary>
/// <remarks>
/// Parses strict DICOM JSON Model objects produced by DICOMweb STOW/WADO-RS or
/// <see cref="DicomJsonWriter"/>. Each JSON property key is an 8-character uppercase hex tag.
/// </remarks>
public static class DicomJsonReader
{
    /// <summary>
    /// Deserializes a UTF-8 JSON byte array in PS3.18 Annex F format to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON bytes.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset Deserialize(byte[] json, BsonSerializationOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(json);
#else
        if (json == null)
            throw new ArgumentNullException(nameof(json));
#endif

        return DeserializeCore(json, options ?? BsonSerializationOptions.Default);
    }

    /// <summary>
    /// Deserializes a UTF-8 JSON <see cref="ReadOnlyMemory{T}"/> in PS3.18 Annex F format to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON memory.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset Deserialize(ReadOnlyMemory<byte> json, BsonSerializationOptions? options = null)
    {
        return DeserializeCore(json, options ?? BsonSerializationOptions.Default);
    }

    /// <summary>
    /// Deserializes a JSON string in PS3.18 Annex F format to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset Deserialize(string json, BsonSerializationOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(json);
#else
        if (json == null)
            throw new ArgumentNullException(nameof(json));
#endif

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return DeserializeCore(bytes, options ?? BsonSerializationOptions.Default);
    }

    /// <summary>
    /// Deserializes a JSON stream in PS3.18 Annex F format to a <see cref="DicomDataset"/>.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 JSON.</param>
    /// <param name="options">Serialization options, or null for defaults.</param>
    /// <returns>A populated <see cref="DicomDataset"/>.</returns>
    public static DicomDataset Deserialize(Stream stream, BsonSerializationOptions? options = null)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
#endif

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return DeserializeCore(ms.ToArray(), options ?? BsonSerializationOptions.Default);
    }

    #region Core Parsing

    private static DicomDataset DeserializeCore(ReadOnlyMemory<byte> jsonBytes, BsonSerializationOptions options)
    {
        using var doc = JsonDocument.Parse(jsonBytes);
        return ReadDataset(doc.RootElement, options);
    }

    private static DicomDataset ReadDataset(JsonElement root, BsonSerializationOptions options)
    {
        var dataset = new DicomDataset();

        if (root.ValueKind != JsonValueKind.Object)
            return dataset;

        foreach (var property in root.EnumerateObject())
        {
            string key = property.Name;

            if (!TryParseTagKey(key, out var tag))
                continue;

            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var element = ReadElementObject(property.Value, tag, options);
            if (element != null)
            {
                dataset.Add(element);
            }
        }

        return dataset;
    }

    private static IDicomElement? ReadElementObject(
        JsonElement elementObj,
        DicomTag tag,
        BsonSerializationOptions options)
    {
        // Read "vr" field
        DicomVR vr;
        if (elementObj.TryGetProperty("vr", out JsonElement vrElement) &&
            vrElement.ValueKind == JsonValueKind.String)
        {
            string vrStr = vrElement.GetString()!;
            if (vrStr.Length == 2)
            {
                vr = new DicomVR(vrStr);
            }
            else
            {
                // Unknown VR string: fall back to dictionary
                var entry = DicomDictionary.Default.GetEntry(tag);
                vr = entry.HasValue ? entry.Value.DefaultVR : DicomVR.UN;
            }
        }
        else
        {
            // No vr field: look up in dictionary
            var entry = DicomDictionary.Default.GetEntry(tag);
            vr = entry.HasValue ? entry.Value.DefaultVR : DicomVR.UN;
        }

        // Check for BulkDataURI
        if (elementObj.TryGetProperty("BulkDataURI", out JsonElement bulkUri) &&
            bulkUri.ValueKind == JsonValueKind.String)
        {
            // Store as empty binary element (data not loaded)
            // The URI is available but we don't resolve it here
            return new DicomBinaryElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        // Check for InlineBinary
        if (elementObj.TryGetProperty("InlineBinary", out JsonElement inlineBinary) &&
            inlineBinary.ValueKind == JsonValueKind.String)
        {
            string base64 = inlineBinary.GetString()!;
            byte[] binaryData = Convert.FromBase64String(base64);
            return new DicomBinaryElement(tag, vr, binaryData);
        }

        // Check for Value array
        if (elementObj.TryGetProperty("Value", out JsonElement valueArray) &&
            valueArray.ValueKind == JsonValueKind.Array)
        {
            return ReconstructFromValueArray(tag, vr, valueArray, options);
        }

        // Empty element (no Value, InlineBinary, or BulkDataURI)
        if (vr == DicomVR.SQ)
        {
            return new DicomSequence(tag, Array.Empty<DicomDataset>());
        }

        if (vr.IsStringVR)
        {
            return new DicomStringElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        if (vr.IsNumericVR)
        {
            return new DicomNumericElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        return new DicomBinaryElement(tag, vr, ReadOnlyMemory<byte>.Empty);
    }

    #endregion

    #region Value Array Reconstruction

    private static IDicomElement? ReconstructFromValueArray(
        DicomTag tag,
        DicomVR vr,
        JsonElement valueArray,
        BsonSerializationOptions options)
    {
        // SQ: recursive
        if (vr == DicomVR.SQ)
        {
            return ReconstructSequence(tag, valueArray, options);
        }

        // PN: special object format
        if (vr == DicomVR.PN)
        {
            return ReconstructPersonName(tag, valueArray);
        }

        // AT: hex string array -> binary
        if (vr == DicomVR.AT)
        {
            return ReconstructATElement(tag, valueArray);
        }

        // IS: JSON numbers -> string representation
        if (vr == DicomVR.IS)
        {
            return ReconstructIntegerString(tag, valueArray);
        }

        // DS: JSON numbers -> string representation
        if (vr == DicomVR.DS)
        {
            return ReconstructDecimalString(tag, valueArray);
        }

        // Numeric VRs (SS, US, SL, UL, FL, FD, SV, UV)
        if (vr.IsNumericVR)
        {
            return ReconstructNumericElement(tag, vr, valueArray);
        }

        // All other string VRs
        if (vr.IsStringVR)
        {
            return ReconstructStringElement(tag, vr, valueArray);
        }

        // Binary VRs that somehow have a Value array (shouldn't happen per spec)
        // Fall back to string
        return ReconstructStringElement(tag, vr, valueArray);
    }

    private static DicomSequence ReconstructSequence(
        DicomTag tag,
        JsonElement valueArray,
        BsonSerializationOptions options)
    {
        var items = new List<DicomDataset>();

        foreach (var item in valueArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var dataset = ReadDataset(item, options);
                items.Add(dataset);
            }
        }

        return new DicomSequence(tag, items);
    }

    private static DicomStringElement ReconstructPersonName(DicomTag tag, JsonElement valueArray)
    {
        // PN Value: array of objects with Alphabetic/Ideographic/Phonetic
        var sb = new StringBuilder();
        int valueIndex = 0;

        foreach (var pnObj in valueArray.EnumerateArray())
        {
            if (valueIndex > 0)
                sb.Append('\\');

            if (pnObj.ValueKind == JsonValueKind.Object)
            {
                string? alphabetic = null;
                string? ideographic = null;
                string? phonetic = null;

                if (pnObj.TryGetProperty("Alphabetic", out JsonElement alpha) &&
                    alpha.ValueKind == JsonValueKind.String)
                {
                    alphabetic = alpha.GetString();
                }

                if (pnObj.TryGetProperty("Ideographic", out JsonElement ideo) &&
                    ideo.ValueKind == JsonValueKind.String)
                {
                    ideographic = ideo.GetString();
                }

                if (pnObj.TryGetProperty("Phonetic", out JsonElement phon) &&
                    phon.ValueKind == JsonValueKind.String)
                {
                    phonetic = phon.GetString();
                }

                // Reconstruct: Alphabetic=Ideographic=Phonetic
                if (phonetic != null)
                {
                    sb.Append(alphabetic ?? "");
                    sb.Append('=');
                    sb.Append(ideographic ?? "");
                    sb.Append('=');
                    sb.Append(phonetic);
                }
                else if (ideographic != null)
                {
                    sb.Append(alphabetic ?? "");
                    sb.Append('=');
                    sb.Append(ideographic);
                }
                else
                {
                    sb.Append(alphabetic ?? "");
                }
            }

            valueIndex++;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new DicomStringElement(tag, DicomVR.PN, bytes);
    }

    private static DicomNumericElement ReconstructATElement(DicomTag tag, JsonElement valueArray)
    {
        int count = valueArray.GetArrayLength();
        byte[] rawBytes = new byte[count * 4];
        int i = 0;

        foreach (var item in valueArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string hexStr = item.GetString()!;
                if (hexStr.Length == 8 &&
                    uint.TryParse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint tagVal))
                {
                    ushort group = (ushort)(tagVal >> 16);
                    ushort element = (ushort)(tagVal & 0xFFFF);
                    BinaryPrimitives.WriteUInt16LittleEndian(rawBytes.AsSpan(i * 4), group);
                    BinaryPrimitives.WriteUInt16LittleEndian(rawBytes.AsSpan(i * 4 + 2), element);
                }
            }
            i++;
        }

        return new DicomNumericElement(tag, DicomVR.AT, rawBytes);
    }

    private static DicomStringElement ReconstructIntegerString(DicomTag tag, JsonElement valueArray)
    {
        // IS in DICOM-JSON: array of JSON numbers -> reconstruct to DICOM IS string
        var sb = new StringBuilder();
        int i = 0;

        foreach (var item in valueArray.EnumerateArray())
        {
            if (i > 0)
                sb.Append('\\');

            if (item.ValueKind == JsonValueKind.Number)
            {
                if (item.TryGetInt64(out long longVal))
                {
                    sb.Append(longVal.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(item.GetDouble().ToString("G", CultureInfo.InvariantCulture));
                }
            }
            else if (item.ValueKind == JsonValueKind.Null)
            {
                // null in IS array: leave empty for this value position
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                // Some producers may write IS values as strings
                sb.Append(item.GetString());
            }

            i++;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new DicomStringElement(tag, DicomVR.IS, bytes);
    }

    private static DicomStringElement ReconstructDecimalString(DicomTag tag, JsonElement valueArray)
    {
        // DS in DICOM-JSON: array of JSON numbers -> reconstruct to DICOM DS string
        var sb = new StringBuilder();
        int i = 0;

        foreach (var item in valueArray.EnumerateArray())
        {
            if (i > 0)
                sb.Append('\\');

            if (item.ValueKind == JsonValueKind.Number)
            {
                sb.Append(item.GetDouble().ToString("G", CultureInfo.InvariantCulture));
            }
            else if (item.ValueKind == JsonValueKind.Null)
            {
                // null in DS array: leave empty
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                // Some producers may write DS values as strings
                sb.Append(item.GetString());
            }

            i++;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new DicomStringElement(tag, DicomVR.DS, bytes);
    }

    private static DicomNumericElement ReconstructNumericElement(
        DicomTag tag,
        DicomVR vr,
        JsonElement valueArray)
    {
        int count = valueArray.GetArrayLength();
        if (count == 0)
        {
            return new DicomNumericElement(tag, vr, ReadOnlyMemory<byte>.Empty);
        }

        int valueSize = GetNumericVRByteSize(vr);
        byte[] rawBytes = new byte[count * valueSize];
        int i = 0;

        foreach (var item in valueArray.EnumerateArray())
        {
            int byteOffset = i * valueSize;

            if (item.ValueKind == JsonValueKind.Number)
            {
                WriteNumericValue(rawBytes, byteOffset, vr, item);
            }
            else if (item.ValueKind == JsonValueKind.String && vr == DicomVR.UV)
            {
                // UV values > Int64.Max encoded as strings per PS3.18 F.2.3
                if (ulong.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong val))
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(rawBytes.AsSpan(byteOffset), val);
                }
            }

            i++;
        }

        return new DicomNumericElement(tag, vr, rawBytes);
    }

    private static DicomStringElement ReconstructStringElement(
        DicomTag tag,
        DicomVR vr,
        JsonElement valueArray)
    {
        var sb = new StringBuilder();
        int i = 0;

        foreach (var item in valueArray.EnumerateArray())
        {
            if (i > 0)
                sb.Append('\\');

            if (item.ValueKind == JsonValueKind.String)
            {
                sb.Append(item.GetString());
            }
            else if (item.ValueKind == JsonValueKind.Number)
            {
                // Best-effort: number in a string VR
                sb.Append(item.GetRawText());
            }
            else if (item.ValueKind == JsonValueKind.Null)
            {
                // null value: leave empty
            }

            i++;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new DicomStringElement(tag, vr, bytes);
    }

    #endregion

    #region Helpers

    private static bool TryParseTagKey(string key, out DicomTag tag)
    {
        tag = default;

        // PS3.18 Annex F: keys are always 8-char hex (GGGGEEEE)
        if (key.Length != 8)
            return false;

        for (int i = 0; i < 8; i++)
        {
            char c = key[i];
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }

        if (uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint val))
        {
            tag = new DicomTag(val);
            return true;
        }

        return false;
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

    private static void WriteNumericValue(byte[] target, int offset, DicomVR vr, JsonElement item)
    {
        if (vr == DicomVR.SS)
        {
            short v = (short)item.GetInt32();
            BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.US)
        {
            ushort v = (ushort)item.GetInt32();
            BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.SL)
        {
            int v = item.GetInt32();
            BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.UL)
        {
            uint v = item.GetUInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.FL)
        {
            float v = (float)item.GetDouble();
#if NETSTANDARD2_0
            byte[] bytes = BitConverter.GetBytes(v);
            Buffer.BlockCopy(bytes, 0, target, offset, 4);
#else
            BinaryPrimitives.WriteSingleLittleEndian(target.AsSpan(offset), v);
#endif
        }
        else if (vr == DicomVR.FD)
        {
            double v = item.GetDouble();
            long bits = BitConverter.DoubleToInt64Bits(v);
            BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset), bits);
        }
        else if (vr == DicomVR.SV)
        {
            long v = item.GetInt64();
            BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset), v);
        }
        else if (vr == DicomVR.UV)
        {
            ulong v = item.GetUInt64();
            BinaryPrimitives.WriteUInt64LittleEndian(target.AsSpan(offset), v);
        }
    }

    #endregion
}
