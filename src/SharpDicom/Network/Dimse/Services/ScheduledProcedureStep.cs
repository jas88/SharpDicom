using System;
using System.Globalization;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Represents a Scheduled Procedure Step from a Modality Worklist query result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides typed access to common SPS attributes. All properties
    /// are nullable because attributes may not be returned by the worklist provider.
    /// </para>
    /// <para>
    /// For vendor-specific or uncommon attributes, use the <see cref="Dataset"/> property
    /// to access the underlying DICOM dataset directly.
    /// </para>
    /// </remarks>
    public sealed class ScheduledProcedureStep
    {
        /// <summary>
        /// Initializes a new instance from an SPS sequence item dataset.
        /// </summary>
        /// <param name="dataset">The SPS item dataset.</param>
        /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
        public ScheduledProcedureStep(DicomDataset dataset)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
#endif
            Dataset = dataset;
        }

        /// <summary>
        /// Gets the underlying DICOM dataset for the SPS item.
        /// </summary>
        /// <remarks>
        /// Use this to access vendor-specific or uncommon attributes not exposed
        /// as typed properties.
        /// </remarks>
        public DicomDataset Dataset { get; }

        /// <summary>
        /// Gets the modality for the scheduled procedure step.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0008,0060). Common values: CT, MR, US, CR, DX, XA, etc.
        /// </remarks>
        public string? Modality => GetString(DicomTag.Modality);

        /// <summary>
        /// Gets the scheduled station AE title.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0001). The Application Entity title of the scheduled imaging device.
        /// </remarks>
        public string? ScheduledStationAETitle => GetString(DicomTag.ScheduledStationAETitle);

        /// <summary>
        /// Gets the scheduled station name.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0010). Human-readable name of the imaging device.
        /// </remarks>
        public string? ScheduledStationName => GetString(DicomTag.ScheduledStationName);

        /// <summary>
        /// Gets the scheduled procedure step start date and time.
        /// </summary>
        /// <remarks>
        /// Combined from DICOM tags (0040,0002) and (0040,0003).
        /// Returns null if date is not available.
        /// </remarks>
        public DateTime? ScheduledStartDateTime
        {
            get
            {
                var dateStr = GetString(DicomTag.ScheduledProcedureStepStartDate);
                if (dateStr == null)
                    return null;

                // Parse date (YYYYMMDD)
                if (!DateTime.TryParseExact(dateStr, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return null;

                // Try to add time if available
                var timeStr = GetString(DicomTag.ScheduledProcedureStepStartTime);
                if (timeStr != null)
                {
                    var time = ParseDicomTime(timeStr);
                    if (time.HasValue)
                    {
                        date = date.Add(time.Value);
                    }
                }

                return date;
            }
        }

        /// <summary>
        /// Gets the scheduled procedure step ID.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0009). Unique identifier for the scheduled procedure step.
        /// </remarks>
        public string? ScheduledProcedureStepId => GetString(DicomTag.ScheduledProcedureStepID);

        /// <summary>
        /// Gets the scheduled procedure step description.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0007). Description of the scheduled procedure step.
        /// </remarks>
        public string? ScheduledProcedureStepDescription => GetString(DicomTag.ScheduledProcedureStepDescription);

        /// <summary>
        /// Gets the scheduled performing physician's name.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0006). Name of the physician scheduled to perform the procedure.
        /// </remarks>
        public string? ScheduledPerformingPhysicianName => GetString(DicomTag.ScheduledPerformingPhysicianName);

        /// <summary>
        /// Gets the scheduled procedure step location.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,0011). Location where the procedure step is scheduled.
        /// </remarks>
        public string? ScheduledProcedureStepLocation => GetString(DicomTag.ScheduledProcedureStepLocation);

        private string? GetString(DicomTag tag)
        {
            if (!Dataset.Contains(tag))
                return null;

            var value = Dataset.GetString(tag);
            if (value == null || string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private static TimeSpan? ParseDicomTime(string timeStr)
        {
            // DICOM TM format: HHMMSS.FFFFFF (with optional precision)
            // Minimum: HH, Maximum: HHMMSS.FFFFFF

            if (timeStr.Length < 2)
                return null;

            int hours, minutes = 0, seconds = 0, milliseconds = 0;

#if NET6_0_OR_GREATER
            var span = timeStr.AsSpan();

            if (!int.TryParse(span.Slice(0, 2), out hours))
                return null;

            if (span.Length >= 4)
            {
                _ = int.TryParse(span.Slice(2, 2), out minutes);
            }

            if (span.Length >= 6)
            {
                var secLen = span.Length > 6 && span[6] == '.' ? 2 : Math.Min(2, span.Length - 4);
                _ = int.TryParse(span.Slice(4, secLen), out seconds);

                var dotIndex = span.IndexOf('.');
                if (dotIndex >= 0 && dotIndex < span.Length - 1)
                {
                    var fracSpan = span.Slice(dotIndex + 1);
                    Span<char> fracBuf = stackalloc char[3];
                    var copyLen = Math.Min(3, fracSpan.Length);
                    fracSpan.Slice(0, copyLen).CopyTo(fracBuf);
                    for (var i = copyLen; i < 3; i++)
                        fracBuf[i] = '0';

                    _ = int.TryParse(fracBuf, out milliseconds);
                }
            }
#else
            if (!int.TryParse(timeStr.Substring(0, 2), out hours))
                return null;

            if (timeStr.Length >= 4)
            {
                _ = int.TryParse(timeStr.Substring(2, 2), out minutes);
            }

            if (timeStr.Length >= 6)
            {
                var secPart = timeStr.Length > 6 && timeStr[6] == '.'
                    ? timeStr.Substring(4, 2)
                    : timeStr.Substring(4, Math.Min(2, timeStr.Length - 4));

                _ = int.TryParse(secPart, out seconds);

                var dotIndex = timeStr.IndexOf('.');
                if (dotIndex >= 0 && dotIndex < timeStr.Length - 1)
                {
                    var fracPart = timeStr.Substring(dotIndex + 1);
                    if (fracPart.Length > 3)
                        fracPart = fracPart.Substring(0, 3);
                    else
                        fracPart = fracPart.PadRight(3, '0');

                    _ = int.TryParse(fracPart, out milliseconds);
                }
            }
#endif

            try
            {
                return new TimeSpan(0, hours, minutes, seconds, milliseconds);
            }
            catch
            {
                return null;
            }
        }
    }
}
