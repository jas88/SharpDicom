using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Tests for all N-Service command factory methods on <see cref="DicomCommand"/>,
    /// verifying correct use of Affected vs Requested UIDs and command field values.
    /// </summary>
    [TestFixture]
    public class NServiceCommandTests
    {
        private static readonly DicomUID TestSopClassUid = new("1.2.3.4.5.6.7.8.9");
        private static readonly DicomUID TestSopInstanceUid = new("1.2.3.4.5.6.7.8.9.1.2.3");

        #region N-CREATE Tests

        [Test]
        public void CreateNCreateRequest_UsesAffectedSOPClassUID()
        {
            var cmd = DicomCommand.CreateNCreateRequest(1, TestSopClassUid);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.False);
        }

        [Test]
        public void CreateNCreateRequest_SetsCommandField_NCreateRequest()
        {
            var cmd = DicomCommand.CreateNCreateRequest(1, TestSopClassUid);

            Assert.That(cmd.CommandFieldValue, Is.EqualTo(CommandField.NCreateRequest));
            Assert.That(cmd.CommandFieldValue, Is.EqualTo((ushort)0x0140));
        }

        [Test]
        public void CreateNCreateRequest_WithoutInstanceUID_OmitsAffectedSOPInstanceUID()
        {
            var cmd = DicomCommand.CreateNCreateRequest(1, TestSopClassUid);

            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPInstanceUID), Is.False);
        }

        [Test]
        public void CreateNCreateRequest_WithInstanceUID_SetsAffectedSOPInstanceUID()
        {
            var cmd = DicomCommand.CreateNCreateRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.AffectedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPInstanceUID), Is.True);
        }

        [Test]
        public void CreateNCreateResponse_SetsAffectedSOPInstanceUID()
        {
            var cmd = DicomCommand.CreateNCreateResponse(1, TestSopClassUid, TestSopInstanceUid, DicomStatus.Success);

            Assert.That(cmd.AffectedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.IsNCreateResponse, Is.True);
        }

        #endregion

        #region N-SET Tests

        [Test]
        public void CreateNSetRequest_UsesRequestedSOPClassUID()
        {
            var cmd = DicomCommand.CreateNSetRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.RequestedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.False);
        }

        [Test]
        public void CreateNSetRequest_UsesRequestedSOPInstanceUID()
        {
            var cmd = DicomCommand.CreateNSetRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.RequestedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPInstanceUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPInstanceUID), Is.False);
        }

        [Test]
        public void CreateNSetRequest_SetsCommandField_NSetRequest()
        {
            var cmd = DicomCommand.CreateNSetRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.CommandFieldValue, Is.EqualTo(CommandField.NSetRequest));
            Assert.That(cmd.CommandFieldValue, Is.EqualTo((ushort)0x0120));
        }

        [Test]
        public void CreateNSetResponse_UsesAffectedSOPClassUID()
        {
            var cmd = DicomCommand.CreateNSetResponse(1, TestSopClassUid, TestSopInstanceUid, DicomStatus.Success);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.AffectedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.True);
            Assert.That(cmd.IsNSetResponse, Is.True);
        }

        #endregion

        #region N-GET Tests

        [Test]
        public void CreateNGetRequest_UsesRequestedUIDs()
        {
            var cmd = DicomCommand.CreateNGetRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.RequestedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.RequestedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPInstanceUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.False);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPInstanceUID), Is.False);
        }

        [Test]
        public void CreateNGetRequest_SetsNoDataSetPresent()
        {
            var cmd = DicomCommand.CreateNGetRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.HasDataset, Is.False);
            Assert.That(cmd.CommandDataSetType, Is.EqualTo(DicomCommand.NoDataSetPresent));
        }

        #endregion

        #region N-DELETE Tests

        [Test]
        public void CreateNDeleteRequest_UsesRequestedUIDs()
        {
            var cmd = DicomCommand.CreateNDeleteRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.RequestedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.RequestedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPInstanceUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.False);
        }

        [Test]
        public void CreateNDeleteRequest_SetsNoDataSetPresent()
        {
            var cmd = DicomCommand.CreateNDeleteRequest(1, TestSopClassUid, TestSopInstanceUid);

            Assert.That(cmd.HasDataset, Is.False);
            Assert.That(cmd.CommandDataSetType, Is.EqualTo(DicomCommand.NoDataSetPresent));
        }

        #endregion

        #region N-ACTION Tests

        [Test]
        public void CreateNActionRequest_SetsActionTypeID()
        {
            var cmd = DicomCommand.CreateNActionRequest(1, TestSopClassUid, TestSopInstanceUid, 42);

            Assert.That(cmd.ActionTypeID, Is.EqualTo(42));
        }

        [Test]
        public void CreateNActionRequest_UsesRequestedUIDs()
        {
            var cmd = DicomCommand.CreateNActionRequest(1, TestSopClassUid, TestSopInstanceUid, 1);

            Assert.That(cmd.RequestedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.RequestedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPInstanceUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.False);
        }

        #endregion

        #region N-EVENT-REPORT Tests

        [Test]
        public void CreateNEventReportRequest_SetsEventTypeID()
        {
            var cmd = DicomCommand.CreateNEventReportRequest(1, TestSopClassUid, TestSopInstanceUid, 7);

            Assert.That(cmd.EventTypeID, Is.EqualTo(7));
        }

        [Test]
        public void CreateNEventReportRequest_UsesAffectedUIDs()
        {
            var cmd = DicomCommand.CreateNEventReportRequest(1, TestSopClassUid, TestSopInstanceUid, 1);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(TestSopClassUid));
            Assert.That(cmd.AffectedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPClassUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.AffectedSOPInstanceUID), Is.True);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPClassUID), Is.False);
            Assert.That(cmd.Dataset.Contains(DicomTag.RequestedSOPInstanceUID), Is.False);
        }

        #endregion

        #region Convenience Property Tests

        [Test]
        public void IsNCreateRequest_ReturnsTrue_ForNCreateCommand()
        {
            var cmd = DicomCommand.CreateNCreateRequest(1, TestSopClassUid);
            Assert.That(cmd.IsNCreateRequest, Is.True);
            Assert.That(cmd.IsRequest, Is.True);
            Assert.That(cmd.IsResponse, Is.False);
        }

        [Test]
        public void IsNCreateResponse_ReturnsTrue_ForNCreateResponseCommand()
        {
            var cmd = DicomCommand.CreateNCreateResponse(1, TestSopClassUid, TestSopInstanceUid, DicomStatus.Success);
            Assert.That(cmd.IsNCreateResponse, Is.True);
            Assert.That(cmd.IsResponse, Is.True);
            Assert.That(cmd.IsRequest, Is.False);
        }

        [Test]
        public void EventTypeID_ReturnsValue_FromCommand()
        {
            var cmd = DicomCommand.CreateNEventReportRequest(1, TestSopClassUid, TestSopInstanceUid, 99);
            Assert.That(cmd.EventTypeID, Is.EqualTo(99));
        }

        [Test]
        public void ActionTypeID_ReturnsValue_FromCommand()
        {
            var cmd = DicomCommand.CreateNActionRequest(1, TestSopClassUid, TestSopInstanceUid, 55);
            Assert.That(cmd.ActionTypeID, Is.EqualTo(55));
        }

        [Test]
        public void RequestedSOPInstanceUID_ReturnsValue_FromCommand()
        {
            var cmd = DicomCommand.CreateNSetRequest(1, TestSopClassUid, TestSopInstanceUid);
            Assert.That(cmd.RequestedSOPInstanceUID, Is.EqualTo(TestSopInstanceUid));
        }

        [Test]
        public void IsNSetRequest_ReturnsTrue_ForNSetCommand()
        {
            var cmd = DicomCommand.CreateNSetRequest(1, TestSopClassUid, TestSopInstanceUid);
            Assert.That(cmd.IsNSetRequest, Is.True);
        }

        [Test]
        public void IsNGetRequest_ReturnsTrue_ForNGetCommand()
        {
            var cmd = DicomCommand.CreateNGetRequest(1, TestSopClassUid, TestSopInstanceUid);
            Assert.That(cmd.IsNGetRequest, Is.True);
        }

        [Test]
        public void IsNDeleteRequest_ReturnsTrue_ForNDeleteCommand()
        {
            var cmd = DicomCommand.CreateNDeleteRequest(1, TestSopClassUid, TestSopInstanceUid);
            Assert.That(cmd.IsNDeleteRequest, Is.True);
        }

        [Test]
        public void IsNActionRequest_ReturnsTrue_ForNActionCommand()
        {
            var cmd = DicomCommand.CreateNActionRequest(1, TestSopClassUid, TestSopInstanceUid, 1);
            Assert.That(cmd.IsNActionRequest, Is.True);
        }

        [Test]
        public void IsNEventReportRequest_ReturnsTrue_ForNEventReportCommand()
        {
            var cmd = DicomCommand.CreateNEventReportRequest(1, TestSopClassUid, TestSopInstanceUid, 1);
            Assert.That(cmd.IsNEventReportRequest, Is.True);
        }

        #endregion
    }
}
