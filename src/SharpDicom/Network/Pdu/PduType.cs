namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the type of a Protocol Data Unit (PDU) per DICOM PS3.8 Section 9.3.
    /// </summary>
    /// <remarks>
    /// PDUs are the fundamental units of data exchange in DICOM network communication.
    /// Each PDU starts with a one-byte type field followed by a reserved byte and a 32-bit length field.
    /// </remarks>
    public enum PduType : byte
    {
        /// <summary>
        /// A-ASSOCIATE-RQ PDU used to request an association.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.2.</remarks>
        AssociateRequest = 0x01,

        /// <summary>
        /// A-ASSOCIATE-AC PDU used to accept an association request.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.3.</remarks>
        AssociateAccept = 0x02,

        /// <summary>
        /// A-ASSOCIATE-RJ PDU used to reject an association request.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.4.</remarks>
        AssociateReject = 0x03,

        /// <summary>
        /// P-DATA-TF PDU used to transfer DIMSE message data.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.5.</remarks>
        PDataTransfer = 0x04,

        /// <summary>
        /// A-RELEASE-RQ PDU used to request a graceful release of an association.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.6.</remarks>
        ReleaseRequest = 0x05,

        /// <summary>
        /// A-RELEASE-RP PDU used to respond to a release request.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.7.</remarks>
        ReleaseResponse = 0x06,

        /// <summary>
        /// A-ABORT PDU used to abort an association.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.8.</remarks>
        Abort = 0x07
    }
}
