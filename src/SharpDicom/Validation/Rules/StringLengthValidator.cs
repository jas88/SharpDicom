using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates string VR values against their maximum length constraints.
/// </summary>
/// <remarks>
/// Uses DicomVRInfo.MaxLength to check per-VR limits:
/// - AE: 16, AS: 4, CS: 16, DA: 8, DS: 16, DT: 26, IS: 12, LO: 64, etc.
/// Does not check UI (handled by UidValidator) or unlimited VRs (UC, UR, UT, LT).
/// </remarks>
public sealed class StringLengthValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-LENGTH";

    /// <inheritdoc />
    public string Description => "Validates string values against VR maximum length";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        var vr = context.DeclaredVR;

        // Only check string VRs
        if (!vr.IsStringVR)
            return null;

        // Skip unlimited length VRs
        if (vr == DicomVR.UC || vr == DicomVR.UR || vr == DicomVR.UT || vr == DicomVR.LT)
            return null;

        // Skip UI (handled by UidValidator with null trimming)
        if (vr == DicomVR.UI)
            return null;

        var info = DicomVRInfo.GetInfo(vr);
        var value = context.RawValue.Span;

        // Trim trailing padding
        byte paddingByte = info.PaddingByte;
        while (value.Length > 0 && value[value.Length - 1] == paddingByte)
            value = value.Slice(0, value.Length - 1);

        // Empty is valid
        if (value.Length == 0)
            return null;

        // For multi-valued VRs, check individual values
        if (info.MultiValueDelimiter.HasValue)
        {
            byte delimiter = (byte)info.MultiValueDelimiter.Value;
            int valueStart = 0;

            for (int i = 0; i <= value.Length; i++)
            {
                if (i == value.Length || value[i] == delimiter)
                {
                    int singleValueLength = i - valueStart;

                    // Trim spaces from individual value for checking
                    var singleValue = value.Slice(valueStart, singleValueLength);
                    while (singleValue.Length > 0 && singleValue[0] == 0x20)
                        singleValue = singleValue.Slice(1);
                    while (singleValue.Length > 0 && singleValue[singleValue.Length - 1] == 0x20)
                        singleValue = singleValue.Slice(0, singleValue.Length - 1);

                    if (singleValue.Length > info.MaxLength)
                    {
                        return ValidationIssue.Warning(
                            ValidationCodes.ValueTooLong,
                            context.Tag,
                            $"{vr} value exceeds maximum {info.MaxLength} characters; got {singleValue.Length}",
                            $"Shorten value to {info.MaxLength} characters or less");
                    }

                    valueStart = i + 1;
                }
            }
        }
        else
        {
            // Single-valued VR - check total length
            if (value.Length > info.MaxLength)
            {
                return ValidationIssue.Warning(
                    ValidationCodes.ValueTooLong,
                    context.Tag,
                    $"{vr} value exceeds maximum {info.MaxLength} characters; got {value.Length}",
                    $"Shorten value to {info.MaxLength} characters or less");
            }
        }

        return null;
    }
}
