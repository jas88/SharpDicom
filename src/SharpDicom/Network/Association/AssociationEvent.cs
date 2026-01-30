namespace SharpDicom.Network.Association
{
    /// <summary>
    /// Events that trigger state transitions in the DICOM association state machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These events correspond to the primitives and PDUs defined in DICOM PS3.8
    /// Section 9.2. They are processed by the association state machine
    /// to transition between <see cref="AssociationState"/> values.
    /// </para>
    /// <para>
    /// Events are categorized into:
    /// <list type="bullet">
    /// <item><description>Service user events (local application requests)</description></item>
    /// <item><description>Transport events (TCP connection state changes)</description></item>
    /// <item><description>PDU received events (remote peer messages)</description></item>
    /// <item><description>Timer events (ARTIM timeout)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public enum AssociationEvent
    {
        // =======================================================================
        // Service User Events (Local Application Requests)
        // =======================================================================

        /// <summary>
        /// A-ASSOCIATE request from the local user (SCU initiating association).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta1 -> Sta4 (begin transport connection)
        /// </remarks>
        AAssociateRequest,

        /// <summary>
        /// A-ASSOCIATE response from the local user (SCP accepting/rejecting).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta3 -> Sta6 (accept) or Sta3 -> Sta13 (reject)
        /// </remarks>
        AAssociateResponse,

        /// <summary>
        /// A-RELEASE request from the local user (initiate graceful release).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta6 -> Sta7 (begin release)
        /// </remarks>
        AReleaseRequest,

        /// <summary>
        /// A-RELEASE response from the local user (confirm release).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta8 -> Sta13 (release confirmed)
        /// </remarks>
        AReleaseResponse,

        /// <summary>
        /// A-ABORT request from the local user (abort association immediately).
        /// </summary>
        /// <remarks>
        /// Triggers: Any state -> Sta1 (abort and close)
        /// </remarks>
        AAbortRequest,

        /// <summary>
        /// P-DATA request from the local user (send DIMSE message data).
        /// </summary>
        /// <remarks>
        /// Valid only in Sta6 (association established).
        /// </remarks>
        PDataRequest,

        // =======================================================================
        // Transport Events (TCP Connection State Changes)
        // =======================================================================

        /// <summary>
        /// TCP connection established (SCU side - outgoing connection confirmed).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta4 -> Sta5 (send A-ASSOCIATE-RQ)
        /// </remarks>
        TransportConnectionConfirm,

        /// <summary>
        /// Incoming TCP connection accepted (SCP side - new connection received).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta1 -> Sta2 (start ARTIM, await A-ASSOCIATE-RQ)
        /// </remarks>
        TransportConnectionIndication,

        /// <summary>
        /// TCP connection closed by remote peer or network failure.
        /// </summary>
        /// <remarks>
        /// Triggers: Any state -> Sta1 (return to idle)
        /// </remarks>
        TransportConnectionClose,

        // =======================================================================
        // PDU Received Events (Remote Peer Messages)
        // =======================================================================

        /// <summary>
        /// A-ASSOCIATE-AC PDU received (association accepted by remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta5 -> Sta6 (association established)
        /// </remarks>
        AssociateAcPduReceived,

        /// <summary>
        /// A-ASSOCIATE-RJ PDU received (association rejected by remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta5 -> Sta1 (return to idle)
        /// </remarks>
        AssociateRjPduReceived,

        /// <summary>
        /// A-ASSOCIATE-RQ PDU received (association request from remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta2 -> Sta3 (await local response)
        /// </remarks>
        AssociateRqPduReceived,

        /// <summary>
        /// P-DATA-TF PDU received (DIMSE message data from remote peer).
        /// </summary>
        /// <remarks>
        /// Valid only in Sta6 (association established).
        /// </remarks>
        PDataPduReceived,

        /// <summary>
        /// A-RELEASE-RQ PDU received (release request from remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta6 -> Sta8 (await local response)
        /// May also trigger release collision (Sta7 -> Sta9)
        /// </remarks>
        ReleaseRqPduReceived,

        /// <summary>
        /// A-RELEASE-RP PDU received (release response from remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Sta7 -> Sta13 (begin transport close)
        /// </remarks>
        ReleaseRpPduReceived,

        /// <summary>
        /// A-ABORT PDU received (abort from remote peer).
        /// </summary>
        /// <remarks>
        /// Triggers: Any state -> Sta1 (return to idle)
        /// </remarks>
        AbortPduReceived,

        /// <summary>
        /// Unrecognized or malformed PDU received.
        /// </summary>
        /// <remarks>
        /// Typically results in sending A-ABORT and returning to Sta1.
        /// </remarks>
        InvalidPduReceived,

        // =======================================================================
        // Timer Events
        // =======================================================================

        /// <summary>
        /// ARTIM (Association Request/Reject/Release Timer) timer expired.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ARTIM timer is started when entering certain states and ensures
        /// the association doesn't hang indefinitely waiting for PDUs.
        /// </para>
        /// <para>
        /// Timeout typically results in aborting the association and returning to Sta1.
        /// </para>
        /// </remarks>
        ArtimTimerExpired
    }
}
