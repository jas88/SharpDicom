using System;
using SharpDicom.Data;

namespace SharpDicom.Validation.Rules;

/// <summary>
/// Validates PN (Person Name) values according to DICOM PS3.5 Section 6.2.
/// </summary>
/// <remarks>
/// PN structure:
/// - Up to 3 component groups separated by '=' (Alphabetic, Ideographic, Phonetic)
/// - Up to 5 components per group separated by '^' (Family, Given, Middle, Prefix, Suffix)
/// - Each component max 64 characters
/// - Total max 64 characters per component group
/// Many real-world files have non-conformant PN values, so warnings are preferred over errors.
/// </remarks>
public sealed class PersonNameValidator : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "VR-PN-FORMAT";

    /// <inheritdoc />
    public string Description => "Validates PN (Person Name) format";

    /// <inheritdoc />
    public ValidationIssue? Validate(in ElementValidationContext context)
    {
        if (context.DeclaredVR != DicomVR.PN)
            return null;

        var value = context.RawValue.Span;

        // Trim trailing space padding (0x20)
        while (value.Length > 0 && value[value.Length - 1] == 0x20)
            value = value.Slice(0, value.Length - 1);

        // Empty value is valid for Type 2 support
        if (value.Length == 0)
            return null;

        // Count component groups (separated by '=')
        int groupCount = 1;
        int lastGroupStart = 0;

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '=')
            {
                var groupSpan = value.Slice(lastGroupStart, i - lastGroupStart);
                var groupIssue = ValidateComponentGroup(context.Tag, groupSpan, groupCount);
                if (groupIssue != null)
                    return groupIssue;

                groupCount++;
                lastGroupStart = i + 1;

                if (groupCount > 3)
                {
                    return ValidationIssue.Warning(
                        ValidationCodes.InvalidPersonNameFormat,
                        context.Tag,
                        "PN value has more than 3 component groups (Alphabetic=Ideographic=Phonetic)",
                        "Use at most 3 groups separated by '='");
                }
            }
        }

        // Validate last group
        var lastGroup = value.Slice(lastGroupStart);
        var lastGroupIssue = ValidateComponentGroup(context.Tag, lastGroup, groupCount);
        if (lastGroupIssue != null)
            return lastGroupIssue;

        return null;
    }

    private static ValidationIssue? ValidateComponentGroup(DicomTag tag, ReadOnlySpan<byte> group, int groupNumber)
    {
        // Check total group length (max 64 chars per DICOM standard)
        if (group.Length > 64)
        {
            return ValidationIssue.Warning(
                ValidationCodes.ValueTooLong,
                tag,
                $"PN component group {groupNumber} exceeds 64 characters; got {group.Length}",
                "Shorten component group to 64 characters or less");
        }

        // Count components (separated by '^')
        int componentCount = 1;
        int componentStart = 0;

        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] == '^')
            {
                var componentLength = i - componentStart;
                if (componentLength > 64)
                {
                    return ValidationIssue.Warning(
                        ValidationCodes.ValueTooLong,
                        tag,
                        $"PN component in group {groupNumber} exceeds 64 characters; got {componentLength}",
                        "Shorten component to 64 characters or less");
                }

                componentCount++;
                componentStart = i + 1;

                if (componentCount > 5)
                {
                    return ValidationIssue.Warning(
                        ValidationCodes.InvalidPersonNameFormat,
                        tag,
                        $"PN group {groupNumber} has more than 5 components (Family^Given^Middle^Prefix^Suffix)",
                        "Use at most 5 components separated by '^'");
                }
            }
        }

        // Check last component length
        var lastComponentLength = group.Length - componentStart;
        if (lastComponentLength > 64)
        {
            return ValidationIssue.Warning(
                ValidationCodes.ValueTooLong,
                tag,
                $"PN component in group {groupNumber} exceeds 64 characters; got {lastComponentLength}",
                "Shorten component to 64 characters or less");
        }

        return null;
    }
}
