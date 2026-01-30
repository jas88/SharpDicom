using System;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse;
using SharpDicom.Network.Dimse.Services;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Unit tests for CFindScu, CFindOptions, and DicomQuery.
    /// </summary>
    [TestFixture]
    public class CFindScuTests
    {
        #region CFindOptions Tests

        [Test]
        public void CFindOptions_Default_HasExpectedValues()
        {
            var options = CFindOptions.Default;

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.Priority, Is.EqualTo(0)); // MEDIUM
            Assert.That(options.UsePatientRoot, Is.True);
        }

        [Test]
        public void CFindOptions_Timeout_CanBeModified()
        {
            var options = new CFindOptions
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void CFindOptions_Priority_CanBeSetToHigh()
        {
            var options = new CFindOptions
            {
                Priority = 1 // HIGH
            };

            Assert.That(options.Priority, Is.EqualTo(1));
        }

        [Test]
        public void CFindOptions_UsePatientRoot_CanBeSetToFalse()
        {
            var options = new CFindOptions
            {
                UsePatientRoot = false
            };

            Assert.That(options.UsePatientRoot, Is.False);
        }

        #endregion

        #region DicomQuery Level Tests

        [Test]
        public void DicomQuery_ForPatients_SetsCorrectLevel()
        {
            var query = DicomQuery.ForPatients();

            Assert.That(query.Level, Is.EqualTo(QueryRetrieveLevel.Patient));
            var ds = query.ToDataset();
            Assert.That(ds.GetString(DicomTag.QueryRetrieveLevel), Is.EqualTo("PATIENT"));
        }

        [Test]
        public void DicomQuery_ForStudies_SetsCorrectLevel()
        {
            var query = DicomQuery.ForStudies();

            Assert.That(query.Level, Is.EqualTo(QueryRetrieveLevel.Study));
            var ds = query.ToDataset();
            Assert.That(ds.GetString(DicomTag.QueryRetrieveLevel), Is.EqualTo("STUDY"));
        }

        [Test]
        public void DicomQuery_ForSeries_SetsCorrectLevel()
        {
            var query = DicomQuery.ForSeries();

            Assert.That(query.Level, Is.EqualTo(QueryRetrieveLevel.Series));
            var ds = query.ToDataset();
            Assert.That(ds.GetString(DicomTag.QueryRetrieveLevel), Is.EqualTo("SERIES"));
        }

        [Test]
        public void DicomQuery_ForImages_SetsCorrectLevel()
        {
            var query = DicomQuery.ForImages();

            Assert.That(query.Level, Is.EqualTo(QueryRetrieveLevel.Image));
            var ds = query.ToDataset();
            Assert.That(ds.GetString(DicomTag.QueryRetrieveLevel), Is.EqualTo("IMAGE"));
        }

        #endregion

        #region DicomQuery Builder Tests

        [Test]
        public void DicomQuery_WithPatientName_AddsTagToDataset()
        {
            var query = DicomQuery.ForStudies()
                .WithPatientName("Smith*");

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.PatientName), Is.True);
            Assert.That(ds.GetString(DicomTag.PatientName), Is.EqualTo("Smith*"));
        }

        [Test]
        public void DicomQuery_WithPatientId_AddsTagToDataset()
        {
            var query = DicomQuery.ForStudies()
                .WithPatientId("12345");

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.PatientID), Is.True);
            Assert.That(ds.GetString(DicomTag.PatientID), Is.EqualTo("12345"));
        }

        [Test]
        public void DicomQuery_WithStudyDate_FormatsCorrectly()
        {
            var date = new DateTime(2026, 1, 15);
            var query = DicomQuery.ForStudies()
                .WithStudyDate(date);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.StudyDate), Is.True);
            Assert.That(ds.GetString(DicomTag.StudyDate), Is.EqualTo("20260115"));
        }

        [Test]
        public void DicomQuery_WithStudyDateRange_FormatsAsRange()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 1, 31);
            var query = DicomQuery.ForStudies()
                .WithStudyDateRange(from, to);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.StudyDate), Is.True);
            Assert.That(ds.GetString(DicomTag.StudyDate), Is.EqualTo("20260101-20260131"));
        }

        [Test]
        public void DicomQuery_WithModality_SingleValue_AddsTag()
        {
            var query = DicomQuery.ForStudies()
                .WithModality("CT");

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.ModalitiesInStudy), Is.True);
            Assert.That(ds.GetString(DicomTag.ModalitiesInStudy), Is.EqualTo("CT"));
        }

        [Test]
        public void DicomQuery_WithModality_MultipleValues_JoinsWithBackslash()
        {
            var query = DicomQuery.ForStudies()
                .WithModality("CT", "MR", "US");

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.ModalitiesInStudy), Is.True);
            Assert.That(ds.GetString(DicomTag.ModalitiesInStudy), Is.EqualTo("CT\\MR\\US"));
        }

        [Test]
        public void DicomQuery_WithAccessionNumber_AddsTag()
        {
            var query = DicomQuery.ForStudies()
                .WithAccessionNumber("ACC123");

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.AccessionNumber), Is.True);
            Assert.That(ds.GetString(DicomTag.AccessionNumber), Is.EqualTo("ACC123"));
        }

        [Test]
        public void DicomQuery_WithStudyInstanceUid_AddsTag()
        {
            var uid = "1.2.3.4.5.6.7.8.9";
            var query = DicomQuery.ForSeries()
                .WithStudyInstanceUid(uid);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.StudyInstanceUID), Is.True);
            // UI VR values are null-padded, so trim for comparison
            Assert.That(ds.GetString(DicomTag.StudyInstanceUID)?.TrimEnd('\0'), Is.EqualTo(uid));
        }

        [Test]
        public void DicomQuery_WithSeriesInstanceUid_AddsTag()
        {
            var uid = "1.2.3.4.5.6.7.8.9.10";
            var query = DicomQuery.ForImages()
                .WithSeriesInstanceUid(uid);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.SeriesInstanceUID), Is.True);
            Assert.That(ds.GetString(DicomTag.SeriesInstanceUID)?.TrimEnd('\0'), Is.EqualTo(uid));
        }

        [Test]
        public void DicomQuery_WithSopInstanceUid_AddsTag()
        {
            var uid = "1.2.3.4.5.6.7.8.9.10.11";
            var query = DicomQuery.ForImages()
                .WithSopInstanceUid(uid);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.SOPInstanceUID), Is.True);
            Assert.That(ds.GetString(DicomTag.SOPInstanceUID)?.TrimEnd('\0'), Is.EqualTo(uid));
        }

        [Test]
        public void DicomQuery_ReturnField_AddsZeroLengthElement()
        {
            var query = DicomQuery.ForStudies()
                .ReturnField(DicomTag.PatientName);

            var ds = query.ToDataset();
            Assert.That(ds.Contains(DicomTag.PatientName), Is.True);
            // Zero-length element should have empty value
            var element = ds[DicomTag.PatientName];
            Assert.That(element, Is.Not.Null);
            Assert.That(element!.RawValue.Length, Is.EqualTo(0));
        }

        [Test]
        public void DicomQuery_ReturnField_DoesNotOverwriteExistingValue()
        {
            var query = DicomQuery.ForStudies()
                .WithPatientName("Smith*")
                .ReturnField(DicomTag.PatientName); // Should not overwrite

            var ds = query.ToDataset();
            Assert.That(ds.GetString(DicomTag.PatientName), Is.EqualTo("Smith*"));
        }

        [Test]
        public void DicomQuery_FluentChaining_Works()
        {
            var query = DicomQuery.ForStudies()
                .WithPatientName("Smith*")
                .WithPatientId("12345")
                .WithStudyDate(new DateTime(2026, 1, 15))
                .WithModality("CT", "MR")
                .ReturnField(DicomTag.SOPClassUID);

            var ds = query.ToDataset();
            Assert.That(ds.Count, Is.EqualTo(6)); // Level + 4 criteria + 1 return field
        }

        [Test]
        public void DicomQuery_ToDataset_ReturnsSameInstance()
        {
            var query = DicomQuery.ForStudies();

            var ds1 = query.ToDataset();
            var ds2 = query.ToDataset();

            Assert.That(ds1, Is.SameAs(ds2));
        }

        #endregion

        #region CFindScu Constructor Tests

        [Test]
        public void CFindScu_NullClient_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CFindScu(null!));
        }

        [Test]
        public void CFindScu_DefaultOptions_UsesDefaults()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            var scu = new CFindScu(client);

            Assert.That(scu.Options, Is.SameAs(CFindOptions.Default));
        }

        [Test]
        public void CFindScu_CustomOptions_UsesProvided()
        {
            var clientOptions = new DicomClientOptions
            {
                Host = "localhost",
                Port = 11112,
                CalledAE = "CALLED",
                CallingAE = "CALLING"
            };
            var client = new DicomClient(clientOptions);
            var options = new CFindOptions { UsePatientRoot = false };
            var scu = new CFindScu(client, options);

            Assert.That(scu.Options, Is.SameAs(options));
            Assert.That(scu.Options.UsePatientRoot, Is.False);
        }

        #endregion

        #region Command Creation Tests

        [Test]
        public void CreateCFindRequest_PatientRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCFindRequest(1, DicomUID.PatientRootQueryRetrieveFind);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.PatientRootQueryRetrieveFind));
            Assert.That(cmd.IsCFindRequest, Is.True);
            Assert.That(cmd.HasDataset, Is.True);
        }

        [Test]
        public void CreateCFindRequest_StudyRoot_CorrectSopClassUid()
        {
            var cmd = DicomCommand.CreateCFindRequest(1, DicomUID.StudyRootQueryRetrieveFind);

            Assert.That(cmd.AffectedSOPClassUID, Is.EqualTo(DicomUID.StudyRootQueryRetrieveFind));
            Assert.That(cmd.IsCFindRequest, Is.True);
        }

        [Test]
        public void CreateCFindRequest_MessageIdIncrement_Works()
        {
            var cmd1 = DicomCommand.CreateCFindRequest(1, DicomUID.PatientRootQueryRetrieveFind);
            var cmd2 = DicomCommand.CreateCFindRequest(2, DicomUID.PatientRootQueryRetrieveFind);

            Assert.That(cmd1.MessageID, Is.EqualTo(1));
            Assert.That(cmd2.MessageID, Is.EqualTo(2));
        }

        [Test]
        public void CreateCCancelRequest_CorrectMessageId()
        {
            var cmd = DicomCommand.CreateCCancelRequest(42);

            Assert.That(cmd.IsCCancelRequest, Is.True);
            Assert.That(cmd.MessageIDBeingRespondedTo, Is.EqualTo(42));
            Assert.That(cmd.HasDataset, Is.False);
        }

        #endregion

        #region QueryRetrieveLevel Extension Tests

        [Test]
        public void GetPatientRootFindSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetPatientRootFindSopClassUid(),
                Is.EqualTo(DicomUID.PatientRootQueryRetrieveFind));
        }

        [Test]
        public void GetStudyRootFindSopClassUid_ReturnsCorrectUid()
        {
            Assert.That(
                QueryRetrieveLevel.Study.GetStudyRootFindSopClassUid(),
                Is.EqualTo(DicomUID.StudyRootQueryRetrieveFind));
        }

        #endregion
    }
}
