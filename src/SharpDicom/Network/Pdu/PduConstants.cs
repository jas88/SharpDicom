namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Contains constants for DICOM PDU parsing and construction per DICOM PS3.8.
    /// </summary>
    public static class PduConstants
    {
        /// <summary>
        /// The length of the PDU header (PDU type, reserved byte, and 32-bit length field).
        /// </summary>
        /// <remarks>
        /// All PDUs begin with a 6-byte header:
        /// - 1 byte: PDU type
        /// - 1 byte: Reserved (0x00)
        /// - 4 bytes: PDU length (big-endian, excludes the 6-byte header itself)
        /// </remarks>
        public const int HeaderLength = 6;

        /// <summary>
        /// The total length of an A-ASSOCIATE-RJ PDU.
        /// </summary>
        /// <remarks>
        /// Structure: Header (6) + Reserved (1) + Result (1) + Source (1) + Reason (1) + Reserved (0) = 10 bytes.
        /// See DICOM PS3.8 Section 9.3.4.
        /// </remarks>
        public const int AssociateRejectLength = 10;

        /// <summary>
        /// The total length of an A-RELEASE-RQ PDU.
        /// </summary>
        /// <remarks>
        /// Structure: Header (6) + Reserved (4) = 10 bytes.
        /// See DICOM PS3.8 Section 9.3.6.
        /// </remarks>
        public const int ReleaseRequestLength = 10;

        /// <summary>
        /// The total length of an A-RELEASE-RP PDU.
        /// </summary>
        /// <remarks>
        /// Structure: Header (6) + Reserved (4) = 10 bytes.
        /// See DICOM PS3.8 Section 9.3.7.
        /// </remarks>
        public const int ReleaseResponseLength = 10;

        /// <summary>
        /// The total length of an A-ABORT PDU.
        /// </summary>
        /// <remarks>
        /// Structure: Header (6) + Reserved (2) + Source (1) + Reason (1) = 10 bytes.
        /// See DICOM PS3.8 Section 9.3.8.
        /// </remarks>
        public const int AbortLength = 10;

        /// <summary>
        /// The length of fixed fields in A-ASSOCIATE-RQ and A-ASSOCIATE-AC PDUs before variable items.
        /// </summary>
        /// <remarks>
        /// Fixed fields after the 6-byte header:
        /// - Protocol Version: 2 bytes
        /// - Reserved: 2 bytes
        /// - Called AE Title: 16 bytes
        /// - Calling AE Title: 16 bytes
        /// - Reserved: 32 bytes
        /// Total: 68 bytes (not including header).
        /// See DICOM PS3.8 Section 9.3.2.
        /// </remarks>
        public const int AssociateFixedFieldsLength = 68;

        /// <summary>
        /// The default maximum PDU length to request during association negotiation.
        /// </summary>
        /// <remarks>
        /// This value (16384 bytes / 16 KB) is a conservative default that works with most PACS systems.
        /// The maximum possible value is 0xFFFFFFFF, but many implementations use smaller values.
        /// </remarks>
        public const uint DefaultMaxPduLength = 16384;

        /// <summary>
        /// The DICOM Upper Layer Protocol version.
        /// </summary>
        /// <remarks>
        /// Currently only version 1 (0x0001) is defined by the DICOM standard.
        /// See DICOM PS3.8 Section 9.3.2.
        /// </remarks>
        public const ushort ProtocolVersion = 0x0001;

        /// <summary>
        /// The fixed length of an Application Entity (AE) title field.
        /// </summary>
        /// <remarks>
        /// AE titles are padded with spaces (0x20) to exactly 16 bytes.
        /// See DICOM PS3.5 Section 6.2.
        /// </remarks>
        public const int AETitleLength = 16;

        /// <summary>
        /// The minimum length of a P-DATA-TF PDU header before presentation data value items.
        /// </summary>
        /// <remarks>
        /// Just the standard 6-byte header; variable-length PDV items follow.
        /// See DICOM PS3.8 Section 9.3.5.
        /// </remarks>
        public const int PDataTransferHeaderLength = HeaderLength;
    }
}
