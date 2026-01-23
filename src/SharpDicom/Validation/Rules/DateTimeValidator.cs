using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates DT (Date Time) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// DT format is YYYYMMDDHHMMSS.FFFFFF+ZZZZ or partial forms.
/// Combines date (YYYYMMDD) + time (HHMMSS.FFFFFF) + optional timezone (+/-HHMM).
/// Maximum 26 characters.
/// </remarks>
public sealed class DateTimeValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-DT-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates DT (Date Time) format: YYYYMMDDHHMMSS.FFFFFF+ZZZZ";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.DT)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Maximum 26 characters
        if (value.Length > 26)
        {
            return ValidationIssue.Error(
                ValidationCodes.ValueTooLong,
                context.Tag,
                $"DT value exceeds maximum 26 characters; got {value.Length}",
                "Use format YYYYMMDDHHMMSS.FFFFFF+ZZZZ (max 26 chars)");
        }

        // Minimum 4 characters (YYYY)
        if (value.Length < 4)
        {
            return ValidationIssue.Error(
                ValidationCodes.InvalidDateFormat,
                context.Tag,
                $"DT value must be at least 4 characters (YYYY); got {value.Length}",
                "Use at least year (YYYY)");
        }

        // Find timezone offset position (+ or -)
        int tzPos = -1;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (value[i] == '+' || value[i] == '-')
            {
                tzPos = i;
                break;
            }
        }

        // Split into datetime part and timezone part
        ReadOnlySpan<byte> dtPart = tzPos >= 0 ? value.Slice(0, tzPos) : value;
        ReadOnlySpan<byte> tzPart = tzPos >= 0 ? value.Slice(tzPos) : ReadOnlySpan<byte>.Empty;

        // Validate date portion (YYYY required, YYYYMM, YYYYMMDD optional)
        if (dtPart.Length >= 4)
        {
            if (!AreDigits(dtPart.Slice(0, 4)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT year must be digits",
                    "Year must be 4 digits (0001-9999)");
            }

            int year = ParseDigits(dtPart.Slice(0, 4));
            if (year < 1 || year > 9999)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidDateValue,
                    context.Tag,
                    $"Year {year} out of valid range (1-9999)",
                    "Use a valid year between 1 and 9999");
            }
        }

        if (dtPart.Length >= 6)
        {
            if (!AreDigits(dtPart.Slice(4, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT month must be digits",
                    "Month must be 2 digits (01-12)");
            }

            int month = ParseDigits(dtPart.Slice(4, 2));
            if (month < 1 || month > 12)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidDateValue,
                    context.Tag,
                    $"Month {month:D2} out of valid range (01-12)",
                    "Use a valid month between 01 and 12");
            }

            if (dtPart.Length >= 8)
            {
                if (!AreDigits(dtPart.Slice(6, 2)))
                {
                    return ValidationIssue.Error(
                        ValidationCodes.InvalidCharacter,
                        context.Tag,
                        "DT day must be digits",
                        "Day must be 2 digits (01-31)");
                }

                int day = ParseDigits(dtPart.Slice(6, 2));
                int year = ParseDigits(dtPart.Slice(0, 4));
                int monthForDay = ParseDigits(dtPart.Slice(4, 2));
                int maxDay = DateTime.DaysInMonth(year, monthForDay);
                if (day < 1 || day > maxDay)
                {
                    return ValidationIssue.Error(
                        ValidationCodes.InvalidDateValue,
                        context.Tag,
                        $"Day {day:D2} invalid for {year}-{monthForDay:D2} (valid range 1-{maxDay})",
                        $"Use a valid day between 01 and {maxDay:D2}");
                }
            }
        }

        // Validate time portion (optional)
        if (dtPart.Length >= 10)
        {
            if (!AreDigits(dtPart.Slice(8, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT hours must be digits",
                    "Hours must be 2 digits (00-23)");
            }

            int hour = ParseDigits(dtPart.Slice(8, 2));
            if (hour < 0 || hour > 23)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Hour {hour:D2} out of valid range (00-23)",
                    "Use a valid hour between 00 and 23");
            }
        }

        if (dtPart.Length >= 12)
        {
            if (!AreDigits(dtPart.Slice(10, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT minutes must be digits",
                    "Minutes must be 2 digits (00-59)");
            }

            int minute = ParseDigits(dtPart.Slice(10, 2));
            if (minute < 0 || minute > 59)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Minute {minute:D2} out of valid range (00-59)",
                    "Use a valid minute between 00 and 59");
            }
        }

        if (dtPart.Length >= 14)
        {
            if (!AreDigits(dtPart.Slice(12, 2)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT seconds must be digits",
                    "Seconds must be 2 digits (00-59)");
            }

            int second = ParseDigits(dtPart.Slice(12, 2));
            if (second < 0 || second > 59)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Second {second:D2} out of valid range (00-59)",
                    "Use a valid second between 00 and 59");
            }
        }

        // Validate fractional seconds (optional)
        if (dtPart.Length > 14)
        {
            if (dtPart[14] != '.')
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    $"DT expected '.' for fractional seconds, got '{(char)dtPart[14]}'",
                    "Use '.' to separate fractional seconds");
            }

            if (dtPart.Length <= 15)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    "DT fractional seconds missing digits after '.'",
                    "Include at least one digit after decimal point");
            }

            int fracLength = dtPart.Length - 15;
            if (fracLength > 6)
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    $"DT fractional seconds exceed 6 digits; got {fracLength}",
                    "Use at most 6 digits for fractional seconds");
            }

            if (!AreDigits(dtPart.Slice(15, fracLength)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT fractional seconds must be digits",
                    "Fractional seconds must be digits only");
            }
        }

        // Validate timezone (optional)
        if (tzPart.Length > 0)
        {
            // Must be +HHMM or -HHMM (5 chars)
            if (tzPart.Length != 5)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeFormat,
                    context.Tag,
                    $"DT timezone must be +/-HHMM (5 chars); got {tzPart.Length}",
                    "Use timezone format +HHMM or -HHMM");
            }

            if (!AreDigits(tzPart.Slice(1, 4)))
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "DT timezone offset must be digits",
                    "Timezone must be +HHMM or -HHMM");
            }

            int tzHour = ParseDigits(tzPart.Slice(1, 2));
            int tzMin = ParseDigits(tzPart.Slice(3, 2));

            if (tzHour < 0 || tzHour > 14)
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Timezone hour {tzHour:D2} out of typical range (00-14)",
                    "Timezone hours are typically 00-14");
            }

            if (tzMin < 0 || tzMin > 59)
            {
                return ValidationIssue.Error(
                    ValidationCodes.InvalidTimeValue,
                    context.Tag,
                    $"Timezone minute {tzMin:D2} out of valid range (00-59)",
                    "Timezone minutes must be 00-59");
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
