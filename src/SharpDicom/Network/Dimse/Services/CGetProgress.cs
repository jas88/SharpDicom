using SharpDicom.Data;

namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Progress update from a C-GET operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CGetProgress provides two types of progress updates:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       When a C-STORE sub-operation completes: Contains the received dataset
    ///       with placeholder sub-operation counts (actual counts come in C-GET-RSP).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       When a C-GET-RSP is received: Contains cumulative sub-operation counts
    ///       but no dataset.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public sealed class CGetProgress
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CGetProgress"/> class.
        /// </summary>
        /// <param name="subOperations">The sub-operation progress counts.</param>
        /// <param name="status">The DIMSE status from this response.</param>
        /// <param name="receivedDataset">Optional dataset from C-STORE sub-operation.</param>
        public CGetProgress(SubOperationProgress subOperations, DicomStatus status, DicomDataset? receivedDataset = null)
        {
            SubOperations = subOperations;
            Status = status;
            ReceivedDataset = receivedDataset;
        }

        /// <summary>
        /// Gets the sub-operation progress counts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per PS3.7 Section 9.1.3, sub-operation counts are cumulative totals reported
        /// in C-GET-RSP messages, not incremental values per sub-operation.
        /// </para>
        /// <para>
        /// When this progress update is for a C-STORE sub-operation completion (HasReceivedDataset is true),
        /// the sub-operation counts are placeholders (all zeros) until the next C-GET-RSP arrives.
        /// </para>
        /// </remarks>
        public SubOperationProgress SubOperations { get; }

        /// <summary>
        /// Gets the DIMSE status from this response.
        /// </summary>
        /// <remarks>
        /// For C-STORE sub-operation completions, this is DicomStatus.Pending.
        /// For C-GET-RSP messages, this reflects the actual operation status.
        /// </remarks>
        public DicomStatus Status { get; }

        /// <summary>
        /// Gets the dataset received in the most recent C-STORE sub-operation, if any.
        /// </summary>
        /// <remarks>
        /// Only populated when yielding after a C-STORE sub-operation completes.
        /// Null when yielding C-GET-RSP progress without associated dataset.
        /// </remarks>
        public DicomDataset? ReceivedDataset { get; }

        /// <summary>
        /// Gets a value indicating whether this is the final C-GET response.
        /// </summary>
        /// <remarks>
        /// True only when the DIMSE status is not Pending, indicating the final
        /// C-GET-RSP has been received. Note that progress updates for C-STORE
        /// sub-operations use placeholder sub-operation counts and should not
        /// be considered final.
        /// </remarks>
        public bool IsFinal => !Status.IsPending;

        /// <summary>
        /// Gets a value indicating whether the overall operation completed successfully.
        /// </summary>
        /// <remarks>
        /// True only when:
        /// <list type="bullet">
        ///   <item><description>This is the final response (IsFinal = true)</description></item>
        ///   <item><description>The status indicates success</description></item>
        ///   <item><description>No sub-operations failed (Failed = 0)</description></item>
        /// </list>
        /// </remarks>
        public bool IsSuccess => IsFinal && Status.IsSuccess && !SubOperations.HasErrors;

        /// <summary>
        /// Gets a value indicating whether a new dataset was received with this progress update.
        /// </summary>
        public bool HasReceivedDataset => ReceivedDataset != null;
    }
}
