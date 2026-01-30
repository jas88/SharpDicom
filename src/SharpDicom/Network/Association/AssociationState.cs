namespace SharpDicom.Network.Association
{
    /// <summary>
    /// Represents the 13 DICOM association states per PS3.8 Section 9.2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The association state machine manages the lifecycle of DICOM associations,
    /// handling connection establishment, negotiation, data exchange, and release/abort.
    /// </para>
    /// <para>
    /// State transitions are triggered by <see cref="AssociationEvent"/> values and
    /// follow the state table defined in DICOM PS3.8 Section 9.2.3.
    /// </para>
    /// </remarks>
    public enum AssociationState
    {
        /// <summary>
        /// Sta1: Idle - no connection.
        /// </summary>
        /// <remarks>
        /// The initial state. No transport connection exists.
        /// </remarks>
        Idle = 1,

        /// <summary>
        /// Sta2: Transport connection open (awaiting A-ASSOCIATE-RQ PDU).
        /// </summary>
        /// <remarks>
        /// SCP side: Transport connection accepted, waiting for A-ASSOCIATE-RQ.
        /// ARTIM timer is running in this state.
        /// </remarks>
        TransportConnectionOpen = 2,

        /// <summary>
        /// Sta3: Awaiting local A-ASSOCIATE response primitive.
        /// </summary>
        /// <remarks>
        /// SCP side: A-ASSOCIATE-RQ received, waiting for the local application
        /// to accept or reject the association.
        /// </remarks>
        AwaitingLocalAssociateResponse = 3,

        /// <summary>
        /// Sta4: Awaiting transport connection opening to complete.
        /// </summary>
        /// <remarks>
        /// SCU side: A-ASSOCIATE request issued, waiting for TCP connection
        /// to be established.
        /// </remarks>
        AwaitingTransportConnectionOpen = 4,

        /// <summary>
        /// Sta5: Awaiting A-ASSOCIATE-AC or A-ASSOCIATE-RJ PDU.
        /// </summary>
        /// <remarks>
        /// SCU side: A-ASSOCIATE-RQ sent, waiting for the remote peer
        /// to accept or reject the association.
        /// </remarks>
        AwaitingAssociateResponse = 5,

        /// <summary>
        /// Sta6: Association established and ready for data transfer.
        /// </summary>
        /// <remarks>
        /// Normal operational state. P-DATA primitives can be exchanged.
        /// Both SCU and SCP can send DIMSE messages in this state.
        /// </remarks>
        AssociationEstablished = 6,

        /// <summary>
        /// Sta7: Awaiting A-RELEASE-RP PDU.
        /// </summary>
        /// <remarks>
        /// The local entity has sent A-RELEASE-RQ and is waiting for
        /// the remote peer to respond with A-RELEASE-RP.
        /// </remarks>
        AwaitingReleaseResponse = 7,

        /// <summary>
        /// Sta8: Awaiting local A-RELEASE response primitive.
        /// </summary>
        /// <remarks>
        /// A-RELEASE-RQ PDU received, waiting for the local application
        /// to respond with A-RELEASE-RP.
        /// </remarks>
        AwaitingLocalReleaseResponse = 8,

        /// <summary>
        /// Sta9: Release collision - requestor side.
        /// </summary>
        /// <remarks>
        /// Both peers have sent A-RELEASE-RQ simultaneously. This state
        /// applies to the entity that initiated the release request.
        /// The requestor sends A-RELEASE-RP and transitions to Sta11.
        /// </remarks>
        ReleaseCollisionRequestor = 9,

        /// <summary>
        /// Sta10: Release collision - acceptor side.
        /// </summary>
        /// <remarks>
        /// Both peers have sent A-RELEASE-RQ simultaneously. This state
        /// applies to the entity that received the release request while
        /// awaiting local release response.
        /// </remarks>
        ReleaseCollisionAcceptor = 10,

        /// <summary>
        /// Sta11: Release collision - requestor side, awaiting A-RELEASE-RP response.
        /// </summary>
        /// <remarks>
        /// Requestor has sent A-RELEASE-RP in response to collision,
        /// now awaiting A-RELEASE-RP from peer.
        /// </remarks>
        ReleaseCollisionRequestorAwaiting = 11,

        /// <summary>
        /// Sta12: Release collision - acceptor side, awaiting A-RELEASE-RP response.
        /// </summary>
        /// <remarks>
        /// Acceptor has sent A-RELEASE-RP in response to collision,
        /// now awaiting A-RELEASE-RP from peer.
        /// </remarks>
        ReleaseCollisionAcceptorAwaiting = 12,

        /// <summary>
        /// Sta13: Awaiting transport connection close indication.
        /// </summary>
        /// <remarks>
        /// A-RELEASE-RP has been sent/received. ARTIM timer is running.
        /// Waiting for the transport connection to close.
        /// </remarks>
        AwaitingTransportClose = 13
    }
}
