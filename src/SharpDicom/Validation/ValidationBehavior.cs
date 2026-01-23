namespace SharpDicom.Validation;

/// <summary>
/// Defines how validation issues are handled during DICOM parsing.
/// </summary>
public enum ValidationBehavior
{
    /// <summary>
    /// Run validation rules and abort on Error-level issues.
    /// </summary>
    /// <remarks>
    /// This is the strictest mode. Validation rules are executed and any
    /// Error-level issue will cause parsing to abort with an exception.
    /// Warnings and info-level issues are still collected if enabled.
    /// </remarks>
    Validate,

    /// <summary>
    /// Run validation rules but continue on errors.
    /// </summary>
    /// <remarks>
    /// All validation rules are executed and issues are collected or
    /// reported via callback, but parsing continues regardless of
    /// severity. This mode is useful for inspecting files without
    /// strict enforcement.
    /// </remarks>
    Warn,

    /// <summary>
    /// Skip validation entirely for maximum performance.
    /// </summary>
    /// <remarks>
    /// No validation rules are executed. This is the fastest mode
    /// and should be used when processing trusted files or when
    /// validation has already been performed.
    /// </remarks>
    Skip
}
