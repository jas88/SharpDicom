using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Fluent builder for Modality Worklist (MWL) C-FIND queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DicomWorklistQuery provides a convenient way to construct MWL query identifiers
    /// without manually building a DicomDataset. It handles the Scheduled Procedure Step
    /// Sequence structure automatically.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var query = DicomWorklistQuery.ForToday()
    ///     .WithScheduledProcedureStep(sps => sps
    ///         .Modality("CT")
    ///         .ScheduledStationAETitle("CT_SCANNER_1"));
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class DicomWorklistQuery
    {
        private readonly DicomDataset _dataset = new();
        private readonly List<DicomTag> _returnKeys = new();
        private DicomDataset? _spsDataset;

        // Default return keys for MWL queries
        private static readonly DicomTag[] DefaultReturnKeys = new[]
        {
            DicomTag.PatientName,
            DicomTag.PatientID,
            DicomTag.PatientBirthDate,
            DicomTag.PatientSex,
            DicomTag.AccessionNumber,
            DicomTag.RequestedProcedureID,
            DicomTag.RequestedProcedureDescription,
            DicomTag.StudyInstanceUID,
        };

        // Default SPS return keys
        private static readonly DicomTag[] DefaultSpsReturnKeys = new[]
        {
            DicomTag.Modality,
            DicomTag.ScheduledStationAETitle,
            DicomTag.ScheduledStationName,
            DicomTag.ScheduledProcedureStepStartDate,
            DicomTag.ScheduledProcedureStepStartTime,
            DicomTag.ScheduledProcedureStepID,
            DicomTag.ScheduledProcedureStepDescription,
            DicomTag.ScheduledPerformingPhysicianName,
        };

        private DicomWorklistQuery()
        {
        }

        #region Static Factory Methods

        /// <summary>
        /// Creates a query for all procedures scheduled today.
        /// </summary>
        /// <returns>A new DicomWorklistQuery for today's worklist.</returns>
        public static DicomWorklistQuery ForToday()
        {
            var query = new DicomWorklistQuery();
            query.WithScheduledProcedureStep(sps => sps.ScheduledDate(DateTime.Today));
            return query;
        }

        /// <summary>
        /// Creates a query filtered by modality.
        /// </summary>
        /// <param name="modality">Modality code (e.g., "CT", "MR", "US").</param>
        /// <returns>A new DicomWorklistQuery for the specified modality.</returns>
        public static DicomWorklistQuery ForModality(string modality)
        {
            var query = new DicomWorklistQuery();
            query.WithScheduledProcedureStep(sps => sps.Modality(modality));
            return query;
        }

        /// <summary>
        /// Creates a query filtered by scheduled station AE title.
        /// </summary>
        /// <param name="stationAeTitle">AE title of the scheduled station.</param>
        /// <returns>A new DicomWorklistQuery for the specified station.</returns>
        public static DicomWorklistQuery ForStation(string stationAeTitle)
        {
            var query = new DicomWorklistQuery();
            query.WithScheduledProcedureStep(sps => sps.ScheduledStationAETitle(stationAeTitle));
            return query;
        }

        /// <summary>
        /// Creates a query filtered by patient name or ID.
        /// </summary>
        /// <param name="patientNameOrId">Patient name pattern or ID.</param>
        /// <returns>A new DicomWorklistQuery for the specified patient.</returns>
        /// <remarks>
        /// If the value contains a caret (^), asterisk (*), or question mark (?),
        /// it's treated as a patient name pattern. Otherwise, it's treated as a patient ID.
        /// </remarks>
        public static DicomWorklistQuery ForPatient(string patientNameOrId)
        {
            var query = new DicomWorklistQuery();
#if NET6_0_OR_GREATER
            if (patientNameOrId.Contains('^') || patientNameOrId.Contains('*') || patientNameOrId.Contains('?'))
#else
            if (patientNameOrId.IndexOf('^') >= 0 || patientNameOrId.IndexOf('*') >= 0 || patientNameOrId.IndexOf('?') >= 0)
#endif
            {
                query.WithPatientName(patientNameOrId);
            }
            else
            {
                query.WithPatientId(patientNameOrId);
            }
            return query;
        }

        /// <summary>
        /// Creates a query for procedures scheduled on a specific date.
        /// </summary>
        /// <param name="date">The scheduled date.</param>
        /// <returns>A new DicomWorklistQuery for the specified date.</returns>
        public static DicomWorklistQuery ForDate(DateTime date)
        {
            var query = new DicomWorklistQuery();
            query.WithScheduledProcedureStep(sps => sps.ScheduledDate(date));
            return query;
        }

        /// <summary>
        /// Creates a query for procedures scheduled within a date range.
        /// </summary>
        /// <param name="from">Start date (inclusive).</param>
        /// <param name="to">End date (inclusive).</param>
        /// <returns>A new DicomWorklistQuery for the specified date range.</returns>
        public static DicomWorklistQuery ForDateRange(DateTime from, DateTime to)
        {
            var query = new DicomWorklistQuery();
            query.WithScheduledProcedureStep(sps => sps.ScheduledDateRange(from, to));
            return query;
        }

        /// <summary>
        /// Creates an empty query (matches all worklist items).
        /// </summary>
        /// <returns>A new DicomWorklistQuery with no match criteria.</returns>
        public static DicomWorklistQuery All() => new DicomWorklistQuery();

        #endregion

        #region Patient Attributes

        /// <summary>
        /// Adds patient name matching criterion.
        /// </summary>
        /// <param name="pattern">Patient name pattern (supports wildcards * and ?).</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithPatientName(string pattern)
        {
            AddString(DicomTag.PatientName, pattern, DicomVR.PN);
            return this;
        }

        /// <summary>
        /// Adds patient ID matching criterion.
        /// </summary>
        /// <param name="id">Patient ID value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithPatientId(string id)
        {
            AddString(DicomTag.PatientID, id, DicomVR.LO);
            return this;
        }

        /// <summary>
        /// Adds patient birth date matching criterion.
        /// </summary>
        /// <param name="birthDate">Patient's birth date.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithPatientBirthDate(DateTime birthDate)
        {
            AddString(DicomTag.PatientBirthDate,
                birthDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Adds patient sex matching criterion.
        /// </summary>
        /// <param name="sex">Patient sex ("M", "F", or "O").</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithPatientSex(string sex)
        {
            AddString(DicomTag.PatientSex, sex, DicomVR.CS);
            return this;
        }

        #endregion

        #region Study/Procedure Attributes

        /// <summary>
        /// Adds accession number matching criterion.
        /// </summary>
        /// <param name="accessionNumber">Accession number value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithAccessionNumber(string accessionNumber)
        {
            AddString(DicomTag.AccessionNumber, accessionNumber, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Adds requested procedure ID matching criterion.
        /// </summary>
        /// <param name="procedureId">Requested Procedure ID.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithRequestedProcedureId(string procedureId)
        {
            AddString(DicomTag.RequestedProcedureID, procedureId, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Adds requested procedure description matching criterion.
        /// </summary>
        /// <param name="description">Requested Procedure Description pattern.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomWorklistQuery WithRequestedProcedureDescription(string description)
        {
            AddString(DicomTag.RequestedProcedureDescription, description, DicomVR.LO);
            return this;
        }

        #endregion

        #region Scheduled Procedure Step

        /// <summary>
        /// Configures the Scheduled Procedure Step sequence for the query.
        /// </summary>
        /// <param name="configure">Action to configure the SPS builder.</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// This method can be called multiple times. Each call will overwrite
        /// the previous SPS configuration.
        /// </remarks>
        public DicomWorklistQuery WithScheduledProcedureStep(Action<ScheduledProcedureStepBuilder> configure)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(configure);
#else
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));
#endif
            var builder = new ScheduledProcedureStepBuilder();
            configure(builder);
            _spsDataset = builder.ToDataset();
            return this;
        }

        #endregion

        #region Return Keys

        /// <summary>
        /// Requests an additional attribute to be returned in results.
        /// </summary>
        /// <param name="tag">The DICOM tag to request.</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// Default return keys are automatically included. Use this method
        /// to request additional vendor-specific or uncommon attributes.
        /// </remarks>
        public DicomWorklistQuery IncludeReturnKey(DicomTag tag)
        {
            if (!_returnKeys.Contains(tag))
            {
                _returnKeys.Add(tag);
            }
            return this;
        }

        #endregion

        #region Output

        /// <summary>
        /// Converts the query to a DicomDataset for use with MWL C-FIND operations.
        /// </summary>
        /// <returns>The query dataset with SPS sequence.</returns>
        public DicomDataset ToDataset()
        {
            var result = new DicomDataset();

            // Add match keys from main dataset
            foreach (var element in _dataset)
            {
                result.Add(element);
            }

            // Add default return keys as zero-length if not already present
            foreach (var tag in DefaultReturnKeys)
            {
                AddReturnKeyIfMissing(result, tag);
            }

            // Add custom return keys
            foreach (var tag in _returnKeys)
            {
                AddReturnKeyIfMissing(result, tag);
            }

            // Build SPS sequence
            var spsItem = _spsDataset ?? new DicomDataset();

            // Add default SPS return keys
            foreach (var tag in DefaultSpsReturnKeys)
            {
                if (!spsItem.Contains(tag))
                {
                    var vr = GetVrForTag(tag);
                    spsItem.Add(new DicomStringElement(tag, vr, Array.Empty<byte>()));
                }
            }

            // Add SPS sequence to result
            result.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, new[] { spsItem }));

            return result;
        }

        private static void AddReturnKeyIfMissing(DicomDataset dataset, DicomTag tag)
        {
            if (!dataset.Contains(tag))
            {
                var vr = GetVrForTag(tag);
                dataset.Add(new DicomStringElement(tag, vr, Array.Empty<byte>()));
            }
        }

        private static DicomVR GetVrForTag(DicomTag tag)
        {
            var entry = DicomDictionary.Default.GetEntry(tag);
            if (entry?.ValueRepresentations?.Length > 0)
            {
                return entry.Value.ValueRepresentations![0];
            }
            return DicomVR.LO;
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

        #endregion
    }
}
