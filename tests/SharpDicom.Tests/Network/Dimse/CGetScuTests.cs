using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Items;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Unit tests for CGetScu, CGetOptions, CGetProgress, and PresentationContext SCP role.
    /// </summary>
    [TestFixture]
    public class CGetScuTests
    {
        #region CGetOptions Tests

        [Test]
        public void CGetOptions_Default_TimeoutIs120Seconds()
        {
            var options = CGetOptions.Default;

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }

        [Test]
        public void CGetOptions_Default_PriorityIsMedium()
        {
            var options = CGetOptions.Default;

            Assert.That(options.Priority, Is.EqualTo(0)); // MEDIUM
        }

        [Test]
        public void CGetOptions_Default_UsePatientRootIsTrue()
        {
            var options = CGetOptions.Default;

            Assert.That(options.UsePatientRoot, Is.True);
        }

        [Test]
        public void CGetOptions_Default_CancellationBehaviorIsRejectInFlight()
        {
            var options = CGetOptions.Default;

            Assert.That(options.CancellationBehavior, Is.EqualTo(CGetCancellationBehavior.RejectInFlight));
        }

        [Test]
        public void CGetOptions_Timeout_CanBeModified()
        {
            var options = new CGetOptions
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
        }

        [Test]
        public void CGetOptions_Priority_CanBeSetToHigh()
        {
            var options = new CGetOptions
            {
                Priority = 1 // HIGH
            };

            Assert.That(options.Priority, Is.EqualTo(1));
        }

        [Test]
        public void CGetOptions_UsePatientRoot_CanBeSetToFalse()
        {
            var options = new CGetOptions
            {
                UsePatientRoot = false
            };

            Assert.That(options.UsePatientRoot, Is.False);
        }

        [Test]
        public void CGetOptions_CancellationBehavior_CanBeSetToCompleteInFlight()
        {
            var options = new CGetOptions
            {
                CancellationBehavior = CGetCancellationBehavior.CompleteInFlight
            };

            Assert.That(options.CancellationBehavior, Is.EqualTo(CGetCancellationBehavior.CompleteInFlight));
        }

        #endregion

        #region CGetCancellationBehavior Tests

        [Test]
        public void CGetCancellationBehavior_RejectInFlight_Exists()
        {
            Assert.That(
                Enum.IsDefined<CGetCancellationBehavior>(CGetCancellationBehavior.RejectInFlight),
                Is.True);
        }

        [Test]
        public void CGetCancellationBehavior_CompleteInFlight_Exists()
        {
            Assert.That(
                Enum.IsDefined<CGetCancellationBehavior>(CGetCancellationBehavior.CompleteInFlight),
                Is.True);
        }

        #endregion

        #region CGetProgress Tests

        [Test]
        public void CGetProgress_IsFinal_TrueWhenRemainingIsZero()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(0, 5, 0, 0),
                DicomStatus.Success);

            Assert.That(progress.IsFinal, Is.True);
        }

        [Test]
        public void CGetProgress_IsFinal_TrueWhenStatusNotPending()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(3, 2, 0, 0), // Still have remaining
                DicomStatus.Success); // But final status

            Assert.That(progress.IsFinal, Is.True);
        }

        [Test]
        public void CGetProgress_IsFinal_FalseWhenPendingWithRemaining()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(3, 2, 0, 0),
                DicomStatus.Pending);

            Assert.That(progress.IsFinal, Is.False);
        }

        [Test]
        public void CGetProgress_IsSuccess_TrueWhenFinalSuccessNoFailures()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(0, 5, 0, 0),
                DicomStatus.Success);

            Assert.That(progress.IsSuccess, Is.True);
        }

        [Test]
        public void CGetProgress_IsSuccess_FalseWhenHasFailures()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(0, 4, 1, 0), // 1 failure
                DicomStatus.Success);

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CGetProgress_IsSuccess_FalseWhenNotFinal()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(1, 4, 0, 0),
                DicomStatus.Pending);

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CGetProgress_IsSuccess_FalseWhenStatusIsFailure()
        {
            var progress = new CGetProgress(
                new SubOperationProgress(0, 0, 0, 0),
                new DicomStatus(0xC000)); // Failure status

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CGetProgress_HasReceivedDataset_TrueWhenDatasetProvided()
        {
            var dataset = new DicomDataset();
            var progress = new CGetProgress(
                SubOperationProgress.Empty,
                DicomStatus.Pending,
                dataset);

            Assert.That(progress.HasReceivedDataset, Is.True);
        }

        [Test]
        public void CGetProgress_HasReceivedDataset_FalseWhenNoDataset()
        {
            var progress = new CGetProgress(
                SubOperationProgress.Empty,
                DicomStatus.Pending);

            Assert.That(progress.HasReceivedDataset, Is.False);
        }

        [Test]
        public void CGetProgress_ReceivedDataset_ReturnsProvidedDataset()
        {
            var dataset = new DicomDataset();
            var progress = new CGetProgress(
                SubOperationProgress.Empty,
                DicomStatus.Pending,
                dataset);

            Assert.That(progress.ReceivedDataset, Is.SameAs(dataset));
        }

        [Test]
        public void CGetProgress_SubOperations_ReturnsProvidedProgress()
        {
            var subOps = new SubOperationProgress(5, 10, 2, 1);
            var progress = new CGetProgress(subOps, DicomStatus.Success);

            Assert.That(progress.SubOperations, Is.EqualTo(subOps));
        }

        [Test]
        public void CGetProgress_Status_ReturnsProvidedStatus()
        {
            var status = new DicomStatus(0xFF00);
            var progress = new CGetProgress(SubOperationProgress.Empty, status);

            Assert.That(progress.Status, Is.EqualTo(status));
        }

        #endregion

        #region PresentationContext SCP Role Tests

        [Test]
        public void PresentationContext_ScpRoleRequested_DefaultIsFalse()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian);

            Assert.That(pc.ScpRoleRequested, Is.False);
        }

        [Test]
        public void PresentationContext_ScuRoleRequested_DefaultIsTrue()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian);

            Assert.That(pc.ScuRoleRequested, Is.True);
        }

        [Test]
        public void PresentationContext_WithScpRole_SetsScpRoleRequestedToTrue()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian)
                .WithScpRole();

            Assert.That(pc.ScpRoleRequested, Is.True);
        }

        [Test]
        public void PresentationContext_WithScpRole_PreservesScuRoleRequested()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian)
                .WithScpRole();

            Assert.That(pc.ScuRoleRequested, Is.True);
        }

        [Test]
        public void PresentationContext_WithScpRole_ReturnsSameInstance()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian);

            var result = pc.WithScpRole();

            Assert.That(result, Is.SameAs(pc));
        }

        [Test]
        public void PresentationContext_WithBothRoles_SetsBothRolesToTrue()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian)
                .WithBothRoles();

            Assert.That(pc.ScuRoleRequested, Is.True);
            Assert.That(pc.ScpRoleRequested, Is.True);
        }

        [Test]
        public void PresentationContext_WithBothRoles_ReturnsSameInstance()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian);

            var result = pc.WithBothRoles();

            Assert.That(result, Is.SameAs(pc));
        }

        [Test]
        public void PresentationContext_ScpRoleRequested_CanBeSetDirectly()
        {
            var pc = new PresentationContext(
                1,
                DicomUID.CTImageStorage,
                TransferSyntax.ImplicitVRLittleEndian);

            pc.ScpRoleRequested = true;

            Assert.That(pc.ScpRoleRequested, Is.True);
        }

        #endregion

        #region CGetScu Constructor Tests

        [Test]
        public void CGetScu_NullClient_ThrowsArgumentNullException()
        {
            Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> handler =
                (_, _, _) => ValueTask.FromResult(DicomStatus.Success);

            Assert.Throws<ArgumentNullException>(() => new CGetScu(null!, handler));
        }

        [Test]
        public void CGetScu_NullStoreHandler_ThrowsArgumentNullException()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);

            Assert.Throws<ArgumentNullException>(() => new CGetScu(client, null!));
        }

        [Test]
        public void CGetScu_DefaultOptions_UsesDefaults()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> handler =
                (_, _, _) => ValueTask.FromResult(DicomStatus.Success);

            var scu = new CGetScu(client, handler);

            Assert.That(scu.Options, Is.SameAs(CGetOptions.Default));
        }

        [Test]
        public void CGetScu_CustomOptions_UsesProvided()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            Func<DicomDataset, DicomDataset?, CancellationToken, ValueTask<DicomStatus>> handler =
                (_, _, _) => ValueTask.FromResult(DicomStatus.Success);
            var options = new CGetOptions
            {
                UsePatientRoot = false,
                CancellationBehavior = CGetCancellationBehavior.CompleteInFlight
            };

            var scu = new CGetScu(client, handler, options);

            Assert.That(scu.Options, Is.SameAs(options));
            Assert.That(scu.Options.UsePatientRoot, Is.False);
            Assert.That(scu.Options.CancellationBehavior, Is.EqualTo(CGetCancellationBehavior.CompleteInFlight));
        }

        #endregion

        #region Command Creation Tests

        [Test]
        public void CreateCGetRequest_PatientRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCGetRequest(1, DicomUID.PatientRootQueryRetrieveGet);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveGet));
            Assert.That(cmd.IsCGetRequest, Is.True);
            Assert.That(cmd.HasDataset, Is.True);
        }

        [Test]
        public void CreateCGetRequest_StudyRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCGetRequest(1, DicomUID.StudyRootQueryRetrieveGet);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.StudyRootQueryRetrieveGet));
            Assert.That(cmd.IsCGetRequest, Is.True);
        }

        [Test]
        public void CreateCGetRequest_MessageId_IsSet()
        {
            var cmd1 = DicomCommand.CreateCGetRequest(42, DicomUID.PatientRootQueryRetrieveGet);
            var cmd2 = DicomCommand.CreateCGetRequest(99, DicomUID.PatientRootQueryRetrieveGet);

            Assert.That(cmd1.MessageID, Is.EqualTo(42));
            Assert.That(cmd2.MessageID, Is.EqualTo(99));
        }

        [Test]
        public void CreateCGetRequest_Priority_IsSet()
        {
            var cmd = DicomCommand.CreateCGetRequest(1, DicomUID.PatientRootQueryRetrieveGet, priority: 1);

            Assert.That(cmd.Priority, Is.EqualTo(1)); // HIGH
        }

        [Test]
        public void CreateCGetResponse_IncludesSubOperationCounts()
        {
            var progress = new SubOperationProgress(5, 10, 2, 1);
            var cmd = DicomCommand.CreateCGetResponse(
                42,
                DicomUID.PatientRootQueryRetrieveGet,
                DicomStatus.Pending,
                progress);

            Assert.That(cmd.IsCGetResponse, Is.True);
            Assert.That(cmd.NumberOfRemainingSuboperations, Is.EqualTo(5));
            Assert.That(cmd.NumberOfCompletedSuboperations, Is.EqualTo(10));
            Assert.That(cmd.NumberOfFailedSuboperations, Is.EqualTo(2));
            Assert.That(cmd.NumberOfWarningSuboperations, Is.EqualTo(1));
        }

        #endregion

        #region QueryRetrieveLevel Extension Tests

        [Test]
        public void GetPatientRootGetSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetPatientRootGetSopClassUid(),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveGet));
        }

        [Test]
        public void GetStudyRootGetSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetStudyRootGetSopClassUid(),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveGet));
        }

        [Test]
        public void GetPatientRootMoveSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetPatientRootMoveSopClassUid(),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveMove));
        }

        [Test]
        public void GetStudyRootMoveSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetStudyRootMoveSopClassUid(),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveMove));
        }

        #endregion
    }
}
