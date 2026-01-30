namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the source field of an A-ABORT PDU per DICOM PS3.8 Section 9.3.8.
    /// </summary>
    /// <remarks>
    /// When the source is <see cref="ServiceProvider"/>, the reason field contains
    /// additional diagnostic information. When the source is <see cref="ServiceUser"/>,
    /// the reason field is not significant and should be set to 0.
    /// </remarks>
    public enum AbortSource : byte
    {
        /// <summary>
        /// Abort originated from the DICOM UL service user (the application).
        /// </summary>
        /// <remarks>
        /// When source is service-user, the reason field is not significant.
        /// </remarks>
        ServiceUser = 0,

        /// <summary>
        /// Reserved value. Not used in conformant implementations.
        /// </summary>
        Reserved = 1,

        /// <summary>
        /// Abort originated from the DICOM UL service provider (protocol layer).
        /// </summary>
        /// <remarks>
        /// When source is service-provider, the reason field contains diagnostic information
        /// per <see cref="AbortReason"/>.
        /// </remarks>
        ServiceProvider = 2
    }
}
