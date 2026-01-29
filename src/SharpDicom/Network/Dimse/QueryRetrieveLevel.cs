using System;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse
{
    /// <summary>
    /// Defines the Query/Retrieve level for C-FIND, C-MOVE, and C-GET operations.
    /// </summary>
    /// <remarks>
    /// The Q/R level determines the granularity of query and retrieve operations.
    /// See DICOM PS3.4 Annex C for Information Model definitions.
    /// </remarks>
    public enum QueryRetrieveLevel
    {
        /// <summary>
        /// Patient level - queries/retrieves at patient level.
        /// </summary>
        Patient = 0,

        /// <summary>
        /// Study level - queries/retrieves at study level.
        /// </summary>
        Study = 1,

        /// <summary>
        /// Series level - queries/retrieves at series level.
        /// </summary>
        Series = 2,

        /// <summary>
        /// Image level - queries/retrieves at individual image/instance level.
        /// </summary>
        Image = 3
    }

    /// <summary>
    /// Extension methods for <see cref="QueryRetrieveLevel"/>.
    /// </summary>
    public static class QueryRetrieveLevelExtensions
    {
        /// <summary>
        /// Converts the level to its DICOM string representation.
        /// </summary>
        /// <param name="level">The query/retrieve level.</param>
        /// <returns>The DICOM string value ("PATIENT", "STUDY", "SERIES", or "IMAGE").</returns>
        public static string ToDicomValue(this QueryRetrieveLevel level)
        {
            return level switch
            {
                QueryRetrieveLevel.Patient => "PATIENT",
                QueryRetrieveLevel.Study => "STUDY",
                QueryRetrieveLevel.Series => "SERIES",
                QueryRetrieveLevel.Image => "IMAGE",
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Invalid QueryRetrieveLevel value.")
            };
        }

        /// <summary>
        /// Parses a DICOM string value to a <see cref="QueryRetrieveLevel"/>.
        /// </summary>
        /// <param name="value">The DICOM string value.</param>
        /// <returns>The corresponding <see cref="QueryRetrieveLevel"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when value is not a valid level.</exception>
        public static QueryRetrieveLevel Parse(string value)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(value);
#else
            if (value == null)
                throw new ArgumentNullException(nameof(value));
#endif
            return value.Trim().ToUpperInvariant() switch
            {
                "PATIENT" => QueryRetrieveLevel.Patient,
                "STUDY" => QueryRetrieveLevel.Study,
                "SERIES" => QueryRetrieveLevel.Series,
                "IMAGE" => QueryRetrieveLevel.Image,
                _ => throw new ArgumentException($"Invalid QueryRetrieveLevel value: '{value}'.", nameof(value))
            };
        }

        /// <summary>
        /// Tries to parse a DICOM string value to a <see cref="QueryRetrieveLevel"/>.
        /// </summary>
        /// <param name="value">The DICOM string value.</param>
        /// <param name="level">When successful, the parsed level.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryParse(string? value, out QueryRetrieveLevel level)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                level = default;
                return false;
            }

            level = value!.Trim().ToUpperInvariant() switch
            {
                "PATIENT" => QueryRetrieveLevel.Patient,
                "STUDY" => QueryRetrieveLevel.Study,
                "SERIES" => QueryRetrieveLevel.Series,
                "IMAGE" => QueryRetrieveLevel.Image,
                _ => (QueryRetrieveLevel)(-1)
            };

            return (int)level >= 0;
        }

        /// <summary>
        /// Gets the Patient Root SOP Class UID for the specified operation type.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used for Patient Root, all levels use same UID).</param>
        /// <param name="commandField">The DIMSE command field (C-FIND, C-MOVE, or C-GET).</param>
        /// <returns>The Patient Root SOP Class UID for the operation.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid command field.</exception>
        /// <remarks>
        /// Patient Root Q/R uses the same SOP Class UID regardless of query level.
        /// </remarks>
        public static DicomUID GetPatientRootSopClassUid(this QueryRetrieveLevel level, ushort commandField)
        {
            // Mask off response bit to get base command
            var baseCommand = (ushort)(commandField & 0x7FFF);
            return baseCommand switch
            {
                CommandField.CFindRequest => DicomUID.PatientRootQueryRetrieveFind,
                CommandField.CMoveRequest => DicomUID.PatientRootQueryRetrieveMove,
                CommandField.CGetRequest => DicomUID.PatientRootQueryRetrieveGet,
                _ => throw new ArgumentException($"Invalid command field for Q/R: 0x{commandField:X4}", nameof(commandField))
            };
        }

        /// <summary>
        /// Gets the Study Root SOP Class UID for the specified operation type.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used for Study Root, all levels use same UID).</param>
        /// <param name="commandField">The DIMSE command field (C-FIND, C-MOVE, or C-GET).</param>
        /// <returns>The Study Root SOP Class UID for the operation.</returns>
        /// <exception cref="ArgumentException">Thrown for invalid command field.</exception>
        /// <remarks>
        /// Study Root Q/R uses the same SOP Class UID regardless of query level.
        /// Note: Study Root does not support Patient level queries.
        /// </remarks>
        public static DicomUID GetStudyRootSopClassUid(this QueryRetrieveLevel level, ushort commandField)
        {
            // Mask off response bit to get base command
            var baseCommand = (ushort)(commandField & 0x7FFF);
            return baseCommand switch
            {
                CommandField.CFindRequest => DicomUID.StudyRootQueryRetrieveFind,
                CommandField.CMoveRequest => DicomUID.StudyRootQueryRetrieveMove,
                CommandField.CGetRequest => DicomUID.StudyRootQueryRetrieveGet,
                _ => throw new ArgumentException($"Invalid command field for Q/R: 0x{commandField:X4}", nameof(commandField))
            };
        }

        /// <summary>
        /// Gets the Patient Root C-FIND SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Patient Root Query/Retrieve Find SOP Class UID.</returns>
        public static DicomUID GetPatientRootFindSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.PatientRootQueryRetrieveFind;

        /// <summary>
        /// Gets the Study Root C-FIND SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Study Root Query/Retrieve Find SOP Class UID.</returns>
        /// <remarks>
        /// Note: Study Root does not support Patient level queries.
        /// </remarks>
        public static DicomUID GetStudyRootFindSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.StudyRootQueryRetrieveFind;

        /// <summary>
        /// Gets the Patient Root C-MOVE SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Patient Root Query/Retrieve Move SOP Class UID.</returns>
        public static DicomUID GetPatientRootMoveSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.PatientRootQueryRetrieveMove;

        /// <summary>
        /// Gets the Study Root C-MOVE SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Study Root Query/Retrieve Move SOP Class UID.</returns>
        /// <remarks>
        /// Note: Study Root does not support Patient level operations.
        /// </remarks>
        public static DicomUID GetStudyRootMoveSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.StudyRootQueryRetrieveMove;

        /// <summary>
        /// Gets the Patient Root C-GET SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Patient Root Query/Retrieve Get SOP Class UID.</returns>
        public static DicomUID GetPatientRootGetSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.PatientRootQueryRetrieveGet;

        /// <summary>
        /// Gets the Study Root C-GET SOP Class UID.
        /// </summary>
        /// <param name="level">The query/retrieve level (not used, provided for API consistency).</param>
        /// <returns>The Study Root Query/Retrieve Get SOP Class UID.</returns>
        /// <remarks>
        /// Note: Study Root does not support Patient level operations.
        /// </remarks>
        public static DicomUID GetStudyRootGetSopClassUid(this QueryRetrieveLevel level)
            => DicomUID.StudyRootQueryRetrieveGet;
    }
}
