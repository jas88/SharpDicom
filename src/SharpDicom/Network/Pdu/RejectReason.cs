namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the reason field of an A-ASSOCIATE-RJ PDU per DICOM PS3.8 Table 9-21.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interpretation of the reason code depends on the <see cref="RejectSource"/>.
    /// This enum provides the raw byte values. The named values correspond to ServiceUser
    /// interpretations (the most common case). For other sources, interpret the raw value:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <term>ServiceUser (1)</term>
    ///     <description>1=NoReasonGiven, 2=ApplicationContextNotSupported,
    ///     3=CallingAETitleNotRecognized, 7=CalledAETitleNotRecognized</description>
    ///   </item>
    ///   <item>
    ///     <term>ServiceProviderAcse (2)</term>
    ///     <description>1=NoReasonGiven, 2=ProtocolVersionNotSupported</description>
    ///   </item>
    ///   <item>
    ///     <term>ServiceProviderPresentation (3)</term>
    ///     <description>1=TemporaryCongestion, 2=LocalLimitExceeded</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public enum RejectReason : byte
    {
        /// <summary>
        /// Reason value 1: No reason given (ServiceUser), no reason given (ServiceProviderAcse),
        /// or temporary congestion (ServiceProviderPresentation).
        /// </summary>
        NoReasonGiven = 1,

        /// <summary>
        /// Reason value 2: Application context not supported (ServiceUser),
        /// protocol version not supported (ServiceProviderAcse),
        /// or local limit exceeded (ServiceProviderPresentation).
        /// </summary>
        ApplicationContextNotSupported = 2,

        /// <summary>
        /// Reason value 3: Calling AE Title not recognized.
        /// </summary>
        /// <remarks>Valid only for Source: ServiceUser (1).</remarks>
        CallingAETitleNotRecognized = 3,

        /// <summary>
        /// Reason value 7: Called AE Title not recognized.
        /// </summary>
        /// <remarks>Valid only for Source: ServiceUser (1).</remarks>
        CalledAETitleNotRecognized = 7
    }
}
