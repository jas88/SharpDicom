using System;

namespace SharpDicom.Data;

/// <summary>
/// DICOM element for binary Value Representations.
/// Covers: OB, OD, OF, OL, OW, UN
/// </summary>
public sealed class DicomBinaryElement : IDicomElement
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
    /// Initializes a new instance of the <see cref="DicomBinaryElement"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="vr">The Value Representation.</param>
    /// <param name="value">The raw byte value.</param>
    public DicomBinaryElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
    {
        Tag = tag;
        VR = vr;
        RawValue = value;
    }

    /// <inheritdoc />
    public IDicomElement ToOwned() =>
        new DicomBinaryElement(Tag, VR, RawValue.ToArray());

    /// <summary>
    /// Get the binary data.
    /// </summary>
    public ReadOnlyMemory<byte> GetBytes() => RawValue;
}
