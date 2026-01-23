namespace SharpDicom.Validation;

/// <summary>
/// Interface for pluggable validation rules.
/// </summary>
/// <remarks>
/// Validation rules implement specific checks against DICOM elements.
/// Rules are invoked during element parsing (if validation is enabled)
/// or during explicit validation passes.
/// </remarks>
public interface IValidationRule
{
    /// <summary>
    /// Gets the unique identifier for this rule.
    /// </summary>
    /// <remarks>
    /// Rule IDs should be descriptive and unique within a validation profile.
    /// Example: "VR-DA-FORMAT", "STRUCT-SEQUENCE", "IOD-TYPE1".
    /// </remarks>
    string RuleId { get; }

    /// <summary>
    /// Gets a human-readable description of what this rule checks.
    /// </summary>
    /// <remarks>
    /// This description is useful for documentation and debugging.
    /// Example: "Validates DA (Date) format: YYYYMMDD"
    /// </remarks>
    string Description { get; }

    /// <summary>
    /// Validates an element in context.
    /// </summary>
    /// <param name="context">The element and dataset context for validation.</param>
    /// <returns>
    /// A ValidationIssue if validation fails; null if the element is valid
    /// or if this rule does not apply to the element.
    /// </returns>
    /// <remarks>
    /// The context parameter is passed by reference (in) to avoid copying.
    /// Rules should return null if the rule doesn't apply (e.g., a DA validator
    /// receiving a non-DA element).
    /// </remarks>
    ValidationIssue? Validate(in ElementValidationContext context);
}
