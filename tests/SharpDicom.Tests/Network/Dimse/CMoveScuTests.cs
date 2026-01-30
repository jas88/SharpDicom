using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Unit tests for CMoveScu, CMoveOptions, and CMoveProgress.
    /// </summary>
    [TestFixture]
    public class CMoveScuTests
    {
        #region CMoveOptions Tests

        [Test]
        public void CMoveOptions_Default_TimeoutIs120Seconds()
        {
            var options = CMoveOptions.Default;

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }

        [Test]
        public void CMoveOptions_Default_PriorityIsMedium()
        {
            var options = CMoveOptions.Default;

            Assert.That(options.Priority, Is.EqualTo(0)); // MEDIUM
        }

        [Test]
        public void CMoveOptions_Default_UsePatientRootIsTrue()
        {
            var options = CMoveOptions.Default;

            Assert.That(options.UsePatientRoot, Is.True);
        }

        [Test]
        public void CMoveOptions_Timeout_CanBeModified()
        {
            var options = new CMoveOptions
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
        }

        [Test]
        public void CMoveOptions_Priority_CanBeSetToHigh()
        {
            var options = new CMoveOptions
            {
                Priority = 1 // HIGH
            };

            Assert.That(options.Priority, Is.EqualTo(1));
        }

        [Test]
        public void CMoveOptions_Priority_CanBeSetToLow()
        {
            var options = new CMoveOptions
            {
                Priority = 2 // LOW
            };

            Assert.That(options.Priority, Is.EqualTo(2));
        }

        [Test]
        public void CMoveOptions_UsePatientRoot_CanBeSetToFalse()
        {
            var options = new CMoveOptions
            {
                UsePatientRoot = false
            };

            Assert.That(options.UsePatientRoot, Is.False);
        }

        #endregion

        #region CMoveProgress Tests

        [Test]
        public void CMoveProgress_IsFinal_TrueWhenRemainingIsZero()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 5, 0, 0),
                DicomStatus.Success);

            Assert.That(progress.IsFinal, Is.True);
        }

        [Test]
        public void CMoveProgress_IsFinal_TrueWhenStatusNotPending()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(3, 2, 0, 0), // Still have remaining
                DicomStatus.Success); // But final status

            Assert.That(progress.IsFinal, Is.True);
        }

        [Test]
        public void CMoveProgress_IsFinal_FalseWhenPendingWithRemaining()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(3, 2, 0, 0),
                DicomStatus.Pending);

            Assert.That(progress.IsFinal, Is.False);
        }

        [Test]
        public void CMoveProgress_IsSuccess_TrueWhenFinalSuccessNoFailures()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 5, 0, 0),
                DicomStatus.Success);

            Assert.That(progress.IsSuccess, Is.True);
        }

        [Test]
        public void CMoveProgress_IsSuccess_FalseWhenHasFailures()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 4, 1, 0), // 1 failure
                DicomStatus.Success);

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_IsSuccess_FalseWhenNotFinal()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(1, 4, 0, 0),
                DicomStatus.Pending);

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_IsSuccess_FalseWhenStatusIsFailure()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 0, 0, 0),
                new DicomStatus(0xC000)); // Failure status

            Assert.That(progress.IsSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_IsPartialSuccess_TrueWhenFailuresButSomeCompleted()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 3, 2, 0), // 2 failures, 3 completed
                DicomStatus.Success);

            Assert.That(progress.IsPartialSuccess, Is.True);
        }

        [Test]
        public void CMoveProgress_IsPartialSuccess_TrueWhenWarningsButSomeCompleted()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 4, 0, 1), // 1 warning, 4 completed
                new DicomStatus(0xB000)); // Warning status

            Assert.That(progress.IsPartialSuccess, Is.True);
        }

        [Test]
        public void CMoveProgress_IsPartialSuccess_FalseWhenAllSucceeded()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 5, 0, 0),
                DicomStatus.Success);

            Assert.That(progress.IsPartialSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_IsPartialSuccess_FalseWhenNoneCompleted()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(0, 0, 5, 0), // All failed
                new DicomStatus(0xC000));

            Assert.That(progress.IsPartialSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_IsPartialSuccess_FalseWhenNotFinal()
        {
            var progress = new CMoveProgress(
                new SubOperationProgress(2, 2, 1, 0),
                DicomStatus.Pending);

            Assert.That(progress.IsPartialSuccess, Is.False);
        }

        [Test]
        public void CMoveProgress_SubOperations_ReturnsProvidedProgress()
        {
            var subOps = new SubOperationProgress(5, 10, 2, 1);
            var progress = new CMoveProgress(subOps, DicomStatus.Success);

            Assert.That(progress.SubOperations, Is.EqualTo(subOps));
        }

        [Test]
        public void CMoveProgress_Status_ReturnsProvidedStatus()
        {
            var status = new DicomStatus(0xFF00);
            var progress = new CMoveProgress(SubOperationProgress.Empty, status);

            Assert.That(progress.Status, Is.EqualTo(status));
        }

        #endregion

        #region CMoveScu Constructor Tests

        [Test]
        public void CMoveScu_NullClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CMoveScu(null!));
        }

        [Test]
        public void CMoveScu_DefaultOptions_UsesDefaults()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            var scu = new CMoveScu(client);

            Assert.That(scu.Options, Is.SameAs(CMoveOptions.Default));
        }

        [Test]
        public void CMoveScu_CustomOptions_UsesProvided()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            var options = new CMoveOptions { UsePatientRoot = false };
            var scu = new CMoveScu(client, options);

            Assert.That(scu.Options, Is.SameAs(options));
            Assert.That(scu.Options.UsePatientRoot, Is.False);
        }

        #endregion

        #region Command Creation Tests

        [Test]
        public void CreateCMoveRequest_IncludesMoveDestination()
        {
            var cmd = DicomCommand.CreateCMoveRequest(
                1,
                DicomUID.PatientRootQueryRetrieveMove,
                "DEST_AE");

            Assert.That(cmd.MoveDestination, Is.EqualTo("DEST_AE"));
        }

        [Test]
        public void CreateCMoveRequest_PatientRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCMoveRequest(
                1,
                DicomUID.PatientRootQueryRetrieveMove,
                "DEST_AE");

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveMove));
            Assert.That(cmd.IsCMoveRequest, Is.True);
            Assert.That(cmd.HasDataset, Is.True);
        }

        [Test]
        public void CreateCMoveRequest_StudyRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCMoveRequest(
                1,
                DicomUID.StudyRootQueryRetrieveMove,
                "DEST_AE");

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.StudyRootQueryRetrieveMove));
            Assert.That(cmd.IsCMoveRequest, Is.True);
        }

        [Test]
        public void CreateCMoveRequest_MessageId_IsSet()
        {
            var cmd1 = DicomCommand.CreateCMoveRequest(42, DicomUID.PatientRootQueryRetrieveMove, "DEST");
            var cmd2 = DicomCommand.CreateCMoveRequest(99, DicomUID.PatientRootQueryRetrieveMove, "DEST");

            Assert.That(cmd1.MessageID, Is.EqualTo(42));
            Assert.That(cmd2.MessageID, Is.EqualTo(99));
        }

        [Test]
        public void CreateCMoveRequest_Priority_IsSet()
        {
            var cmd = DicomCommand.CreateCMoveRequest(
                1,
                DicomUID.PatientRootQueryRetrieveMove,
                "DEST_AE",
                priority: 1); // HIGH

            Assert.That(cmd.Priority, Is.EqualTo(1));
        }

        [Test]
        public void CreateCMoveResponse_IncludesSubOperationCounts()
        {
            var progress = new SubOperationProgress(5, 10, 2, 1);
            var cmd = DicomCommand.CreateCMoveResponse(
                42,
                DicomUID.PatientRootQueryRetrieveMove,
                DicomStatus.Pending,
                progress);

            Assert.That(cmd.IsCMoveResponse, Is.True);
            Assert.That(cmd.NumberOfRemainingSuboperations, Is.EqualTo(5));
            Assert.That(cmd.NumberOfCompletedSuboperations, Is.EqualTo(10));
            Assert.That(cmd.NumberOfFailedSuboperations, Is.EqualTo(2));
            Assert.That(cmd.NumberOfWarningSuboperations, Is.EqualTo(1));
        }

        #endregion

        #region QueryRetrieveLevel Extension Tests

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

        [Test]
        public void GetPatientRootMoveSopClassUid_AllLevelsReturnSameUid()
        {
            // Patient Root uses same SOP Class UID regardless of level
            var expected = DicomUID.PatientRootQueryRetrieveMove;

            Assert.That(QueryRetrieveLevel.Patient.GetPatientRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Study.GetPatientRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Series.GetPatientRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Image.GetPatientRootMoveSopClassUid(), Is.EqualTo(expected));
        }

        [Test]
        public void GetStudyRootMoveSopClassUid_AllLevelsReturnSameUid()
        {
            // Study Root uses same SOP Class UID regardless of level
            var expected = DicomUID.StudyRootQueryRetrieveMove;

            Assert.That(QueryRetrieveLevel.Patient.GetStudyRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Study.GetStudyRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Series.GetStudyRootMoveSopClassUid(), Is.EqualTo(expected));
            Assert.That(QueryRetrieveLevel.Image.GetStudyRootMoveSopClassUid(), Is.EqualTo(expected));
        }

        #endregion

        #region MoveDestination Status Tests

        [Test]
        public void DicomStatus_MoveDestinationUnknown_HasCorrectCode()
        {
            // Status 0xA801 = Move Destination Unknown
            var status = new DicomStatus(0xA801);

            Assert.That(status.Code, Is.EqualTo(0xA801));
            Assert.That(status.IsPending, Is.False);
            Assert.That(status.IsSuccess, Is.False);
        }

        #endregion
    }
}
