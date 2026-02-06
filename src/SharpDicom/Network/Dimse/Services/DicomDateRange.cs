using System;
using System.Globalization;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Represents a DICOM date range for DA/DT range matching per PS3.4 C.2.2.2.5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DICOM date ranges use a dash separator with the format "YYYYMMDD-YYYYMMDD".
    /// Both open-ended ranges ("-YYYYMMDD" and "YYYYMMDD-") and single dates ("YYYYMMDD")
    /// are supported.
    /// </para>
    /// <para>
    /// A universal range (both From and To are null) matches all dates.
    /// </para>
    /// </remarks>
    public readonly struct DicomDateRange : IEquatable<DicomDateRange>
    {
        /// <summary>
        /// Gets the start of the date range (inclusive), or null for open-ended start.
        /// </summary>
        public DateTime? From { get; }

        /// <summary>
        /// Gets the end of the date range (inclusive), or null for open-ended end.
        /// </summary>
        public DateTime? To { get; }

        /// <summary>
        /// Gets a value indicating whether this range matches all dates.
        /// </summary>
        /// <remarks>
        /// A universal range has both From and To set to null, which occurs when
        /// parsing an empty or null value.
        /// </remarks>
        public bool IsUniversal => From == null && To == null;

        /// <summary>
        /// Initializes a new instance of <see cref="DicomDateRange"/>.
        /// </summary>
        /// <param name="from">The start of the range (inclusive), or null for open-ended start.</param>
        /// <param name="to">The end of the range (inclusive), or null for open-ended end.</param>
        public DicomDateRange(DateTime? from, DateTime? to)
        {
            From = from;
            To = to;
        }

        /// <summary>
        /// Determines whether the specified date falls within this range.
        /// </summary>
        /// <param name="date">The date to check.</param>
        /// <returns>True if the date is within the range; false otherwise.</returns>
        /// <remarks>
        /// Both From and To are inclusive. A universal range contains all dates.
        /// </remarks>
        public bool Contains(DateTime date)
        {
            if (From.HasValue && date.Date < From.Value.Date)
                return false;
            if (To.HasValue && date.Date > To.Value.Date)
                return false;
            return true;
        }

        /// <summary>
        /// Parses a DICOM date or date range string.
        /// </summary>
        /// <param name="value">
        /// The DICOM date range string in one of these formats:
        /// <list type="bullet">
        ///   <item><description>"YYYYMMDD" - single date (From and To are both that date)</description></item>
        ///   <item><description>"YYYYMMDD-YYYYMMDD" - closed range</description></item>
        ///   <item><description>"-YYYYMMDD" - open start (From is null)</description></item>
        ///   <item><description>"YYYYMMDD-" - open end (To is null)</description></item>
        ///   <item><description>null or empty - universal range (matches all)</description></item>
        /// </list>
        /// </param>
        /// <returns>A parsed <see cref="DicomDateRange"/>.</returns>
        /// <exception cref="FormatException">Thrown when a date component cannot be parsed.</exception>
        public static DicomDateRange Parse(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return new DicomDateRange(null, null);

            var trimmed = value!.Trim();
            if (trimmed.Length == 0)
                return new DicomDateRange(null, null);

            var dashIndex = trimmed.IndexOf('-');
            if (dashIndex < 0)
            {
                // Single date value
                var date = ParseDate(trimmed);
                return new DicomDateRange(date, date);
            }

            DateTime? from = null;
            DateTime? to = null;

            var fromStr = trimmed.Substring(0, dashIndex).Trim();
            var toStr = trimmed.Substring(dashIndex + 1).Trim();

            if (fromStr.Length > 0)
                from = ParseDate(fromStr);
            if (toStr.Length > 0)
                to = ParseDate(toStr);

            return new DicomDateRange(from, to);
        }

        private static DateTime ParseDate(string dateStr)
        {
            return DateTime.ParseExact(
                dateStr,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
        }

        /// <inheritdoc />
        public bool Equals(DicomDateRange other) =>
            From == other.From && To == other.To;

        /// <inheritdoc />
        public override bool Equals(object? obj) =>
            obj is DicomDateRange other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            return (From?.GetHashCode() ?? 0) ^ (To?.GetHashCode() ?? 0);
#else
            return HashCode.Combine(From, To);
#endif
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (IsUniversal) return "(universal)";
            if (From.HasValue && To.HasValue && From.Value == To.Value)
                return From.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var fromStr = From?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";
            var toStr = To?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";
            return $"{fromStr}-{toStr}";
        }

        /// <summary>
        /// Determines whether two <see cref="DicomDateRange"/> values are equal.
        /// </summary>
        public static bool operator ==(DicomDateRange left, DicomDateRange right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="DicomDateRange"/> values are not equal.
        /// </summary>
        public static bool operator !=(DicomDateRange left, DicomDateRange right) => !left.Equals(right);
    }
}
