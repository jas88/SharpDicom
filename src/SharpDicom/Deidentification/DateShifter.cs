using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Interface for storing and retrieving date offsets per patient.
    /// </summary>
    public interface IDateOffsetStore
    {
        /// <summary>
        /// Gets or creates a date offset for the given patient ID.
        /// </summary>
        /// <param name="patientId">The patient identifier.</param>
        /// <param name="minOffset">Minimum offset range.</param>
        /// <param name="maxOffset">Maximum offset range.</param>
        /// <param name="seed">Optional seed for reproducibility.</param>
        /// <returns>The time offset to apply.</returns>
        TimeSpan GetOrCreateOffset(string patientId, TimeSpan minOffset, TimeSpan maxOffset, int? seed);

        /// <summary>
        /// Tries to get existing offset for patient.
        /// </summary>
        /// <param name="patientId">The patient identifier.</param>
        /// <param name="offset">The offset if found.</param>
        /// <returns>True if found, false otherwise.</returns>
        bool TryGetOffset(string patientId, out TimeSpan offset);
    }

    /// <summary>
    /// In-memory implementation of <see cref="IDateOffsetStore"/>.
    /// </summary>
    public sealed class InMemoryDateOffsetStore : IDateOffsetStore
    {
        private readonly ConcurrentDictionary<string, TimeSpan> _offsets = new();
        private readonly Random _random;
        private readonly object _lock = new();

        /// <summary>
        /// Creates a new in-memory date offset store.
        /// </summary>
        /// <param name="seed">Optional seed for reproducibility.</param>
        public InMemoryDateOffsetStore(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <inheritdoc/>
        public TimeSpan GetOrCreateOffset(string patientId, TimeSpan minOffset, TimeSpan maxOffset, int? seed)
        {
            return _offsets.GetOrAdd(patientId, _ =>
            {
                lock (_lock)
                {
                    var range = (maxOffset - minOffset).TotalDays;
                    var offsetDays = minOffset.TotalDays + (_random.NextDouble() * range);
                    return TimeSpan.FromDays(Math.Round(offsetDays));
                }
            });
        }

        /// <inheritdoc/>
        public bool TryGetOffset(string patientId, out TimeSpan offset)
            => _offsets.TryGetValue(patientId, out offset);
    }

    /// <summary>
    /// Shifts dates consistently for de-identification while preserving temporal relationships.
    /// </summary>
    public sealed class DateShifter
    {
        private readonly DateShiftConfig _config;
        private readonly IDateOffsetStore? _offsetStore;
        private readonly Dictionary<string, TimeSpan> _patientOffsets = new();
        private readonly object _lock = new();
        private readonly Random? _random;

        /// <summary>
        /// Creates a date shifter with the specified configuration.
        /// </summary>
        /// <param name="config">The date shift configuration.</param>
        /// <param name="offsetStore">Optional offset store for persisting patient offsets.</param>
        public DateShifter(DateShiftConfig config, IDateOffsetStore? offsetStore = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _offsetStore = offsetStore;

            if (_config.Strategy == DateShiftStrategy.RandomPerPatient && _offsetStore == null)
            {
                _random = _config.RandomSeed.HasValue
                    ? new Random(_config.RandomSeed.Value)
                    : new Random();
            }
        }

        /// <summary>
        /// Gets the offset for a patient, creating one if needed.
        /// </summary>
        /// <param name="patientId">The patient identifier.</param>
        /// <returns>The time offset to apply.</returns>
        public TimeSpan GetOffset(string? patientId)
        {
            return _config.Strategy switch
            {
                DateShiftStrategy.None => TimeSpan.Zero,
                DateShiftStrategy.Fixed => _config.FixedOffset,
                DateShiftStrategy.RandomPerPatient => GetOrCreatePatientOffset(patientId ?? "UNKNOWN"),
                DateShiftStrategy.RemoveTime => _config.FixedOffset, // Apply date shift, remove time
                DateShiftStrategy.Remove => TimeSpan.Zero,
                _ => TimeSpan.Zero
            };
        }

        private TimeSpan GetOrCreatePatientOffset(string patientId)
        {
            // Try external offset store first
            if (_offsetStore != null)
            {
                return _offsetStore.GetOrCreateOffset(
                    patientId,
                    TimeSpan.FromDays(_config.MinOffsetDays),
                    TimeSpan.FromDays(_config.MaxOffsetDays),
                    _config.RandomSeed);
            }

            lock (_lock)
            {
                if (_patientOffsets.TryGetValue(patientId, out var existing))
                {
                    return existing;
                }

                // Generate random offset within configured range
                var minDays = _config.MinOffsetDays;
                var maxDays = _config.MaxOffsetDays;
                var days = _random!.Next(minDays, maxDays + 1);
                var offset = TimeSpan.FromDays(days);

                _patientOffsets[patientId] = offset;
                return offset;
            }
        }

        /// <summary>
        /// Shifts a DICOM date string.
        /// </summary>
        /// <param name="dateString">The date string in YYYYMMDD format.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>The shifted date string, or empty string on error.</returns>
        /// <remarks>
        /// DICOM DA values can be multi-valued (VM&gt;1) with values separated by backslash.
        /// Each component is shifted independently.
        /// </remarks>
        public string ShiftDate(string? dateString, string? patientId)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return string.Empty;
            }

            // After null check, dateString is guaranteed non-null
            var ds = dateString!;

            // Handle multi-valued DA (VM>1) - values separated by backslash
#if NETSTANDARD2_0
            if (ds.IndexOf('\\') >= 0)
#else
            if (ds.Contains('\\'))
#endif
            {
                var components = ds.Split('\\');
                for (int i = 0; i < components.Length; i++)
                {
                    components[i] = ShiftSingleDate(components[i], patientId);
                }
                return string.Join("\\", components);
            }

            return ShiftSingleDate(ds, patientId);
        }

        private string ShiftSingleDate(string dateString, string? patientId)
        {
            // Handle Remove strategy - replace with dummy date
            if (_config.Strategy == DateShiftStrategy.Remove)
            {
                return DummyDate;
            }

            if (dateString.Length < 8)
            {
                return dateString; // Invalid format, return as-is
            }

            if (!TryParseDate(dateString, out var date))
            {
                return dateString; // Can't parse, return as-is
            }

            var offset = GetOffset(patientId);
            var shifted = date.Add(offset);

            return shifted.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        // Dummy values for Remove strategy
        private const string DummyDate = "19000101";
        private const string DummyTime = "000000.000000";
        private const string DummyDateTime = "19000101000000.000000";

        /// <summary>
        /// Shifts a DICOM time string.
        /// </summary>
        /// <param name="timeString">The time string in HHMMSS.FFFFFF format.</param>
        /// <param name="patientId">The patient ID (not used for time-only shifting).</param>
        /// <returns>The time string (unchanged unless config specifies time shifting).</returns>
        /// <remarks>
        /// DICOM TM values can be multi-valued (VM&gt;1) with values separated by backslash.
        /// Each component is processed independently.
        /// </remarks>
        public string ShiftTime(string? timeString, string? patientId)
        {
            // Suppress unused parameter warning - parameter kept for API consistency
            _ = patientId;

            if (string.IsNullOrEmpty(timeString))
            {
                return string.Empty;
            }

            // Handle Remove or RemoveTime strategy - remove time component
            if (_config.Strategy == DateShiftStrategy.Remove ||
                _config.Strategy == DateShiftStrategy.RemoveTime)
            {
                // Handle multi-valued TM (VM>1) - values separated by backslash
                // After null check above, timeString is guaranteed non-null
#if NETSTANDARD2_0
                if (timeString!.IndexOf('\\') >= 0)
#else
                if (timeString!.Contains('\\'))
#endif
                {
                    var components = timeString.Split('\\');
                    for (int i = 0; i < components.Length; i++)
                    {
                        components[i] = DummyTime;
                    }
                    return string.Join("\\", components);
                }
                return DummyTime;
            }

            // Time shifting is generally not needed for de-identification
            // Preserve as-is
            return timeString ?? string.Empty;
        }

        // Regex for extracting timezone from DT VR: &ZZXX or +ZZXX or -ZZXX at end
        private static readonly Regex TimezonePattern = new Regex(@"([&+-]\d{4})$", RegexOptions.Compiled);

        /// <summary>
        /// Shifts a DICOM datetime string.
        /// </summary>
        /// <param name="dateTimeString">The datetime string in YYYYMMDDHHMMSS.FFFFFF&amp;ZZXX format.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>The shifted datetime string.</returns>
        /// <remarks>
        /// DICOM DT values can be multi-valued (VM&gt;1) with values separated by backslash.
        /// Each component is shifted independently.
        /// </remarks>
        public string ShiftDateTime(string? dateTimeString, string? patientId)
        {
            if (string.IsNullOrWhiteSpace(dateTimeString))
            {
                return string.Empty;
            }

            // After null check, dateTimeString is guaranteed non-null
            var dts = dateTimeString!;

            // Handle multi-valued DT (VM>1) - values separated by backslash
#if NETSTANDARD2_0
            if (dts.IndexOf('\\') >= 0)
#else
            if (dts.Contains('\\'))
#endif
            {
                var components = dts.Split('\\');
                for (int i = 0; i < components.Length; i++)
                {
                    components[i] = ShiftSingleDateTime(components[i], patientId);
                }
                return string.Join("\\", components);
            }

            return ShiftSingleDateTime(dts, patientId);
        }

        private string ShiftSingleDateTime(string dateTimeString, string? patientId)
        {
            // Handle Remove strategy - replace with dummy datetime
            if (_config.Strategy == DateShiftStrategy.Remove)
            {
                return DummyDateTime;
            }

            if (dateTimeString.Length < 8)
            {
                return dateTimeString; // Invalid format
            }

            // Extract timezone suffix if present (preserve it)
            string timezone = string.Empty;
            string dtWithoutTz = dateTimeString;
            var tzMatch = TimezonePattern.Match(dateTimeString);
            if (tzMatch.Success)
            {
                timezone = tzMatch.Groups[1].Value;
                dtWithoutTz = dateTimeString.Substring(0, dateTimeString.Length - timezone.Length);
            }

            // Extract date part and shift it
            var datePart = dtWithoutTz.Substring(0, 8);
            var timePart = dtWithoutTz.Length > 8 ? dtWithoutTz.Substring(8) : "";

            // Handle RemoveTime strategy - keep shifted date, remove time
            if (_config.Strategy == DateShiftStrategy.RemoveTime)
            {
                var shiftedDateOnly = ShiftSingleDate(datePart, patientId);
                return shiftedDateOnly + DummyTime + timezone;
            }

            var shiftedDate = ShiftSingleDate(datePart, patientId);
            return shiftedDate + timePart + timezone;
        }

        /// <summary>
        /// Shifts all date/time elements in a dataset.
        /// </summary>
        /// <param name="dataset">The dataset to process.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>Number of dates shifted.</returns>
        public int ShiftDates(DicomDataset dataset, string? patientId = null)
        {
            var result = ShiftDatesWithResult(dataset, patientId);
            return result.TotalShifted;
        }

        /// <summary>
        /// Shifts all date/time elements in a dataset and returns detailed result.
        /// </summary>
        /// <param name="dataset">The dataset to process.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>Detailed result of the shifting operation.</returns>
        public DateShiftResult ShiftDatesWithResult(DicomDataset dataset, string? patientId = null)
        {
            // Try to get patient ID from dataset if not provided
            if (string.IsNullOrEmpty(patientId))
            {
                patientId = dataset.GetString(DicomTag.PatientID);
            }

            var result = new DateShiftResult
            {
                AppliedOffset = GetOffset(patientId)
            };

            ShiftDatesInternal(dataset, patientId, result);
            return result;
        }

        private void ShiftDatesInternal(DicomDataset dataset, string? patientId, DateShiftResult result)
        {
            // Collect tags to process
            var tagsToProcess = new List<DicomTag>();
            foreach (var element in dataset)
            {
                tagsToProcess.Add(element.Tag);
            }

            foreach (var tag in tagsToProcess)
            {
                var element = dataset[tag];
                if (element == null) continue;

                // Handle sequences recursively
                if (element is DicomSequence seq)
                {
                    foreach (var item in seq.Items)
                    {
                        ShiftDatesInternal(item, patientId, result);
                    }
                    continue;
                }

                // Handle date/time VRs
                if (element is DicomStringElement stringElement)
                {
                    var originalValue = stringElement.GetString(DicomEncoding.Default);
                    string? newValue = null;

                    if (element.VR == DicomVR.DA)
                    {
                        newValue = ShiftDate(originalValue, patientId);
                        if (newValue != null && newValue != originalValue)
                        {
                            result.DatesShifted++;
                        }
                    }
                    else if (element.VR == DicomVR.TM)
                    {
                        newValue = ShiftTime(originalValue, patientId);
                        if (newValue != null && newValue != originalValue)
                        {
                            result.TimesShifted++;
                        }
                    }
                    else if (element.VR == DicomVR.DT)
                    {
                        newValue = ShiftDateTime(originalValue, patientId);
                        if (newValue != null && newValue != originalValue)
                        {
                            result.DateTimesShifted++;
                        }
                    }

                    if (newValue != null && newValue != originalValue)
                    {
                        var bytes = System.Text.Encoding.ASCII.GetBytes(newValue);
                        var newElement = new DicomStringElement(tag, element.VR, bytes);
                        dataset.Add(newElement);
                    }
                }
            }
        }

        private static bool TryParseDate(string dateString, out DateTime date)
        {
            // DICOM date format: YYYYMMDD
            return DateTime.TryParseExact(
                dateString.Substring(0, Math.Min(8, dateString.Length)),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }
    }

    /// <summary>
    /// Configuration for date shifting.
    /// </summary>
    public sealed class DateShiftConfig
    {
        /// <summary>
        /// Gets or sets the date shift strategy.
        /// </summary>
        public DateShiftStrategy Strategy { get; init; } = DateShiftStrategy.Fixed;

        /// <summary>
        /// Gets or sets the fixed offset when using Fixed strategy.
        /// </summary>
        public TimeSpan FixedOffset { get; init; } = TimeSpan.FromDays(-365);

        /// <summary>
        /// Gets or sets the minimum offset days for random strategy.
        /// </summary>
        public int MinOffsetDays { get; init; } = -365 * 5;

        /// <summary>
        /// Gets or sets the maximum offset days for random strategy.
        /// </summary>
        public int MaxOffsetDays { get; init; } = -30;

        /// <summary>
        /// Gets or sets the random seed for reproducible results.
        /// </summary>
        public int? RandomSeed { get; init; }

        /// <summary>
        /// Default configuration with -365 days fixed offset.
        /// </summary>
        public static DateShiftConfig Default { get; } = new();

        /// <summary>
        /// Configuration with no date shifting.
        /// </summary>
        public static DateShiftConfig None { get; } = new() { Strategy = DateShiftStrategy.None };

        /// <summary>
        /// Research preset with random offset per patient.
        /// </summary>
        public static DateShiftConfig Research { get; } = new()
        {
            Strategy = DateShiftStrategy.RandomPerPatient,
            MinOffsetDays = -365,
            MaxOffsetDays = 365
        };

        /// <summary>
        /// Clinical trial preset - shifts dates, removes time component.
        /// </summary>
        public static DateShiftConfig ClinicalTrial { get; } = new()
        {
            Strategy = DateShiftStrategy.RemoveTime,
            FixedOffset = TimeSpan.FromDays(-100)
        };
    }

    /// <summary>
    /// Strategy for date shifting.
    /// </summary>
    public enum DateShiftStrategy
    {
        /// <summary>No date shifting - keep original dates.</summary>
        None,

        /// <summary>Apply a fixed offset to all dates.</summary>
        Fixed,

        /// <summary>Apply a random but consistent offset per patient.</summary>
        RandomPerPatient,

        /// <summary>Remove time component, keep date shifted.</summary>
        RemoveTime,

        /// <summary>Remove date entirely (replace with dummy value).</summary>
        Remove
    }

    /// <summary>
    /// Result of date shifting operation containing statistics.
    /// </summary>
    public sealed class DateShiftResult
    {
        /// <summary>
        /// Gets or sets the offset that was applied.
        /// </summary>
        public TimeSpan AppliedOffset { get; set; }

        /// <summary>
        /// Gets or sets the number of DA (date) values shifted.
        /// </summary>
        public int DatesShifted { get; set; }

        /// <summary>
        /// Gets or sets the number of TM (time) values shifted.
        /// </summary>
        public int TimesShifted { get; set; }

        /// <summary>
        /// Gets or sets the number of DT (datetime) values shifted.
        /// </summary>
        public int DateTimesShifted { get; set; }

        /// <summary>
        /// Gets the list of warnings encountered during shifting.
        /// </summary>
        public List<string> Warnings { get; } = new();

        /// <summary>
        /// Gets the total number of values shifted.
        /// </summary>
        public int TotalShifted => DatesShifted + TimesShifted + DateTimesShifted;
    }
}
