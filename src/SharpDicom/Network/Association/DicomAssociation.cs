using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;
using SharpDicom.Network.Pdu;

namespace SharpDicom.Network.Association
{
    /// <summary>
    /// Manages the DICOM association state machine per PS3.8 Section 9.2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class implements the DICOM association state machine which manages
    /// the lifecycle of DICOM associations including connection establishment,
    /// negotiation, data exchange, and release/abort handling.
    /// </para>
    /// <para>
    /// State transitions are triggered by <see cref="ProcessEvent"/> and follow
    /// the state table defined in DICOM PS3.8 Section 9.2.3.
    /// </para>
    /// <para>
    /// The ARTIM (Association Request/Reject/Release Timer) is used to prevent
    /// indefinite waits during association establishment and release.
    /// </para>
    /// </remarks>
    public sealed class DicomAssociation : IDisposable
    {
        private AssociationState _state = AssociationState.Idle;
        private readonly AssociationOptions _options;
        private CancellationTokenSource? _artimCts;
        private bool _disposed;

        // Negotiated parameters (set after A-ASSOCIATE-AC received/sent)
        private string? _calledAE;
        private string? _callingAE;
        private List<PresentationContext>? _acceptedContexts;
        private uint _maxPduLength;

        /// <summary>
        /// Gets the current association state.
        /// </summary>
        public AssociationState State => _state;

        /// <summary>
        /// Gets the Called AE Title (remote/server).
        /// </summary>
        /// <remarks>
        /// Set during association negotiation.
        /// </remarks>
        public string? CalledAE => _calledAE;

        /// <summary>
        /// Gets the Calling AE Title (local/client).
        /// </summary>
        /// <remarks>
        /// Set during association negotiation.
        /// </remarks>
        public string? CallingAE => _callingAE;

        /// <summary>
        /// Gets the list of accepted presentation contexts.
        /// </summary>
        /// <remarks>
        /// Populated after successful association negotiation.
        /// </remarks>
        public IReadOnlyList<PresentationContext>? AcceptedContexts =>
            _acceptedContexts?.AsReadOnly();

        /// <summary>
        /// Gets the negotiated maximum PDU length.
        /// </summary>
        /// <remarks>
        /// This is the minimum of the local and remote max PDU lengths.
        /// </remarks>
        public uint MaxPduLength => _maxPduLength;

        /// <summary>
        /// Gets a value indicating whether the association is established.
        /// </summary>
        /// <remarks>
        /// Returns true only when in <see cref="AssociationState.AssociationEstablished"/> (Sta6).
        /// </remarks>
        public bool IsEstablished => _state == AssociationState.AssociationEstablished;

        /// <summary>
        /// Gets the association options.
        /// </summary>
        public AssociationOptions Options => _options;

        /// <summary>
        /// Event raised when the ARTIM timer should be started.
        /// </summary>
        /// <remarks>
        /// The handler should start a timer using <see cref="AssociationOptions.ArtimTimeout"/>
        /// and call <see cref="ProcessEvent"/> with <see cref="AssociationEvent.ArtimTimerExpired"/>
        /// when it expires.
        /// </remarks>
        public event EventHandler? ArtimTimerStartRequested;

        /// <summary>
        /// Event raised when the ARTIM timer should be stopped.
        /// </summary>
        public event EventHandler? ArtimTimerStopRequested;

        /// <summary>
        /// Event raised when the transport connection should be closed.
        /// </summary>
        public event EventHandler? TransportCloseRequested;

        /// <summary>
        /// Event raised when an association has been accepted (SCU side).
        /// </summary>
        public event EventHandler<AssociateAcceptEventArgs>? AssociationAccepted;

        /// <summary>
        /// Event raised when an association has been rejected (SCU side).
        /// </summary>
        public event EventHandler<AssociateRejectEventArgs>? AssociationRejected;

        /// <summary>
        /// Event raised when an association has been aborted.
        /// </summary>
        public event EventHandler<AbortEventArgs>? AssociationAborted;

        /// <summary>
        /// Initializes a new instance of the <see cref="DicomAssociation"/> class.
        /// </summary>
        /// <param name="options">The association options.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="options"/> is null.
        /// </exception>
        public DicomAssociation(AssociationOptions options)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options == null)
                throw new ArgumentNullException(nameof(options));
#endif
            _options = options;
            _calledAE = options.CalledAETitle;
            _callingAE = options.CallingAETitle;
            _maxPduLength = options.UserInformation.MaxPduLength;
        }

        /// <summary>
        /// Processes a state machine event, transitioning to new state and executing actions.
        /// </summary>
        /// <param name="evt">The event to process.</param>
        /// <param name="context">Optional context data associated with the event.</param>
        /// <exception cref="DicomAssociationException">
        /// Thrown when the event is invalid for the current state.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when this instance has been disposed.
        /// </exception>
        public void ProcessEvent(AssociationEvent evt, object? context = null)
        {
            ThrowIfDisposed();

            var (nextState, action) = GetTransition(_state, evt);
            action?.Invoke(context);
            _state = nextState;
        }

        /// <summary>
        /// Gets the presentation context with the specified ID.
        /// </summary>
        /// <param name="contextId">The presentation context ID.</param>
        /// <returns>
        /// The presentation context with the specified ID, or null if not found.
        /// </returns>
        public PresentationContext? GetPresentationContext(byte contextId)
        {
            return _acceptedContexts?.FirstOrDefault(c => c.Id == contextId);
        }

        /// <summary>
        /// Sets the accepted presentation contexts after negotiation.
        /// </summary>
        /// <param name="contexts">The accepted presentation contexts.</param>
        /// <remarks>
        /// This is called internally during association acceptance.
        /// </remarks>
        internal void SetAcceptedContexts(IEnumerable<PresentationContext> contexts)
        {
            _acceptedContexts = contexts.ToList();
        }

        /// <summary>
        /// Sets the negotiated max PDU length.
        /// </summary>
        /// <param name="maxPduLength">The negotiated max PDU length.</param>
        internal void SetMaxPduLength(uint maxPduLength)
        {
            _maxPduLength = maxPduLength;
        }

        /// <summary>
        /// Gets the state transition for the given current state and event.
        /// </summary>
        private (AssociationState, Action<object?>?) GetTransition(
            AssociationState current, AssociationEvent evt)
        {
            // State table from PS3.8 Section 9.2.3
            return (current, evt) switch
            {
                // =======================================================================
                // Sta1: Idle
                // =======================================================================
                (AssociationState.Idle, AssociationEvent.AAssociateRequest) =>
                    (AssociationState.AwaitingTransportConnectionOpen, null),
                (AssociationState.Idle, AssociationEvent.TransportConnectionIndication) =>
                    (AssociationState.TransportConnectionOpen, StartArtimTimer),

                // =======================================================================
                // Sta2: Transport connection open (SCP side)
                // =======================================================================
                (AssociationState.TransportConnectionOpen, AssociationEvent.AssociateRqPduReceived) =>
                    (AssociationState.AwaitingLocalAssociateResponse, StopArtimTimer),
                (AssociationState.TransportConnectionOpen, AssociationEvent.ArtimTimerExpired) =>
                    (AssociationState.Idle, CloseTransport),
                (AssociationState.TransportConnectionOpen, AssociationEvent.InvalidPduReceived) =>
                    (AssociationState.Idle, CloseTransport),

                // =======================================================================
                // Sta3: Awaiting local associate response (SCP side)
                // =======================================================================
                (AssociationState.AwaitingLocalAssociateResponse, AssociationEvent.AAssociateResponse) =>
                    (AssociationState.AssociationEstablished, null),

                // =======================================================================
                // Sta4: Awaiting transport connection (SCU side)
                // =======================================================================
                (AssociationState.AwaitingTransportConnectionOpen, AssociationEvent.TransportConnectionConfirm) =>
                    (AssociationState.AwaitingAssociateResponse, null),
                (AssociationState.AwaitingTransportConnectionOpen, AssociationEvent.TransportConnectionClose) =>
                    (AssociationState.Idle, null),

                // =======================================================================
                // Sta5: Awaiting A-ASSOCIATE-AC/RJ (SCU side)
                // =======================================================================
                (AssociationState.AwaitingAssociateResponse, AssociationEvent.AssociateAcPduReceived) =>
                    (AssociationState.AssociationEstablished, HandleAssociateAccept),
                (AssociationState.AwaitingAssociateResponse, AssociationEvent.AssociateRjPduReceived) =>
                    (AssociationState.Idle, HandleAssociateReject),
                (AssociationState.AwaitingAssociateResponse, AssociationEvent.InvalidPduReceived) =>
                    (AssociationState.Idle, CloseTransport),

                // =======================================================================
                // Sta6: Association established
                // =======================================================================
                (AssociationState.AssociationEstablished, AssociationEvent.PDataRequest) =>
                    (AssociationState.AssociationEstablished, null),
                (AssociationState.AssociationEstablished, AssociationEvent.PDataPduReceived) =>
                    (AssociationState.AssociationEstablished, null),
                (AssociationState.AssociationEstablished, AssociationEvent.AReleaseRequest) =>
                    (AssociationState.AwaitingReleaseResponse, null),
                (AssociationState.AssociationEstablished, AssociationEvent.ReleaseRqPduReceived) =>
                    (AssociationState.AwaitingLocalReleaseResponse, null),
                (AssociationState.AssociationEstablished, AssociationEvent.AbortPduReceived) =>
                    (AssociationState.Idle, HandleAbort),
                (AssociationState.AssociationEstablished, AssociationEvent.AAbortRequest) =>
                    (AssociationState.Idle, CloseTransport),
                (AssociationState.AssociationEstablished, AssociationEvent.InvalidPduReceived) =>
                    (AssociationState.Idle, CloseTransport),

                // =======================================================================
                // Sta7: Awaiting release response (requestor side)
                // =======================================================================
                (AssociationState.AwaitingReleaseResponse, AssociationEvent.ReleaseRpPduReceived) =>
                    (AssociationState.AwaitingTransportClose, StartArtimTimer),
                (AssociationState.AwaitingReleaseResponse, AssociationEvent.ReleaseRqPduReceived) =>
                    (AssociationState.ReleaseCollisionRequestor, null),
                (AssociationState.AwaitingReleaseResponse, AssociationEvent.AbortPduReceived) =>
                    (AssociationState.Idle, HandleAbort),

                // =======================================================================
                // Sta8: Awaiting local release response (acceptor side)
                // =======================================================================
                (AssociationState.AwaitingLocalReleaseResponse, AssociationEvent.AReleaseResponse) =>
                    (AssociationState.AwaitingTransportClose, StartArtimTimer),
                (AssociationState.AwaitingLocalReleaseResponse, AssociationEvent.AReleaseRequest) =>
                    (AssociationState.ReleaseCollisionAcceptor, null),
                (AssociationState.AwaitingLocalReleaseResponse, AssociationEvent.AbortPduReceived) =>
                    (AssociationState.Idle, HandleAbort),

                // =======================================================================
                // Sta9: Release collision - requestor side
                // =======================================================================
                (AssociationState.ReleaseCollisionRequestor, AssociationEvent.ReleaseRpPduReceived) =>
                    (AssociationState.ReleaseCollisionRequestorAwaiting, null),

                // =======================================================================
                // Sta10: Release collision - acceptor side
                // =======================================================================
                (AssociationState.ReleaseCollisionAcceptor, AssociationEvent.AReleaseResponse) =>
                    (AssociationState.ReleaseCollisionAcceptorAwaiting, null),

                // =======================================================================
                // Sta11: Release collision - requestor awaiting response
                // =======================================================================
                (AssociationState.ReleaseCollisionRequestorAwaiting, AssociationEvent.AReleaseResponse) =>
                    (AssociationState.AwaitingTransportClose, StartArtimTimer),

                // =======================================================================
                // Sta12: Release collision - acceptor awaiting response
                // =======================================================================
                (AssociationState.ReleaseCollisionAcceptorAwaiting, AssociationEvent.ReleaseRpPduReceived) =>
                    (AssociationState.AwaitingTransportClose, StartArtimTimer),

                // =======================================================================
                // Sta13: Awaiting transport close
                // =======================================================================
                (AssociationState.AwaitingTransportClose, AssociationEvent.TransportConnectionClose) =>
                    (AssociationState.Idle, StopArtimTimer),
                (AssociationState.AwaitingTransportClose, AssociationEvent.ArtimTimerExpired) =>
                    (AssociationState.Idle, CloseTransport),
                (AssociationState.AwaitingTransportClose, AssociationEvent.AbortPduReceived) =>
                    (AssociationState.Idle, HandleAbort),

                // =======================================================================
                // Global handlers: AbortPduReceived from any state
                // =======================================================================
                (_, AssociationEvent.AbortPduReceived) =>
                    (AssociationState.Idle, HandleAbort),

                // =======================================================================
                // Global handlers: AAbortRequest from any state
                // =======================================================================
                (_, AssociationEvent.AAbortRequest) =>
                    (AssociationState.Idle, CloseTransport),

                // =======================================================================
                // Global handlers: TransportConnectionClose from any non-idle state
                // =======================================================================
                (var s, AssociationEvent.TransportConnectionClose) when s != AssociationState.Idle =>
                    (AssociationState.Idle, null),

                // =======================================================================
                // Invalid transition
                // =======================================================================
                _ => throw new DicomAssociationException(
                    $"Invalid state transition: {current} + {evt}")
            };
        }

        // =======================================================================
        // Action Methods
        // =======================================================================

        private void StartArtimTimer(object? _)
        {
            ArtimTimerStartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void StopArtimTimer(object? _)
        {
            ArtimTimerStopRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CloseTransport(object? _)
        {
            StopArtimTimer(null);
            TransportCloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void HandleAssociateAccept(object? context)
        {
            var args = context as AssociateAcceptEventArgs;
            if (args != null)
            {
                if (args.AcceptedContexts != null)
                {
                    _acceptedContexts = args.AcceptedContexts.ToList();
                }

                // Use minimum of local and remote max PDU length
                if (args.MaxPduLength > 0 && args.MaxPduLength < _maxPduLength)
                {
                    _maxPduLength = args.MaxPduLength;
                }
            }

            AssociationAccepted?.Invoke(this, args ?? new AssociateAcceptEventArgs());
        }

        private void HandleAssociateReject(object? context)
        {
            var args = context as AssociateRejectEventArgs;
            AssociationRejected?.Invoke(this, args ?? new AssociateRejectEventArgs());
        }

        private void HandleAbort(object? context)
        {
            StopArtimTimer(null);
            var args = context as AbortEventArgs;
            AssociationAborted?.Invoke(this, args ?? new AbortEventArgs());
        }

        // =======================================================================
        // IDisposable
        // =======================================================================

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
                throw new ObjectDisposedException(nameof(DicomAssociation));
#endif
        }

        /// <summary>
        /// Releases resources used by this instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _artimCts?.Cancel();
            _artimCts?.Dispose();
            _artimCts = null;
        }
    }

    /// <summary>
    /// Event arguments for association acceptance.
    /// </summary>
    public class AssociateAcceptEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the accepted presentation contexts.
        /// </summary>
        public IReadOnlyList<PresentationContext>? AcceptedContexts { get; set; }

        /// <summary>
        /// Gets or sets the remote max PDU length.
        /// </summary>
        public uint MaxPduLength { get; set; }
    }

    /// <summary>
    /// Event arguments for association rejection.
    /// </summary>
    public class AssociateRejectEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the rejection result (permanent or transient).
        /// </summary>
        public RejectResult Result { get; set; }

        /// <summary>
        /// Gets or sets the rejection source.
        /// </summary>
        public RejectSource Source { get; set; }

        /// <summary>
        /// Gets or sets the rejection reason.
        /// </summary>
        public RejectReason Reason { get; set; }
    }

    /// <summary>
    /// Event arguments for association abort.
    /// </summary>
    public class AbortEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the abort source.
        /// </summary>
        public AbortSource Source { get; set; }

        /// <summary>
        /// Gets or sets the abort reason.
        /// </summary>
        public AbortReason Reason { get; set; }
    }
}
