namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the source field of an A-ASSOCIATE-RJ PDU per DICOM PS3.8 Table 9-21.
    /// </summary>
    /// <remarks>
    /// The source indicates which layer (service user, ACSE provider, or presentation provider)
    /// caused the association rejection. The valid reason codes depend on the source.
    /// </remarks>
    public enum RejectSource : byte
    {
        /// <summary>
        /// Rejection originated from the DICOM UL service user (the application).
        /// </summary>
        /// <remarks>
        /// Valid reasons: NoReasonGiven (1), ApplicationContextNotSupported (2),
        /// CallingAETitleNotRecognized (3), CalledAETitleNotRecognized (7).
        /// </remarks>
        ServiceUser = 1,

        /// <summary>
        /// Rejection originated from the ACSE (Association Control Service Element) provider.
        /// </summary>
        /// <remarks>
        /// Valid reasons: NoReasonGiven (1), ProtocolVersionNotSupported (2).
        /// </remarks>
        ServiceProviderAcse = 2,

        /// <summary>
        /// Rejection originated from the presentation layer provider.
        /// </summary>
        /// <remarks>
        /// Valid reasons: TemporaryCongestion (1), LocalLimitExceeded (2).
        /// </remarks>
        ServiceProviderPresentation = 3
    }
}
