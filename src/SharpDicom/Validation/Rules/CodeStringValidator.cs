using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates CS (Code String) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// CS constraints:
/// - Allowed characters: A-Z, 0-9, space, underscore
/// - No lowercase letters (common real-world issue)
/// - Maximum 16 characters per value
/// - Leading/trailing spaces are trimmed for validation
/// Warns on lowercase rather than erroring since many real files have issues.
/// </remarks>
public sealed class CodeStringValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-CS-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates CS (Code String) format: A-Z, 0-9, space, underscore";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.CS)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Process each value in a potentially multi-valued string
        int valueStart = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '\\')
            {
                var singleValue = value.Slice(valueStart, i - valueStart);
                var issue = ValidateSingleValue(context.Tag, singleValue);
                if (issue != null)
                    return issue;
                valueStart = i + 1;
            }
        }

        return null;
    }

    private static ValidationIssue? ValidateSingleValue(DicomTag tag, ReadOnlySpan<byte> value)
    {
        // Trim leading/trailing spaces for validation
        while (value.Length > 0 && value[0] == 0x20)
            value = value.Slice(1);
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty after trimming is valid
        if (value.Length == 0)
            return null;

        // Maximum 16 characters per value
        if (value.Length > 16)
        {
            return ValidationIssue.Warning(
                ValidationCodes.ValueTooLong,
                tag,
                $"CS value exceeds maximum 16 characters; got {value.Length}",
                "Shorten code string to 16 characters or less");
        }

        bool hasLowercase = false;
        int lowercasePosition = -1;

        for (int i = 0; i < value.Length; i++)
        {
            byte c = value[i];

            // Valid: A-Z
            if (c >= 'A' && c <= 'Z')
                continue;

            // Valid: 0-9
            if (c >= '0' && c <= '9')
                continue;

            // Valid: space, underscore
            if (c == ' ' || c == '_')
                continue;

            // Lowercase: warn but don't error (common issue)
            if (c >= 'a' && c <= 'z')
            {
                if (!hasLowercase)
                {
                    hasLowercase = true;
                    lowercasePosition = i;
                }
                continue;
            }

            // Invalid character
            return ValidationIssue.Warning(
                ValidationCodes.InvalidCodeString,
                tag,
                $"CS value contains invalid character '{(char)c}' at position {i}",
                "CS allows only A-Z, 0-9, space, and underscore");
        }

        if (hasLowercase)
        {
            return ValidationIssue.Warning(
                ValidationCodes.InvalidCodeString,
                tag,
                $"CS value contains lowercase letters (first at position {lowercasePosition}); should be uppercase",
                "Convert to uppercase for DICOM compliance");
        }

        return null;
    }
}
