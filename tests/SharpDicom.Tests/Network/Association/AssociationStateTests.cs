using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Association;
using SharpDicom.Network.Exceptions;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network.Association
{
    /// <summary>
    /// Tests for the DICOM association state machine.
    /// </summary>
    [TestFixture]
    public class AssociationStateTests
    {
        private static AssociationOptions CreateDefaultOptions()
        {
            var context = new PresentationContext(
                1,
                new DicomUID("1.2.840.10008.1.1"), // Verification SOP Class
                TransferSyntax.ExplicitVRLittleEndian);

            return new AssociationOptions(
                "CALLED_AE",
                "CALLING_AE",
                new[] { context });
        }

        // =======================================================================
        // SCU Path Tests
        // =======================================================================

        [Test]
        public void SCU_Path_Idle_AAssociateRequest_TransitionsToAwaitingTransportConnectionOpen()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingTransportConnectionOpen));
        }

        [Test]
        public void SCU_Path_AwaitingTransportConnectionOpen_TransportConnectionConfirm_TransitionsToAwaitingAssociateResponse()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingAssociateResponse));
        }

        [Test]
        public void SCU_Path_AwaitingAssociateResponse_AssociateAcPduReceived_TransitionsToAssociationEstablished()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AssociationEstablished));
            Assert.That(assoc.IsEstablished, Is.True);
        }

        [Test]
        public void SCU_Path_AwaitingAssociateResponse_AssociateRjPduReceived_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateRjPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));
            Assert.That(assoc.IsEstablished, Is.False);
        }

        [Test]
        public void SCU_Path_AssociationEstablished_AReleaseRequest_TransitionsToAwaitingReleaseResponse()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish association
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);

            // Request release
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingReleaseResponse));
        }

        [Test]
        public void SCU_Path_AwaitingReleaseResponse_ReleaseRpPduReceived_TransitionsToAwaitingTransportClose()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish and release
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);
            assoc.ProcessEvent(AssociationEvent.ReleaseRpPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingTransportClose));
        }

        // =======================================================================
        // SCP Path Tests
        // =======================================================================

        [Test]
        public void SCP_Path_Idle_TransportConnectionIndication_TransitionsToTransportConnectionOpen()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.TransportConnectionOpen));
        }

        [Test]
        public void SCP_Path_TransportConnectionOpen_AssociateRqPduReceived_TransitionsToAwaitingLocalAssociateResponse()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingLocalAssociateResponse));
        }

        [Test]
        public void SCP_Path_AwaitingLocalAssociateResponse_AAssociateResponse_TransitionsToAssociationEstablished()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AAssociateResponse);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AssociationEstablished));
            Assert.That(assoc.IsEstablished, Is.True);
        }

        [Test]
        public void SCP_Path_AssociationEstablished_ReleaseRqPduReceived_TransitionsToAwaitingLocalReleaseResponse()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish association (SCP path)
            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AAssociateResponse);

            // Receive release request
            assoc.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingLocalReleaseResponse));
        }

        [Test]
        public void SCP_Path_AwaitingLocalReleaseResponse_AReleaseResponse_TransitionsToAwaitingTransportClose()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish and release (SCP path)
            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AAssociateResponse);
            assoc.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AReleaseResponse);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingTransportClose));
        }

        // =======================================================================
        // Invalid Transition Tests
        // =======================================================================

        [Test]
        public void InvalidTransition_Idle_PDataRequest_ThrowsDicomAssociationException()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            var ex = Assert.Throws<DicomAssociationException>(() =>
                assoc.ProcessEvent(AssociationEvent.PDataRequest));

            Assert.That(ex!.Message, Does.Contain("Invalid state transition"));
            Assert.That(ex.Message, Does.Contain("Idle"));
            Assert.That(ex.Message, Does.Contain("PDataRequest"));
        }

        [Test]
        public void InvalidTransition_TransportConnectionOpen_AssociateAcPduReceived_ThrowsDicomAssociationException()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);

            var ex = Assert.Throws<DicomAssociationException>(() =>
                assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived));

            Assert.That(ex!.Message, Does.Contain("Invalid state transition"));
        }

        [Test]
        public void InvalidTransition_AssociationEstablished_AssociateRqPduReceived_ThrowsDicomAssociationException()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish association
            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AAssociateResponse);

            // Try to receive another associate request
            var ex = Assert.Throws<DicomAssociationException>(() =>
                assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived));

            Assert.That(ex!.Message, Does.Contain("Invalid state transition"));
        }

        // =======================================================================
        // Abort Handling Tests
        // =======================================================================

        [Test]
        public void AbortHandling_AnyState_AbortPduReceived_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();

            // Test from multiple states
            var statesToTest = new[]
            {
                AssociationState.TransportConnectionOpen,
                AssociationState.AwaitingAssociateResponse,
                AssociationState.AssociationEstablished,
                AssociationState.AwaitingReleaseResponse,
                AssociationState.AwaitingLocalReleaseResponse
            };

            foreach (var state in statesToTest)
            {
                var assoc = new DicomAssociation(options);

                // Get to the test state
                TransitionToState(assoc, state);
                Assert.That(assoc.State, Is.EqualTo(state), $"Failed to reach state {state}");

                // Process abort
                assoc.ProcessEvent(AssociationEvent.AbortPduReceived);

                Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle),
                    $"AbortPduReceived from {state} should transition to Idle");
            }
        }

        [Test]
        public void AbortHandling_AnyState_AAbortRequest_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();

            // Test from multiple states
            var statesToTest = new[]
            {
                AssociationState.TransportConnectionOpen,
                AssociationState.AwaitingAssociateResponse,
                AssociationState.AssociationEstablished,
                AssociationState.AwaitingReleaseResponse
            };

            foreach (var state in statesToTest)
            {
                var assoc = new DicomAssociation(options);

                // Get to the test state
                TransitionToState(assoc, state);
                Assert.That(assoc.State, Is.EqualTo(state), $"Failed to reach state {state}");

                // Process abort request
                assoc.ProcessEvent(AssociationEvent.AAbortRequest);

                Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle),
                    $"AAbortRequest from {state} should transition to Idle");
            }
        }

        // =======================================================================
        // Property Tests
        // =======================================================================

        [Test]
        public void IsEstablished_ReturnsTrueOnlyInAssociationEstablishedState()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Initially false
            Assert.That(assoc.IsEstablished, Is.False);

            // Transition through states
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            Assert.That(assoc.IsEstablished, Is.False);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            Assert.That(assoc.IsEstablished, Is.False);

            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
            Assert.That(assoc.IsEstablished, Is.True);

            // After release request, no longer established
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);
            Assert.That(assoc.IsEstablished, Is.False);
        }

        [Test]
        public void GetPresentationContext_ReturnsNullBeforeNegotiation()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            var ctx = assoc.GetPresentationContext(1);

            Assert.That(ctx, Is.Null);
        }

        [Test]
        public void State_ReturnsCurrentState()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingTransportConnectionOpen));
        }

        // =======================================================================
        // P-DATA Tests
        // =======================================================================

        [Test]
        public void PData_AssociationEstablished_PDataRequest_StaysInEstablished()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish association
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);

            // P-DATA should not change state
            assoc.ProcessEvent(AssociationEvent.PDataRequest);
            Assert.That(assoc.State, Is.EqualTo(AssociationState.AssociationEstablished));

            assoc.ProcessEvent(AssociationEvent.PDataPduReceived);
            Assert.That(assoc.State, Is.EqualTo(AssociationState.AssociationEstablished));
        }

        // =======================================================================
        // Release Collision Tests
        // =======================================================================

        [Test]
        public void ReleaseCollision_AwaitingReleaseResponse_ReleaseRqPduReceived_TransitionsToReleaseCollisionRequestor()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish and start release
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);

            // Collision: receive release request while awaiting response
            assoc.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.ReleaseCollisionRequestor));
        }

        [Test]
        public void ReleaseCollision_AwaitingLocalReleaseResponse_AReleaseRequest_TransitionsToReleaseCollisionAcceptor()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Establish (SCP path) and receive release request
            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
            assoc.ProcessEvent(AssociationEvent.AAssociateResponse);
            assoc.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);

            // Collision: local application also wants to release
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.ReleaseCollisionAcceptor));
        }

        // =======================================================================
        // ARTIM Timer Tests
        // =======================================================================

        [Test]
        public void ArtimTimer_TransportConnectionOpen_ArtimTimerExpired_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.ArtimTimerExpired);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));
        }

        [Test]
        public void ArtimTimer_AwaitingTransportClose_ArtimTimerExpired_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Get to AwaitingTransportClose
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);
            assoc.ProcessEvent(AssociationEvent.ReleaseRpPduReceived);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.AwaitingTransportClose));

            // ARTIM timer expiry
            assoc.ProcessEvent(AssociationEvent.ArtimTimerExpired);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));
        }

        [Test]
        public void ArtimTimer_StartRequestedEvent_RaisedOnTransportConnectionIndication()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            bool eventRaised = false;
            assoc.ArtimTimerStartRequested += (s, e) => eventRaised = true;

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);

            Assert.That(eventRaised, Is.True);
        }

        [Test]
        public void ArtimTimer_StopRequestedEvent_RaisedOnAssociateRqReceived()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            bool eventRaised = false;
            assoc.ArtimTimerStopRequested += (s, e) => eventRaised = true;

            assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
            assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);

            Assert.That(eventRaised, Is.True);
        }

        // =======================================================================
        // Transport Connection Close Tests
        // =======================================================================

        [Test]
        public void TransportConnectionClose_AwaitingTransportConnectionOpen_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionClose);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));
        }

        [Test]
        public void TransportConnectionClose_AwaitingTransportClose_TransitionsToIdle()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            // Get to AwaitingTransportClose
            assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
            assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
            assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
            assoc.ProcessEvent(AssociationEvent.AReleaseRequest);
            assoc.ProcessEvent(AssociationEvent.ReleaseRpPduReceived);

            assoc.ProcessEvent(AssociationEvent.TransportConnectionClose);

            Assert.That(assoc.State, Is.EqualTo(AssociationState.Idle));
        }

        // =======================================================================
        // Disposal Tests
        // =======================================================================

        [Test]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            Assert.DoesNotThrow(() =>
            {
                assoc.Dispose();
                assoc.Dispose();
            });
        }

        [Test]
        public void ProcessEvent_AfterDispose_ThrowsObjectDisposedException()
        {
            var options = CreateDefaultOptions();
            var assoc = new DicomAssociation(options);

            assoc.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                assoc.ProcessEvent(AssociationEvent.AAssociateRequest));
        }

        // =======================================================================
        // Helper Methods
        // =======================================================================

        private static void TransitionToState(DicomAssociation assoc, AssociationState targetState)
        {
            switch (targetState)
            {
                case AssociationState.Idle:
                    // Already there
                    break;

                case AssociationState.TransportConnectionOpen:
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
                    break;

                case AssociationState.AwaitingLocalAssociateResponse:
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
                    assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
                    break;

                case AssociationState.AwaitingTransportConnectionOpen:
                    assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
                    break;

                case AssociationState.AwaitingAssociateResponse:
                    assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
                    break;

                case AssociationState.AssociationEstablished:
                    assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
                    assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
                    break;

                case AssociationState.AwaitingReleaseResponse:
                    assoc.ProcessEvent(AssociationEvent.AAssociateRequest);
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionConfirm);
                    assoc.ProcessEvent(AssociationEvent.AssociateAcPduReceived);
                    assoc.ProcessEvent(AssociationEvent.AReleaseRequest);
                    break;

                case AssociationState.AwaitingLocalReleaseResponse:
                    assoc.ProcessEvent(AssociationEvent.TransportConnectionIndication);
                    assoc.ProcessEvent(AssociationEvent.AssociateRqPduReceived);
                    assoc.ProcessEvent(AssociationEvent.AAssociateResponse);
                    assoc.ProcessEvent(AssociationEvent.ReleaseRqPduReceived);
                    break;

                default:
                    throw new ArgumentException($"TransitionToState not implemented for {targetState}");
            }
        }
    }
}
