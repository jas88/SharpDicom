using System;

namespace SharpDicom.Codecs.Jpeg2000.Subband
{
    /// <summary>
    /// Computes subband descriptors for a DWT decomposition and maps code-block
    /// positions to their owning subbands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Given an image (or tile) of size W x H with N decomposition levels, the DWT produces:
    /// <list type="bullet">
    ///   <item>One LL subband at the deepest level (resolution level 0).</item>
    ///   <item>Three detail subbands (HL, LH, HH) at each decomposition level 1..N.</item>
    /// </list>
    /// Total subbands: 1 + 3*N.
    /// </para>
    /// <para>
    /// Subband dimensions follow ITU-T T.800 Section B.5. For a region of size (w, h),
    /// one level of decomposition produces:
    /// <list type="bullet">
    ///   <item>LL: ceil(w/2) x ceil(h/2)</item>
    ///   <item>HL: floor(w/2) x ceil(h/2)</item>
    ///   <item>LH: ceil(w/2) x floor(h/2)</item>
    ///   <item>HH: floor(w/2) x floor(h/2)</item>
    /// </list>
    /// The LL subband is then recursively decomposed for subsequent levels.
    /// </para>
    /// </remarks>
    public static class SubbandPartitioner
    {
        /// <summary>
        /// Computes all subband descriptors for the given image dimensions and decomposition parameters.
        /// </summary>
        /// <param name="imageWidth">Image (or tile) width in pixels.</param>
        /// <param name="imageHeight">Image (or tile) height in pixels.</param>
        /// <param name="levels">Number of DWT decomposition levels (must be >= 0).</param>
        /// <param name="codeBlockWidth">Nominal code-block width (must be > 0).</param>
        /// <param name="codeBlockHeight">Nominal code-block height (must be > 0).</param>
        /// <returns>
        /// Array of <see cref="SubbandDescriptor"/> ordered as:
        /// [0] = LL at level 0, then [1..3] = HL/LH/HH at level 1, [4..6] = HL/LH/HH at level 2, etc.
        /// For 0 levels: a single LL descriptor covering the entire image.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">If any dimension or level count is invalid.</exception>
        public static SubbandDescriptor[] GetSubbands(
            int imageWidth, int imageHeight, int levels, int codeBlockWidth, int codeBlockHeight)
        {
            if (imageWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageWidth), "Must be positive.");
            }

            if (imageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageHeight), "Must be positive.");
            }

            if (levels < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levels), "Must be non-negative.");
            }

            if (codeBlockWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockWidth), "Must be positive.");
            }

            if (codeBlockHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockHeight), "Must be positive.");
            }

            // Special case: 0 decomposition levels means the entire image is a single LL band
            if (levels == 0)
            {
                return new[]
                {
                    new SubbandDescriptor(
                        SubbandType.LL, 0,
                        imageWidth, imageHeight,
                        0, 0,
                        codeBlockWidth, codeBlockHeight)
                };
            }

            // Total subbands: 1 LL + 3 detail per level
            int totalSubbands = 1 + 3 * levels;
            var result = new SubbandDescriptor[totalSubbands];

            // Track the dimensions of the LL region being decomposed at each level.
            // We decompose from the outermost (full image) level inward.
            // Store the LL dimensions at each level for later use.
            // levelWidths[i] and levelHeights[i] = LL dimensions BEFORE decomposing at level i.
            // Level numbering: level 1 decomposes the full image, level N is the deepest.
            var levelWidths = new int[levels + 1];
            var levelHeights = new int[levels + 1];
            levelWidths[0] = imageWidth;
            levelHeights[0] = imageHeight;

            for (int i = 1; i <= levels; i++)
            {
                levelWidths[i] = (levelWidths[i - 1] + 1) / 2;
                levelHeights[i] = (levelHeights[i - 1] + 1) / 2;
            }

            // The LL subband (resolution level 0) is the deepest LL.
            // Its dimensions are levelWidths[levels] x levelHeights[levels].
            result[0] = new SubbandDescriptor(
                SubbandType.LL, 0,
                levelWidths[levels], levelHeights[levels],
                0, 0,
                codeBlockWidth, codeBlockHeight);

            // Detail subbands: for decomposition level d (1-based), the LL region being decomposed
            // has dimensions levelWidths[d-1] x levelHeights[d-1].
            // The detail subbands of that decomposition are:
            //   HL: width = floor(w/2), height = ceil(h/2), origin = (ceil(w/2), 0)
            //   LH: width = ceil(w/2), height = floor(h/2), origin = (0, ceil(h/2))
            //   HH: width = floor(w/2), height = floor(h/2), origin = (ceil(w/2), ceil(h/2))
            //
            // We store them as resolution level = levels - d + 1 (so level 1 = shallowest detail).
            // Actually, let's use the simpler convention: detail subbands from decomposition d
            // are at resolution level d (matching GetSubbands array layout expectations).
            //
            // Index layout: [0]=LL, [1..3]=level 1 detail, [4..6]=level 2 detail, etc.
            // where level 1 is the deepest decomposition (smallest subbands) and
            // level N is the shallowest (largest subbands).
            //
            // Decomposition proceeds: full image -> level N subbands, then LL of that -> level N-1, etc.
            // So decomposition step "d" (from 1 to N) takes the LL at step d-1 dimensions.
            // The "level" label in our output: the deepest decomposition is level 1.

            for (int d = 1; d <= levels; d++)
            {
                // The LL region being decomposed at step d has dimensions:
                // After d-1 recursive halvings from the full image.
                // That's levelWidths[d-1] x levelHeights[d-1] for the outermost decomposition,
                // but we need to map correctly.
                //
                // decomposition step 1 = operates on full image (levelWidths[0] x levelHeights[0])
                //   produces detail subbands at resolution level = levels (shallowest)
                // decomposition step levels = operates on levelWidths[levels-1] x levelHeights[levels-1]
                //   produces detail subbands at resolution level = 1 (deepest detail)
                //
                // So decomposition step d operates on levelWidths[d-1] x levelHeights[d-1]
                // and produces subbands at resolution level = levels - d + 1.

                int w = levelWidths[d - 1];
                int h = levelHeights[d - 1];
                int resLevel = levels - d + 1;

                int llW = (w + 1) / 2;  // ceil(w/2)
                int llH = (h + 1) / 2;  // ceil(h/2)
                int hlW = w / 2;         // floor(w/2)
                int lhH = h / 2;         // floor(h/2)

                int idx = 1 + 3 * (resLevel - 1);

                // HL: top-right quadrant
                result[idx] = new SubbandDescriptor(
                    SubbandType.HL, resLevel,
                    hlW, llH,
                    llW, 0,
                    codeBlockWidth, codeBlockHeight);

                // LH: bottom-left quadrant
                result[idx + 1] = new SubbandDescriptor(
                    SubbandType.LH, resLevel,
                    llW, lhH,
                    0, llH,
                    codeBlockWidth, codeBlockHeight);

                // HH: bottom-right quadrant
                result[idx + 2] = new SubbandDescriptor(
                    SubbandType.HH, resLevel,
                    hlW, lhH,
                    llW, llH,
                    codeBlockWidth, codeBlockHeight);
            }

            return result;
        }

        /// <summary>
        /// Gets the <see cref="SubbandType"/> for a code-block at a given position within a specific subband.
        /// </summary>
        /// <param name="subbands">Subband descriptors as returned by <see cref="GetSubbands"/>.</param>
        /// <param name="codeBlockX">Horizontal code-block index within the subband grid.</param>
        /// <param name="codeBlockY">Vertical code-block index within the subband grid.</param>
        /// <param name="subbandIndex">Index into the <paramref name="subbands"/> array.</param>
        /// <returns>The <see cref="SubbandType"/> for the specified code-block.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If the subband index or code-block position is out of range.</exception>
        public static SubbandType GetSubbandForCodeBlock(
            SubbandDescriptor[] subbands, int codeBlockX, int codeBlockY, int subbandIndex)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(subbands);
#else
            if (subbands is null)
            {
                throw new ArgumentNullException(nameof(subbands));
            }
#endif

            if (subbandIndex < 0 || subbandIndex >= subbands.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(subbandIndex),
                    $"Must be between 0 and {subbands.Length - 1}.");
            }

            var sb = subbands[subbandIndex];

            if (codeBlockX < 0 || codeBlockX >= sb.CodeBlockGridWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockX),
                    $"Must be between 0 and {sb.CodeBlockGridWidth - 1}.");
            }

            if (codeBlockY < 0 || codeBlockY >= sb.CodeBlockGridHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(codeBlockY),
                    $"Must be between 0 and {sb.CodeBlockGridHeight - 1}.");
            }

            return sb.Type;
        }

        /// <summary>
        /// Finds the subband descriptor that contains the given coefficient position at a specified
        /// decomposition level.
        /// </summary>
        /// <param name="subbands">Subband descriptors as returned by <see cref="GetSubbands"/>.</param>
        /// <param name="coeffX">Horizontal coefficient position within the level's coordinate space.</param>
        /// <param name="coeffY">Vertical coefficient position within the level's coordinate space.</param>
        /// <param name="resolutionLevel">Resolution level to search (0 = LL, higher = coarser detail).</param>
        /// <returns>The matching <see cref="SubbandDescriptor"/>, or <c>null</c> if no match found.</returns>
        public static SubbandDescriptor? FindSubbandAt(
            SubbandDescriptor[] subbands, int coeffX, int coeffY, int resolutionLevel)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(subbands);
#else
            if (subbands is null)
            {
                throw new ArgumentNullException(nameof(subbands));
            }
#endif

            for (int i = 0; i < subbands.Length; i++)
            {
                var sb = subbands[i];
                if (sb.ResolutionLevel != resolutionLevel)
                {
                    continue;
                }

                if (coeffX >= sb.OriginX && coeffX < sb.OriginX + sb.Width &&
                    coeffY >= sb.OriginY && coeffY < sb.OriginY + sb.Height)
                {
                    return sb;
                }
            }

            return null;
        }
    }
}
