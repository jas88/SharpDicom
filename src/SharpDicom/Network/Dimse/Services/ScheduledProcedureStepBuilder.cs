using System;
using System.Globalization;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Builder for Scheduled Procedure Step sequence items in MWL queries.
    /// </summary>
    /// <remarks>
    /// Used with <see cref="DicomWorklistQuery.WithScheduledProcedureStep"/> to
    /// configure match keys for the Scheduled Procedure Step Sequence (0040,0100).
    /// </remarks>
    public sealed class ScheduledProcedureStepBuilder
    {
        private readonly DicomDataset _dataset = new();

        /// <summary>
        /// Gets the built dataset for the SPS item.
        /// </summary>
        internal DicomDataset ToDataset() => _dataset;

        /// <summary>
        /// Adds modality matching criterion.
        /// </summary>
        /// <param name="modality">Modality code (e.g., "CT", "MR", "US").</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder Modality(string modality)
        {
            AddString(DicomTag.Modality, modality, DicomVR.CS);
            return this;
        }

        /// <summary>
        /// Adds scheduled station AE title matching criterion.
        /// </summary>
        /// <param name="aeTitle">AE title of the scheduled station.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledStationAETitle(string aeTitle)
        {
            AddString(DicomTag.ScheduledStationAETitle, aeTitle, DicomVR.AE);
            return this;
        }

        /// <summary>
        /// Adds scheduled station name matching criterion.
        /// </summary>
        /// <param name="stationName">Name of the scheduled station.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledStationName(string stationName)
        {
            AddString(DicomTag.ScheduledStationName, stationName, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Adds scheduled date matching criterion for a specific date.
        /// </summary>
        /// <param name="date">The scheduled date to match.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledDate(DateTime date)
        {
            AddString(DicomTag.ScheduledProcedureStepStartDate,
                date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Adds scheduled date range matching criterion.
        /// </summary>
        /// <param name="from">Start date (inclusive).</param>
        /// <param name="to">End date (inclusive).</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledDateRange(DateTime from, DateTime to)
        {
            var range = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyyMMdd}-{1:yyyyMMdd}",
                from, to);
            AddString(DicomTag.ScheduledProcedureStepStartDate, range, DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Adds scheduled time matching criterion.
        /// </summary>
        /// <param name="time">The scheduled time to match.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledTime(TimeSpan time)
        {
            var timeStr = string.Format(CultureInfo.InvariantCulture,
                "{0:D2}{1:D2}{2:D2}", time.Hours, time.Minutes, time.Seconds);
            AddString(DicomTag.ScheduledProcedureStepStartTime, timeStr, DicomVR.TM);
            return this;
        }

        /// <summary>
        /// Adds scheduled performing physician name matching criterion.
        /// </summary>
        /// <param name="physicianName">Physician name pattern.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledPerformingPhysician(string physicianName)
        {
            AddString(DicomTag.ScheduledPerformingPhysicianName, physicianName, DicomVR.PN);
            return this;
        }

        /// <summary>
        /// Adds scheduled procedure step ID matching criterion.
        /// </summary>
        /// <param name="spsId">Scheduled Procedure Step ID.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ScheduledProcedureStepId(string spsId)
        {
            AddString(DicomTag.ScheduledProcedureStepID, spsId, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Requests a field to be returned in results (zero-length value).
        /// </summary>
        /// <param name="tag">The DICOM tag to request.</param>
        /// <returns>This builder for fluent chaining.</returns>
        public ScheduledProcedureStepBuilder ReturnField(DicomTag tag)
        {
            if (!_dataset.Contains(tag))
            {
                var vr = DicomVR.LO;
                var entry = DicomDictionary.Default.GetEntry(tag);
                if (entry?.ValueRepresentations?.Length > 0)
                {
                    vr = entry.Value.ValueRepresentations![0];
                }
                _dataset.Add(new DicomStringElement(tag, vr, Array.Empty<byte>()));
            }
            return this;
        }

        private void AddString(DicomTag tag, string value, DicomVR vr)
        {
            var bytes = Encoding.ASCII.GetBytes(value);

            // Pad to even length per DICOM
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)'\0' : (byte)' ';
                bytes = padded;
            }

            _dataset.Add(new DicomStringElement(tag, vr, bytes));
        }
    }
}
