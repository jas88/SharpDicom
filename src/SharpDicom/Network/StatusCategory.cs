namespace SharpDicom.Network
{
    /// <summary>
    /// Categories of DICOM DIMSE service status codes per PS3.7 Annex C.
    /// </summary>
    /// <remarks>
    /// DIMSE operations return status codes that fall into these categories.
    /// The category determines how the application should handle the response.
    /// </remarks>
    public enum StatusCategory
    {
        /// <summary>
        /// Operation completed successfully (status 0x0000).
        /// </summary>
        Success,

        /// <summary>
        /// Operation is still in progress with more results pending (status 0xFF00-0xFF01).
        /// </summary>
        /// <remarks>
        /// Used by C-FIND, C-MOVE, and C-GET to indicate partial results.
        /// The client should continue receiving responses.
        /// </remarks>
        Pending,

        /// <summary>
        /// Operation completed with warnings (status 0xB000-0xBFFF).
        /// </summary>
        /// <remarks>
        /// The operation succeeded but with non-fatal issues.
        /// Examples: attribute coercion, data set missing requested elements.
        /// </remarks>
        Warning,

        /// <summary>
        /// Operation failed (status 0xA000-0xAFFF, 0xC000-0xCFFF, or other failure codes).
        /// </summary>
        /// <remarks>
        /// The operation could not be completed. The specific status code
        /// provides more detail about the failure reason.
        /// </remarks>
        Failure,

        /// <summary>
        /// Operation was cancelled by the client (status 0xFE00).
        /// </summary>
        /// <remarks>
        /// Used with C-FIND, C-MOVE, and C-GET to indicate the client
        /// requested cancellation of the operation.
        /// </remarks>
        Cancel
    }
}
