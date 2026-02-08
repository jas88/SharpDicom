using System;

namespace SharpDicom.Codecs.Jpeg2000.Subband
{
    /// <summary>
    /// Identifies the type of a DWT subband.
    /// </summary>
    /// <remarks>
    /// Values match the convention used by EBCOT context tables (ITU-T T.800 Table D.1):
    /// LL/LH share context behavior, HL has its own, and HH has its own.
    /// The numeric values match the existing <c>subbandType</c> parameter used
    /// throughout <see cref="Tier1.EbcotEncoder"/> and <see cref="Wavelet.DwtTransform"/>.
    /// </remarks>
    public enum SubbandType
    {
        /// <summary>Low-Low (approximation) subband. Only present at the lowest resolution level.</summary>
        LL = 0,

        /// <summary>High-Low (vertical detail) subband: high-pass horizontal, low-pass vertical.</summary>
        HL = 1,

        /// <summary>Low-High (horizontal detail) subband: low-pass horizontal, high-pass vertical.</summary>
        LH = 2,

        /// <summary>High-High (diagonal detail) subband: high-pass both directions.</summary>
        HH = 3
    }

    /// <summary>
    /// Describes a single subband within a DWT decomposition, including its dimensions,
    /// position in the coefficient array, and code-block grid size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After DWT decomposition of an image, the coefficient array is partitioned into subbands.
    /// At each decomposition level, one LL subband is recursively split into LL, HL, LH, HH.
    /// The final structure has one LL at the deepest level plus three detail subbands per level.
    /// </para>
    /// <para>
    /// The layout in the coefficient array (using stride = original image width) for a
    /// single decomposition level of a WxH region is:
    /// <code>
    /// +--------+--------+
    /// |   LL   |   HL   |
    /// | ceil/2 | floor/2|
    /// +--------+--------+
    /// |   LH   |   HH   |
    /// | ceil/2 | floor/2|
    /// +--------+--------+
    /// </code>
    /// Width: LL and LH get ceil(W/2), HL and HH get floor(W/2).
    /// Height: LL and HL get ceil(H/2), LH and HH get floor(H/2).
    /// </para>
    /// </remarks>
    public readonly struct SubbandDescriptor : IEquatable<SubbandDescriptor>
    {
        /// <summary>Gets the subband type (LL, HL, LH, or HH).</summary>
        public SubbandType Type { get; }

        /// <summary>
        /// Gets the resolution level this subband belongs to.
        /// Level 0 is the deepest (lowest resolution); the LL subband is at level 0.
        /// Detail subbands at level N correspond to decomposition from level N-1 to N.
        /// </summary>
        public int ResolutionLevel { get; }

        /// <summary>Gets the width of this subband in coefficients.</summary>
        public int Width { get; }

        /// <summary>Gets the height of this subband in coefficients.</summary>
        public int Height { get; }

        /// <summary>
        /// Gets the horizontal origin (column offset) of this subband within the coefficient array
        /// at its parent resolution level.
        /// </summary>
        public int OriginX { get; }

        /// <summary>
        /// Gets the vertical origin (row offset) of this subband within the coefficient array
        /// at its parent resolution level.
        /// </summary>
        public int OriginY { get; }

        /// <summary>Gets the number of code-blocks horizontally in this subband.</summary>
        public int CodeBlockGridWidth { get; }

        /// <summary>Gets the number of code-blocks vertically in this subband.</summary>
        public int CodeBlockGridHeight { get; }

        /// <summary>
        /// Initializes a new <see cref="SubbandDescriptor"/>.
        /// </summary>
        /// <param name="type">Subband type.</param>
        /// <param name="resolutionLevel">Resolution level (0 = deepest).</param>
        /// <param name="width">Subband width in coefficients.</param>
        /// <param name="height">Subband height in coefficients.</param>
        /// <param name="originX">Column offset in the coefficient array at this level.</param>
        /// <param name="originY">Row offset in the coefficient array at this level.</param>
        /// <param name="codeBlockWidth">Nominal code-block width (for grid calculation).</param>
        /// <param name="codeBlockHeight">Nominal code-block height (for grid calculation).</param>
        public SubbandDescriptor(
            SubbandType type,
            int resolutionLevel,
            int width,
            int height,
            int originX,
            int originY,
            int codeBlockWidth,
            int codeBlockHeight)
        {
            Type = type;
            ResolutionLevel = resolutionLevel;
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;

            // Code-block grid: ceil(subbandDim / cbDim), but 0 if subband dimension is 0
            CodeBlockGridWidth = width > 0 && codeBlockWidth > 0
                ? (width + codeBlockWidth - 1) / codeBlockWidth
                : 0;
            CodeBlockGridHeight = height > 0 && codeBlockHeight > 0
                ? (height + codeBlockHeight - 1) / codeBlockHeight
                : 0;
        }

        /// <summary>
        /// Gets the total number of code-blocks in this subband.
        /// </summary>
        public int TotalCodeBlocks => CodeBlockGridWidth * CodeBlockGridHeight;

        /// <inheritdoc />
        public bool Equals(SubbandDescriptor other) =>
            Type == other.Type &&
            ResolutionLevel == other.ResolutionLevel &&
            Width == other.Width &&
            Height == other.Height &&
            OriginX == other.OriginX &&
            OriginY == other.OriginY &&
            CodeBlockGridWidth == other.CodeBlockGridWidth &&
            CodeBlockGridHeight == other.CodeBlockGridHeight;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SubbandDescriptor other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
#if NETSTANDARD2_0
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)Type;
                hash = hash * 31 + ResolutionLevel;
                hash = hash * 31 + Width;
                hash = hash * 31 + Height;
                hash = hash * 31 + OriginX;
                hash = hash * 31 + OriginY;
                return hash;
            }
#else
            return HashCode.Combine(Type, ResolutionLevel, Width, Height, OriginX, OriginY);
#endif
        }

        /// <summary>Equality operator.</summary>
        public static bool operator ==(SubbandDescriptor left, SubbandDescriptor right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(SubbandDescriptor left, SubbandDescriptor right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() =>
            $"{Type} r{ResolutionLevel} {Width}x{Height} @({OriginX},{OriginY}) cb={CodeBlockGridWidth}x{CodeBlockGridHeight}";
    }
}
