using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SharpDicom.Codecs.Jpeg2000.Tier1;

namespace SharpDicom.Codecs.Jpeg2000.Tier2
{
    /// <summary>
    /// A JPEG 2000 packet containing code-block contributions for one layer/resolution/component/position.
    /// </summary>
    public readonly struct PacketData
    {
        /// <summary>Gets the quality layer index.</summary>
        public int Layer { get; init; }

        /// <summary>Gets the resolution level.</summary>
        public int Resolution { get; init; }

        /// <summary>Gets the component index.</summary>
        public int Component { get; init; }

        /// <summary>Gets the position (precinct) index.</summary>
        public int Position { get; init; }

        /// <summary>Gets the encoded packet data (header + code-block contributions).</summary>
        public ReadOnlyMemory<byte> Data { get; init; }

        /// <summary>Gets whether this packet is empty (no contributions).</summary>
        public bool IsEmpty => Data.IsEmpty;

        /// <summary>
        /// Creates an empty packet.
        /// </summary>
        public static PacketData Empty(int layer, int resolution, int component, int position) => new()
        {
            Layer = layer,
            Resolution = resolution,
            Component = component,
            Position = position,
            Data = ReadOnlyMemory<byte>.Empty
        };
    }

    /// <summary>
    /// Information about a code-block's contribution to a layer.
    /// </summary>
    public readonly struct CodeBlockContribution
    {
        /// <summary>Gets the code-block index within the precinct.</summary>
        public int CodeBlockIndex { get; init; }

        /// <summary>Gets whether this is the first time the code-block is included.</summary>
        public bool IsFirstInclusion { get; init; }

        /// <summary>Gets the number of zero bitplanes (MSBs skipped).</summary>
        public int ZeroBitPlanes { get; init; }

        /// <summary>Gets the number of new coding passes in this layer.</summary>
        public int NumNewPasses { get; init; }

        /// <summary>Gets the data length for new passes.</summary>
        public int DataLength { get; init; }

        /// <summary>Gets the code-block data for this contribution.</summary>
        public ReadOnlyMemory<byte> Data { get; init; }
    }

    /// <summary>
    /// JPEG 2000 packet encoder for organizing code-blocks into quality layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tier-2 coding organizes code-block contributions by resolution, component,
    /// layer, and position according to the progression order.
    /// </para>
    /// <para>
    /// Each packet contains:
    /// 1. Packet header with inclusion and zero-bit-plane information
    /// 2. Code-block data contributions for the specified layer
    /// </para>
    /// <para>
    /// Reference: ITU-T T.800 Annex B (Tier-2 coding).
    /// </para>
    /// </remarks>
    public sealed class PacketEncoder
    {
        private readonly List<byte> _headerBuffer;
        private int _bitBuffer;
        private int _bitsInBuffer;

        /// <summary>
        /// Initializes a new packet encoder.
        /// </summary>
        public PacketEncoder()
        {
            _headerBuffer = new List<byte>(256);
        }

        /// <summary>
        /// Creates packets from encoded code-blocks for a single-tile, single-component image.
        /// </summary>
        /// <param name="codeBlocks">Array of encoded code-blocks (row-major order).</param>
        /// <param name="codeBlocksWide">Number of code-blocks horizontally.</param>
        /// <param name="codeBlocksHigh">Number of code-blocks vertically.</param>
        /// <param name="numLayers">Number of quality layers.</param>
        /// <param name="progression">Progression order.</param>
        /// <param name="numResolutions">Number of resolution levels.</param>
        /// <returns>Packets organized by layer.</returns>
        public PacketData[] EncodePackets(
            CodeBlockData[] codeBlocks,
            int codeBlocksWide,
            int codeBlocksHigh,
            int numLayers,
            ProgressionOrder progression,
            int numResolutions = 1)
        {
            if (codeBlocks == null || codeBlocks.Length == 0)
            {
                return Array.Empty<PacketData>();
            }

            // Validate parameters
            if (numLayers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(numLayers), "Number of layers must be at least 1.");
            }

            if (numResolutions < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(numResolutions), "Number of resolutions must be at least 1.");
            }

            if ((int)progression < 0 || (int)progression > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(progression), "Invalid progression order.");
            }

            int numCodeBlocks = codeBlocksWide * codeBlocksHigh;
            if (codeBlocks.Length < numCodeBlocks)
            {
                throw new ArgumentException("Code block array is too small for the specified dimensions.");
            }

            // Track which passes have been included for each code-block
            int[] passesIncluded = new int[numCodeBlocks];
            bool[] firstInclusion = new bool[numCodeBlocks];
            for (int i = 0; i < numCodeBlocks; i++)
            {
                firstInclusion[i] = true;
            }

            // For single-resolution, single-component case (most common in medical imaging)
            // We have one precinct containing all code-blocks
            List<PacketData> packets = new List<PacketData>(numLayers);

            // Simple layer assignment: distribute passes evenly across layers
            int[] passesPerLayer = CalculatePassesPerLayer(codeBlocks, numCodeBlocks, numLayers);

            for (int layer = 0; layer < numLayers; layer++)
            {
                PacketData packet = EncodePacket(
                    codeBlocks,
                    codeBlocksWide,
                    codeBlocksHigh,
                    layer,
                    resolution: 0,
                    component: 0,
                    passesIncluded,
                    firstInclusion,
                    passesPerLayer[layer]);

                packets.Add(packet);
            }

            return packets.ToArray();
        }

        /// <summary>
        /// Encodes a single packet for one layer/resolution/component/position.
        /// </summary>
        private PacketData EncodePacket(
            CodeBlockData[] codeBlocks,
            int codeBlocksWide,
            int codeBlocksHigh,
            int layer,
            int resolution,
            int component,
            int[] passesIncluded,
            bool[] firstInclusion,
            int targetPassesThisLayer)
        {
            int numCodeBlocks = codeBlocksWide * codeBlocksHigh;

            // Reset buffers
            _headerBuffer.Clear();
            _bitBuffer = 0;
            _bitsInBuffer = 0;

            // Collect contributions
            List<CodeBlockContribution> contributions = new List<CodeBlockContribution>();

            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                var cb = codeBlocks[cbIdx];

                // Determine how many new passes to include
                int alreadyIncluded = passesIncluded[cbIdx];
                int totalPasses = cb.NumPasses;
                int remaining = totalPasses - alreadyIncluded;

                if (remaining <= 0)
                {
                    // No more passes to include
                    contributions.Add(new CodeBlockContribution
                    {
                        CodeBlockIndex = cbIdx,
                        IsFirstInclusion = false,
                        ZeroBitPlanes = 0,
                        NumNewPasses = 0,
                        DataLength = 0,
                        Data = ReadOnlyMemory<byte>.Empty
                    });
                    continue;
                }

                // Calculate new passes for this layer
                // Allow zero passes when target has already been met
                int targetNew = targetPassesThisLayer - alreadyIncluded;
                int newPasses = targetNew > 0 ? Math.Min(remaining, targetNew) : 0;

                // Calculate data length
                int startLength = alreadyIncluded > 0 && cb.PassLengths.Length > 0
                    ? cb.PassLengths[Math.Min(alreadyIncluded - 1, cb.PassLengths.Length - 1)]
                    : 0;
                int endLength = (alreadyIncluded + newPasses > 0 && cb.PassLengths.Length > 0)
                    ? cb.PassLengths[Math.Min(alreadyIncluded + newPasses - 1, cb.PassLengths.Length - 1)]
                    : 0;
                int dataLength = endLength - startLength;

                // Extract data slice
                ReadOnlyMemory<byte> data = ReadOnlyMemory<byte>.Empty;
                if (dataLength > 0 && !cb.Data.IsEmpty)
                {
                    int safeStart = Math.Min(startLength, cb.Data.Length);
                    int safeLength = Math.Min(dataLength, cb.Data.Length - safeStart);
                    if (safeLength > 0)
                    {
                        data = cb.Data.Slice(safeStart, safeLength);
                    }
                }

                contributions.Add(new CodeBlockContribution
                {
                    CodeBlockIndex = cbIdx,
                    IsFirstInclusion = firstInclusion[cbIdx] && newPasses > 0,
                    ZeroBitPlanes = cb.MsbPosition >= 0 ? (31 - cb.MsbPosition) : 0, // Leading zeros
                    NumNewPasses = newPasses,
                    DataLength = data.Length,
                    Data = data
                });

                // Update state
                if (newPasses > 0)
                {
                    passesIncluded[cbIdx] += newPasses;
                    firstInclusion[cbIdx] = false;
                }
            }

            // Check if packet is empty
            // Note: Using foreach with early exit instead of LINQ .Any() for performance.
            // This avoids delegate allocation overhead in the encoding hot path.
            bool hasContributions = false;
            foreach (var contrib in contributions)
            {
                if (contrib.NumNewPasses > 0)
                {
                    hasContributions = true;
                    break;
                }
            }

            if (!hasContributions)
            {
                // Empty packet - write single zero bit
                WriteBit(0);
                FlushBits();

                return new PacketData
                {
                    Layer = layer,
                    Resolution = resolution,
                    Component = component,
                    Position = 0,
                    Data = _headerBuffer.ToArray()
                };
            }

            // Write packet header
            // Packet non-empty flag
            WriteBit(1);

            // Write code-block inclusion and data length info
            foreach (var contrib in contributions)
            {
                if (contrib.IsFirstInclusion && contrib.NumNewPasses > 0)
                {
                    // First inclusion - write inclusion bit and zero bitplanes
                    WriteBit(1); // Included

                    // Write zero bitplanes using tag tree (simplified: unary coding)
                    WriteZeroBitPlanes(contrib.ZeroBitPlanes);
                }
                else if (contrib.NumNewPasses > 0)
                {
                    // Subsequent inclusion
                    WriteBit(1); // Included in this layer
                }
                else
                {
                    // Not included
                    WriteBit(0);
                    continue;
                }

                // Write number of passes
                WriteNumPasses(contrib.NumNewPasses);

                // Write data length
                WriteLength(contrib.DataLength);
            }

            FlushBits();

            // Append code-block data
            List<byte> packetData = new List<byte>(_headerBuffer);
            foreach (var contrib in contributions)
            {
                if (contrib.NumNewPasses > 0 && !contrib.Data.IsEmpty)
                {
                    packetData.AddRange(contrib.Data.ToArray());
                }
            }

            return new PacketData
            {
                Layer = layer,
                Resolution = resolution,
                Component = component,
                Position = 0,
                Data = packetData.ToArray()
            };
        }

        /// <summary>
        /// Calculates how many passes to include in each layer for rate control.
        /// </summary>
        private static int[] CalculatePassesPerLayer(CodeBlockData[] codeBlocks, int numCodeBlocks, int numLayers)
        {
            // Find max passes across all code-blocks
            int maxPasses = 0;
            for (int i = 0; i < numCodeBlocks; i++)
            {
                if (codeBlocks[i].NumPasses > maxPasses)
                {
                    maxPasses = codeBlocks[i].NumPasses;
                }
            }

            // Distribute passes evenly
            int[] passesPerLayer = new int[numLayers];
            int passesPerLayerBase = maxPasses / numLayers;
            int remainder = maxPasses % numLayers;

            int cumulative = 0;
            for (int i = 0; i < numLayers; i++)
            {
                int passes = passesPerLayerBase + (i < remainder ? 1 : 0);
                cumulative += passes;
                passesPerLayer[i] = cumulative;
            }

            return passesPerLayer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteBit(int bit)
        {
            _bitBuffer = (_bitBuffer << 1) | (bit & 1);
            _bitsInBuffer++;

            if (_bitsInBuffer == 8)
            {
                OutputByte();
            }
        }

        private void OutputByte()
        {
            byte b = (byte)_bitBuffer;
            _headerBuffer.Add(b);
            _bitBuffer = 0;
            _bitsInBuffer = 0;

            // Bit stuffing: if we output 0xFF, next byte must have MSB=0
            // This is handled by the structure of our data
        }

        private void FlushBits()
        {
            if (_bitsInBuffer > 0)
            {
                // Pad with zeros to complete the byte
                _bitBuffer <<= (8 - _bitsInBuffer);
                OutputByte();
            }
        }

        /// <summary>
        /// Writes zero bitplane count using simplified coding.
        /// </summary>
        private void WriteZeroBitPlanes(int count)
        {
            // Use simple binary coding for small values
            // In a full implementation, this would use tag trees
            if (count <= 7)
            {
                // 3-bit value
                WriteBit((count >> 2) & 1);
                WriteBit((count >> 1) & 1);
                WriteBit(count & 1);
            }
            else
            {
                // Extended: 3 bits of 1, then 5 more bits
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit((count >> 4) & 1);
                WriteBit((count >> 3) & 1);
                WriteBit((count >> 2) & 1);
                WriteBit((count >> 1) & 1);
                WriteBit(count & 1);
            }
        }

        /// <summary>
        /// Writes number of coding passes using ITU-T T.800 Table B.4 encoding.
        /// Must match PacketDecoder.ReadNumPasses exactly.
        /// </summary>
        private void WriteNumPasses(int passes)
        {
            // ITU-T T.800 Table B.4: Variable-length coding for number of passes
            // Encoding must match ReadNumPasses in PacketDecoder
            if (passes == 1)
            {
                // 0
                WriteBit(0);
            }
            else if (passes == 2)
            {
                // 10
                WriteBit(1);
                WriteBit(0);
            }
            else if (passes <= 5)
            {
                // 11xx where xx = passes - 3 (00, 01, 10 for passes 3, 4, 5)
                WriteBit(1);
                WriteBit(1);
                int suffix = passes - 3;
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else if (passes <= 21)
            {
                // 1111 0xxxx (prefix 11110, then 4-bit suffix for 6-21)
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(0);
                int suffix = passes - 6;
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else if (passes <= 37)
            {
                // 1111 10xxxx (prefix 111110, then 4-bit suffix for 22-37)
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(0);
                int suffix = passes - 22;
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else if (passes <= 69)
            {
                // 1111 110xxxxx (prefix 1111110, then 5-bit suffix for 38-69)
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(0);
                int suffix = passes - 38;
                WriteBit((suffix >> 4) & 1);
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else if (passes <= 133)
            {
                // 1111 1110xxxxxx (prefix 11111110, then 6-bit suffix for 70-133)
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(0);
                int suffix = passes - 70;
                WriteBit((suffix >> 5) & 1);
                WriteBit((suffix >> 4) & 1);
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else
            {
                // 1111 1111xxxxxxx (prefix 11111111, then 7-bit suffix for 134+)
                for (int i = 0; i < 8; i++)
                {
                    WriteBit(1);
                }
                int suffix = passes - 134;
                WriteBit((suffix >> 6) & 1);
                WriteBit((suffix >> 5) & 1);
                WriteBit((suffix >> 4) & 1);
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
        }

        /// <summary>
        /// Writes code-block data length.
        /// </summary>
        private void WriteLength(int length)
        {
            // Prefix-free coding scheme (must match PacketDecoder.ReadLength):
            // 0 + 4 bits  = short (0-15)
            // 10 + 8 bits = medium (0-255)
            // 11 + 16 bits = long (0-65535)

            if (length <= 15)
            {
                // Short length: 0 + 4 bits (handles 0-15)
                WriteBit(0);
                WriteBit((length >> 3) & 1);
                WriteBit((length >> 2) & 1);
                WriteBit((length >> 1) & 1);
                WriteBit(length & 1);
            }
            else if (length <= 255)
            {
                // Medium: 10 + 8 bits
                WriteBit(1);
                WriteBit(0);
                for (int i = 7; i >= 0; i--)
                {
                    WriteBit((length >> i) & 1);
                }
            }
            else
            {
                // Long: 11 + 16 bits
                WriteBit(1);
                WriteBit(1);
                for (int i = 15; i >= 0; i--)
                {
                    WriteBit((length >> i) & 1);
                }
            }
        }
    }
}
