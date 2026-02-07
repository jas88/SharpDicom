using System;

namespace SharpDicom.Codecs.Video
{
    /// <summary>
    /// Reports progress of a video encoding operation.
    /// </summary>
    /// <remarks>
    /// Used with <see cref="IProgress{T}"/> to report encoding progress.
    /// The <see cref="Percentage"/> field provides a normalized 0-100 value
    /// for progress bars.
    /// </remarks>
    public readonly struct VideoEncodeProgress
#if NET7_0_OR_GREATER
        : IEquatable<VideoEncodeProgress>
#endif
    {
        /// <summary>
        /// Gets the number of frames encoded so far.
        /// </summary>
        public int FramesEncoded { get; }

        /// <summary>
        /// Gets the total number of frames to encode, or 0 if unknown.
        /// </summary>
        public int TotalFrames { get; }

        /// <summary>
        /// Gets the encoding progress as a percentage (0.0 to 100.0).
        /// </summary>
        /// <remarks>
        /// Returns 0.0 if <see cref="TotalFrames"/> is 0 (unknown total).
        /// </remarks>
        public double Percentage { get; }

        /// <summary>
        /// Gets the time elapsed since encoding started.
        /// </summary>
        public TimeSpan Elapsed { get; }

        /// <summary>
        /// Gets the estimated remaining time, or null if unknown.
        /// </summary>
        /// <remarks>
        /// Estimated based on the current encoding rate. May be inaccurate
        /// early in the encoding process or for variable-complexity content.
        /// </remarks>
        public TimeSpan? EstimatedRemaining { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoEncodeProgress"/> struct.
        /// </summary>
        /// <param name="framesEncoded">Number of frames encoded so far.</param>
        /// <param name="totalFrames">Total number of frames to encode, or 0 if unknown.</param>
        /// <param name="percentage">Encoding progress as a percentage (0-100).</param>
        /// <param name="elapsed">Time elapsed since encoding started.</param>
        /// <param name="estimatedRemaining">Estimated remaining time, or null if unknown.</param>
        public VideoEncodeProgress(
            int framesEncoded,
            int totalFrames,
            double percentage,
            TimeSpan elapsed,
            TimeSpan? estimatedRemaining)
        {
            FramesEncoded = framesEncoded;
            TotalFrames = totalFrames;
            Percentage = percentage;
            Elapsed = elapsed;
            EstimatedRemaining = estimatedRemaining;
        }

        /// <summary>
        /// Creates a progress report for a known total frame count.
        /// </summary>
        /// <param name="framesEncoded">Number of frames encoded.</param>
        /// <param name="totalFrames">Total frames to encode.</param>
        /// <param name="elapsed">Elapsed time.</param>
        /// <returns>A new progress report with calculated percentage and estimated remaining time.</returns>
        public static VideoEncodeProgress Create(int framesEncoded, int totalFrames, TimeSpan elapsed)
        {
            double percentage = totalFrames > 0
                ? (double)framesEncoded / totalFrames * 100.0
                : 0.0;

            TimeSpan? remaining = null;
            if (framesEncoded > 0 && totalFrames > 0)
            {
                double rate = elapsed.TotalSeconds / framesEncoded;
                int framesLeft = totalFrames - framesEncoded;
                remaining = TimeSpan.FromSeconds(rate * framesLeft);
            }

            return new VideoEncodeProgress(framesEncoded, totalFrames, percentage, elapsed, remaining);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
            => obj is VideoEncodeProgress other && Equals(other);

        /// <summary>
        /// Determines whether two progress reports are equal.
        /// </summary>
        public bool Equals(VideoEncodeProgress other)
            => FramesEncoded == other.FramesEncoded
               && TotalFrames == other.TotalFrames
               && Percentage.Equals(other.Percentage)
               && Elapsed == other.Elapsed
               && EstimatedRemaining == other.EstimatedRemaining;

        /// <inheritdoc />
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + FramesEncoded;
                hash = hash * 31 + TotalFrames;
                hash = hash * 31 + Percentage.GetHashCode();
                hash = hash * 31 + Elapsed.GetHashCode();
                hash = hash * 31 + (EstimatedRemaining?.GetHashCode() ?? 0);
                return hash;
            }
#else
            return HashCode.Combine(FramesEncoded, TotalFrames, Percentage, Elapsed, EstimatedRemaining);
#endif
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(VideoEncodeProgress left, VideoEncodeProgress right)
            => left.Equals(right);

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(VideoEncodeProgress left, VideoEncodeProgress right)
            => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
            => TotalFrames > 0
                ? $"{FramesEncoded}/{TotalFrames} ({Percentage:F1}%)"
                : $"{FramesEncoded} frames ({Elapsed.TotalSeconds:F1}s)";
    }
}
