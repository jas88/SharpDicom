using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Unit tests for DIMSE types (QueryRetrieveLevel, SubOperationProgress, DicomTransferProgress, DicomCommand factory methods).
    /// </summary>
    [TestFixture]
    public class DimseTypesTests
    {
        #region QueryRetrieveLevel Tests

        [Test]
        public void QueryRetrieveLevel_ToDicomValue_ReturnsCorrectStrings()
        {
            Assert.That(QueryRetrieveLevel.Patient.ToDicomValue(), Is.EqualTo("PATIENT"));
            Assert.That(QueryRetrieveLevel.Study.ToDicomValue(), Is.EqualTo("STUDY"));
            Assert.That(QueryRetrieveLevel.Series.ToDicomValue(), Is.EqualTo("SERIES"));
            Assert.That(QueryRetrieveLevel.Image.ToDicomValue(), Is.EqualTo("IMAGE"));
        }

        [Test]
        [TestCase("PATIENT", QueryRetrieveLevel.Patient)]
        [TestCase("STUDY", QueryRetrieveLevel.Study)]
        [TestCase("SERIES", QueryRetrieveLevel.Series)]
        [TestCase("IMAGE", QueryRetrieveLevel.Image)]
        [TestCase("patient", QueryRetrieveLevel.Patient)]
        [TestCase("  study  ", QueryRetrieveLevel.Study)]
        public void QueryRetrieveLevel_Parse_ValidValues_ReturnsCorrectLevel(string input, QueryRetrieveLevel expected)
        {
            var result = QueryRetrieveLevelExtensions.Parse(input);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void QueryRetrieveLevel_Parse_InvalidValue_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => QueryRetrieveLevelExtensions.Parse("INVALID"));
        }

        [Test]
        public void QueryRetrieveLevel_Parse_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => QueryRetrieveLevelExtensions.Parse(null!));
        }

        [Test]
        [TestCase("PATIENT", true)]
        [TestCase("INVALID", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void QueryRetrieveLevel_TryParse_ReturnsExpectedResult(string? input, bool expectedSuccess)
        {
            var success = QueryRetrieveLevelExtensions.TryParse(input, out var level);
            Assert.That(success, Is.EqualTo(expectedSuccess));
            if (expectedSuccess)
            {
                Assert.That(level, Is.EqualTo(QueryRetrieveLevel.Patient));
            }
        }

        [Test]
        public void QueryRetrieveLevel_GetPatientRootSopClassUid_ReturnsCorrectUids()
        {
            Assert.That(QueryRetrieveLevel.Patient.GetPatientRootSopClassUid(CommandField.CFindRequest),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveFind));
            Assert.That(QueryRetrieveLevel.Study.GetPatientRootSopClassUid(CommandField.CMoveRequest),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveMove));
            Assert.That(QueryRetrieveLevel.Series.GetPatientRootSopClassUid(CommandField.CGetRequest),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveGet));
        }

        [Test]
        public void QueryRetrieveLevel_GetStudyRootSopClassUid_ReturnsCorrectUids()
        {
            Assert.That(QueryRetrieveLevel.Study.GetStudyRootSopClassUid(CommandField.CFindRequest),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveFind));
            Assert.That(QueryRetrieveLevel.Series.GetStudyRootSopClassUid(CommandField.CMoveRequest),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveMove));
            Assert.That(QueryRetrieveLevel.Image.GetStudyRootSopClassUid(CommandField.CGetRequest),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveGet));
        }

        [Test]
        public void QueryRetrieveLevel_GetSopClassUid_InvalidCommandField_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                QueryRetrieveLevel.Patient.GetPatientRootSopClassUid(CommandField.CStoreRequest));
        }

        #endregion

        #region SubOperationProgress Tests

        [Test]
        public void SubOperationProgress_Total_CalculatesCorrectly()
        {
            var progress = new SubOperationProgress(5, 10, 2, 1);
            Assert.That(progress.Total, Is.EqualTo(18));
        }

        [Test]
        public void SubOperationProgress_IsFinal_TrueWhenRemainingIsZero()
        {
            var progress = new SubOperationProgress(0, 10, 0, 0);
            Assert.That(progress.IsFinal, Is.True);
        }

        [Test]
        public void SubOperationProgress_IsFinal_FalseWhenRemainingIsNotZero()
        {
            var progress = new SubOperationProgress(5, 10, 0, 0);
            Assert.That(progress.IsFinal, Is.False);
        }

        [Test]
        public void SubOperationProgress_HasErrors_TrueWhenFailedGreaterThanZero()
        {
            var progress = new SubOperationProgress(0, 10, 2, 0);
            Assert.That(progress.HasErrors, Is.True);
        }

        [Test]
        public void SubOperationProgress_HasErrors_FalseWhenFailedIsZero()
        {
            var progress = new SubOperationProgress(0, 10, 0, 0);
            Assert.That(progress.HasErrors, Is.False);
        }

        [Test]
        public void SubOperationProgress_HasWarnings_TrueWhenWarningGreaterThanZero()
        {
            var progress = new SubOperationProgress(0, 10, 0, 3);
            Assert.That(progress.HasWarnings, Is.True);
        }

        [Test]
        public void SubOperationProgress_HasWarnings_FalseWhenWarningIsZero()
        {
            var progress = new SubOperationProgress(0, 10, 0, 0);
            Assert.That(progress.HasWarnings, Is.False);
        }

        [Test]
        public void SubOperationProgress_Empty_ReturnsAllZeros()
        {
            var progress = SubOperationProgress.Empty;
            Assert.That(progress.Remaining, Is.EqualTo(0));
            Assert.That(progress.Completed, Is.EqualTo(0));
            Assert.That(progress.Failed, Is.EqualTo(0));
            Assert.That(progress.Warning, Is.EqualTo(0));
        }

        [Test]
        public void SubOperationProgress_Successful_CreatesCorrectProgress()
        {
            var progress = SubOperationProgress.Successful(15);
            Assert.That(progress.Remaining, Is.EqualTo(0));
            Assert.That(progress.Completed, Is.EqualTo(15));
            Assert.That(progress.Failed, Is.EqualTo(0));
            Assert.That(progress.Warning, Is.EqualTo(0));
            Assert.That(progress.IsFinal, Is.True);
        }

        #endregion

        #region DicomTransferProgress Tests

        [Test]
        public void DicomTransferProgress_PercentComplete_CalculatesCorrectly()
        {
            var progress = new DicomTransferProgress(500, 1000, 100);
            Assert.That(progress.PercentComplete, Is.EqualTo(50.0));
        }

        [Test]
        public void DicomTransferProgress_PercentComplete_ReturnsZeroWhenTotalBytesIsZero()
        {
            var progress = new DicomTransferProgress(500, 0, 100);
            Assert.That(progress.PercentComplete, Is.EqualTo(0));
        }

        [Test]
        public void DicomTransferProgress_EstimatedTimeRemaining_CalculatesCorrectly()
        {
            var progress = new DicomTransferProgress(500, 1000, 100);
            var eta = progress.EstimatedTimeRemaining;
            Assert.That(eta, Is.Not.Null);
            Assert.That(eta!.Value.TotalSeconds, Is.EqualTo(5)); // 500 bytes remaining at 100 B/s
        }

        [Test]
        public void DicomTransferProgress_EstimatedTimeRemaining_ReturnsNullWhenBytesPerSecondIsZero()
        {
            var progress = new DicomTransferProgress(500, 1000, 0);
            Assert.That(progress.EstimatedTimeRemaining, Is.Null);
        }

        [Test]
        public void DicomTransferProgress_EstimatedTimeRemaining_ReturnsNullWhenTotalBytesIsZero()
        {
            var progress = new DicomTransferProgress(500, 0, 100);
            Assert.That(progress.EstimatedTimeRemaining, Is.Null);
        }

        [Test]
        public void DicomTransferProgress_EstimatedTimeRemaining_ReturnsZeroWhenComplete()
        {
            var progress = new DicomTransferProgress(1000, 1000, 100);
            var eta = progress.EstimatedTimeRemaining;
            Assert.That(eta, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void DicomTransferProgress_IsComplete_TrueWhenBytesTransferredEqualsTotal()
        {
            var progress = new DicomTransferProgress(1000, 1000, 100);
            Assert.That(progress.IsComplete, Is.True);
        }

        [Test]
        public void DicomTransferProgress_IsComplete_FalseWhenNotComplete()
        {
            var progress = new DicomTransferProgress(500, 1000, 100);
            Assert.That(progress.IsComplete, Is.False);
        }

        [Test]
        public void DicomTransferProgress_Initial_CreatesCorrectProgress()
        {
            var progress = DicomTransferProgress.Initial(1000);
            Assert.That(progress.BytesTransferred, Is.EqualTo(0));
            Assert.That(progress.TotalBytes, Is.EqualTo(1000));
            Assert.That(progress.BytesPerSecond, Is.EqualTo(0));
        }

        [Test]
        public void DicomTransferProgress_Completed_CreatesCorrectProgress()
        {
            var progress = DicomTransferProgress.Completed(1000, 250);
            Assert.That(progress.BytesTransferred, Is.EqualTo(1000));
            Assert.That(progress.TotalBytes, Is.EqualTo(1000));
            Assert.That(progress.BytesPerSecond, Is.EqualTo(250));
            Assert.That(progress.IsComplete, Is.True);
        }

        #endregion

        #region DicomCommand Factory Method Tests

        [Test]
        public void CreateCFindRequest_ProducesValidCommand()
        {
            var cmd = DicomCommand.CreateCFindRequest(1, DicomUID.PatientRootQueryRetrieveFind);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveFind));
            Assert.That(cmd.CommandFieldValue, Is.EqualTo(CommandField.CFindRequest));
            Assert.That(cmd.MessageID, Is.EqualTo(1));
            Assert.That(cmd.HasDataset, Is.True);
            Assert.That(cmd.IsCFindRequest, Is.True);
        }

        [Test]
        public void CreateCFindResponse_WithPendingStatus_HasDataset()
        {
            var cmd = DicomCommand.CreateCFindResponse(1, DicomUID.PatientRootQueryRetrieveFind, DicomStatus.Pending);

            Assert.That(cmd.IsCFindResponse, Is.True);
            Assert.That(cmd.HasDataset, Is.True);
            Assert.That(cmd.Status.IsPending, Is.True);
        }

        [Test]
        public void CreateCFindResponse_WithSuccessStatus_HasNoDataset()
        {
            var cmd = DicomCommand.CreateCFindResponse(1, DicomUID.PatientRootQueryRetrieveFind, DicomStatus.Success);

            Assert.That(cmd.IsCFindResponse, Is.True);
            Assert.That(cmd.HasDataset, Is.False);
            Assert.That(cmd.Status.IsSuccess, Is.True);
        }

        [Test]
        public void CreateCMoveRequest_IncludesMoveDestination()
        {
            var cmd = DicomCommand.CreateCMoveRequest(1, DicomUID.PatientRootQueryRetrieveMove, "DESTAE");

            Assert.That(cmd.IsCMoveRequest, Is.True);
            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveMove));
            Assert.That(cmd.MoveDestination, Is.EqualTo("DESTAE"));
            Assert.That(cmd.HasDataset, Is.True);
        }

        [Test]
        public void CreateCMoveResponse_IncludesSubOperationCounts()
        {
            var progress = new SubOperationProgress(5, 10, 2, 1);
            var cmd = DicomCommand.CreateCMoveResponse(1, DicomUID.PatientRootQueryRetrieveMove, DicomStatus.Pending, progress);

            Assert.That(cmd.IsCMoveResponse, Is.True);
            Assert.That(cmd.NumberOfRemainingSuboperations, Is.EqualTo(5));
            Assert.That(cmd.NumberOfCompletedSuboperations, Is.EqualTo(10));
            Assert.That(cmd.NumberOfFailedSuboperations, Is.EqualTo(2));
            Assert.That(cmd.NumberOfWarningSuboperations, Is.EqualTo(1));
        }

        [Test]
        public void CreateCGetRequest_ProducesValidCommand()
        {
            var cmd = DicomCommand.CreateCGetRequest(1, DicomUID.PatientRootQueryRetrieveGet);

            Assert.That(cmd.IsCGetRequest, Is.True);
            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveGet));
            Assert.That(cmd.HasDataset, Is.True);
        }

        [Test]
        public void CreateCGetResponse_IncludesSubOperationCounts()
        {
            var progress = new SubOperationProgress(0, 15, 0, 0);
            var cmd = DicomCommand.CreateCGetResponse(1, DicomUID.PatientRootQueryRetrieveGet, DicomStatus.Success, progress);

            Assert.That(cmd.IsCGetResponse, Is.True);
            Assert.That(cmd.NumberOfRemainingSuboperations, Is.EqualTo(0));
            Assert.That(cmd.NumberOfCompletedSuboperations, Is.EqualTo(15));
            Assert.That(cmd.NumberOfFailedSuboperations, Is.EqualTo(0));
            Assert.That(cmd.NumberOfWarningSuboperations, Is.EqualTo(0));
        }

        [Test]
        public void CreateCCancelRequest_ProducesValidCommand()
        {
            var cmd = DicomCommand.CreateCCancelRequest(5);

            Assert.That(cmd.IsCCancelRequest, Is.True);
            Assert.That(cmd.CommandFieldValue, Is.EqualTo(CommandField.CCancelRequest));
            Assert.That(cmd.MessageIDBeingRespondedTo, Is.EqualTo(5));
            Assert.That(cmd.HasDataset, Is.False);
        }

        [Test]
        public void GetSubOperationProgress_ExtractsCountsCorrectly()
        {
            var progress = new SubOperationProgress(3, 7, 1, 2);
            var cmd = DicomCommand.CreateCMoveResponse(1, DicomUID.PatientRootQueryRetrieveMove, DicomStatus.Pending, progress);

            var extracted = cmd.GetSubOperationProgress();

            Assert.That(extracted.Remaining, Is.EqualTo(3));
            Assert.That(extracted.Completed, Is.EqualTo(7));
            Assert.That(extracted.Failed, Is.EqualTo(1));
            Assert.That(extracted.Warning, Is.EqualTo(2));
        }

        [Test]
        public void CreateCFindRequest_WithPriority_SetsPriorityCorrectly()
        {
            var cmd = DicomCommand.CreateCFindRequest(1, DicomUID.StudyRootQueryRetrieveFind, priority: 1);

            Assert.That(cmd.Priority, Is.EqualTo(1)); // HIGH
        }

        #endregion
    }
}
