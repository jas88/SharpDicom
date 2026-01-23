using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates TM (Time) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// TM format is HHMMSS.FFFFFF with partial forms allowed:
/// HH, HHMM, HHMMSS, with optional .FFFFFF fractional seconds.
/// Maximum 14 characters (16 with padding).
/// </remarks>
public sealed class TimeValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-TM-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates TM (Time) format: HHMMSS.FFFFFF";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.TM)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Maximum 14 characters (HHMMSS.FFFFFF)
        if (value.Length > 14)
        {
            return ValidationIssue.Error(
                ValidationCodes.ValueTooLong,
                context.Tag,
                $"TM value exceeds maximum 14 characters; got {value.Length}",
                "Use format HHMMSS.FFFFFF (max 14 chars)");
        }

        // Minimum 2 characters (HH)
        if (value.Length < 2)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidTimeFormat,
                context.Tag,
                $"TM value must be at least 2 characters (HH); got {value.Length}",
                "Use at least hours (HH)");
        }

        // Parse hours (always required)
        if (!AreDigits(value.Slice(0, 2)))
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidCharacter,
                context.Tag,
                "TM hours must be digits",
                "Hours must be 00-23");
        }

        int hour = ParseDigits(value.Slice(0, 2));
        if (hour < 0 || hour > 23)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidTimeValue,
                context.Tag,
                $"Hour {hour:D2} out of valid range (00-23)",
                "Use a valid hour between 00 and 23");
        }

        int pos = 2;

        // Parse minutes (optional)
        if (value.Length > pos)
        {
            if (value.Length < pos + 2)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    "TM minutes incomplete; expected 2 digits",
                    "Minutes must be 2 digits (00-59)");
            }

            if (!AreDigits(value.Slice(pos, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "TM minutes must be digits",
                    "Minutes must be 00-59");
            }

            int minute = ParseDigits(value.Slice(pos, 2));
            if (minute < 0 || minute > 59)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Minute {minute:D2} out of valid range (00-59)",
                    "Use a valid minute between 00 and 59");
            }

            pos += 2;
        }

        // Parse seconds (optional)
        if (value.Length > pos)
        {
            // Check for decimal point without seconds
            if (value[pos] == '.')
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    "TM fractional seconds require seconds portion",
                    "Include seconds (HHMMSS) before fractional part");
            }

            if (value.Length < pos + 2)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    "TM seconds incomplete; expected 2 digits",
                    "Seconds must be 2 digits (00-59)");
            }

            if (!AreDigits(value.Slice(pos, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "TM seconds must be digits",
                    "Seconds must be 00-59");
            }

            int second = ParseDigits(value.Slice(pos, 2));
            if (second < 0 || second > 59)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Second {second:D2} out of valid range (00-59)",
                    "Use a valid second between 00 and 59");
            }

            pos += 2;
        }

        // Parse fractional seconds (optional)
        if (value.Length > pos)
        {
            if (value[pos] != '.')
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    $"TM expected '.' for fractional seconds, got '{(char)value[pos]}'",
                    "Use '.' to separate fractional seconds");
            }

            pos++;

            // Must have at least one digit after decimal
            if (value.Length <= pos)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    "TM fractional seconds missing digits after '.'",
                    "Include at least one digit after decimal point");
            }

            // Validate remaining digits (up to 6)
            int fracLength = value.Length - pos;
            if (fracLength > 6)
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    $"TM fractional seconds exceed 6 digits; got {fracLength}",
                    "Use at most 6 digits for fractional seconds");
            }

            if (!AreDigits(value.Slice(pos, fracLength)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "TM fractional seconds must be digits",
                    "Fractional seconds must be digits only");
            }
        }

        return null;
    }

    private static bool AreDigits(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] < '0' || bytes[i] > '9')
                return false;
        }
        return true;
    }

    private static int ParseDigits(ReadOnlySpan<byte> digits)
    {
        int result = 0;
        for (int i = 0; i < digits.Length; i++)
            result = result * 10 + (digits[i] - '0');
        return result;
    }
}
