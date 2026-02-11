using System;

namespace SharpDicom.Codecs.Jpeg2000.Tier2
{
    /// <summary>
    /// Tag tree implementation for JPEG 2000 packet header coding (ITU-T T.800 B.10.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tag tree is a hierarchical structure used to efficiently code a 2D array of
    /// non-negative integer values. Each leaf represents one code-block. Internal nodes
    /// hold the minimum value of their children. The tree is traversed from root to leaf,
    /// coding at each level whether the value exceeds a threshold.
    /// </para>
    /// <para>
    /// Used for two purposes in packet header coding:
    /// <list type="bullet">
    ///   <item>Inclusion tag tree: value = first layer in which code-block is included</item>
    ///   <item>Zero bitplane tag tree: value = number of zero (missing MSB) bitplanes</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class TagTree
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int[] _values;
        private readonly int[] _states;
        private readonly int _numLevels;
        private readonly int[] _levelOffsets;
        private readonly int[] _levelWidths;
        private readonly int[] _levelHeights;

        /// <summary>
        /// Creates a new tag tree for a 2D grid of the specified dimensions.
        /// </summary>
        /// <param name="width">Number of columns (code-blocks wide).</param>
        /// <param name="height">Number of rows (code-blocks high).</param>
        public TagTree(int width, int height)
        {
            _width = width;
            _height = height;

            // Calculate number of levels
            int maxDim = Math.Max(width, height);
            _numLevels = 1;
            while ((1 << (_numLevels - 1)) < maxDim)
            {
                _numLevels++;
            }

            // Calculate level dimensions and offsets
            _levelWidths = new int[_numLevels];
            _levelHeights = new int[_numLevels];
            _levelOffsets = new int[_numLevels];

            int totalNodes = 0;
            int w = width;
            int h = height;
            for (int level = 0; level < _numLevels; level++)
            {
                _levelWidths[level] = w;
                _levelHeights[level] = h;
                _levelOffsets[level] = totalNodes;
                totalNodes += w * h;
                w = (w + 1) >> 1;
                h = (h + 1) >> 1;
            }

            _values = new int[totalNodes];
            _states = new int[totalNodes];

            // Initialize values to MaxValue (unknown/undetermined per ITU-T T.800 B.10.2).
            // This matches OpenJPEG's approach (initializes to 999).
            // States start at 0 (threshold not yet coded).
            for (int i = 0; i < _values.Length; i++)
                _values[i] = int.MaxValue;
        }

        /// <summary>
        /// Resets all node values and states.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _values.Length; i++)
                _values[i] = int.MaxValue;
            Array.Clear(_states, 0, _states.Length);
        }

        /// <summary>
        /// Sets the value of a leaf node (code-block).
        /// </summary>
        /// <param name="x">Column index.</param>
        /// <param name="y">Row index.</param>
        /// <param name="value">The value to set.</param>
        public void SetValue(int x, int y, int value)
        {
            int idx = _levelOffsets[0] + y * _levelWidths[0] + x;
            _values[idx] = value;

            // Propagate minimum up the tree
            int px = x;
            int py = y;
            for (int level = 1; level < _numLevels; level++)
            {
                px >>= 1;
                py >>= 1;
                int parentIdx = _levelOffsets[level] + py * _levelWidths[level] + px;
                int minVal = int.MaxValue;

                // Find min among children
                int cx = px << 1;
                int cy = py << 1;
                int childW = _levelWidths[level - 1];
                int childH = _levelHeights[level - 1];

                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int ccx = cx + dx;
                        int ccy = cy + dy;
                        if (ccx < childW && ccy < childH)
                        {
                            int childIdx = _levelOffsets[level - 1] + ccy * childW + ccx;
                            minVal = Math.Min(minVal, _values[childIdx]);
                        }
                    }
                }

                if (minVal == int.MaxValue)
                {
                    minVal = 0;
                }

                _values[parentIdx] = minVal;
            }
        }

        /// <summary>
        /// Decodes a tag tree value for the specified leaf from a bit reader.
        /// </summary>
        /// <param name="x">Leaf column index.</param>
        /// <param name="y">Leaf row index.</param>
        /// <param name="threshold">The threshold to decode up to.</param>
        /// <param name="readBit">Function to read a single bit from the stream.</param>
        /// <returns>The decoded value, or a value >= threshold if not yet reached.</returns>
        public int Decode(int x, int y, int threshold, Func<int> readBit)
        {
            // Build path from root to leaf
            Span<int> pathX = stackalloc int[_numLevels];
            Span<int> pathY = stackalloc int[_numLevels];
            int px = x;
            int py = y;
            for (int level = 0; level < _numLevels; level++)
            {
                pathX[level] = px;
                pathY[level] = py;
                px >>= 1;
                py >>= 1;
            }

            // Traverse from root to leaf
            int minValue = 0;
            for (int level = _numLevels - 1; level >= 0; level--)
            {
                int nodeIdx = _levelOffsets[level] + pathY[level] * _levelWidths[level] + pathX[level];

                // Start from the current state (previously coded threshold)
                int state = _states[nodeIdx];
                if (state < minValue)
                {
                    state = minValue;
                }

                while (state < threshold)
                {
                    int bit = readBit();
                    if (bit == 1)
                    {
                        // Value equals current state (ITU-T T.800 B.10.2: 1 = value matches)
                        _values[nodeIdx] = state;
                        _states[nodeIdx] = state;
                        // For parent levels, the value at this node IS the threshold
                        // so we know this node's value. Pass it down.
                        minValue = state;
                        goto nextLevel;
                    }
                    else
                    {
                        // Value is greater than current state (ITU-T T.800 B.10.2: 0 = exceeds)
                        state++;
                    }
                }

                // state >= threshold: value not yet determined, just update state for future calls
                _states[nodeIdx] = state;
                minValue = state;

                nextLevel:;
            }

            // Return the leaf value.
            // If determined by a 0-bit, _values[leafIdx] holds the exact value.
            // If not yet determined, _values[leafIdx] is still int.MaxValue (> any threshold),
            // which correctly signals "not included at this layer" to the caller.
            int leafIdx = _levelOffsets[0] + y * _levelWidths[0] + x;
            return _values[leafIdx];
        }

        /// <summary>
        /// Encodes a tag tree value for the specified leaf to a bit writer.
        /// </summary>
        /// <param name="x">Leaf column index.</param>
        /// <param name="y">Leaf row index.</param>
        /// <param name="threshold">The threshold to encode up to.</param>
        /// <param name="writeBit">Action to write a single bit to the stream.</param>
        public void Encode(int x, int y, int threshold, Action<int> writeBit)
        {
            // Build path from root to leaf
            Span<int> pathX = stackalloc int[_numLevels];
            Span<int> pathY = stackalloc int[_numLevels];
            int px = x;
            int py = y;
            for (int level = 0; level < _numLevels; level++)
            {
                pathX[level] = px;
                pathY[level] = py;
                px >>= 1;
                py >>= 1;
            }

            // Traverse from root to leaf
            int minValue = 0;
            for (int level = _numLevels - 1; level >= 0; level--)
            {
                int nodeIdx = _levelOffsets[level] + pathY[level] * _levelWidths[level] + pathX[level];
                int value = _values[nodeIdx];

                int state = _states[nodeIdx];
                if (state < minValue)
                {
                    state = minValue;
                }

                while (state < threshold)
                {
                    if (value > state)
                    {
                        writeBit(0); // ITU-T T.800 B.10.2: 0 = value exceeds current threshold
                        state++;
                    }
                    else
                    {
                        writeBit(1); // ITU-T T.800 B.10.2: 1 = value matches current threshold
                        _states[nodeIdx] = state;
                        minValue = state;
                        goto nextLevel;
                    }
                }

                _states[nodeIdx] = state;
                minValue = state;

                nextLevel:;
            }
        }
    }
}
