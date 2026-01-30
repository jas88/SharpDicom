namespace SharpDicom.Network.Dimse.Services
{
    /// <summary>
    /// Progress update from a C-MOVE operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-MOVE operations report progress via pending responses containing cumulative
    /// sub-operation counts. Unlike C-GET, the actual data transfer happens on a
    /// separate association to the move destination AE.
    /// </para>
    /// <para>
    /// Sub-operation counts are cumulative totals as per PS3.7 Section 9.1.4:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Remaining: sub-operations not yet started</description></item>
    ///   <item><description>Completed: sub-operations completed successfully</description></item>
    ///   <item><description>Failed: sub-operations that failed</description></item>
    ///   <item><description>Warning: sub-operations completed with warnings</description></item>
    /// </list>
    /// </remarks>
    public sealed class CMoveProgress
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CMoveProgress"/> class.
        /// </summary>
        /// <param name="subOperations">The sub-operation progress counts.</param>
        /// <param name="status">The DIMSE status from this response.</param>
        public CMoveProgress(SubOperationProgress subOperations, DicomStatus status)
        {
            SubOperations = subOperations;
            Status = status;
        }

        /// <summary>
        /// Gets the sub-operation progress counts.
        /// </summary>
        /// <remarks>
        /// Per PS3.7 Section 9.1.4, sub-operation counts are cumulative totals reported
        /// in C-MOVE-RSP messages, not incremental values per sub-operation.
        /// </remarks>
        public SubOperationProgress SubOperations { get; }

        /// <summary>
        /// Gets the DIMSE status from this response.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Common status values for C-MOVE-RSP:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>0xFF00 - Pending: sub-operations in progress</description></item>
        ///   <item><description>0x0000 - Success: all sub-operations completed successfully</description></item>
        ///   <item><description>0xB000 - Warning: sub-operations completed with warnings</description></item>
        ///   <item><description>0xA801 - Move destination unknown</description></item>
        ///   <item><description>0xA900 - Unable to calculate number of matches</description></item>
        ///   <item><description>0xC000+ - Failure codes</description></item>
        /// </list>
        /// </remarks>
        public DicomStatus Status { get; }

        /// <summary>
        /// Gets a value indicating whether this is the final C-MOVE response.
        /// </summary>
        /// <remarks>
        /// True only when the DIMSE status is not Pending, indicating the final
        /// C-MOVE-RSP has been received.
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
        /// Gets a value indicating whether the operation completed with partial success.
        /// </summary>
        /// <remarks>
        /// True when the operation is final and some sub-operations failed or had
        /// warnings, but at least one completed successfully.
        /// </remarks>
        public bool IsPartialSuccess => IsFinal &&
            (SubOperations.HasErrors || SubOperations.HasWarnings) &&
            SubOperations.Completed > 0;
    }
}
