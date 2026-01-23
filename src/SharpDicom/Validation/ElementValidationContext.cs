using System;
using SharpDicom.Data;

namespace SharpDicom.Validation;

/// <summary>
/// Context information passed to validation rules for element validation.
/// </summary>
/// <remarks>
/// ElementValidationContext provides all the information a validation rule
/// might need to validate a DICOM element. The context is passed by reference
/// (via 'in' parameter) to avoid copying the struct.
/// </remarks>
public readonly struct ElementValidationContext
{
    /// <summary>
    /// Gets the DICOM tag being validated.
    /// </summary>
    public DicomTag Tag { get; init; }

    /// <summary>
    /// Gets the VR declared in the file (explicit VR) or resolved from dictionary (implicit VR).
    /// </summary>
    public DicomVR DeclaredVR { get; init; }

    /// <summary>
    /// Gets the VR expected from the DICOM dictionary, if known.
    /// </summary>
    /// <remarks>
    /// May be null for private tags or unknown tags not in the dictionary.
    /// For multi-VR tags, this is the primary/default VR.
    /// </remarks>
    public DicomVR? ExpectedVR { get; init; }

    /// <summary>
    /// Gets the raw bytes of the element value.
    /// </summary>
    public ReadOnlyMemory<byte> RawValue { get; init; }

    /// <summary>
    /// Gets the containing dataset for conditional validation checks.
    /// </summary>
    /// <remarks>
    /// Used by validation rules that need to check other elements in the dataset
    /// (e.g., Type 1C/2C conditional requirements).
    /// </remarks>
    public DicomDataset Dataset { get; init; }

    /// <summary>
    /// Gets the character encoding for this element's context.
    /// </summary>
    /// <remarks>
    /// Used for validating string VRs with character set constraints.
    /// </remarks>
    public DicomEncoding Encoding { get; init; }

    /// <summary>
    /// Gets the stream position where this element was read.
    /// </summary>
    /// <remarks>
    /// Useful for error reporting and debugging.
    /// </remarks>
    public long StreamPosition { get; init; }

    /// <summary>
    /// Gets whether this is a private tag (odd group number).
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Gets the private creator identifier, if this is a private tag.
    /// </summary>
    /// <remarks>
    /// Null for standard tags or private creator elements themselves.
    /// For private data elements, this is the creator string from (gggg,00xx).
    /// </remarks>
    public string? PrivateCreator { get; init; }
}
