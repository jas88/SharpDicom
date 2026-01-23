using System;
using System.Buffers.Binary;
using System.Linq;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Data;

/// <summary>
/// DICOM element for binary numeric Value Representations.
/// Covers: FL, FD, SL, SS, UL, US, AT
/// </summary>
public sealed class DicomNumericElement : IDicomElement
{
    /// <inheritdoc />
    public DicomTag Tag { get; }

    /// <inheritdoc />
    public DicomVR VR { get; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawValue { get; }

    /// <inheritdoc />
    public int Length => RawValue.Length;

    /// <inheritdoc />
    public bool IsEmpty => RawValue.IsEmpty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomNumericElement"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="vr">The Value Representation.</param>
    /// <param name="value">The raw byte value.</param>
    public DicomNumericElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
    {
        Tag = tag;
        VR = vr;
        RawValue = value;
    }

    /// <inheritdoc />
    public IDicomElement ToOwned() =>
        new DicomNumericElement(Tag, VR, RawValue.ToArray());

    /// <summary>
    /// Get signed 16-bit integer (SS VR).
    /// </summary>
    public short? GetInt16()
    {
        if (RawValue.Length < 2)
            return null;

        return BinaryPrimitives.ReadInt16LittleEndian(RawValue.Span);
    }

    /// <summary>
    /// Get unsigned 16-bit integer (US VR).
    /// </summary>
    public ushort? GetUInt16()
    {
        if (RawValue.Length < 2)
            return null;

        return BinaryPrimitives.ReadUInt16LittleEndian(RawValue.Span);
    }

    /// <summary>
    /// Get signed 32-bit integer (SL VR).
    /// </summary>
    public int? GetInt32()
    {
        if (RawValue.Length < 4)
            return null;

        return BinaryPrimitives.ReadInt32LittleEndian(RawValue.Span);
    }

    /// <summary>
    /// Get unsigned 32-bit integer (UL VR).
    /// </summary>
    public uint? GetUInt32()
    {
        if (RawValue.Length < 4)
            return null;

        return BinaryPrimitives.ReadUInt32LittleEndian(RawValue.Span);
    }

    /// <summary>
    /// Get 32-bit float (FL VR).
    /// </summary>
    public float? GetFloat32()
    {
        if (RawValue.Length < 4)
            return null;

#if NETSTANDARD2_0
        return BitConverter.ToSingle(RawValue.ToArray(), 0);
#else
        return BitConverter.ToSingle(RawValue.Span);
#endif
    }

    /// <summary>
    /// Get 64-bit double (FD VR).
    /// </summary>
    public double? GetFloat64()
    {
        if (RawValue.Length < 8)
            return null;

#if NETSTANDARD2_0
        return BitConverter.ToDouble(RawValue.ToArray(), 0);
#else
        return BitConverter.ToDouble(RawValue.Span);
#endif
    }

    /// <summary>
    /// Get DicomTag (AT VR).
    /// </summary>
    public DicomTag? GetTag()
    {
        if (RawValue.Length < 4)
            return null;

        var group = BinaryPrimitives.ReadUInt16LittleEndian(RawValue.Span);
        var element = BinaryPrimitives.ReadUInt16LittleEndian(RawValue.Span.Slice(2));
        return new DicomTag(group, element);
    }

    /// <summary>
    /// Get array of signed 16-bit integers.
    /// </summary>
    public short[]? GetInt16Array()
    {
        if (IsEmpty || RawValue.Length % 2 != 0)
            return null;

        var count = RawValue.Length / 2;
        var result = new short[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadInt16LittleEndian(RawValue.Span.Slice(i * 2));
        }
        return result;
    }

    /// <summary>
    /// Get array of unsigned 16-bit integers.
    /// </summary>
    public ushort[]? GetUInt16Array()
    {
        if (IsEmpty || RawValue.Length % 2 != 0)
            return null;

        var count = RawValue.Length / 2;
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt16LittleEndian(RawValue.Span.Slice(i * 2));
        }
        return result;
    }

    /// <summary>
    /// Get array of signed 32-bit integers.
    /// </summary>
    public int[]? GetInt32Array()
    {
        if (IsEmpty || RawValue.Length % 4 != 0)
            return null;

        var count = RawValue.Length / 4;
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadInt32LittleEndian(RawValue.Span.Slice(i * 4));
        }
        return result;
    }

    /// <summary>
    /// Get array of unsigned 32-bit integers.
    /// </summary>
    public uint[]? GetUInt32Array()
    {
        if (IsEmpty || RawValue.Length % 4 != 0)
            return null;

        var count = RawValue.Length / 4;
        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(RawValue.Span.Slice(i * 4));
        }
        return result;
    }

    /// <summary>
    /// Get array of 32-bit floats.
    /// </summary>
    public float[]? GetFloat32Array()
    {
        if (IsEmpty || RawValue.Length % 4 != 0)
            return null;

        var count = RawValue.Length / 4;
        var result = new float[count];
#if NETSTANDARD2_0
        var bytes = RawValue.ToArray();
        for (int i = 0; i < count; i++)
        {
            result[i] = BitConverter.ToSingle(bytes, i * 4);
        }
#else
        for (int i = 0; i < count; i++)
        {
            result[i] = BitConverter.ToSingle(RawValue.Span.Slice(i * 4));
        }
#endif
        return result;
    }

    /// <summary>
    /// Get array of 64-bit doubles.
    /// </summary>
    public double[]? GetFloat64Array()
    {
        if (IsEmpty || RawValue.Length % 8 != 0)
            return null;

        var count = RawValue.Length / 8;
        var result = new double[count];
#if NETSTANDARD2_0
        var bytes = RawValue.ToArray();
        for (int i = 0; i < count; i++)
        {
            result[i] = BitConverter.ToDouble(bytes, i * 8);
        }
#else
        for (int i = 0; i < count; i++)
        {
            result[i] = BitConverter.ToDouble(RawValue.Span.Slice(i * 8));
        }
#endif
        return result;
    }
}
