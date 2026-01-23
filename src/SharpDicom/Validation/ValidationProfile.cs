using System;
using System.Collections.Generic;
using SharpDicom.Data;

namespace SharpDicom.Validation;

/// <summary>
/// Configuration for validation behavior during DICOM parsing.
/// </summary>
/// <remarks>
/// ValidationProfile combines a set of validation rules with behavior settings
/// to control how strictly DICOM files are validated. Use the static presets
/// (<see cref="Strict"/>, <see cref="Lenient"/>, <see cref="Permissive"/>, <see cref="None"/>)
/// for common scenarios or create custom profiles for specialized needs.
/// </remarks>
public sealed class ValidationProfile
{
    /// <summary>
    /// Gets or sets the profile name for logging and debugging.
    /// </summary>
    public string Name { get; init; } = "Custom";

    /// <summary>
    /// Gets or sets the validation rules to apply.
    /// </summary>
    /// <remarks>
    /// Rules are executed in order for each element. Each rule returns
    /// a ValidationIssue if validation fails, or null if the rule passes
    /// or does not apply to the element.
    /// </remarks>
    public IReadOnlyList<IValidationRule> Rules { get; init; } = Array.Empty<IValidationRule>();

    /// <summary>
    /// Gets or sets the default behavior for tags not in <see cref="TagOverrides"/>.
    /// </summary>
    public ValidationBehavior DefaultBehavior { get; init; } = ValidationBehavior.Validate;

    /// <summary>
    /// Gets or sets per-tag behavior overrides.
    /// </summary>
    /// <remarks>
    /// When validating a specific tag, if it exists in this dictionary,
    /// the override behavior is used instead of <see cref="DefaultBehavior"/>.
    /// This allows selective strict/lenient handling of specific tags.
    /// </remarks>
    public IReadOnlyDictionary<DicomTag, ValidationBehavior>? TagOverrides { get; init; }

    /// <summary>
    /// Gets a strict profile that validates with all rules and aborts on errors.
    /// </summary>
    /// <remarks>
    /// Use this profile for applications requiring strict DICOM compliance.
    /// Any validation error will cause parsing to abort with an exception.
    /// </remarks>
    public static ValidationProfile Strict { get; } = new()
    {
        Name = "Strict",
        Rules = StandardRules.All,
        DefaultBehavior = ValidationBehavior.Validate
    };

    /// <summary>
    /// Gets a lenient profile that validates with all rules but continues on errors.
    /// </summary>
    /// <remarks>
    /// Use this profile for applications that want to inspect all validation
    /// issues without aborting parsing. Issues are collected in the result
    /// and can be examined after parsing completes.
    /// </remarks>
    public static ValidationProfile Lenient { get; } = new()
    {
        Name = "Lenient",
        Rules = StandardRules.All,
        DefaultBehavior = ValidationBehavior.Warn
    };

    /// <summary>
    /// Gets a permissive profile that validates with minimal rules and continues on errors.
    /// </summary>
    /// <remarks>
    /// Use this profile for maximum compatibility with non-conformant files.
    /// Only structural rules (length limits) are checked, and errors are
    /// treated as warnings. This balances some safety with permissiveness.
    /// </remarks>
    public static ValidationProfile Permissive { get; } = new()
    {
        Name = "Permissive",
        Rules = StandardRules.StructuralOnly,
        DefaultBehavior = ValidationBehavior.Warn
    };

    /// <summary>
    /// Gets a profile that skips all validation for maximum performance.
    /// </summary>
    /// <remarks>
    /// Use this profile when processing trusted files or when validation
    /// has already been performed externally. No rules are executed and
    /// no issues are reported.
    /// </remarks>
    public static ValidationProfile None { get; } = new()
    {
        Name = "None",
        Rules = Array.Empty<IValidationRule>(),
        DefaultBehavior = ValidationBehavior.Skip
    };

    /// <summary>
    /// Gets the effective validation behavior for a specific tag.
    /// </summary>
    /// <param name="tag">The DICOM tag to get behavior for.</param>
    /// <returns>
    /// The behavior from <see cref="TagOverrides"/> if the tag has an override,
    /// otherwise <see cref="DefaultBehavior"/>.
    /// </returns>
    public ValidationBehavior GetBehavior(DicomTag tag)
    {
        if (TagOverrides != null && TagOverrides.TryGetValue(tag, out var behavior))
            return behavior;
        return DefaultBehavior;
    }
}
