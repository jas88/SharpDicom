using System;
using System.Collections.Generic;
using System.Text;
using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services.StorageCommitment
{
    /// <summary>
    /// Represents a Storage Commitment Push Model N-ACTION request per DICOM PS3.4 Annex J.
    /// </summary>
    /// <remarks>
    /// Contains the Transaction UID and a list of SOP Instance references for which
    /// storage commitment is being requested. The request is serialized as the
    /// action information dataset for an N-ACTION with Action Type ID = 1.
    /// </remarks>
    public sealed class StorageCommitmentRequest
    {
        /// <summary>
        /// Gets the Transaction UID that uniquely identifies this commitment request.
        /// </summary>
        public DicomUID TransactionUID { get; }

        /// <summary>
        /// Gets the list of SOP Instance references for which commitment is requested.
        /// </summary>
        public IReadOnlyList<SopInstanceReference> ReferencedInstances { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageCommitmentRequest"/> class.
        /// </summary>
        /// <param name="transactionUid">The Transaction UID for this request.</param>
        /// <param name="instances">The SOP Instance references to request commitment for.</param>
        /// <exception cref="ArgumentNullException">Thrown when instances is null.</exception>
        public StorageCommitmentRequest(DicomUID transactionUid, IReadOnlyList<SopInstanceReference> instances)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(instances);
#else
            if (instances == null)
                throw new ArgumentNullException(nameof(instances));
#endif
            TransactionUID = transactionUid;
            ReferencedInstances = instances;
        }

        /// <summary>
        /// Parses a <see cref="StorageCommitmentRequest"/> from an N-ACTION action information dataset.
        /// </summary>
        /// <param name="dataset">The action information dataset.</param>
        /// <returns>The parsed request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when dataset is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when required fields are missing.</exception>
        public static StorageCommitmentRequest FromDataset(DicomDataset dataset)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(dataset);
#else
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));
#endif

            var transactionUid = dataset.GetUID(DicomTag.TransactionUID);
            if (transactionUid == null || transactionUid.Value.IsEmpty)
            {
                throw new InvalidOperationException("TransactionUID (0008,1195) is required.");
            }

            var instances = new List<SopInstanceReference>();
            var sequence = dataset.GetSequence(DicomTag.ReferencedSOPSequence);
            if (sequence != null)
            {
                foreach (var item in sequence.Items)
                {
                    var sopClassUid = item.GetUID(DicomTag.ReferencedSOPClassUID);
                    var sopInstanceUid = item.GetUID(DicomTag.ReferencedSOPInstanceUID);

                    if (sopClassUid != null && sopInstanceUid != null)
                    {
                        instances.Add(new SopInstanceReference(sopClassUid.Value, sopInstanceUid.Value));
                    }
                }
            }

            return new StorageCommitmentRequest(transactionUid.Value, instances);
        }

        /// <summary>
        /// Builds the N-ACTION action information dataset from this request.
        /// </summary>
        /// <returns>The action information dataset.</returns>
        public DicomDataset ToDataset()
        {
            var dataset = new DicomDataset();

            // Transaction UID (0008,1195) - UI VR
            var transactionUidString = TransactionUID.ToString();
            var transactionUidBytes = Encoding.ASCII.GetBytes(transactionUidString);
            if (transactionUidBytes.Length % 2 != 0)
            {
                var padded = new byte[transactionUidBytes.Length + 1];
                Array.Copy(transactionUidBytes, padded, transactionUidBytes.Length);
                padded[transactionUidBytes.Length] = 0; // UI VR padded with null
                transactionUidBytes = padded;
            }
            dataset.Add(new DicomStringElement(DicomTag.TransactionUID, DicomVR.UI, transactionUidBytes));

            // Referenced SOP Sequence (0008,1199)
            var items = new DicomDataset[ReferencedInstances.Count];
            for (int i = 0; i < ReferencedInstances.Count; i++)
            {
                var item = new DicomDataset();
                AddUidElement(item, DicomTag.ReferencedSOPClassUID, ReferencedInstances[i].SOPClassUID);
                AddUidElement(item, DicomTag.ReferencedSOPInstanceUID, ReferencedInstances[i].SOPInstanceUID);
                items[i] = item;
            }
            dataset.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, items));

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
