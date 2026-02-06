namespace FellowOakDicom.Network
{
    /// <summary>
    /// C-FIND response matching fo-dicom 5.x DicomCFindResponse.
    /// Wraps a response dataset and DIMSE status.
    /// </summary>
    public sealed class DicomCFindResponse : DicomResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DicomCFindResponse"/> class.
        /// </summary>
        /// <param name="status">The DIMSE status.</param>
        /// <param name="dataset">The optional response dataset.</param>
        public DicomCFindResponse(DicomStatus status, DicomDataset? dataset = null)
            : base(status, dataset)
        {
        }
    }
}
