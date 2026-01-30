namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the reason field of an A-ABORT PDU when the source is service-provider
    /// per DICOM PS3.8 Section 9.3.8.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These reason codes are only meaningful when <see cref="AbortSource.ServiceProvider"/> is set.
    /// When <see cref="AbortSource.ServiceUser"/> is set, the reason should be
    /// <see cref="NotSpecified"/> (0).
    /// </para>
    /// <para>
    /// Note: Value 3 is not defined in the DICOM standard.
    /// </para>
    /// </remarks>
    public enum AbortReason : byte
    {
        /// <summary>
        /// No specific reason given for the abort.
        /// </summary>
        /// <remarks>Used when source is service-user or when no specific provider reason applies.</remarks>
        NotSpecified = 0,

        /// <summary>
        /// The received PDU type was not recognized.
        /// </summary>
        /// <remarks>Indicates protocol violation - an unknown PDU type byte was received.</remarks>
        UnrecognizedPdu = 1,

        /// <summary>
        /// The received PDU type was not expected at this point in the protocol.
        /// </summary>
        /// <remarks>
        /// Indicates protocol violation - a valid PDU type was received but in an unexpected
        /// state (e.g., P-DATA before association acceptance).
        /// </remarks>
        UnexpectedPdu = 2,

        /// <summary>
        /// A required PDU parameter was not recognized.
        /// </summary>
        /// <remarks>Indicates malformed PDU content.</remarks>
        UnrecognizedPduParameter = 4,

        /// <summary>
        /// A PDU parameter was not expected at this point.
        /// </summary>
        /// <remarks>Indicates protocol violation in PDU content.</remarks>
        UnexpectedPduParameter = 5,

        /// <summary>
        /// A PDU parameter contained an invalid value.
        /// </summary>
        /// <remarks>Indicates malformed or out-of-range PDU content.</remarks>
        InvalidPduParameter = 6
    }
}
