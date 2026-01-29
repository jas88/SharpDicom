using System;

namespace SharpDicom.Network.Dimse
{
    /// <summary>
    /// Reports progress of data transfer operations (C-STORE).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This struct is used to report progress during C-STORE operations,
    /// providing information about bytes transferred, transfer speed, and
    /// estimated time remaining.
    /// </para>
    /// <para>
    /// Progress can be reported via <see cref="IProgress{T}"/> during SCU operations.
    /// </para>
    /// </remarks>
    /// <param name="BytesTransferred">Number of bytes transferred so far.</param>
    /// <param name="TotalBytes">Total bytes to transfer (0 if unknown).</param>
    /// <param name="BytesPerSecond">Current transfer rate in bytes per second (0 if unknown).</param>
    public readonly record struct DicomTransferProgress(
        long BytesTransferred,
        long TotalBytes,
        double BytesPerSecond)
    {
        /// <summary>
        /// Gets the percentage of completion (0-100).
        /// </summary>
        /// <remarks>
        /// Returns 0 if TotalBytes is 0 or unknown.
        /// </remarks>
        public double PercentComplete => TotalBytes > 0
            ? (double)BytesTransferred / TotalBytes * 100
            : 0;

        /// <summary>
        /// Gets the estimated time remaining to complete the transfer.
        /// </summary>
        /// <remarks>
        /// Returns null if the transfer rate is unknown (BytesPerSecond is 0 or less)
        /// or if TotalBytes is unknown.
        /// </remarks>
        public TimeSpan? EstimatedTimeRemaining
        {
            get
            {
                if (BytesPerSecond <= 0 || TotalBytes <= 0)
                    return null;

                var remainingBytes = TotalBytes - BytesTransferred;
                if (remainingBytes <= 0)
                    return TimeSpan.Zero;

                return TimeSpan.FromSeconds(remainingBytes / BytesPerSecond);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the transfer is complete.
        /// </summary>
        public bool IsComplete => TotalBytes > 0 && BytesTransferred >= TotalBytes;

        /// <summary>
        /// Gets a progress value representing an initial state (no data transferred).
        /// </summary>
        /// <param name="totalBytes">The total bytes to transfer.</param>
        /// <returns>A new progress value.</returns>
        public static DicomTransferProgress Initial(long totalBytes)
            => new(0, totalBytes, 0);

        /// <summary>
        /// Gets a progress value representing a completed transfer.
        /// </summary>
        /// <param name="totalBytes">The total bytes transferred.</param>
        /// <param name="averageBytesPerSecond">The average transfer rate.</param>
        /// <returns>A new progress value.</returns>
        public static DicomTransferProgress Completed(long totalBytes, double averageBytesPerSecond = 0)
            => new(totalBytes, totalBytes, averageBytesPerSecond);

        /// <summary>
        /// Returns a string representation of the progress.
        /// </summary>
        public override string ToString()
        {
            var percent = PercentComplete;
            var eta = EstimatedTimeRemaining;
            var etaStr = eta.HasValue ? $", ETA={eta.Value:g}" : "";
            return $"DicomTransferProgress {{ {BytesTransferred}/{TotalBytes} ({percent:F1}%), {BytesPerSecond:F0} B/s{etaStr} }}";
        }
    }
}
