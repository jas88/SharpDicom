using System;
using System.Collections.Generic;
using System.Globalization;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Represents a single item from a Modality Worklist query result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WorklistItem provides typed access to common MWL attributes including
    /// patient demographics, procedure information, and scheduled procedure steps.
    /// All properties are nullable because attributes may not be returned by the
    /// worklist provider.
    /// </para>
    /// <para>
    /// For vendor-specific or uncommon attributes, use the <see cref="Dataset"/> property
    /// to access the underlying DICOM dataset directly.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// await foreach (var item in mwl.QueryAsync(query))
    /// {
    ///     Console.WriteLine($"Patient: {item.PatientName}");
    ///     Console.WriteLine($"ID: {item.PatientId}");
    ///     foreach (var sps in item.ScheduledProcedureSteps)
    ///     {
    ///         Console.WriteLine($"  Modality: {sps.Modality} at {sps.ScheduledStartDateTime}");
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class WorklistItem
    {
        private readonly List<ScheduledProcedureStep> _scheduledProcedureSteps;

        /// <summary>
        /// Initializes a new instance from an MWL result dataset.
        /// </summary>
        /// <param name="dataset">The MWL result dataset.</param>
        /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
        public WorklistItem(DicomDataset dataset)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
#endif
            Dataset = dataset;
            _scheduledProcedureSteps = ParseScheduledProcedureSteps(dataset);
        }

        /// <summary>
        /// Gets the underlying DICOM dataset for the worklist item.
        /// </summary>
        /// <remarks>
        /// Use this to access vendor-specific or uncommon attributes not exposed
        /// as typed properties.
        /// </remarks>
        public DicomDataset Dataset { get; }

        #region Patient Attributes

        /// <summary>
        /// Gets the patient's name.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0010,0010). Format: FamilyName^GivenName^MiddleName^Prefix^Suffix.
        /// </remarks>
        public string? PatientName => GetString(DicomTag.PatientName);

        /// <summary>
        /// Gets the patient ID.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0010,0020). Primary patient identifier.
        /// </remarks>
        public string? PatientId => GetString(DicomTag.PatientID);

        /// <summary>
        /// Gets the patient's birth date.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0010,0030). Format: YYYYMMDD.
        /// </remarks>
        public DateTime? PatientBirthDate
        {
            get
            {
                var dateStr = GetString(DicomTag.PatientBirthDate);
                if (dateStr == null)
                    return null;

                if (DateTime.TryParseExact(dateStr, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return date;

                return null;
            }
        }

        /// <summary>
        /// Gets the patient's sex.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0010,0040). Values: M (male), F (female), O (other).
        /// </remarks>
        public string? PatientSex => GetString(DicomTag.PatientSex);

        #endregion

        #region Procedure Attributes

        /// <summary>
        /// Gets the accession number.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0008,0050). RIS-generated order number.
        /// </remarks>
        public string? AccessionNumber => GetString(DicomTag.AccessionNumber);

        /// <summary>
        /// Gets the requested procedure ID.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0040,1001). Identifier for the requested procedure.
        /// </remarks>
        public string? RequestedProcedureId => GetString(DicomTag.RequestedProcedureID);

        /// <summary>
        /// Gets the requested procedure description.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0032,1060). Description of the procedure to perform.
        /// </remarks>
        public string? RequestedProcedureDescription => GetString(DicomTag.RequestedProcedureDescription);

        /// <summary>
        /// Gets the Study Instance UID.
        /// </summary>
        /// <remarks>
        /// DICOM tag (0020,000D). Pre-assigned UID for the study to be created.
        /// </remarks>
        public string? StudyInstanceUid => GetString(DicomTag.StudyInstanceUID);

        #endregion

        #region Scheduled Procedure Steps

        /// <summary>
        /// Gets the scheduled procedure steps for this worklist item.
        /// </summary>
        /// <remarks>
        /// Parsed from DICOM tag (0040,0100) - Scheduled Procedure Step Sequence.
        /// Most worklist items contain a single SPS, but multiple are possible.
        /// </remarks>
        public IReadOnlyList<ScheduledProcedureStep> ScheduledProcedureSteps => _scheduledProcedureSteps;

        #endregion

        #region Convenience Properties

        /// <summary>
        /// Gets the first scheduled procedure step, or null if none.
        /// </summary>
        /// <remarks>
        /// Convenience property for the common case where only one SPS exists.
        /// </remarks>
        public ScheduledProcedureStep? FirstScheduledProcedureStep =>
            _scheduledProcedureSteps.Count > 0 ? _scheduledProcedureSteps[0] : null;

        /// <summary>
        /// Gets the modality from the first scheduled procedure step.
        /// </summary>
        /// <remarks>
        /// Convenience property equivalent to FirstScheduledProcedureStep?.Modality.
        /// </remarks>
        public string? Modality => FirstScheduledProcedureStep?.Modality;

        /// <summary>
        /// Gets the scheduled start date/time from the first scheduled procedure step.
        /// </summary>
        /// <remarks>
        /// Convenience property equivalent to FirstScheduledProcedureStep?.ScheduledStartDateTime.
        /// </remarks>
        public DateTime? ScheduledStartDateTime => FirstScheduledProcedureStep?.ScheduledStartDateTime;

        #endregion

        private string? GetString(DicomTag tag)
        {
            if (!Dataset.Contains(tag))
                return null;

            var value = Dataset.GetString(tag);
            if (value == null || string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private static List<ScheduledProcedureStep> ParseScheduledProcedureSteps(DicomDataset dataset)
        {
            var result = new List<ScheduledProcedureStep>();

            if (!dataset.Contains(DicomTag.ScheduledProcedureStepSequence))
                return result;

            var element = dataset[DicomTag.ScheduledProcedureStepSequence];
            if (element is DicomSequence sequence)
            {
                foreach (var item in sequence.Items)
                {
                    result.Add(new ScheduledProcedureStep(item));
                }
            }

            return result;
        }
    }
}
