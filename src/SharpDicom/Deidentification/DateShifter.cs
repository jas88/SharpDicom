using System;
using System.Collections.Generic;
using System.Globalization;
using SharpDicom.Data;

namespace SharpDicom.Deidentification
{
    /// <summary>
    /// Shifts dates consistently for de-identification while preserving temporal relationships.
    /// </summary>
    public sealed class DateShifter
    {
        private readonly DateShiftConfig _config;
        private readonly Dictionary<string, TimeSpan> _patientOffsets = new();
        private readonly object _lock = new();
        private readonly Random? _random;

        /// <summary>
        /// Creates a date shifter with the specified configuration.
        /// </summary>
        /// <param name="config">The date shift configuration.</param>
        public DateShifter(DateShiftConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            if (_config.Strategy == DateShiftStrategy.RandomPerPatient)
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
                _ => TimeSpan.Zero
            };
        }

        private TimeSpan GetOrCreatePatientOffset(string patientId)
        {
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
        public string ShiftDate(string? dateString, string? patientId)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                return string.Empty;
            }

            if (dateString!.Length < 8)
            {
                return dateString; // Invalid format, return as-is
            }

            if (!TryParseDate(dateString!, out var date))
            {
                return dateString; // Can't parse, return as-is
            }

            var offset = GetOffset(patientId);
            var shifted = date.Add(offset);

            return shifted.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Shifts a DICOM time string.
        /// </summary>
        /// <param name="timeString">The time string in HHMMSS.FFFFFF format.</param>
        /// <param name="patientId">The patient ID (not used for time-only shifting).</param>
        /// <returns>The time string (unchanged unless config specifies time shifting).</returns>
#pragma warning disable CA1822 // Mark members as static - kept instance for API consistency
        public string ShiftTime(string? timeString, string? patientId)
#pragma warning restore CA1822
        {
            // Suppress unused parameter warning - parameter kept for API consistency
            _ = patientId;

            // Time shifting is generally not needed for de-identification
            // Preserve as-is
            return timeString ?? string.Empty;
        }

        /// <summary>
        /// Shifts a DICOM datetime string.
        /// </summary>
        /// <param name="dateTimeString">The datetime string.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>The shifted datetime string.</returns>
        public string ShiftDateTime(string? dateTimeString, string? patientId)
        {
            if (string.IsNullOrWhiteSpace(dateTimeString))
            {
                return string.Empty;
            }

            if (dateTimeString!.Length < 8)
            {
                return dateTimeString; // Invalid format
            }

            // Extract date part and shift it
            var datePart = dateTimeString!.Substring(0, 8);
            var timePart = dateTimeString!.Length > 8 ? dateTimeString!.Substring(8) : "";

            var shiftedDate = ShiftDate(datePart, patientId);
            return shiftedDate + timePart;
        }

        /// <summary>
        /// Shifts all date/time elements in a dataset.
        /// </summary>
        /// <param name="dataset">The dataset to process.</param>
        /// <param name="patientId">The patient ID for consistent shifting.</param>
        /// <returns>Number of dates shifted.</returns>
        public int ShiftDates(DicomDataset dataset, string? patientId = null)
        {
            // Try to get patient ID from dataset if not provided
            if (string.IsNullOrEmpty(patientId))
            {
                patientId = dataset.GetString(DicomTag.PatientID);
            }

            int count = 0;
            count += ShiftDatesInternal(dataset, patientId);
            return count;
        }

        private int ShiftDatesInternal(DicomDataset dataset, string? patientId)
        {
            int count = 0;

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
                        count += ShiftDatesInternal(item, patientId);
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
                    }
                    else if (element.VR == DicomVR.DT)
                    {
                        newValue = ShiftDateTime(originalValue, patientId);
                    }

                    if (newValue != null && newValue != originalValue)
                    {
                        var bytes = System.Text.Encoding.ASCII.GetBytes(newValue);
                        var newElement = new DicomStringElement(tag, element.VR, bytes);
                        dataset.Add(newElement);
                        count++;
                    }
                }
            }

            return count;
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
        RandomPerPatient
    }
}
