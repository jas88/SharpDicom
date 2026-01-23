using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates AS (Age String) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// AS format is nnnD, nnnW, nnnM, or nnnY where:
/// - nnn is a 3-digit number (000-999)
/// - D = days, W = weeks, M = months, Y = years
/// Fixed 4 characters, no padding allowed.
/// </remarks>
public sealed class AgeStringValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-AS-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates AS (Age String) format: nnnD/W/M/Y";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.AS)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Must be exactly 4 characters
        if (value.Length != 4)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidAgeString,
                context.Tag,
                $"AS value must be exactly 4 characters (nnnD/W/M/Y); got {value.Length}",
                "Use format like 045Y for 45 years");
        }

        // First 3 characters must be digits
        for (int i = 0; i < 3; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    $"AS value position {i} must be a digit (0-9); got '{(char)value[i]}'",
                    "First 3 characters must be digits");
            }
        }

        // Fourth character must be D, W, M, or Y (case-sensitive)
        byte unit = value[3];
        if (unit != 'D' && unit != 'W' && unit != 'M' && unit != 'Y')
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidAgeString,
                context.Tag,
                $"AS unit must be D, W, M, or Y; got '{(char)unit}'",
                "Use D (days), W (weeks), M (months), or Y (years)");
        }

        return null;
    }
}
