namespace SharpDicom.Network.Dimse
{
    /// <summary>
    /// Constants for DIMSE Command Field values per DICOM PS3.7 Section 9.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Command Field (0000,0100) identifies the type of DIMSE operation being performed.
    /// Bit 15 (0x8000) indicates whether this is a request (0) or response (1).
    /// </para>
    /// <para>
    /// C-* commands (Composite) operate on composite SOP instances.
    /// N-* commands (Normalized) operate on normalized SOP instances.
    /// </para>
    /// </remarks>
    public static class CommandField
    {
        #region C-STORE

        /// <summary>
        /// C-STORE-RQ command field value (0x0001).
        /// </summary>
        /// <remarks>
        /// Used to request storage of a composite SOP instance.
        /// See DICOM PS3.7 Section 9.1.1.
        /// </remarks>
        public const ushort CStoreRequest = 0x0001;

        /// <summary>
        /// C-STORE-RSP command field value (0x8001).
        /// </summary>
        /// <remarks>
        /// Response to a C-STORE-RQ operation.
        /// See DICOM PS3.7 Section 9.1.1.
        /// </remarks>
        public const ushort CStoreResponse = 0x8001;

        #endregion

        #region C-GET

        /// <summary>
        /// C-GET-RQ command field value (0x0010).
        /// </summary>
        /// <remarks>
        /// Used to request retrieval of composite SOP instances.
        /// See DICOM PS3.7 Section 9.1.3.
        /// </remarks>
        public const ushort CGetRequest = 0x0010;

        /// <summary>
        /// C-GET-RSP command field value (0x8010).
        /// </summary>
        /// <remarks>
        /// Response to a C-GET-RQ operation.
        /// See DICOM PS3.7 Section 9.1.3.
        /// </remarks>
        public const ushort CGetResponse = 0x8010;

        #endregion

        #region C-FIND

        /// <summary>
        /// C-FIND-RQ command field value (0x0020).
        /// </summary>
        /// <remarks>
        /// Used to query for composite SOP instances.
        /// See DICOM PS3.7 Section 9.1.2.
        /// </remarks>
        public const ushort CFindRequest = 0x0020;

        /// <summary>
        /// C-FIND-RSP command field value (0x8020).
        /// </summary>
        /// <remarks>
        /// Response to a C-FIND-RQ operation.
        /// See DICOM PS3.7 Section 9.1.2.
        /// </remarks>
        public const ushort CFindResponse = 0x8020;

        #endregion

        #region C-MOVE

        /// <summary>
        /// C-MOVE-RQ command field value (0x0021).
        /// </summary>
        /// <remarks>
        /// Used to request that composite SOP instances be moved to another AE.
        /// See DICOM PS3.7 Section 9.1.4.
        /// </remarks>
        public const ushort CMoveRequest = 0x0021;

        /// <summary>
        /// C-MOVE-RSP command field value (0x8021).
        /// </summary>
        /// <remarks>
        /// Response to a C-MOVE-RQ operation.
        /// See DICOM PS3.7 Section 9.1.4.
        /// </remarks>
        public const ushort CMoveResponse = 0x8021;

        #endregion

        #region C-ECHO

        /// <summary>
        /// C-ECHO-RQ command field value (0x0030).
        /// </summary>
        /// <remarks>
        /// Used to verify DICOM connectivity (ping).
        /// See DICOM PS3.7 Section 9.1.5.
        /// </remarks>
        public const ushort CEchoRequest = 0x0030;

        /// <summary>
        /// C-ECHO-RSP command field value (0x8030).
        /// </summary>
        /// <remarks>
        /// Response to a C-ECHO-RQ operation.
        /// See DICOM PS3.7 Section 9.1.5.
        /// </remarks>
        public const ushort CEchoResponse = 0x8030;

        #endregion

        #region N-EVENT-REPORT

        /// <summary>
        /// N-EVENT-REPORT-RQ command field value (0x0100).
        /// </summary>
        /// <remarks>
        /// Used to report an event on a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.1.
        /// </remarks>
        public const ushort NEventReportRequest = 0x0100;

        /// <summary>
        /// N-EVENT-REPORT-RSP command field value (0x8100).
        /// </summary>
        /// <remarks>
        /// Response to an N-EVENT-REPORT-RQ operation.
        /// See DICOM PS3.7 Section 10.1.1.
        /// </remarks>
        public const ushort NEventReportResponse = 0x8100;

        #endregion

        #region N-GET

        /// <summary>
        /// N-GET-RQ command field value (0x0110).
        /// </summary>
        /// <remarks>
        /// Used to retrieve attribute values from a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.2.
        /// </remarks>
        public const ushort NGetRequest = 0x0110;

        /// <summary>
        /// N-GET-RSP command field value (0x8110).
        /// </summary>
        /// <remarks>
        /// Response to an N-GET-RQ operation.
        /// See DICOM PS3.7 Section 10.1.2.
        /// </remarks>
        public const ushort NGetResponse = 0x8110;

        #endregion

        #region N-SET

        /// <summary>
        /// N-SET-RQ command field value (0x0120).
        /// </summary>
        /// <remarks>
        /// Used to set attribute values on a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.3.
        /// </remarks>
        public const ushort NSetRequest = 0x0120;

        /// <summary>
        /// N-SET-RSP command field value (0x8120).
        /// </summary>
        /// <remarks>
        /// Response to an N-SET-RQ operation.
        /// See DICOM PS3.7 Section 10.1.3.
        /// </remarks>
        public const ushort NSetResponse = 0x8120;

        #endregion

        #region N-ACTION

        /// <summary>
        /// N-ACTION-RQ command field value (0x0130).
        /// </summary>
        /// <remarks>
        /// Used to request an action on a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.4.
        /// </remarks>
        public const ushort NActionRequest = 0x0130;

        /// <summary>
        /// N-ACTION-RSP command field value (0x8130).
        /// </summary>
        /// <remarks>
        /// Response to an N-ACTION-RQ operation.
        /// See DICOM PS3.7 Section 10.1.4.
        /// </remarks>
        public const ushort NActionResponse = 0x8130;

        #endregion

        #region N-CREATE

        /// <summary>
        /// N-CREATE-RQ command field value (0x0140).
        /// </summary>
        /// <remarks>
        /// Used to create a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.5.
        /// </remarks>
        public const ushort NCreateRequest = 0x0140;

        /// <summary>
        /// N-CREATE-RSP command field value (0x8140).
        /// </summary>
        /// <remarks>
        /// Response to an N-CREATE-RQ operation.
        /// See DICOM PS3.7 Section 10.1.5.
        /// </remarks>
        public const ushort NCreateResponse = 0x8140;

        #endregion

        #region N-DELETE

        /// <summary>
        /// N-DELETE-RQ command field value (0x0150).
        /// </summary>
        /// <remarks>
        /// Used to delete a normalized SOP instance.
        /// See DICOM PS3.7 Section 10.1.6.
        /// </remarks>
        public const ushort NDeleteRequest = 0x0150;

        /// <summary>
        /// N-DELETE-RSP command field value (0x8150).
        /// </summary>
        /// <remarks>
        /// Response to an N-DELETE-RQ operation.
        /// See DICOM PS3.7 Section 10.1.6.
        /// </remarks>
        public const ushort NDeleteResponse = 0x8150;

        #endregion

        #region C-CANCEL

        /// <summary>
        /// C-CANCEL-RQ command field value (0x0FFF).
        /// </summary>
        /// <remarks>
        /// Used to cancel an outstanding operation.
        /// See DICOM PS3.7 Section 9.3.2.3.
        /// </remarks>
        public const ushort CCancelRequest = 0x0FFF;

        #endregion

        /// <summary>
        /// Determines whether the specified command field value represents a response.
        /// </summary>
        /// <param name="commandField">The command field value.</param>
        /// <returns>true if the command is a response; false if it is a request.</returns>
        /// <remarks>
        /// Bit 15 (0x8000) is set for response commands.
        /// </remarks>
        public static bool IsResponse(ushort commandField) => (commandField & 0x8000) != 0;

        /// <summary>
        /// Determines whether the specified command field value represents a request.
        /// </summary>
        /// <param name="commandField">The command field value.</param>
        /// <returns>true if the command is a request; false if it is a response.</returns>
        /// <remarks>
        /// Bit 15 (0x8000) is clear for request commands.
        /// </remarks>
        public static bool IsRequest(ushort commandField) => (commandField & 0x8000) == 0;

        /// <summary>
        /// Converts a request command field to its corresponding response command field.
        /// </summary>
        /// <param name="requestCommandField">The request command field value.</param>
        /// <returns>The corresponding response command field value.</returns>
        public static ushort ToResponse(ushort requestCommandField) => (ushort)(requestCommandField | 0x8000);

        /// <summary>
        /// Converts a response command field to its corresponding request command field.
        /// </summary>
        /// <param name="responseCommandField">The response command field value.</param>
        /// <returns>The corresponding request command field value.</returns>
        public static ushort ToRequest(ushort responseCommandField) => (ushort)(responseCommandField & 0x7FFF);
    }
}
