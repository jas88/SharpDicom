namespace SharpDicom.Network
{
    /// <summary>
    /// Constants for DICOM PDU (Protocol Data Unit) processing.
    /// </summary>
    public static class PduConstants
    {
        /// <summary>
        /// The minimum allowed maximum PDU length.
        /// </summary>
        /// <remarks>
        /// Some implementations require a minimum PDU size to function correctly.
        /// 4096 bytes is a common minimum that accommodates most command data.
        /// </remarks>
        public const uint MinMaxPduLength = 4096;

        /// <summary>
        /// The absolute maximum allowed PDU length to prevent denial-of-service attacks.
        /// </summary>
        /// <remarks>
        /// This limit (128 MB) prevents malicious peers from causing memory exhaustion
        /// by sending PDUs with extremely large length fields. This is a security safeguard
        /// independent of the negotiated MaxPduLength, which typically limits data transfer PDUs.
        /// Association PDUs (A-ASSOCIATE-RQ/AC/RJ) are further limited to <see cref="MaxAssociationPduLength"/>.
        /// </remarks>
        public const uint AbsoluteMaxPduLength = 128 * 1024 * 1024; // 128 MB

        /// <summary>
        /// The maximum allowed length for association PDUs (A-ASSOCIATE-RQ/AC/RJ).
        /// </summary>
        /// <remarks>
        /// Association PDUs contain presentation contexts and user information.
        /// Even with hundreds of presentation contexts, 1 MB is more than sufficient.
        /// This limit prevents memory exhaustion during association negotiation
        /// before a MaxPduLength has been negotiated.
        /// </remarks>
        public const uint MaxAssociationPduLength = 1 * 1024 * 1024; // 1 MB

        /// <summary>
        /// The default maximum PDU length.
        /// </summary>
        /// <remarks>
        /// 16384 bytes (16 KB) is a reasonable default that balances memory usage
        /// and network efficiency for typical DICOM operations.
        /// </remarks>
        public const uint DefaultMaxPduLength = 16384;

        /// <summary>
        /// The maximum allowed AE Title length in characters.
        /// </summary>
        public const int MaxAETitleLength = 16;

        /// <summary>
        /// The maximum allowed implementation version name length.
        /// </summary>
        public const int MaxImplementationVersionNameLength = 16;

        /// <summary>
        /// PDU type for A-ASSOCIATE-RQ (Association Request).
        /// </summary>
        public const byte AssociateRequest = 0x01;

        /// <summary>
        /// PDU type for A-ASSOCIATE-AC (Association Accept).
        /// </summary>
        public const byte AssociateAccept = 0x02;

        /// <summary>
        /// PDU type for A-ASSOCIATE-RJ (Association Reject).
        /// </summary>
        public const byte AssociateReject = 0x03;

        /// <summary>
        /// PDU type for P-DATA-TF (Data Transfer).
        /// </summary>
        public const byte DataTransfer = 0x04;

        /// <summary>
        /// PDU type for A-RELEASE-RQ (Release Request).
        /// </summary>
        public const byte ReleaseRequest = 0x05;

        /// <summary>
        /// PDU type for A-RELEASE-RP (Release Response).
        /// </summary>
        public const byte ReleaseResponse = 0x06;

        /// <summary>
        /// PDU type for A-ABORT (Association Abort).
        /// </summary>
        public const byte Abort = 0x07;
    }
}
