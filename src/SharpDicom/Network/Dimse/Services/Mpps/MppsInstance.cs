using System;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.Mpps
{
    /// <summary>
    /// Typed wrapper around a DICOM dataset representing a Modality Performed Procedure Step instance.
    /// Provides typed access to key MPPS attributes per DICOM PS3.3 Annex F.
    /// </summary>
    public sealed class MppsInstance
    {
        /// <summary>DICOM string value for InProgress status.</summary>
        internal const string StatusInProgress = "IN PROGRESS";

        /// <summary>DICOM string value for Completed status.</summary>
        internal const string StatusCompleted = "COMPLETED";

        /// <summary>DICOM string value for Discontinued status.</summary>
        internal const string StatusDiscontinued = "DISCONTINUED";

        /// <summary>
        /// Gets the SOP Instance UID of this MPPS instance.
        /// </summary>
        public DicomUID SOPInstanceUID { get; }

        /// <summary>
        /// Gets the current status of the performed procedure step.
        /// </summary>
        public MppsStatus Status { get; private set; }

        /// <summary>
        /// Gets the underlying DICOM dataset.
        /// </summary>
        public DicomDataset Dataset { get; }

        private MppsInstance(DicomUID sopInstanceUid, MppsStatus status, DicomDataset dataset)
        {
            SOPInstanceUID = sopInstanceUid;
            Status = status;
            Dataset = dataset;
        }

        /// <summary>
        /// Creates a new MPPS instance in the InProgress state.
        /// </summary>
        /// <param name="sopInstanceUid">The SOP Instance UID for this MPPS instance.</param>
        /// <param name="attributes">The initial attribute list dataset.</param>
        /// <returns>A new <see cref="MppsInstance"/> in the InProgress state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when attributes is null.</exception>
        public static MppsInstance CreateInProgress(DicomUID sopInstanceUid, DicomDataset attributes)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(attributes);
#else
            if (attributes == null)
                throw new ArgumentNullException(nameof(attributes));
#endif

            // Set the status attribute in the dataset
            SetStatusString(attributes, StatusInProgress);

            return new MppsInstance(sopInstanceUid, MppsStatus.InProgress, attributes);
        }

        /// <summary>
        /// Creates a modification dataset that sets the status to Completed.
        /// </summary>
        /// <returns>A dataset containing PerformedProcedureStepStatus set to COMPLETED.</returns>
        public static DicomDataset CreateCompletedModification()
        {
            var mod = new DicomDataset();
            SetStatusString(mod, StatusCompleted);
            return mod;
        }

        /// <summary>
        /// Creates a modification dataset that sets the status to Discontinued.
        /// </summary>
        /// <returns>A dataset containing PerformedProcedureStepStatus set to DISCONTINUED.</returns>
        public static DicomDataset CreateDiscontinuedModification()
        {
            var mod = new DicomDataset();
            SetStatusString(mod, StatusDiscontinued);
            return mod;
        }

        /// <summary>
        /// Applies a modification list to this instance, updating attributes and status.
        /// </summary>
        /// <param name="modificationList">The modification list to apply.</param>
        /// <exception cref="ArgumentNullException">Thrown when modificationList is null.</exception>
        internal void ApplyModification(DicomDataset modificationList)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(modificationList);
#else
            if (modificationList == null)
                throw new ArgumentNullException(nameof(modificationList));
#endif

            // Copy all elements from the modification list into the dataset
            foreach (var element in modificationList)
            {
                Dataset.AddOrUpdate(element.ToOwned());
            }

            // Update the status from the dataset
            var statusString = Dataset.GetString(DicomTag.PerformedProcedureStepStatus);
            if (statusString != null)
            {
                Status = ParseStatus(statusString);
            }
        }

        /// <summary>
        /// Parses an MPPS status from its DICOM string representation.
        /// </summary>
        /// <param name="value">The status string value.</param>
        /// <returns>The parsed <see cref="MppsStatus"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when the value is not a recognized status string.</exception>
        public static MppsStatus ParseStatus(string value)
        {
            if (string.Equals(value, StatusInProgress, StringComparison.OrdinalIgnoreCase))
                return MppsStatus.InProgress;
            if (string.Equals(value, StatusCompleted, StringComparison.OrdinalIgnoreCase))
                return MppsStatus.Completed;
            if (string.Equals(value, StatusDiscontinued, StringComparison.OrdinalIgnoreCase))
                return MppsStatus.Discontinued;

            throw new ArgumentException($"Unknown MPPS status: '{value}'", nameof(value));
        }

        /// <summary>
        /// Gets the DICOM string representation of the specified status.
        /// </summary>
        /// <param name="status">The status value.</param>
        /// <returns>The DICOM string representation.</returns>
        public static string GetStatusString(MppsStatus status)
        {
            return status switch
            {
                MppsStatus.InProgress => StatusInProgress,
                MppsStatus.Completed => StatusCompleted,
                MppsStatus.Discontinued => StatusDiscontinued,
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown MPPS status")
            };
        }

        private static void SetStatusString(DicomDataset dataset, string statusValue)
        {
            var bytes = Encoding.ASCII.GetBytes(statusValue);
            // CS VR is padded with spaces to even length
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Array.Copy(bytes, padded, bytes.Length);
                padded[bytes.Length] = (byte)' ';
                bytes = padded;
            }
            dataset.AddOrUpdate(new DicomStringElement(DicomTag.PerformedProcedureStepStatus, DicomVR.CS, bytes));
        }
    }
}
