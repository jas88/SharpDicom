using System;

namespace SharpDicom.Data;

/// <summary>
/// Base interface for all DICOM element types.
/// </summary>
public interface IDicomElement
{
    /// <summary>
    /// The DICOM tag identifying this element.
    /// </summary>
    DicomTag Tag { get; }

    /// <summary>
    /// The Value Representation (VR) of this element.
    /// </summary>
    DicomVR VR { get; }

    /// <summary>
    /// The raw byte value of this element.
    /// </summary>
    ReadOnlyMemory<byte> RawValue { get; }

    /// <summary>
    /// The length of the raw value in bytes.
    /// </summary>
    int Length { get; }

    /// <summary>
    /// True if the element has no value (zero length).
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Create a deep copy of this element that owns its memory.
    /// </summary>
    /// <returns>A new element with copied data.</returns>
    IDicomElement ToOwned();
}
