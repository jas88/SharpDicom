using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Response from an N-Service operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All N-Service handlers return this type, which encapsulates the DIMSE status,
    /// an optional response dataset (e.g., attribute values from N-GET, created attributes
    /// from N-CREATE), and an optional Affected SOP Instance UID.
    /// </para>
    /// <para>
    /// The <see cref="AffectedSOPInstanceUID"/> is particularly important for N-CREATE
    /// responses where the SCP assigns the SOP Instance UID.
    /// </para>
    /// </remarks>
    public sealed class NServiceResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NServiceResponse"/> class.
        /// </summary>
        /// <param name="status">The DIMSE response status.</param>
        /// <param name="dataset">Optional response dataset containing attribute values.</param>
        /// <param name="affectedSopInstanceUid">
        /// Optional Affected SOP Instance UID. Required for N-CREATE responses
        /// when the SCP assigns the instance UID.
        /// </param>
        public NServiceResponse(
            DicomStatus status,
            DicomDataset? dataset = null,
            DicomUID? affectedSopInstanceUid = null)
        {
            Status = status;
            Dataset = dataset;
            AffectedSOPInstanceUID = affectedSopInstanceUid;
        }

        /// <summary>Gets the DIMSE response status.</summary>
        public DicomStatus Status { get; }

        /// <summary>Gets the optional response dataset containing attribute values.</summary>
        public DicomDataset? Dataset { get; }

        /// <summary>
        /// Gets the optional Affected SOP Instance UID.
        /// </summary>
        /// <remarks>
        /// For N-CREATE, this is the SOP Instance UID assigned by the SCP (or echoed back if the SCU provided one).
        /// For other N-Services, this is typically the same as the requested SOP Instance UID.
        /// </remarks>
        public DicomUID? AffectedSOPInstanceUID { get; }
    }
}
