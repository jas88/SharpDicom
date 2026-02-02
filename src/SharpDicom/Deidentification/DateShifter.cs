using System;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Deidentification;

/// <summary>
/// Handles date/time shifting with VR-aware logic per PS3.15.
/// </summary>
/// <remarks>
/// <para>
/// Date shifting is a key privacy technique that preserves temporal relationships
/// while obscuring the actual dates. This class handles the three date-related VRs:
/// </para>
/// <list type="bullet">
/// <item>DA (Date): YYYYMMDD format</item>
/// <item>TM (Time): HHMMSS.FFFFFF format</item>
/// <item>DT (DateTime): YYYYMMDDHHMMSS.FFFFFF format</item>
/// </list>
/// </remarks>
public static class DateShifter
{
    /// <summary>
    /// Shifts a date element by the given offset.
    /// </summary>
    /// <param name="element">The element to shift (must be DA, TM, or DT VR).</param>
    /// <param name="offset">The time offset to apply (typically days).</param>
    /// <param name="zeroTime">Whether to zero out time components in TM and DT values.</param>
    /// <returns>A new element with shifted date, or the original element if not a date VR or invalid.</returns>
    public static IDicomElement Shift(IDicomElement element, TimeSpan offset, bool zeroTime)
    {
        if (element is not DicomStringElement se)
            return element;

        var vr = element.VR;
        if (vr == DicomVR.DA)
            return ShiftDate(element.Tag, se, offset);
        if (vr == DicomVR.TM)
            return zeroTime ? ZeroTime(element.Tag) : element;
        if (vr == DicomVR.DT)
            return ShiftDateTime(element.Tag, se, offset, zeroTime);

        return element;
    }

    private static DicomStringElement ShiftDate(DicomTag tag, DicomStringElement element, TimeSpan offset)
    {
        var value = element.GetString();
        if (string.IsNullOrEmpty(value) || value!.Length < 8)
            return element;

        // Parse YYYYMMDD format
#if NETSTANDARD2_0
        if (!int.TryParse(value.Substring(0, 4), out var year) ||
            !int.TryParse(value.Substring(4, 2), out var month) ||
            !int.TryParse(value.Substring(6, 2), out var day))
            return element;
#else
        if (!int.TryParse(value.AsSpan(0, 4), out var year) ||
            !int.TryParse(value.AsSpan(4, 2), out var month) ||
            !int.TryParse(value.AsSpan(6, 2), out var day))
            return element;
#endif

        try
        {
#if NET6_0_OR_GREATER
            var date = new DateOnly(year, month, day);
            var shifted = date.AddDays((int)offset.TotalDays);
            var result = $"{shifted.Year:D4}{shifted.Month:D2}{shifted.Day:D2}";
#else
            var date = new DateTime(year, month, day);
            var shifted = date.AddDays((int)offset.TotalDays);
            var result = $"{shifted.Year:D4}{shifted.Month:D2}{shifted.Day:D2}";
#endif
            return CreateStringElement(tag, DicomVR.DA, result);
        }
        catch
        {
            return element; // Invalid date - keep original
        }
    }

    private static DicomStringElement ShiftDateTime(DicomTag tag, DicomStringElement element,
        TimeSpan offset, bool zeroTime)
    {
        var value = element.GetString();
        if (string.IsNullOrEmpty(value) || value!.Length < 8)
            return element;

        // Parse YYYYMMDD portion
#if NETSTANDARD2_0
        if (!int.TryParse(value.Substring(0, 4), out var year) ||
            !int.TryParse(value.Substring(4, 2), out var month) ||
            !int.TryParse(value.Substring(6, 2), out var day))
            return element;
#else
        if (!int.TryParse(value.AsSpan(0, 4), out var year) ||
            !int.TryParse(value.AsSpan(4, 2), out var month) ||
            !int.TryParse(value.AsSpan(6, 2), out var day))
            return element;
#endif

        try
        {
#if NET6_0_OR_GREATER
            var date = new DateOnly(year, month, day);
            var shifted = date.AddDays((int)offset.TotalDays);
#else
            var date = new DateTime(year, month, day);
            var shifted = date.AddDays((int)offset.TotalDays);
#endif

            string result;
            if (zeroTime)
            {
                // YYYYMMDD000000 format with zeroed time
                result = $"{shifted.Year:D4}{shifted.Month:D2}{shifted.Day:D2}000000";
            }
            else
            {
                // Keep original time portion if present
                var time = value.Length > 8 ? value.Substring(8) : "";
                result = $"{shifted.Year:D4}{shifted.Month:D2}{shifted.Day:D2}{time}";
            }

            return CreateStringElement(tag, DicomVR.DT, result);
        }
        catch
        {
            return element;
        }
    }

    private static DicomStringElement ZeroTime(DicomTag tag)
    {
        return CreateStringElement(tag, DicomVR.TM, "000000");
    }

    /// <summary>
    /// Recalculates PatientAge (AS VR) from birth date and study date.
    /// </summary>
    /// <param name="birthDate">The patient's birth date.</param>
    /// <param name="studyDate">The study date.</param>
    /// <returns>The calculated age in AS format (nnnY), or null if dates are invalid.</returns>
#if NET6_0_OR_GREATER
    public static string? CalculateAge(DateOnly? birthDate, DateOnly? studyDate)
    {
        if (birthDate == null || studyDate == null)
            return null;

        var years = studyDate.Value.Year - birthDate.Value.Year;
        if (studyDate.Value.DayOfYear < birthDate.Value.DayOfYear)
            years--;

        if (years < 0)
            return "000Y";

        // AS VR format: nnnD, nnnW, nnnM, or nnnY
        return $"{Math.Min(years, 999):D3}Y";
    }
#else
    public static string? CalculateAge(DateTime? birthDate, DateTime? studyDate)
    {
        if (birthDate == null || studyDate == null)
            return null;

        var years = studyDate.Value.Year - birthDate.Value.Year;
        if (studyDate.Value.DayOfYear < birthDate.Value.DayOfYear)
            years--;

        if (years < 0)
            return "000Y";

        // AS VR format: nnnD, nnnW, nnnM, or nnnY
        return $"{Math.Min(years, 999):D3}Y";
    }
#endif

    /// <summary>
    /// Parses a DA (Date) value to DateOnly (or DateTime on .NET Standard).
    /// </summary>
    /// <param name="value">The date string in YYYYMMDD format.</param>
    /// <returns>The parsed date, or null if invalid.</returns>
#if NET6_0_OR_GREATER
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length < 8)
            return null;

        if (int.TryParse(value.AsSpan(0, 4), out var year) &&
            int.TryParse(value.AsSpan(4, 2), out var month) &&
            int.TryParse(value.AsSpan(6, 2), out var day))
        {
            try { return new DateOnly(year, month, day); }
            catch { return null; }
        }
        return null;
    }
#else
    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length < 8)
            return null;

        if (int.TryParse(value.Substring(0, 4), out var year) &&
            int.TryParse(value.Substring(4, 2), out var month) &&
            int.TryParse(value.Substring(6, 2), out var day))
        {
            try { return new DateTime(year, month, day); }
            catch { return null; }
        }
        return null;
    }
#endif

    /// <summary>
    /// Creates a string element with properly padded value.
    /// </summary>
    private static DicomStringElement CreateStringElement(DicomTag tag, DicomVR vr, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        // Pad to even length if necessary
        if (bytes.Length % 2 != 0)
        {
            var padded = new byte[bytes.Length + 1];
            bytes.CopyTo(padded, 0);
            padded[padded.Length - 1] = DicomVRInfo.GetInfo(vr).PaddingByte;
            bytes = padded;
        }
        return new DicomStringElement(tag, vr, bytes);
    }
}
