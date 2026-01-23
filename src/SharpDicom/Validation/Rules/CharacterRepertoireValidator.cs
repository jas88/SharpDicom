using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates character repertoire constraints for VR-specific string types.
/// </summary>
/// <remarks>
/// Per-VR character validation based on DICOM PS3.5 Section 6.2:
/// - AE: default repertoire, no backslash or control chars
/// - CS: A-Z, 0-9, space, underscore (handled by CodeStringValidator)
/// - DA: 0-9 only
/// - DS: 0-9, +, -, E, e, ., space
/// - IS: 0-9, +, -, space
/// - TM: 0-9, ., space
/// - UI: 0-9, . (handled by UidValidator)
/// Passes through for unconstrained string VRs (LO, LT, PN, SH, ST, UC, UR, UT).
/// </remarks>
public sealed class CharacterRepertoireValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-CHARS";

    /// <inheritdoc />
    public string Description => "Validates VR-specific character repertoire constraints";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        var vr = context.DeclaredVR;

        // Only check string VRs
        if (!vr.IsStringVR)
            return null;

        // These VRs have dedicated validators or no character restrictions
        // UI: UidValidator, CS: CodeStringValidator, DA/TM/DT/AS: dedicated validators
        // LO, LT, PN, SH, ST, UC, UR, UT: no strict character constraints
        if (vr == DicomVR.UI || vr == DicomVR.CS || vr == DicomVR.DA ||
            vr == DicomVR.TM || vr == DicomVR.DT || vr == DicomVR.AS)
            return null;

        // These have no character restrictions (general text)
        if (vr == DicomVR.LO || vr == DicomVR.LT || vr == DicomVR.PN ||
            vr == DicomVR.SH || vr == DicomVR.ST || vr == DicomVR.UC ||
            vr == DicomVR.UR || vr == DicomVR.UT)
            return null;

        var value = context.RawValue.Span;
        var info = DicomVRInfo.GetInfo(vr);

        // AE: Check for space-only values before trimming (invalid per DICOM)
        if (vr == DicomVR.AE && value.Length > 0)
        {
            bool allSpaces = true;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != ' ')
                {
                    allSpaces = false;
                    break;
                }
            }

            if (allSpaces)
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidCharacter,
                    context.Tag,
                    "AE value contains only spaces which is not a valid Application Entity",
                    "Provide a non-empty Application Entity name");
            }
        }

        // Trim trailing padding
        while (value.Length > 0 && value[value.Length - 1] == info.PaddingByte)
            value = value.Slice(0, value.Length - 1);

        if (value.Length == 0)
            return null;

        // AE: Application Entity - default repertoire, no backslash or control chars
        if (vr == DicomVR.AE)
            return ValidateAE(context.Tag, value);

        // DS: Decimal String - 0-9, +, -, E, e, ., space
        if (vr == DicomVR.DS)
            return ValidateDS(context.Tag, value);

        // IS: Integer String - 0-9, +, -, space
        if (vr == DicomVR.IS)
            return ValidateIS(context.Tag, value);

        return null;
    }

    private static ValidationIssue? ValidateAE(DicomTag tag, ReadOnlySpan<byte> value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            byte c = value[i];

            // Check for backslash (not allowed)
            if (c == '\\')
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidCharacter,
                    tag,
                    "AE value contains backslash which is not allowed",
                    "Remove backslash from Application Entity value");
            }

            // Check for control characters (0x00-0x1F except for value separator checking)
            if (c < 0x20 && c != 0x1B) // Allow ESC for ISO 2022
            {
                return ValidationIssue.Warning(
                    ValidationCodes.InvalidCharacter,
                    tag,
                    $"AE value contains control character 0x{c:X2} at position {i}",
                    "Remove control characters from Application Entity value");
            }
        }

        return null;
    }

    private static ValidationIssue? ValidateDS(DicomTag tag, ReadOnlySpan<byte> value)
    {
        // Process each value in a multi-valued string
        int valueStart = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '\\')
            {
                var singleValue = value.Slice(valueStart, i - valueStart);
                var issue = ValidateSingleDS(tag, singleValue);
                if (issue != null)
                    return issue;
                valueStart = i + 1;
            }
        }
        return null;
    }

    private static ValidationIssue? ValidateSingleDS(DicomTag tag, ReadOnlySpan<byte> value)
    {
        // Trim leading/trailing spaces
        while (value.Length > 0 && value[0] == ' ')
            value = value.Slice(1);
        while (value.Length > 0 && value[value.Length - 1] == ' ')
            value = value.Slice(0, value.Length - 1);

        if (value.Length == 0)
            return null;

        for (int i = 0; i < value.Length; i++)
        {
            byte c = value[i];

            // Valid: 0-9, +, -, E, e, ., space (embedded spaces technically not allowed but tolerate)
            if ((c >= '0' && c <= '9') || c == '+' || c == '-' ||
                c == 'E' || c == 'e' || c == '.' || c == ' ')
                continue;

            return ValidationIssue.Warning(
                ValidationCodes.InvalidDecimalString,
                tag,
                $"DS value contains invalid character '{(char)c}' at position {i}",
                "DS allows only 0-9, +, -, E, e, ., and leading/trailing spaces");
        }

        return null;
    }

    private static ValidationIssue? ValidateIS(DicomTag tag, ReadOnlySpan<byte> value)
    {
        // Process each value in a multi-valued string
        int valueStart = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '\\')
            {
                var singleValue = value.Slice(valueStart, i - valueStart);
                var issue = ValidateSingleIS(tag, singleValue);
                if (issue != null)
                    return issue;
                valueStart = i + 1;
            }
        }
        return null;
    }

    private static ValidationIssue? ValidateSingleIS(DicomTag tag, ReadOnlySpan<byte> value)
    {
        // Trim leading/trailing spaces
        while (value.Length > 0 && value[0] == ' ')
            value = value.Slice(1);
        while (value.Length > 0 && value[value.Length - 1] == ' ')
            value = value.Slice(0, value.Length - 1);

        if (value.Length == 0)
            return null;

        for (int i = 0; i < value.Length; i++)
        {
            byte c = value[i];

            // Valid: 0-9, +, -, space
            if ((c >= '0' && c <= '9') || c == '+' || c == '-' || c == ' ')
                continue;

            return ValidationIssue.Warning(
                ValidationCodes.InvalidIntegerString,
                tag,
                $"IS value contains invalid character '{(char)c}' at position {i}",
                "IS allows only 0-9, +, -, and leading/trailing spaces");
        }

        return null;
    }
}
