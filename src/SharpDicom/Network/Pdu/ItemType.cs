namespace SharpDicom.Network.Pdu
{
    /// <summary>
    /// Specifies the type of variable items within A-ASSOCIATE-RQ and A-ASSOCIATE-AC PDUs
    /// per DICOM PS3.8 Section 9.3.2 and 9.3.3.
    /// </summary>
    /// <remarks>
    /// Variable items follow the fixed fields in association request/accept PDUs and define
    /// the application context, presentation contexts, and user information.
    /// </remarks>
    public enum ItemType : byte
    {
        /// <summary>
        /// Application Context Item containing the application context name.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.2.1.</remarks>
        ApplicationContext = 0x10,

        /// <summary>
        /// Presentation Context Item in an A-ASSOCIATE-RQ PDU.
        /// </summary>
        /// <remarks>
        /// Contains an abstract syntax and one or more proposed transfer syntaxes.
        /// See DICOM PS3.8 Section 9.3.2.2.
        /// </remarks>
        PresentationContextRequest = 0x20,

        /// <summary>
        /// Presentation Context Item in an A-ASSOCIATE-AC PDU.
        /// </summary>
        /// <remarks>
        /// Contains the acceptance result and the selected transfer syntax.
        /// See DICOM PS3.8 Section 9.3.3.2.
        /// </remarks>
        PresentationContextAccept = 0x21,

        /// <summary>
        /// Abstract Syntax Sub-Item within a Presentation Context Item.
        /// </summary>
        /// <remarks>
        /// Contains the UID of the SOP Class being proposed.
        /// See DICOM PS3.8 Section 9.3.2.2.1.
        /// </remarks>
        AbstractSyntax = 0x30,

        /// <summary>
        /// Transfer Syntax Sub-Item within a Presentation Context Item.
        /// </summary>
        /// <remarks>
        /// Contains the UID of a transfer syntax being proposed or accepted.
        /// See DICOM PS3.8 Section 9.3.2.2.2.
        /// </remarks>
        TransferSyntax = 0x40,

        /// <summary>
        /// User Information Item containing user-defined sub-items.
        /// </summary>
        /// <remarks>See DICOM PS3.8 Section 9.3.2.3.</remarks>
        UserInformation = 0x50,

        /// <summary>
        /// Maximum Length Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Specifies the maximum PDU length the sender can receive.
        /// See DICOM PS3.8 Section D.1.
        /// </remarks>
        MaximumLength = 0x51,

        /// <summary>
        /// Implementation Class UID Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Identifies the implementation of the DICOM application.
        /// See DICOM PS3.7 Section D.3.3.2.
        /// </remarks>
        ImplementationClassUid = 0x52,

        /// <summary>
        /// Asynchronous Operations Window Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Negotiates the number of outstanding operations.
        /// See DICOM PS3.7 Section D.3.3.3.
        /// </remarks>
        AsynchronousOperationsWindow = 0x53,

        /// <summary>
        /// SCP/SCU Role Selection Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Negotiates roles for specific SOP Classes.
        /// See DICOM PS3.7 Section D.3.3.4.
        /// </remarks>
        ScpScuRoleSelection = 0x54,

        /// <summary>
        /// Implementation Version Name Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Contains a version string for the implementation.
        /// See DICOM PS3.7 Section D.3.3.2.
        /// </remarks>
        ImplementationVersionName = 0x55,

        /// <summary>
        /// SOP Class Extended Negotiation Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Allows negotiation of extended features for specific SOP Classes.
        /// See DICOM PS3.7 Section D.3.3.5.
        /// </remarks>
        SopClassExtendedNegotiation = 0x56,

        /// <summary>
        /// SOP Class Common Extended Negotiation Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Negotiates common extended features across SOP Classes.
        /// See DICOM PS3.7 Section D.3.3.6.
        /// </remarks>
        SopClassCommonExtendedNegotiation = 0x57,

        /// <summary>
        /// User Identity Negotiation Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Provides user authentication information.
        /// See DICOM PS3.7 Section D.3.3.7.
        /// </remarks>
        UserIdentityNegotiation = 0x58,

        /// <summary>
        /// User Identity Response Sub-Item within User Information.
        /// </summary>
        /// <remarks>
        /// Server response to user identity negotiation.
        /// See DICOM PS3.7 Section D.3.3.7.
        /// </remarks>
        UserIdentityResponse = 0x59
    }
}
