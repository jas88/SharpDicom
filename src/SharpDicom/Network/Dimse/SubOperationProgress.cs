namespace SharpDicom.Network.Dimse
{
    /// <summary>
    /// Tracks progress of sub-operations in C-MOVE and C-GET operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sub-operation counts are reported in Pending responses during C-MOVE and C-GET
    /// operations. These counts are cumulative, not incremental, per PS3.7 Section 9.1.4.
    /// </para>
    /// <para>
    /// The counts represent the total number of sub-operations in each state at the
    /// time the response was generated:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Remaining: sub-operations not yet started</description></item>
    /// <item><description>Completed: sub-operations completed successfully</description></item>
    /// <item><description>Failed: sub-operations that failed completely</description></item>
    /// <item><description>Warning: sub-operations completed with warnings</description></item>
    /// </list>
    /// </remarks>
    /// <param name="Remaining">Number of sub-operations remaining (not yet started).</param>
    /// <param name="Completed">Number of sub-operations completed successfully.</param>
    /// <param name="Failed">Number of sub-operations that failed.</param>
    /// <param name="Warning">Number of sub-operations completed with warnings.</param>
    public readonly record struct SubOperationProgress(
        ushort Remaining,
        ushort Completed,
        ushort Failed,
        ushort Warning)
    {
        /// <summary>
        /// Gets the total number of sub-operations (sum of all states).
        /// </summary>
        public ushort Total => (ushort)(Remaining + Completed + Failed + Warning);

        /// <summary>
        /// Gets a value indicating whether all sub-operations have been processed.
        /// </summary>
        /// <remarks>
        /// When true, this is the final progress report and no more Pending responses
        /// will be received.
        /// </remarks>
        public bool IsFinal => Remaining == 0;

        /// <summary>
        /// Gets a value indicating whether any sub-operations failed.
        /// </summary>
        public bool HasErrors => Failed > 0;

        /// <summary>
        /// Gets a value indicating whether any sub-operations completed with warnings.
        /// </summary>
        public bool HasWarnings => Warning > 0;

        /// <summary>
        /// Gets a progress value with all zeros (initial state).
        /// </summary>
        public static SubOperationProgress Empty => default;

        /// <summary>
        /// Creates a progress value for a completed operation with no errors.
        /// </summary>
        /// <param name="completedCount">The number of successfully completed sub-operations.</param>
        /// <returns>A new progress value.</returns>
        public static SubOperationProgress Successful(ushort completedCount)
            => new(0, completedCount, 0, 0);

        /// <summary>
        /// Returns a string representation of the progress.
        /// </summary>
        public override string ToString()
            => $"SubOperationProgress {{ Remaining={Remaining}, Completed={Completed}, Failed={Failed}, Warning={Warning}, Total={Total} }}";
    }
}
