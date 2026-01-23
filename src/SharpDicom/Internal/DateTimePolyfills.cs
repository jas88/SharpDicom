#if NETSTANDARD2_0 || NETFRAMEWORK

using System;
using System.Globalization;

namespace SharpDicom.Internal;

/// <summary>
/// Polyfill for DateOnly (not available on .NET Standard 2.0).
/// </summary>
public readonly struct DateOnly : IEquatable<DateOnly>, IComparable<DateOnly>
{
    private readonly DateTime _value;

    /// <summary>
    /// Initializes a new DateOnly instance.
    /// </summary>
    public DateOnly(int year, int month, int day)
    {
        _value = new DateTime(year, month, day);
    }

    /// <summary>
    /// Gets the year component.
    /// </summary>
    public int Year => _value.Year;

    /// <summary>
    /// Gets the month component.
    /// </summary>
    public int Month => _value.Month;

    /// <summary>
    /// Gets the day component.
    /// </summary>
    public int Day => _value.Day;

    /// <summary>
    /// Gets the day of week.
    /// </summary>
    public DayOfWeek DayOfWeek => _value.DayOfWeek;

    /// <summary>
    /// Gets the day of year.
    /// </summary>
    public int DayOfYear => _value.DayOfYear;

    /// <summary>
    /// Creates a DateOnly from a DateTime.
    /// </summary>
    public static DateOnly FromDateTime(DateTime dateTime) =>
        new(dateTime.Year, dateTime.Month, dateTime.Day);

    /// <summary>
    /// Converts to DateTime with the specified time.
    /// </summary>
    public DateTime ToDateTime(TimeOnly time)
        => _value.AddTicks(time.Ticks);

    /// <summary>
    /// Parses a DateOnly from string (YYYYMMDD format).
    /// </summary>
    public static DateOnly Parse(string s)
        => FromDateTime(DateTime.ParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture));

    /// <summary>
    /// Tries to parse a DateOnly from string.
    /// </summary>
    public static bool TryParse(string? s, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrEmpty(s))
            return false;

        if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt))
        {
            result = FromDateTime(dt);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool Equals(DateOnly other) => _value.Date == other._value.Date;

    /// <inheritdoc />
    public int CompareTo(DateOnly other) => _value.Date.CompareTo(other._value.Date);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DateOnly other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.Date.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _value.ToString("yyyy-MM-dd");

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(DateOnly left, DateOnly right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(DateOnly left, DateOnly right) => !left.Equals(right);

    /// <summary>
    /// Less than operator.
    /// </summary>
    public static bool operator <(DateOnly left, DateOnly right) => left._value < right._value;

    /// <summary>
    /// Greater than operator.
    /// </summary>
    public static bool operator >(DateOnly left, DateOnly right) => left._value > right._value;

    /// <summary>
    /// Less than or equal operator.
    /// </summary>
    public static bool operator <=(DateOnly left, DateOnly right) => left._value <= right._value;

    /// <summary>
    /// Greater than or equal operator.
    /// </summary>
    public static bool operator >=(DateOnly left, DateOnly right) => left._value >= right._value;
}

/// <summary>
/// Polyfill for TimeOnly (not available on .NET Standard 2.0).
/// </summary>
public readonly struct TimeOnly : IEquatable<TimeOnly>, IComparable<TimeOnly>
{
    private readonly TimeSpan _value;

    /// <summary>
    /// Initializes a new TimeOnly instance.
    /// </summary>
    public TimeOnly(int hour, int minute, int second = 0, int millisecond = 0)
    {
        _value = new TimeSpan(0, hour, minute, second, millisecond);
    }

    /// <summary>
    /// Initializes a new TimeOnly instance from ticks.
    /// </summary>
    public TimeOnly(long ticks)
    {
        _value = new TimeSpan(ticks);
    }

    /// <summary>
    /// Gets the hour component.
    /// </summary>
    public int Hour => _value.Hours;

    /// <summary>
    /// Gets the minute component.
    /// </summary>
    public int Minute => _value.Minutes;

    /// <summary>
    /// Gets the second component.
    /// </summary>
    public int Second => _value.Seconds;

    /// <summary>
    /// Gets the millisecond component.
    /// </summary>
    public int Millisecond => _value.Milliseconds;

    /// <summary>
    /// Gets the ticks.
    /// </summary>
    public long Ticks => _value.Ticks;

    /// <summary>
    /// Creates a TimeOnly from a DateTime.
    /// </summary>
    public static TimeOnly FromDateTime(DateTime dateTime) => new(dateTime.TimeOfDay.Ticks);

    /// <summary>
    /// Creates a TimeOnly from a TimeSpan.
    /// </summary>
    public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => new(timeSpan.Ticks);

    /// <summary>
    /// Converts to TimeSpan.
    /// </summary>
    public TimeSpan ToTimeSpan() => _value;

    /// <summary>
    /// Parses a TimeOnly from string (HHMMSS format).
    /// </summary>
    public static TimeOnly Parse(string s)
    {
        var dt = DateTime.ParseExact(s, new[] { "HHmmss", "HHmmss.ffffff", "HHmmss.FFFFFF" },
            CultureInfo.InvariantCulture, DateTimeStyles.None);
        return FromDateTime(dt);
    }

    /// <summary>
    /// Tries to parse a TimeOnly from string.
    /// </summary>
    public static bool TryParse(string? s, out TimeOnly result)
    {
        result = default;
        if (string.IsNullOrEmpty(s))
            return false;

        // Try HHMMSS.FFFFFF format
        if (TimeSpan.TryParseExact(s, @"hhmmss\.FFFFFF", CultureInfo.InvariantCulture, out var ts))
        {
            result = FromTimeSpan(ts);
            return true;
        }
        // Try HHMMSS format
        if (TimeSpan.TryParseExact(s, "hhmmss", CultureInfo.InvariantCulture, out ts))
        {
            result = FromTimeSpan(ts);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool Equals(TimeOnly other) => _value == other._value;

    /// <inheritdoc />
    public int CompareTo(TimeOnly other) => _value.CompareTo(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TimeOnly other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => _value.ToString(@"hh\:mm\:ss");

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(TimeOnly left, TimeOnly right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(TimeOnly left, TimeOnly right) => !left.Equals(right);

    /// <summary>
    /// Less than operator.
    /// </summary>
    public static bool operator <(TimeOnly left, TimeOnly right) => left._value < right._value;

    /// <summary>
    /// Greater than operator.
    /// </summary>
    public static bool operator >(TimeOnly left, TimeOnly right) => left._value > right._value;

    /// <summary>
    /// Less than or equal operator.
    /// </summary>
    public static bool operator <=(TimeOnly left, TimeOnly right) => left._value <= right._value;

    /// <summary>
    /// Greater than or equal operator.
    /// </summary>
    public static bool operator >=(TimeOnly left, TimeOnly right) => left._value >= right._value;
}

#else

// On .NET 6+, just use the built-in types
global using DateOnly = System.DateOnly;
global using TimeOnly = System.TimeOnly;

#endif
