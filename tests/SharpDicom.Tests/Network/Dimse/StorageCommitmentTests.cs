using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Data;
using SharpDicom.Network;
using SharpDicom.Network.Dimse.Services;
using SharpDicom.Network.Dimse.Services.StorageCommitment;

namespace SharpDicom.Tests.Network.Dimse
{
    /// <summary>
    /// Tests for Storage Commitment types (SopInstanceReference, StorageCommitmentRequest,
    /// StorageCommitmentResult) and StorageCommitmentScpHandler.
    /// </summary>
    [TestFixture]
    public class StorageCommitmentTests
    {
        private static readonly DicomUID TestClassUid1 = new("1.2.840.10008.5.1.4.1.1.2");
        private static readonly DicomUID TestClassUid2 = new("1.2.840.10008.5.1.4.1.1.7");
        private static readonly DicomUID TestInstanceUid1 = DicomUID.Generate();
        private static readonly DicomUID TestInstanceUid2 = DicomUID.Generate();
        private static readonly DicomUID TestTransactionUid = DicomUID.Generate();

        #region SopInstanceReference Tests

        [Test]
        public void Equality_SameValues_AreEqual()
        {
            var ref1 = new SopInstanceReference(TestClassUid1, TestInstanceUid1);
            var ref2 = new SopInstanceReference(TestClassUid1, TestInstanceUid1);

            Assert.That(ref1, Is.EqualTo(ref2));
            Assert.That(ref1 == ref2, Is.True);
            Assert.That(ref1.GetHashCode(), Is.EqualTo(ref2.GetHashCode()));
        }

        [Test]
        public void Equality_DifferentValues_AreNotEqual()
        {
            var ref1 = new SopInstanceReference(TestClassUid1, TestInstanceUid1);
            var ref2 = new SopInstanceReference(TestClassUid2, TestInstanceUid2);

            Assert.That(ref1, Is.Not.EqualTo(ref2));
            Assert.That(ref1 != ref2, Is.True);
        }

        #endregion

        #region StorageCommitmentRequest Tests

        [Test]
        public void ToDataset_ContainsTransactionUID()
        {
            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1)
            };
            var request = new StorageCommitmentRequest(TestTransactionUid, instances);

            var dataset = request.ToDataset();

            var uid = dataset.GetUID(DicomTag.TransactionUID);
            Assert.That(uid, Is.Not.Null);
            Assert.That(uid!.Value, Is.EqualTo(TestTransactionUid));
        }

        [Test]
        public void ToDataset_ContainsReferencedSOPSequence()
        {
            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1),
                new SopInstanceReference(TestClassUid2, TestInstanceUid2)
            };
            var request = new StorageCommitmentRequest(TestTransactionUid, instances);

            var dataset = request.ToDataset();

            var sequence = dataset.GetSequence(DicomTag.ReferencedSOPSequence);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(2));
        }

        [Test]
        public void FromDataset_ParsesTransactionUID()
        {
            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1)
            };
            var original = new StorageCommitmentRequest(TestTransactionUid, instances);
            var dataset = original.ToDataset();

            var parsed = StorageCommitmentRequest.FromDataset(dataset);

            Assert.That(parsed.TransactionUID, Is.EqualTo(TestTransactionUid));
        }

        [Test]
        public void FromDataset_ParsesReferencedInstances()
        {
            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1),
                new SopInstanceReference(TestClassUid2, TestInstanceUid2)
            };
            var original = new StorageCommitmentRequest(TestTransactionUid, instances);
            var dataset = original.ToDataset();

            var parsed = StorageCommitmentRequest.FromDataset(dataset);

            Assert.That(parsed.ReferencedInstances.Count, Is.EqualTo(2));
            Assert.That(parsed.ReferencedInstances[0].SOPClassUID, Is.EqualTo(TestClassUid1));
            Assert.That(parsed.ReferencedInstances[0].SOPInstanceUID, Is.EqualTo(TestInstanceUid1));
            Assert.That(parsed.ReferencedInstances[1].SOPClassUID, Is.EqualTo(TestClassUid2));
            Assert.That(parsed.ReferencedInstances[1].SOPInstanceUID, Is.EqualTo(TestInstanceUid2));
        }

        [Test]
        public void RoundTrip_ToDatasetThenFromDataset_PreservesAll()
        {
            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1),
                new SopInstanceReference(TestClassUid2, TestInstanceUid2)
            };
            var original = new StorageCommitmentRequest(TestTransactionUid, instances);

            var dataset = original.ToDataset();
            var parsed = StorageCommitmentRequest.FromDataset(dataset);

            Assert.That(parsed.TransactionUID, Is.EqualTo(original.TransactionUID));
            Assert.That(parsed.ReferencedInstances.Count, Is.EqualTo(original.ReferencedInstances.Count));
            for (int i = 0; i < original.ReferencedInstances.Count; i++)
            {
                Assert.That(parsed.ReferencedInstances[i], Is.EqualTo(original.ReferencedInstances[i]));
            }
        }

        #endregion

        #region StorageCommitmentResult Tests

        [Test]
        public void AllSuccessful_EventType1_ReturnsTrue()
        {
            var result = new StorageCommitmentResult(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeAllSuccess,
                new List<SopInstanceReference> { new SopInstanceReference(TestClassUid1, TestInstanceUid1) },
                new List<FailedSopInstanceReference>());

            Assert.That(result.AllSuccessful, Is.True);
            Assert.That(result.EventTypeID, Is.EqualTo(1));
        }

        [Test]
        public void AllSuccessful_EventType2_ReturnsFalse()
        {
            var failedRef = new FailedSopInstanceReference(
                new SopInstanceReference(TestClassUid1, TestInstanceUid1), 0x0112);

            var result = new StorageCommitmentResult(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeFailures,
                new List<SopInstanceReference>(),
                new List<FailedSopInstanceReference> { failedRef });

            Assert.That(result.AllSuccessful, Is.False);
            Assert.That(result.EventTypeID, Is.EqualTo(2));
        }

        [Test]
        public void ToDataset_ContainsSuccessInstances()
        {
            var successRef = new SopInstanceReference(TestClassUid1, TestInstanceUid1);
            var result = new StorageCommitmentResult(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeAllSuccess,
                new List<SopInstanceReference> { successRef },
                new List<FailedSopInstanceReference>());

            var dataset = result.ToDataset();

            var sequence = dataset.GetSequence(DicomTag.ReferencedSOPSequence);
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence!.Items.Count, Is.EqualTo(1));

            var item = sequence.Items[0];
            var sopClassUid = item.GetUID(DicomTag.ReferencedSOPClassUID);
            var sopInstanceUid = item.GetUID(DicomTag.ReferencedSOPInstanceUID);
            Assert.That(sopClassUid, Is.Not.Null);
            Assert.That(sopClassUid!.Value, Is.EqualTo(TestClassUid1));
            Assert.That(sopInstanceUid, Is.Not.Null);
            Assert.That(sopInstanceUid!.Value, Is.EqualTo(TestInstanceUid1));
        }

        [Test]
        public void ToDataset_ContainsFailureInstances()
        {
            var failedRef = new FailedSopInstanceReference(
                new SopInstanceReference(TestClassUid1, TestInstanceUid1), 0x0112);

            var result = new StorageCommitmentResult(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeFailures,
                new List<SopInstanceReference>(),
                new List<FailedSopInstanceReference> { failedRef });

            var dataset = result.ToDataset();

            var failedSeq = dataset.GetSequence(DicomTag.FailedSOPSequence);
            Assert.That(failedSeq, Is.Not.Null);
            Assert.That(failedSeq!.Items.Count, Is.EqualTo(1));

            var item = failedSeq.Items[0];
            var failureReasonElement = item[DicomTag.FailureReason] as DicomNumericElement;
            Assert.That(failureReasonElement, Is.Not.Null);
            Assert.That(failureReasonElement!.GetUInt16(), Is.EqualTo(0x0112));
        }

        [Test]
        public void FromDataset_Roundtrip_PreservesAll()
        {
            var successRef = new SopInstanceReference(TestClassUid1, TestInstanceUid1);
            var failedRef = new FailedSopInstanceReference(
                new SopInstanceReference(TestClassUid2, TestInstanceUid2), 0x0110);

            var original = new StorageCommitmentResult(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeFailures,
                new List<SopInstanceReference> { successRef },
                new List<FailedSopInstanceReference> { failedRef });

            var dataset = original.ToDataset();
            var parsed = StorageCommitmentResult.FromDataset(
                TestTransactionUid,
                StorageCommitmentResult.EventTypeFailures,
                dataset);

            Assert.That(parsed.TransactionUID, Is.EqualTo(original.TransactionUID));
            Assert.That(parsed.EventTypeID, Is.EqualTo(original.EventTypeID));
            Assert.That(parsed.SuccessInstances.Count, Is.EqualTo(1));
            Assert.That(parsed.SuccessInstances[0], Is.EqualTo(successRef));
            Assert.That(parsed.FailureInstances.Count, Is.EqualTo(1));
            Assert.That(parsed.FailureInstances[0].Reference, Is.EqualTo(failedRef.Reference));
            Assert.That(parsed.FailureInstances[0].FailureReason, Is.EqualTo(failedRef.FailureReason));
        }

        #endregion

        #region StorageCommitmentScpHandler Tests

        [Test]
        public async Task OnNAction_ValidRequest_ReturnsSuccess()
        {
            var verifier = new AllSuccessVerifier();
            var handler = new StorageCommitmentScpHandler(verifier);

            var instances = new List<SopInstanceReference>
            {
                new SopInstanceReference(TestClassUid1, TestInstanceUid1)
            };
            var request = new StorageCommitmentRequest(TestTransactionUid, instances);
            var actionDataset = request.ToDataset();

            var context = CreateNActionContext(
                DicomUID.StorageCommitmentPushModel,
                DicomUID.StorageCommitmentPushModelInstance,
                actionTypeId: 1);

            var response = await handler.OnNActionAsync(context, actionDataset, CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.Success.Code));
            Assert.That(handler.HasPendingResult, Is.True);

            var result = handler.TakeResult();
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.AllSuccessful, Is.True);
        }

        [Test]
        public async Task OnNAction_WrongSOPClass_ReturnsNoSuchSOPClass()
        {
            var verifier = new AllSuccessVerifier();
            var handler = new StorageCommitmentScpHandler(verifier);

            var wrongClassUid = new DicomUID("1.2.3.4.5.6.99");
            var context = CreateNActionContext(wrongClassUid, TestInstanceUid1, actionTypeId: 1);

            var response = await handler.OnNActionAsync(context, new DicomDataset(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.NoSuchSOPClass.Code));
        }

        [Test]
        public async Task OnNAction_WrongActionTypeId_ReturnsNoSuchActionType()
        {
            var verifier = new AllSuccessVerifier();
            var handler = new StorageCommitmentScpHandler(verifier);

            var context = CreateNActionContext(
                DicomUID.StorageCommitmentPushModel,
                DicomUID.StorageCommitmentPushModelInstance,
                actionTypeId: 99); // Wrong action type

            var response = await handler.OnNActionAsync(context, new DicomDataset(), CancellationToken.None);

            Assert.That(response.Status.Code, Is.EqualTo(DicomStatus.NoSuchActionType.Code));
        }

        #endregion

        #region Helper Classes

        private sealed class AllSuccessVerifier : IStorageVerifier
        {
            public ValueTask<IReadOnlyList<FailedSopInstanceReference>> VerifyAsync(
                IReadOnlyList<SopInstanceReference> instances,
                CancellationToken ct)
            {
                return new ValueTask<IReadOnlyList<FailedSopInstanceReference>>(
                    (IReadOnlyList<FailedSopInstanceReference>)Array.Empty<FailedSopInstanceReference>());
            }
        }

        #endregion

        #region Helper Methods

        private static NActionRequestContext CreateNActionContext(
            DicomUID sopClassUid,
            DicomUID sopInstanceUid,
            ushort actionTypeId)
        {
            return new NActionRequestContext(
                callingAE: "SCU_AE",
                calledAE: "SCP_AE",
                sopClassUid: sopClassUid,
                sopInstanceUid: sopInstanceUid,
                messageId: 1,
                presentationContextId: 1,
                actionTypeId: actionTypeId);
        }

        #endregion
    }
}
