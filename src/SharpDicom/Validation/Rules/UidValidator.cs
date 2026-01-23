using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates UI (Unique Identifier) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// UID constraints:
/// - Maximum 64 characters
/// - Only digits (0-9) and dots (.)
/// - No empty components (consecutive dots)
/// - No leading zeros in components (except "0" itself)
/// - No leading/trailing dots
/// Null padding (0x00) is trimmed before validation.
/// </remarks>
public sealed class UidValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-UI-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates UI (Unique Identifier) format";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.UI)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing null padding (0x00) - UI uses null padding
        while (value.Length > 0 && value[value.Length - 1] == 0x00)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Maximum 64 characters
        if (value.Length > 64)
        {
            return ValidationIssue.Error(
                ValidationCodes.ValueTooLong,
                context.Tag,
                $"UID exceeds maximum 64 characters; got {value.Length}",
                "Shorten UID to 64 characters or less");
        }

        // Check for leading dot
        if (value[0] == '.')
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidUidFormat,
                context.Tag,
                "UID cannot start with a dot",
                "Remove leading dot from UID");
        }

        // Check for trailing dot
        if (value[value.Length - 1] == '.')
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidUidFormat,
                context.Tag,
                "UID cannot end with a dot",
                "Remove trailing dot from UID");
        }

        // Validate character by character, tracking components
        int componentStart = 0;
        int componentLength = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '.')
            {
                // End of component
                if (componentLength == 0)
                {
                    return ValidationIssue.Error(
                        ValidationCodes.InvalidUidFormat,
                        context.Tag,
                        "UID contains empty component (consecutive dots)",
                        "Remove consecutive dots from UID");
                }

                // Check for leading zero (only "0" itself is allowed)
                if (componentLength > 1 && value[componentStart] == '0')
                {
                    return ValidationIssue.Error(
                        ValidationCodes.InvalidUidFormat,
                        context.Tag,
                        "UID component has leading zero (not allowed except for single '0')",
                        "Remove leading zeros from UID components");
                }

                componentStart = i + 1;
                componentLength = 0;
            }
            else if (value[i] >= '0' && value[i] <= '9')
            {
                componentLength++;
            }
            else
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    $"UID contains invalid character '{(char)value[i]}' at position {i}",
                    "UIDs may only contain digits (0-9) and dots (.)");
            }
        }

        return null;
    }
}
