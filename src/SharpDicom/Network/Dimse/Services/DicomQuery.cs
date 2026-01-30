using System;
using System.Globalization;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Fluent builder for DICOM C-FIND queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DicomQuery provides a convenient way to construct C-FIND query identifiers
    /// without manually building a DicomDataset. It supports common query parameters
    /// and automatically handles DICOM encoding rules.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var query = DicomQuery.ForStudies()
    ///     .WithPatientName("Smith*")
    ///     .WithStudyDateRange(DateTime.Today.AddDays(-7), DateTime.Today)
    ///     .WithModality("CT", "MR")
    ///     .ReturnField(DicomTag.StudyDescription);
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class DicomQuery
    {
        private readonly DicomDataset _dataset = new();
        private readonly QueryRetrieveLevel _level;

        private DicomQuery(QueryRetrieveLevel level)
        {
            _level = level;
            AddString(DicomTag.QueryRetrieveLevel, level.ToDicomValue(), DicomVR.CS);
        }

        /// <summary>
        /// Creates a patient-level query.
        /// </summary>
        /// <returns>A new DicomQuery for patient-level queries.</returns>
        public static DicomQuery ForPatients() => new(QueryRetrieveLevel.Patient);

        /// <summary>
        /// Creates a study-level query.
        /// </summary>
        /// <returns>A new DicomQuery for study-level queries.</returns>
        public static DicomQuery ForStudies() => new(QueryRetrieveLevel.Study);

        /// <summary>
        /// Creates a series-level query.
        /// </summary>
        /// <returns>A new DicomQuery for series-level queries.</returns>
        public static DicomQuery ForSeries() => new(QueryRetrieveLevel.Series);

        /// <summary>
        /// Creates an image-level query.
        /// </summary>
        /// <returns>A new DicomQuery for image-level queries.</returns>
        public static DicomQuery ForImages() => new(QueryRetrieveLevel.Image);

        /// <summary>
        /// Adds patient name matching criterion.
        /// </summary>
        /// <param name="pattern">Patient name pattern (supports wildcards * and ?).</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// DICOM wildcard matching:
        /// <list type="bullet">
        ///   <item><description>* matches zero or more characters</description></item>
        ///   <item><description>? matches exactly one character</description></item>
        /// </list>
        /// </remarks>
        public DicomQuery WithPatientName(string pattern)
        {
            AddString(DicomTag.PatientName, pattern, DicomVR.PN);
            return this;
        }

        /// <summary>
        /// Adds patient ID matching criterion.
        /// </summary>
        /// <param name="id">Patient ID value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithPatientId(string id)
        {
            AddString(DicomTag.PatientID, id, DicomVR.LO);
            return this;
        }

        /// <summary>
        /// Adds study date matching criterion for a specific date.
        /// </summary>
        /// <param name="date">The study date to match.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithStudyDate(DateTime date)
        {
            AddString(DicomTag.StudyDate, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Adds study date range matching criterion.
        /// </summary>
        /// <param name="from">Start date (inclusive).</param>
        /// <param name="to">End date (inclusive).</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// DICOM date ranges are formatted as "YYYYMMDD-YYYYMMDD".
        /// </remarks>
        public DicomQuery WithStudyDateRange(DateTime from, DateTime to)
        {
            var range = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyyMMdd}-{1:yyyyMMdd}",
                from, to);
            AddString(DicomTag.StudyDate, range, DicomVR.DA);
            return this;
        }

        /// <summary>
        /// Adds modality matching criterion.
        /// </summary>
        /// <param name="modalities">One or more modality codes (e.g., "CT", "MR", "US").</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// Uses ModalitiesInStudy (0008,0061) for patient/study-level queries,
        /// and Modality (0008,0060) for series/image-level queries.
        /// Multiple modalities are separated by backslash.
        /// </remarks>
        public DicomQuery WithModality(params string[] modalities)
        {
            // For series and image level, use Modality (0008,0060)
            // For patient and study level, use ModalitiesInStudy (0008,0061)
            var tag = _level == QueryRetrieveLevel.Series || _level == QueryRetrieveLevel.Image
                ? DicomTag.Modality
                : DicomTag.ModalitiesInStudy;
            AddString(tag, string.Join("\\", modalities), DicomVR.CS);
            return this;
        }

        /// <summary>
        /// Adds accession number matching criterion.
        /// </summary>
        /// <param name="accession">Accession number value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithAccessionNumber(string accession)
        {
            AddString(DicomTag.AccessionNumber, accession, DicomVR.SH);
            return this;
        }

        /// <summary>
        /// Adds Study Instance UID matching criterion.
        /// </summary>
        /// <param name="uid">Study Instance UID value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithStudyInstanceUid(string uid)
        {
            AddString(DicomTag.StudyInstanceUID, uid, DicomVR.UI);
            return this;
        }

        /// <summary>
        /// Adds Series Instance UID matching criterion.
        /// </summary>
        /// <param name="uid">Series Instance UID value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithSeriesInstanceUid(string uid)
        {
            AddString(DicomTag.SeriesInstanceUID, uid, DicomVR.UI);
            return this;
        }

        /// <summary>
        /// Adds SOP Instance UID matching criterion.
        /// </summary>
        /// <param name="uid">SOP Instance UID value.</param>
        /// <returns>This query for fluent chaining.</returns>
        public DicomQuery WithSopInstanceUid(string uid)
        {
            AddString(DicomTag.SOPInstanceUID, uid, DicomVR.UI);
            return this;
        }

        /// <summary>
        /// Requests a field to be returned in results (zero-length value).
        /// </summary>
        /// <param name="tag">The DICOM tag to request.</param>
        /// <returns>This query for fluent chaining.</returns>
        /// <remarks>
        /// Adding a tag with zero-length value tells the SCP to include
        /// this field in matching responses. If the tag already exists
        /// with a value (as a matching criterion), it is not modified.
        /// </remarks>
        public DicomQuery ReturnField(DicomTag tag)
        {
            if (!_dataset.Contains(tag))
            {
                // Add zero-length element to request this field
                // Use the VR from the dictionary if available
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

        /// <summary>
        /// Gets the query/retrieve level.
        /// </summary>
        public QueryRetrieveLevel Level => _level;

        /// <summary>
        /// Converts the query to a DicomDataset for use with C-FIND operations.
        /// </summary>
        /// <returns>The query dataset.</returns>
        public DicomDataset ToDataset() => _dataset;

        private void AddString(DicomTag tag, string value, DicomVR vr)
        {
            var bytes = Encoding.ASCII.GetBytes(value);

            // Pad to even length per DICOM
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                // UI padded with null, others with space
                padded[padded.Length - 1] = vr == DicomVR.UI ? (byte)'\0' : (byte)' ';
                bytes = padded;
            }

            _dataset.Add(new DicomStringElement(tag, vr, bytes));
        }
    }
}
