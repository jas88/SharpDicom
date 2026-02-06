using SharpDicom.Data;

namespace SharpDicom.Cli.Diagnostics;

/// <summary>
/// Describes a single repair action applied to a DICOM dataset.
/// </summary>
/// <param name="Tag">The DICOM tag that was modified.</param>
/// <param name="Description">Human-readable description of the fix.</param>
/// <param name="OldValue">The original value (null if element was added).</param>
/// <param name="NewValue">The new value (null if element was removed).</param>
public readonly record struct FixAction(
    DicomTag Tag,
    string Description,
    string? OldValue,
    string? NewValue);
