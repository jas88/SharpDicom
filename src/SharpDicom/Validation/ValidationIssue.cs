using System;
using SharpDicom.Data;

namespace SharpDicom.Validation;

/// <summary>
/// Represents a validation issue with full diagnostic context.
/// </summary>
/// <remarks>
/// ValidationIssue captures all information needed to diagnose and potentially
/// fix validation failures. Issues can be created with varying levels of detail
/// depending on the validation context.
/// </remarks>
/// <param name="Code">Unique error code (e.g., "DICOM-001").</param>
/// <param name="Severity">The severity level of this issue.</param>
/// <param name="Tag">The associated DICOM tag, if element-level.</param>
/// <param name="DeclaredVR">The VR declared in the file.</param>
/// <param name="ExpectedVR">The VR expected from the dictionary.</param>
/// <param name="Position">The stream position where the issue was detected.</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="SuggestedFix">Suggested remediation for the issue.</param>
/// <param name="RawValue">The raw bytes of the value for inspection.</param>
public readonly record struct ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    DicomTag? Tag,
    DicomVR? DeclaredVR,
    DicomVR? ExpectedVR,
    long? Position,
    string Message,
    string? SuggestedFix,
    ReadOnlyMemory<byte> RawValue)
{
    /// <summary>
    /// Creates an error-level issue with minimal context.
    /// </summary>
    /// <param name="code">The validation code (e.g., "DICOM-001").</param>
    /// <param name="tag">The associated DICOM tag.</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="suggestedFix">Optional suggested fix.</param>
    /// <returns>A new ValidationIssue with Error severity.</returns>
    public static ValidationIssue Error(string code, DicomTag tag, string message, string? suggestedFix = null)
        => new(code, ValidationSeverity.Error, tag, null, null, null, message, suggestedFix, default);

    /// <summary>
    /// Creates a warning-level issue with minimal context.
    /// </summary>
    /// <param name="code">The validation code (e.g., "DICOM-003").</param>
    /// <param name="tag">The associated DICOM tag.</param>
    /// <param name="message">Human-readable warning description.</param>
    /// <param name="suggestedFix">Optional suggested fix.</param>
    /// <returns>A new ValidationIssue with Warning severity.</returns>
    public static ValidationIssue Warning(string code, DicomTag tag, string message, string? suggestedFix = null)
        => new(code, ValidationSeverity.Warning, tag, null, null, null, message, suggestedFix, default);

    /// <summary>
    /// Creates an info-level issue with minimal context.
    /// </summary>
    /// <param name="code">The validation code (e.g., "DICOM-025").</param>
    /// <param name="tag">The associated DICOM tag.</param>
    /// <param name="message">Human-readable information.</param>
    /// <param name="suggestedFix">Optional suggested fix.</param>
    /// <returns>A new ValidationIssue with Info severity.</returns>
    public static ValidationIssue Info(string code, DicomTag tag, string message, string? suggestedFix = null)
        => new(code, ValidationSeverity.Info, tag, null, null, null, message, suggestedFix, default);

    /// <summary>
    /// Creates a validation issue with full diagnostic context.
    /// </summary>
    /// <param name="code">The validation code (e.g., "DICOM-003").</param>
    /// <param name="severity">The severity level.</param>
    /// <param name="tag">The associated DICOM tag, if element-level.</param>
    /// <param name="declaredVR">The VR declared in the file.</param>
    /// <param name="expectedVR">The VR expected from the dictionary.</param>
    /// <param name="position">The stream position where the issue was detected.</param>
    /// <param name="message">Human-readable description of the issue.</param>
    /// <param name="suggestedFix">Suggested remediation for the issue.</param>
    /// <param name="rawValue">The raw bytes of the value for inspection.</param>
    /// <returns>A new ValidationIssue with full context.</returns>
    public static ValidationIssue Create(
        string code,
        ValidationSeverity severity,
        DicomTag? tag,
        DicomVR? declaredVR,
        DicomVR? expectedVR,
        long? position,
        string message,
        string? suggestedFix,
        ReadOnlyMemory<byte> rawValue)
        => new(code, severity, tag, declaredVR, expectedVR, position, message, suggestedFix, rawValue);

    /// <summary>
    /// Returns a string representation of this issue for logging.
    /// </summary>
    /// <returns>A formatted string describing the issue.</returns>
    public override string ToString()
    {
        var tagStr = Tag.HasValue ? $" at {Tag.Value}" : "";
        var posStr = Position.HasValue ? $" (position {Position.Value})" : "";
        return $"[{Severity}] {Code}{tagStr}: {Message}{posStr}";
    }
}
