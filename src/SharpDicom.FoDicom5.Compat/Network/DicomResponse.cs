namespace FellowOakDicom.Network
{
    /// <summary>
    /// Base class for DIMSE responses matching fo-dicom 5.x DicomResponse.
    /// </summary>
    public class DicomResponse
    {
        /// <summary>
        /// Gets the status of this response.
        /// </summary>
        public DicomStatus Status { get; }

        /// <summary>
        /// Gets the dataset associated with this response (may be null for final responses).
        /// </summary>
        public DicomDataset? Dataset { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomResponse"/> class.
        /// </summary>
        /// <param name="status">The DIMSE status.</param>
        /// <param name="dataset">The optional response dataset.</param>
        public DicomResponse(DicomStatus status, DicomDataset? dataset = null)
        {
            Status = status;
            Dataset = dataset;
        }
    }
}
