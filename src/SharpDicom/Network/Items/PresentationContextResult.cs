namespace SharpDicom.Network.Items
{
    /// <summary>
    /// Defines the result values for presentation context negotiation in A-ASSOCIATE-AC PDUs.
    /// </summary>
    /// <remarks>
    /// These values are defined in DICOM PS3.8 Section 9.3.3.2 (Presentation Context Item Structure).
    /// </remarks>
    public enum PresentationContextResult : byte
    {
        /// <summary>
        /// The presentation context was accepted.
        /// </summary>
        Acceptance = 0,

        /// <summary>
        /// The user rejected the presentation context (reason unspecified).
        /// </summary>
        UserRejection = 1,

        /// <summary>
        /// The provider rejected the presentation context without specifying a reason.
        /// </summary>
        NoReason = 2,

        /// <summary>
        /// The abstract syntax (SOP Class) is not supported.
        /// </summary>
        AbstractSyntaxNotSupported = 3,

        /// <summary>
        /// None of the proposed transfer syntaxes are supported.
        /// </summary>
        TransferSyntaxesNotSupported = 4
    }
}
