using System;

namespace SharpDicom.Codecs.Rle
{
    /// <summary>
    /// Options for RLE codec encoding operations.
    /// </summary>
    public sealed class RleCodecOptions
    {
        /// <summary>
        /// Gets or sets whether to generate the Basic Offset Table.
        /// </summary>
        /// <remarks>
        /// The Basic Offset Table provides 32-bit offsets to each frame for random access.
        /// Default is <c>true</c>.
        /// </remarks>
        public bool GenerateBasicOffsetTable { get; init; } = true;

        /// <summary>
        /// Gets or sets whether to generate the Extended Offset Table.
        /// </summary>
        /// <remarks>
        /// The Extended Offset Table (7FE0,0001) provides 64-bit offsets for large multi-frame images.
        /// Default is <c>false</c>.
        /// </remarks>
        public bool GenerateExtendedOffsetTable { get; init; } = false;

        /// <summary>
        /// Gets or sets the maximum degree of parallelism for frame encoding.
        /// </summary>
        /// <remarks>
        /// Each frame can be encoded independently. Set to 1 for sequential encoding.
        /// Default is <see cref="Environment.ProcessorCount"/>.
        /// </remarks>
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

        /// <summary>
        /// Default options for RLE encoding.
        /// </summary>
        public static RleCodecOptions Default { get; } = new();
    }
}
