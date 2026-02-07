using System;

namespace FellowOakDicom.Network
{
    /// <summary>
    /// C-FIND request matching fo-dicom 5.x DicomCFindRequest pattern.
    /// Stores query parameters and an OnResponseReceived callback.
    /// </summary>
    public sealed class DicomCFindRequest : DicomRequest
    {
        /// <summary>
        /// Gets the query/retrieve level for this C-FIND request.
        /// </summary>
        public DicomQueryRetrieveLevel Level { get; }

        /// <summary>
        /// Gets or sets the callback invoked for each C-FIND response.
        /// Matches fo-dicom's OnResponseReceived delegate pattern.
        /// </summary>
        public Action<DicomCFindRequest, DicomCFindResponse>? OnResponseReceived { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomCFindRequest"/> class.
        /// </summary>
        /// <param name="level">The query/retrieve level.</param>
        public DicomCFindRequest(DicomQueryRetrieveLevel level)
            : base(DicomRequestType.CFind)
        {
            Level = level;
        }
    }
}
