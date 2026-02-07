namespace FellowOakDicom.Network
{
    /// <summary>
    /// Abstract base for DIMSE requests matching fo-dicom 5.x DicomRequest.
    /// </summary>
    public abstract class DicomRequest
    {
        /// <summary>
        /// Gets or sets the dataset (query identifier, data, etc.) for this request.
        /// </summary>
        public DicomDataset Dataset { get; set; }

        /// <summary>
        /// Gets the request type.
        /// </summary>
        public DicomRequestType Type { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomRequest"/> class.
        /// </summary>
        /// <param name="type">The request type.</param>
        protected DicomRequest(DicomRequestType type)
        {
            Type = type;
            Dataset = new DicomDataset();
        }
    }

    /// <summary>
    /// DIMSE request type enum matching fo-dicom usage patterns.
    /// </summary>
    public enum DicomRequestType
    {
        /// <summary>C-FIND request.</summary>
        CFind,

        /// <summary>C-STORE request.</summary>
        CStore,

        /// <summary>C-MOVE request.</summary>
        CMove,

        /// <summary>C-GET request.</summary>
        CGet,

        /// <summary>C-ECHO request.</summary>
        CEcho
    }
}
