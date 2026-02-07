using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Dimse.Services.Mpps;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Tests for MPPS state machine, persistence, and SCU/SCP handler behavior.
    /// </summary>
    [TestFixture]
    public class MppsTests
    {
        private static readonly DicomUID TestInstanceUid = DicomUID.Generate();

        #region MppsStatus Tests

        [Test]
        public void MppsStatus_HasThreeValues()
        {
            var values = Enum.GetValues<MppsStatus>();
            Assert.That(values.Length, Is.EqualTo(3));
            Assert.That(Enum.IsDefined<MppsStatus>(MppsStatus.InProgress), Is.True);
            Assert.That(Enum.IsDefined<MppsStatus>(MppsStatus.Completed), Is.True);
            Assert.That(Enum.IsDefined<MppsStatus>(MppsStatus.Discontinued), Is.True);
        }

        #endregion

        #region MppsInstance Tests

        [Test]
        public void CreateInProgress_SetsStatusInProgress()
        {
            var uid = DicomUID.Generate();
            var dataset = new DicomDataset();
            var instance = MppsInstance.CreateInProgress(uid, dataset);

            Assert.That(instance.Status, Is.EqualTo(MppsStatus.InProgress));
        }

        [Test]
        public void CreateInProgress_StoresSOPInstanceUID()
        {
            var uid = DicomUID.Generate();
            var dataset = new DicomDataset();
            var instance = MppsInstance.CreateInProgress(uid, dataset);

            Assert.That(instance.SOPInstanceUID, Is.EqualTo(uid));
        }

        [Test]
        public void CreateCompletedModification_SetsStatusCompleted()
        {
            var mod = MppsInstance.CreateCompletedModification();
            var statusString = mod.GetString(DicomTag.PerformedProcedureStepStatus);

            Assert.That(statusString, Is.Not.Null);
            Assert.That(MppsInstance.ParseStatus(statusString!), Is.EqualTo(MppsStatus.Completed));
        }

        [Test]
        public void CreateDiscontinuedModification_SetsStatusDiscontinued()
        {
            var mod = MppsInstance.CreateDiscontinuedModification();
            var statusString = mod.GetString(DicomTag.PerformedProcedureStepStatus);

            Assert.That(statusString, Is.Not.Null);
            Assert.That(MppsInstance.ParseStatus(statusString!), Is.EqualTo(MppsStatus.Discontinued));
        }

        #endregion

        #region MppsScpHandler State Machine Tests

        [Test]
        public async Task OnNCreate_InProgress_ReturnsSuccess()
        {
            var handler = new MppsScpHandler();
            var context = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep);
            var attributes = new DicomDataset();

            var response = await handler.OnNCreateAsync(context, attributes, CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
            Assert.That(response.AffectedSOPInstanceUID, Is.Not.Null);
        }

        [Test]
        public async Task OnNCreate_WrongSOPClass_ReturnsNoSuchSOPClass()
        {
            var handler = new MppsScpHandler();
            var wrongClassUid = new DicomUID("1.2.3.4.5.6.99");
            var context = CreateNCreateContext(wrongClassUid);

            var response = await handler.OnNCreateAsync(context, new DicomDataset(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.NoSuchSOPClass.Code));
        }

        [Test]
        public async Task OnNSet_InProgressToCompleted_ReturnsSuccess()
        {
            var handler = new MppsScpHandler();
            var instanceUid = DicomUID.Generate();

            // Create the MPPS instance first
            var createCtx = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNCreateAsync(createCtx, new DicomDataset(), CancellationToken.None);

            // Set to Completed
            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            var modificationList = MppsInstance.CreateCompletedModification();
            var response = await handler.OnNSetAsync(setCtx, modificationList, CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
        }

        [Test]
        public async Task OnNSet_InProgressToDiscontinued_ReturnsSuccess()
        {
            var handler = new MppsScpHandler();
            var instanceUid = DicomUID.Generate();

            // Create the MPPS instance
            var createCtx = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNCreateAsync(createCtx, new DicomDataset(), CancellationToken.None);

            // Set to Discontinued
            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            var modificationList = MppsInstance.CreateDiscontinuedModification();
            var response = await handler.OnNSetAsync(setCtx, modificationList, CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
        }

        [Test]
        public async Task OnNSet_CompletedToInProgress_ReturnsInvalidAttributeValue()
        {
            var handler = new MppsScpHandler();
            var instanceUid = DicomUID.Generate();

            // Create and complete
            var createCtx = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNCreateAsync(createCtx, new DicomDataset(), CancellationToken.None);

            var completeCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNSetAsync(completeCtx, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            // Try to transition from Completed -- should fail (terminal state)
            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            var response = await handler.OnNSetAsync(setCtx, MppsInstance.CreateDiscontinuedModification(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.InvalidAttributeValue.Code));
        }

        [Test]
        public async Task OnNSet_CompletedToCompleted_ReturnsInvalidAttributeValue()
        {
            var handler = new MppsScpHandler();
            var instanceUid = DicomUID.Generate();

            // Create and complete
            var createCtx = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNCreateAsync(createCtx, new DicomDataset(), CancellationToken.None);

            var completeCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNSetAsync(completeCtx, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            // Try Completed -> Completed -- should also fail
            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            var response = await handler.OnNSetAsync(setCtx, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.InvalidAttributeValue.Code));
        }

        [Test]
        public async Task OnNSet_DiscontinuedToCompleted_ReturnsInvalidAttributeValue()
        {
            var handler = new MppsScpHandler();
            var instanceUid = DicomUID.Generate();

            // Create and discontinue
            var createCtx = CreateNCreateContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNCreateAsync(createCtx, new DicomDataset(), CancellationToken.None);

            var discCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            await handler.OnNSetAsync(discCtx, MppsInstance.CreateDiscontinuedModification(), CancellationToken.None);

            // Try Discontinued -> Completed -- should fail (terminal state)
            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, instanceUid);
            var response = await handler.OnNSetAsync(setCtx, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.InvalidAttributeValue.Code));
        }

        [Test]
        public async Task OnNSet_NonExistent_ReturnsNoSuchObjectInstance()
        {
            var handler = new MppsScpHandler();
            var nonExistentUid = DicomUID.Generate();

            var setCtx = CreateNSetContext(DicomUID.ModalityPerformedProcedureStep, nonExistentUid);
            var response = await handler.OnNSetAsync(setCtx, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.NoSuchObjectInstance.Code));
        }

        #endregion

        #region InMemoryMppsPersistence Tests

        [Test]
        public async Task Put_NewInstance_Succeeds()
        {
            var persistence = new InMemoryMppsPersistence();
            var uid = DicomUID.Generate();
            var instance = MppsInstance.CreateInProgress(uid, new DicomDataset());

            await persistence.PutAsync(instance, CancellationToken.None);

            Assert.That(persistence.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Get_ExistingInstance_ReturnsInstance()
        {
            var persistence = new InMemoryMppsPersistence();
            var uid = DicomUID.Generate();
            var instance = MppsInstance.CreateInProgress(uid, new DicomDataset());
            await persistence.PutAsync(instance, CancellationToken.None);

            var retrieved = await persistence.GetAsync(uid, CancellationToken.None);

            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved!.SOPInstanceUID, Is.EqualTo(uid));
            Assert.That(retrieved.Status, Is.EqualTo(MppsStatus.InProgress));
        }

        [Test]
        public async Task Get_NonExistent_ReturnsNull()
        {
            var persistence = new InMemoryMppsPersistence();
            var nonExistentUid = DicomUID.Generate();

            var result = await persistence.GetAsync(nonExistentUid, CancellationToken.None);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task Update_ExistingInstance_UpdatesStatus()
        {
            var persistence = new InMemoryMppsPersistence();
            var uid = DicomUID.Generate();
            var instance = MppsInstance.CreateInProgress(uid, new DicomDataset());
            await persistence.PutAsync(instance, CancellationToken.None);

            await persistence.UpdateAsync(uid, MppsInstance.CreateCompletedModification(), CancellationToken.None);

            var updated = await persistence.GetAsync(uid, CancellationToken.None);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Status, Is.EqualTo(MppsStatus.Completed));
        }

        #endregion

        #region MppsScu API Shape Tests

        [Test]
        public void MppsScu_Constructor_AcceptsNServiceScu()
        {
            // Verify the API shape: MppsScu takes an NServiceScu
            // We can't fully test without a connected client, but we can verify
            // the constructor doesn't throw for null (it should throw ArgumentNullException)
            Assert.Throws<ArgumentNullException>(() => new MppsScu(null!));
        }

        #endregion

        #region Helper Methods

        private static NCreateRequestContext CreateNCreateContext(DicomUID sopClassUid, DicomUID? instanceUid = null)
        {
            return new NCreateRequestContext(
                callingAE: "SCU_AE",
                calledAE: "SCP_AE",
                sopClassUid: sopClassUid,
                sopInstanceUid: instanceUid ?? default,
                messageId: 1,
                presentationContextId: 1);
        }

        private static NSetRequestContext CreateNSetContext(DicomUID sopClassUid, DicomUID instanceUid)
        {
            return new NSetRequestContext(
                callingAE: "SCU_AE",
                calledAE: "SCP_AE",
                sopClassUid: sopClassUid,
                sopInstanceUid: instanceUid,
                messageId: 2,
                presentationContextId: 1);
        }

        #endregion
    }
}
