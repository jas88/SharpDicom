using System;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// A single failed SOP Instance reference with its failure reason.
    /// </summary>
    public readonly struct FailedSopInstanceReference
    {
        /// <summary>
        /// Gets the SOP Instance reference that failed.
        /// </summary>
        public SopInstanceReference Reference { get; }

        /// <summary>
        /// Gets the failure reason code per DICOM PS3.4 Table J.3-1.
        /// </summary>
        /// <remarks>
        /// Common failure reasons:
        /// 0x0110 - Processing failure
        /// 0x0112 - No such object instance
        /// 0x0213 - Resource limitation
        /// 0x0122 - Referenced SOP Class not supported
        /// </remarks>
        public ushort FailureReason { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FailedSopInstanceReference"/> struct.
        /// </summary>
        /// <param name="reference">The failed SOP Instance reference.</param>
        /// <param name="failureReason">The failure reason code.</param>
        public FailedSopInstanceReference(SopInstanceReference reference, ushort failureReason)
        {
            Reference = reference;
            FailureReason = failureReason;
        }
    }

    /// <summary>
    /// Storage Commitment result delivered via N-EVENT-REPORT per DICOM PS3.4 Annex J.
    /// </summary>
    /// <remarks>
    /// Contains the outcome of a Storage Commitment request:
    /// <list type="bullet">
    ///   <item><description>Event Type ID 1: all instances successfully committed</description></item>
    ///   <item><description>Event Type ID 2: one or more instances failed commitment</description></item>
    /// </list>
    /// </remarks>
    public sealed class StorageCommitmentResult
    {
        /// <summary>Event Type ID for all instances successfully committed.</summary>
        public const ushort EventTypeAllSuccess = 1;

        /// <summary>Event Type ID for one or more instances failed.</summary>
        public const ushort EventTypeFailures = 2;

        /// <summary>
        /// Gets the Transaction UID identifying the original commitment request.
        /// </summary>
        public DicomUID TransactionUID { get; }

        /// <summary>
        /// Gets the Event Type ID (1 = all success, 2 = failures present).
        /// </summary>
        public ushort EventTypeID { get; }

        /// <summary>
        /// Gets the list of successfully committed SOP Instances.
        /// </summary>
        public IReadOnlyList<SopInstanceReference> SuccessInstances { get; }

        /// <summary>
        /// Gets the list of failed SOP Instances with failure reasons.
        /// </summary>
        public IReadOnlyList<FailedSopInstanceReference> FailureInstances { get; }

        /// <summary>
        /// Gets a value indicating whether all instances were successfully committed.
        /// </summary>
        public bool AllSuccessful => EventTypeID == EventTypeAllSuccess;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageCommitmentResult"/> class.
        /// </summary>
        /// <param name="transactionUid">The Transaction UID.</param>
        /// <param name="eventTypeId">The Event Type ID.</param>
        /// <param name="successInstances">Successfully committed instances.</param>
        /// <param name="failureInstances">Failed instances with reasons.</param>
        public StorageCommitmentResult(
            DicomUID transactionUid,
            ushort eventTypeId,
            IReadOnlyList<SopInstanceReference> successInstances,
            IReadOnlyList<FailedSopInstanceReference> failureInstances)
        {
            TransactionUID = transactionUid;
            EventTypeID = eventTypeId;
            SuccessInstances = successInstances;
            FailureInstances = failureInstances;
        }

        /// <summary>
        /// Creates a result from a Storage Commitment N-EVENT-REPORT event information dataset.
        /// </summary>
        /// <param name="transactionUid">The Transaction UID from the event report.</param>
        /// <param name="eventTypeId">The Event Type ID from the event report.</param>
        /// <param name="dataset">The event information dataset.</param>
        /// <returns>The parsed result.</returns>
        public static StorageCommitmentResult FromDataset(
            DicomUID transactionUid,
            ushort eventTypeId,
            DicomDataset dataset)
        {
            var successInstances = new List<SopInstanceReference>();
            var failureInstances = new List<FailedSopInstanceReference>();

            // Parse Referenced SOP Sequence (success instances)
            var successSeq = dataset.GetSequence(DicomTag.ReferencedSOPSequence);
            if (successSeq != null)
            {
                foreach (var item in successSeq.Items)
                {
                    var sopClassUid = item.GetUID(DicomTag.ReferencedSOPClassUID);
                    var sopInstanceUid = item.GetUID(DicomTag.ReferencedSOPInstanceUID);
                    if (sopClassUid != null && sopInstanceUid != null)
                    {
                        successInstances.Add(new SopInstanceReference(sopClassUid.Value, sopInstanceUid.Value));
                    }
                }
            }

            // Parse Failed SOP Sequence (failure instances)
            var failedSeq = dataset.GetSequence(DicomTag.FailedSOPSequence);
            if (failedSeq != null)
            {
                foreach (var item in failedSeq.Items)
                {
                    var sopClassUid = item.GetUID(DicomTag.ReferencedSOPClassUID);
                    var sopInstanceUid = item.GetUID(DicomTag.ReferencedSOPInstanceUID);
                    var failureReason = item.GetInt32(DicomTag.FailureReason);
                    if (sopClassUid != null && sopInstanceUid != null)
                    {
                        var reference = new SopInstanceReference(sopClassUid.Value, sopInstanceUid.Value);
                        failureInstances.Add(new FailedSopInstanceReference(reference, (ushort)(failureReason ?? 0x0110)));
                    }
                }
            }

            return new StorageCommitmentResult(transactionUid, eventTypeId, successInstances, failureInstances);
        }

        /// <summary>
        /// Builds the N-EVENT-REPORT event information dataset from this result.
        /// </summary>
        /// <returns>The event information dataset.</returns>
        public DicomDataset ToDataset()
        {
            var dataset = new DicomDataset();

            // Transaction UID
            AddUidElement(dataset, DicomTag.TransactionUID, TransactionUID);

            // Referenced SOP Sequence (success instances)
            if (SuccessInstances.Count > 0)
            {
                var items = new DicomDataset[SuccessInstances.Count];
                for (int i = 0; i < SuccessInstances.Count; i++)
                {
                    var item = new DicomDataset();
                    AddUidElement(item, DicomTag.ReferencedSOPClassUID, SuccessInstances[i].SOPClassUID);
                    AddUidElement(item, DicomTag.ReferencedSOPInstanceUID, SuccessInstances[i].SOPInstanceUID);
                    items[i] = item;
                }
                dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, items));
            }

            // Failed SOP Sequence (failure instances)
            if (FailureInstances.Count > 0)
            {
                var items = new DicomDataset[FailureInstances.Count];
                for (int i = 0; i < FailureInstances.Count; i++)
                {
                    var item = new DicomDataset();
                    AddUidElement(item, DicomTag.ReferencedSOPClassUID, FailureInstances[i].Reference.SOPClassUID);
                    AddUidElement(item, DicomTag.ReferencedSOPInstanceUID, FailureInstances[i].Reference.SOPInstanceUID);

                    // Failure Reason (0008,1197) - US VR
                    var failureReasonBytes = BitConverter.GetBytes(FailureInstances[i].FailureReason);
                    item.Add(new DicomNumericElement(DicomTag.FailureReason, DicomVR.US, failureReasonBytes));
                    items[i] = item;
                }
                dataset.Add(new DicomSequence(DicomTag.FailedSOPSequence, items));
            }

            return dataset;
        }

        private static void AddUidElement(DicomDataset dataset, DicomTag tag, DicomUID uid)
        {
            var uidString = uid.ToString();
            var uidBytes = Encoding.ASCII.GetBytes(uidString);
            if (uidBytes.Length % 2 != 0)
            {
                var padded = new byte[uidBytes.Length + 1];
                Array.Copy(uidBytes, padded, uidBytes.Length);
                padded[uidBytes.Length] = 0; // UI VR padded with null
                uidBytes = padded;
            }
            dataset.Add(new DicomStringElement(tag, DicomVR.UI, uidBytes));
        }
    }
}
