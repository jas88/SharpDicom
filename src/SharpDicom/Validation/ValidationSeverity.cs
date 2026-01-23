namespace SharpDicom.Validation;

/// <summary>
/// Severity level for validation issues.
/// </summary>
/// <remarks>
/// Validation issues are categorized by severity to enable filtering and
/// appropriate handling during parsing or validation operations.
/// </remarks>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational only (cosmetic issues like trailing spaces, deprecated attributes).
    /// </summary>
    /// <remarks>
    /// Info-level issues do not indicate data quality problems and
    /// typically require no action. Examples include:
    /// - Retired attributes present
    /// - Minor cosmetic issues like inconsistent padding
    /// </remarks>
    Info = 0,

    /// <summary>
    /// Recoverable issue (VR mismatch, format issues).
    /// </summary>
    /// <remarks>
    /// Warning-level issues indicate data that may not conform to the DICOM
    /// standard but can still be processed. Examples include:
    /// - VR mismatch between file and dictionary
    /// - Invalid date/time format
    /// - Value exceeds VR maximum length
    /// </remarks>
    Warning = 1,

    /// <summary>
    /// Fatal issue (structural corruption, cannot proceed).
    /// </summary>
    /// <remarks>
    /// Error-level issues indicate problems that prevent correct interpretation
    /// of the data. Examples include:
    /// - Truncated element (length exceeds available data)
    /// - Invalid sequence structure
    /// - Missing required attributes (Type 1)
    /// </remarks>
    Error = 2
}
