using System;
using System.Globalization;
using System.Linq;
using SharpDicom.Data.Exceptions;

#if NETSTANDARD2_0 || NETFRAMEWORK
using SharpDicom.Internal;
#endif

namespace SharpDicom.Data;

/// <summary>
/// DICOM element for string-based Value Representations.
/// Covers: AE, AS, CS, DA, DS, DT, IS, LO, LT, PN, SH, ST, TM, UC, UI, UR, UT
/// </summary>
public sealed class DicomStringElement : IDicomElement
{
    /// <inheritdoc />
    public DicomTag Tag { get; }

    /// <inheritdoc />
    public DicomVR VR { get; }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> RawValue { get; }

    /// <inheritdoc />
    public int Length => RawValue.Length;

    /// <inheritdoc />
    public bool IsEmpty => RawValue.IsEmpty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DicomStringElement"/> class.
    /// </summary>
    /// <param name="tag">The DICOM tag.</param>
    /// <param name="vr">The Value Representation.</param>
    /// <param name="value">The raw byte value.</param>
    public DicomStringElement(DicomTag tag, DicomVR vr, ReadOnlyMemory<byte> value)
    {
        Tag = tag;
        VR = vr;
        RawValue = value;
    }

    /// <inheritdoc />
    public IDicomElement ToOwned() =>
        new DicomStringElement(Tag, VR, RawValue.ToArray());

    // Characters to trim from DICOM strings (whitespace and null padding)
    private static readonly char[] TrimChars = { ' ', '\0' };

    /// <summary>
    /// Get the string value using the specified encoding.
    /// </summary>
    public string? GetString(DicomEncoding? encoding = null)
    {
        if (IsEmpty)
            return null;

        var enc = encoding ?? DicomEncoding.Default;
#if NETSTANDARD2_0 || NETFRAMEWORK
        return enc.Primary.GetString(RawValue.ToArray()).TrimEnd(TrimChars);
#else
        return enc.Primary.GetString(RawValue.Span).TrimEnd(TrimChars);
#endif
    }

    /// <summary>
    /// Get the string value or throw if empty.
    /// </summary>
    public string GetStringOrThrow(DicomEncoding? encoding = null)
        => GetString(encoding) ?? throw new DicomDataException($"Tag {Tag} has no value");

    /// <summary>
    /// Get multiple string values (split by backslash for VM > 1).
    /// </summary>
    public string[]? GetStrings(DicomEncoding? encoding = null)
    {
        var str = GetString(encoding);
        if (str == null)
            return null;

        return str.Split('\\');
    }

    /// <summary>
    /// Get multiple string values or throw if empty.
    /// </summary>
    public string[] GetStringsOrThrow(DicomEncoding? encoding = null)
        => GetStrings(encoding) ?? throw new DicomDataException($"Tag {Tag} has no value");

    /// <summary>
    /// Parse as a date (DA VR format: YYYYMMDD).
    /// </summary>
    public DateOnly? GetDate()
    {
        var str = GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        // DICOM DA format is YYYYMMDD (no separators)
        // Use DateTime.TryParseExact then convert to DateOnly for netstandard2.0 compatibility
        if (DateTime.TryParseExact(str, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dateTime))
            return new DateOnly(dateTime.Year, dateTime.Month, dateTime.Day);

        // Fallback to standard parsing for older formats with separators
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            return new DateOnly(dateTime.Year, dateTime.Month, dateTime.Day);

        return null;
    }

    /// <summary>
    /// Parse as a date or throw if invalid.
    /// </summary>
    public DateOnly GetDateOrThrow()
        => GetDate() ?? throw new DicomDataException($"Tag {Tag} is not a valid date");

    /// <summary>
    /// Parse as a time (TM VR format: HHMMSS.FFFFFF).
    /// </summary>
    public TimeOnly? GetTime()
    {
        var str = GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        // DICOM TM formats (compact, no colons): HHMMSS.FFFFFF, HHMMSS, HHMM, HH
        // Use DateTime.TryParseExact then convert to TimeOnly for netstandard2.0 compatibility
        var formats = new[]
        {
            "HHmmss.ffffff",
            "HHmmss.fffff",
            "HHmmss.ffff",
            "HHmmss.fff",
            "HHmmss.ff",
            "HHmmss.f",
            "HHmmss",
            "HHmm",
            "HH"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(str, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateTime))
                return new TimeOnly(dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond);
        }

        // Fallback to standard parsing for formats with colons
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallbackDateTime))
            return new TimeOnly(fallbackDateTime.Hour, fallbackDateTime.Minute, fallbackDateTime.Second, fallbackDateTime.Millisecond);

        return null;
    }

    /// <summary>
    /// Parse as a time or throw if invalid.
    /// </summary>
    public TimeOnly GetTimeOrThrow()
        => GetTime() ?? throw new DicomDataException($"Tag {Tag} is not a valid time");

    /// <summary>
    /// Parse as a datetime (DT VR format: YYYYMMDDHHMMSS.FFFFFF±ZZZZ).
    /// </summary>
    public DateTime? GetDateTime()
    {
        var str = GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        // Try parsing various DT formats
        var formats = new[]
        {
            "yyyyMMddHHmmss.FFFFFF",
            "yyyyMMddHHmmss",
            "yyyyMMddHHmm",
            "yyyyMMddHH",
            "yyyyMMdd"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(str, format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
                return dt;
        }

        return null;
    }

    /// <summary>
    /// Parse as a datetime or throw if invalid.
    /// </summary>
    public DateTime GetDateTimeOrThrow()
        => GetDateTime() ?? throw new DicomDataException($"Tag {Tag} is not a valid datetime");

    /// <summary>
    /// Parse as an integer (IS VR format). Returns first value if multiple.
    /// </summary>
    public int? GetInt32()
    {
        var str = GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        // Handle multi-valued strings - return first value
        string firstValue;
        if (str!.Contains("\\"))
        {
            var parts = str.Split('\\');
            firstValue = parts[0];
        }
        else
        {
            firstValue = str;
        }

        if (int.TryParse(firstValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Parse as an integer or throw if invalid.
    /// </summary>
    public int GetInt32OrThrow()
        => GetInt32() ?? throw new DicomDataException($"Tag {Tag} is not a valid integer");

    /// <summary>
    /// Parse as a double (DS VR format). Returns first value if multiple.
    /// </summary>
    public double? GetFloat64()
    {
        var str = GetString();
        if (string.IsNullOrEmpty(str))
            return null;

        // Handle multi-valued strings - return first value
        string firstValue;
        if (str!.Contains("\\"))
        {
            var parts = str.Split('\\');
            firstValue = parts[0];
        }
        else
        {
            firstValue = str;
        }

        if (double.TryParse(firstValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Parse as a double or throw if invalid.
    /// </summary>
    public double GetFloat64OrThrow()
        => GetFloat64() ?? throw new DicomDataException($"Tag {Tag} is not a valid number");

    /// <summary>
    /// Parse multiple integers (IS VR with VM > 1).
    /// </summary>
    public int[]? GetInt32Array()
    {
        var strings = GetStrings();
        if (strings == null)
            return null;

        return strings.Select(s => int.TryParse(s, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
    }

    /// <summary>
    /// Parse multiple doubles (DS VR with VM > 1).
    /// </summary>
    public double[]? GetFloat64Array()
    {
        var strings = GetStrings();
        if (strings == null)
            return null;

        return strings.Select(s => double.TryParse(s, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToArray();
    }

    /// <summary>
    /// Get a string value wrapper that supports zero-copy UTF-8 access.
    /// </summary>
    /// <remarks>
    /// For UTF-8 or ASCII encoding, TryGetUtf8 on the result returns the raw bytes
    /// without allocation. For other encodings, AsString() allocates a string.
    /// </remarks>
    public DicomStringValue GetStringValue(DicomEncoding? encoding = null)
    {
        return new DicomStringValue(RawValue, encoding ?? DicomEncoding.Default);
    }
}

/// <summary>
/// Wrapper for string element value supporting zero-copy UTF-8 access.
/// </summary>
public readonly ref struct DicomStringValue
{
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly DicomEncoding _encoding;

    internal DicomStringValue(ReadOnlyMemory<byte> bytes, DicomEncoding encoding)
    {
        _bytes = bytes.Span;
        _encoding = encoding;
    }

    /// <summary>Raw bytes of the string value.</summary>
    public ReadOnlySpan<byte> RawBytes => _bytes;

    /// <summary>True if encoding is UTF-8 compatible (zero-copy possible).</summary>
    public bool IsUtf8 => _encoding.IsUtf8Compatible;

    /// <summary>
    /// Try to get UTF-8 bytes without allocation.
    /// Returns true if encoding is UTF-8/ASCII compatible.
    /// </summary>
    public bool TryGetUtf8(out ReadOnlySpan<byte> utf8)
        => _encoding.TryGetUtf8(_bytes, out utf8);

    /// <summary>
    /// Get the string value (allocates for non-UTF-8 encodings).
    /// </summary>
    public string AsString()
    {
        if (_bytes.IsEmpty) return string.Empty;
        return _encoding.GetString(_bytes).TrimEnd(' ', '\0');
    }
}
