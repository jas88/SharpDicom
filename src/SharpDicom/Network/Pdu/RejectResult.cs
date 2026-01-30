namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the result field of an A-ASSOCIATE-RJ PDU per DICOM PS3.8 Table 9-21.
    /// </summary>
    /// <remarks>
    /// The result indicates whether the rejection is permanent (the association should not
    /// be retried) or transient (the association might succeed if retried later).
    /// </remarks>
    public enum RejectResult : byte
    {
        /// <summary>
        /// The association is permanently rejected and should not be retried.
        /// </summary>
        PermanentRejection = 1,

        /// <summary>
        /// The association is transiently rejected and may be retried later.
        /// </summary>
        TransientRejection = 2
    }
}
