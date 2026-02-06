using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SharpDicom.Data;
using SharpDicom.Deidentification;

namespace SharpDicom.Cli.Diagnostics;

/// <summary>
/// Options controlling which fix categories to apply.
/// </summary>
internal sealed class FixOptions
{
    /// <summary>Fix UIDs that do not conform to DICOM UID rules.</summary>
    public bool FixInvalidUids { get; init; } = true;

    /// <summary>Fix dates that use separators or non-standard formats.</summary>
    public bool FixInvalidDates { get; init; } = true;

    /// <summary>Fix times that contain non-standard characters.</summary>
    public bool FixInvalidTimes { get; init; } = true;

    /// <summary>Remove elements that fail strict validation. Opt-in because destructive.</summary>
    public bool RemoveInvalidElements { get; init; }

    /// <summary>Add missing SpecificCharacterSet when non-ASCII data is detected.</summary>
    public bool FixCharacterEncoding { get; init; } = true;
}

/// <summary>
/// Applies automated repairs to a DICOM dataset.
/// </summary>
internal static class DicomFixer
{
    /// <summary>
    /// Applies all enabled fixes to a dataset, returning a list of changes made.
    /// </summary>
    /// <param name="dataset">The dataset to repair (modified in place).</param>
    /// <param name="options">Which fix categories to apply.</param>
    /// <returns>A list of all fix actions performed.</returns>
    public static List<FixAction> Fix(DicomDataset dataset, FixOptions options)
    {
        var actions = new List<FixAction>();

        if (options.FixInvalidUids)
            FixInvalidUids(dataset, actions);

        if (options.FixInvalidDates)
            FixInvalidDates(dataset, actions);

        if (options.FixInvalidTimes)
            FixInvalidTimes(dataset, actions);

        if (options.FixCharacterEncoding)
            FixCharacterEncoding(dataset, actions);

        // RemoveInvalidElements is last because it is destructive
        if (options.RemoveInvalidElements)
            RemoveInvalidElements(dataset, actions);

        return actions;
    }

    // ---- Fix categories -------------------------------------------------------

    private static void FixInvalidUids(DicomDataset dataset, List<FixAction> actions)
    {
        // Collect tags first to avoid modifying collection during iteration
        var uiElements = dataset
            .Where(e => e.VR == DicomVR.UI)
            .ToList();

        foreach (var element in uiElements)
        {
            if (element is not DicomStringElement se)
                continue;

            var value = se.GetString();
            if (string.IsNullOrEmpty(value))
                continue;

            if (!UidGenerator.IsValidUid(value))
            {
                var newUid = UidGenerator.GenerateUid();
                var newBytes = Encoding.ASCII.GetBytes(newUid);
                // Ensure even length per DICOM
                byte[] paddedBytes;
                if (newBytes.Length % 2 != 0)
                {
                    paddedBytes = new byte[newBytes.Length + 1];
                    Array.Copy(newBytes, paddedBytes, newBytes.Length);
                    // UI VR uses null byte padding
                    paddedBytes[newBytes.Length] = 0;
                }
                else
                {
                    paddedBytes = newBytes;
                }

                dataset.Add(new DicomStringElement(element.Tag, DicomVR.UI, paddedBytes));
                actions.Add(new FixAction(
                    element.Tag,
                    "Invalid UID replaced",
                    value,
                    newUid));
            }
        }
    }

    private static void FixInvalidDates(DicomDataset dataset, List<FixAction> actions)
    {
        var daElements = dataset
            .Where(e => e.VR == DicomVR.DA)
            .ToList();

        foreach (var element in daElements)
        {
            if (element is not DicomStringElement se)
                continue;

            var value = se.GetString();
            if (string.IsNullOrEmpty(value))
                continue; // Empty is valid for Type 2

            // Already valid DICOM date?
            if (IsValidDicomDate(value))
                continue;

            var cleaned = TryCleanDate(value);
            if (cleaned != null && cleaned != value)
            {
                var newBytes = Encoding.ASCII.GetBytes(cleaned);
                // DA VR is always 8 bytes, padded with space if shorter
                if (newBytes.Length % 2 != 0)
                {
                    var padded = new byte[newBytes.Length + 1];
                    Array.Copy(newBytes, padded, newBytes.Length);
                    padded[newBytes.Length] = (byte)' ';
                    newBytes = padded;
                }

                dataset.Add(new DicomStringElement(element.Tag, DicomVR.DA, newBytes));
                actions.Add(new FixAction(
                    element.Tag,
                    "Date reformatted",
                    value,
                    cleaned));
            }
        }
    }

    private static void FixInvalidTimes(DicomDataset dataset, List<FixAction> actions)
    {
        var tmElements = dataset
            .Where(e => e.VR == DicomVR.TM)
            .ToList();

        foreach (var element in tmElements)
        {
            if (element is not DicomStringElement se)
                continue;

            var value = se.GetString();
            if (string.IsNullOrEmpty(value))
                continue;

            var cleaned = CleanTime(value);
            if (cleaned != value)
            {
                var newBytes = Encoding.ASCII.GetBytes(cleaned);
                if (newBytes.Length % 2 != 0)
                {
                    var padded = new byte[newBytes.Length + 1];
                    Array.Copy(newBytes, padded, newBytes.Length);
                    padded[newBytes.Length] = (byte)' ';
                    newBytes = padded;
                }

                dataset.Add(new DicomStringElement(element.Tag, DicomVR.TM, newBytes));
                actions.Add(new FixAction(
                    element.Tag,
                    "Time reformatted",
                    value,
                    cleaned));
            }
        }
    }

    private static void FixCharacterEncoding(DicomDataset dataset, List<FixAction> actions)
    {
        // Only fix if SpecificCharacterSet is missing
        if (dataset.Contains(DicomTag.SpecificCharacterSet))
            return;

        // Check if any string element contains non-ASCII bytes
        bool hasNonAscii = false;
        foreach (var element in dataset)
        {
            if (!element.VR.IsStringVR || element.IsEmpty)
                continue;

            var span = element.RawValue.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] > 127)
                {
                    hasNonAscii = true;
                    break;
                }
            }

            if (hasNonAscii)
                break;
        }

        if (hasNonAscii)
        {
            // Add ISO_IR 100 (Latin-1) as a sensible default
            var value = "ISO_IR 100";
            var bytes = Encoding.ASCII.GetBytes(value);
            dataset.Add(new DicomStringElement(
                DicomTag.SpecificCharacterSet,
                DicomVR.CS,
                bytes));

            actions.Add(new FixAction(
                DicomTag.SpecificCharacterSet,
                "Added missing character set",
                null,
                value));
        }
    }

    private static void RemoveInvalidElements(DicomDataset dataset, List<FixAction> actions)
    {
        // Check each element for basic validity; remove those that are malformed.
        var tagsToRemove = new List<(DicomTag tag, string reason)>();

        foreach (var element in dataset)
        {
            if (element.VR == DicomVR.SQ)
                continue; // Don't remove sequences

            // Check basic format rules for string VRs
            if (element.VR.IsStringVR && element is DicomStringElement se)
            {
                var str = se.GetString();
                if (str != null)
                {
                    // Check for invalid characters (control chars other than ESC)
                    bool hasInvalid = false;
                    foreach (var c in str)
                    {
                        if (c < 0x20 && c != 0x1B && c != '\r' && c != '\n' && c != '\t')
                        {
                            hasInvalid = true;
                            break;
                        }
                    }

                    if (hasInvalid)
                    {
                        tagsToRemove.Add((element.Tag, "Contains invalid control characters"));
                    }
                }
            }
        }

        foreach (var (tag, reason) in tagsToRemove)
        {
            var element = dataset[tag];
            string? oldValue = null;
            if (element is DicomStringElement se)
                oldValue = se.GetString();

            dataset.Remove(tag);
            actions.Add(new FixAction(
                tag,
                $"Removed: {reason}",
                oldValue,
                null));
        }
    }

    // ---- Helpers ---------------------------------------------------------------

    private static bool IsValidDicomDate(string value)
    {
        // Valid DICOM DA: YYYYMMDD (8 chars) or YYYY (4), YYYYMM (6) for range
        if (value.Length != 8 && value.Length != 6 && value.Length != 4)
            return false;

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    private static string? TryCleanDate(string value)
    {
        // Strip common separators: dots, dashes, slashes
        var stripped = value.Replace(".", "").Replace("-", "").Replace("/", "");

        // If stripping produced a valid DICOM date, use it
        if (IsValidDicomDate(stripped))
            return stripped;

        // Try parsing as a date and reformatting
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        return null;
    }

    private static string CleanTime(string value)
    {
        // Strip non-numeric characters except '.'
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if ((c >= '0' && c <= '9') || c == '.')
                sb.Append(c);
        }

        return sb.ToString();
    }
}
