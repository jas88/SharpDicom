using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates DA (Date) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// DA format is YYYYMMDD or partial forms YYYY or YYYYMM.
/// All characters must be digits 0-9.
/// Date components must represent a valid calendar date.
/// </remarks>
public sealed class DateValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-DA-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates DA (Date) format: YYYYMMDD";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.DA)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Must be 8, 6, or 4 characters (YYYYMMDD, YYYYMM, or YYYY)
        if (value.Length != 8 && value.Length != 6 && value.Length != 4)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidDateFormat,
                context.Tag,
                $"DA value must be YYYYMMDD, YYYYMM, or YYYY; got {value.Length} characters",
                "Use format YYYYMMDD (e.g., 20240115)");
        }

        // Validate all characters are digits
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    $"DA value contains invalid character '{(char)value[i]}' at position {i}",
                    "DA values must contain only digits 0-9");
            }
        }

        // Parse and validate date components
        int year = ParseDigits(value.Slice(0, 4));
        if (year < 1 || year > 9999)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidDateValue,
                context.Tag,
                $"Year {year} out of valid range (1-9999)",
                "Use a valid year between 1 and 9999");
        }

        if (value.Length >= 6)
        {
            int month = ParseDigits(value.Slice(4, 2));
            if (month < 1 || month > 12)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidDateValue,
                    context.Tag,
                    $"Month {month:D2} out of valid range (01-12)",
                    "Use a valid month between 01 and 12");
            }

            if (value.Length == 8)
            {
                int day = ParseDigits(value.Slice(6, 2));
                int maxDay = DateTime.DaysInMonth(year, month);
                if (day < 1 || day > maxDay)
                {
                    return ValidationIssue.Error(
                        ValidationCodes.InvalidDateValue,
                        context.Tag,
                        $"Day {day:D2} invalid for {year}-{month:D2} (valid range 1-{maxDay})",
                        $"Use a valid day between 01 and {maxDay:D2} for this month");
                }
            }
        }

        return null;
    }

    private static int ParseDigits(ReadOnlySpan<byte> digits)
    {
        int result = 0;
        for (int i = 0; i < digits.Length; i++)
            result = result * 10 + (digits[i] - '0');
        return result;
    }
}
