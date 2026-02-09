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
    /// Conforms to ITU-T T.800 Annex B (Tier-2 coding).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses tag trees for inclusion and zero bitplane coding (B.10.2-B.10.4),
    /// Lblock-based data length coding (B.10.5), and bit-stuffing after 0xFF bytes.
    /// </para>
    /// </remarks>
    public sealed class PacketEncoder
    {
        private readonly List<byte> _headerBuffer;
        private int _bitBuffer;
        private int _bitsInBuffer;
        private bool _lastByteWasFF;

        // Per-code-block Lblock values (ITU-T T.800 B.10.5)
        private int[]? _lblock;

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

            int numCodeBlocks = codeBlocksWide * codeBlocksHigh;
            if (codeBlocks.Length < numCodeBlocks)
            {
                throw new ArgumentException("Code block array is too small for the specified dimensions.");
            }

            // Initialize per-code-block Lblock values (starts at 3 per ITU-T T.800 B.10.5)
            _lblock = new int[numCodeBlocks];
            for (int i = 0; i < numCodeBlocks; i++)
            {
                _lblock[i] = 3;
            }

            // Tag trees for inclusion and zero bitplanes
            var inclusionTree = new TagTree(codeBlocksWide, codeBlocksHigh);
            var zeroBitPlaneTree = new TagTree(codeBlocksWide, codeBlocksHigh);

            // Track which passes have been included for each code-block
            int[] passesIncluded = new int[numCodeBlocks];
            bool[] firstInclusion = new bool[numCodeBlocks];
            for (int i = 0; i < numCodeBlocks; i++)
            {
                firstInclusion[i] = true;
            }

            // Set inclusion layer values in tag tree.
            // For single-layer: all code-blocks with data are included in layer 0.
            // For multi-layer: code-blocks first appear in their assigned layer.
            int[] passesPerLayer = CalculatePassesPerLayer(codeBlocks, numCodeBlocks, numLayers);

            // Determine first inclusion layer for each code-block
            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                int x = cbIdx % codeBlocksWide;
                int y = cbIdx / codeBlocksWide;

                if (codeBlocks[cbIdx].NumPasses == 0 || codeBlocks[cbIdx].Data.IsEmpty)
                {
                    // Never included - set to a very high layer value
                    inclusionTree.SetValue(x, y, numLayers);
                }
                else
                {
                    // Determine which layer this code-block first appears in
                    int firstLayer = 0;
                    for (int layer = 0; layer < numLayers; layer++)
                    {
                        // A code-block is included in a layer if it has passes to contribute
                        int targetPasses = passesPerLayer[layer];
                        if (targetPasses > 0)
                        {
                            firstLayer = layer;
                            break;
                        }
                    }
                    inclusionTree.SetValue(x, y, firstLayer);
                }

                // Set zero bitplane values
                int zeroBitPlanes = codeBlocks[cbIdx].MsbPosition >= 0
                    ? (31 - codeBlocks[cbIdx].MsbPosition)
                    : 0;
                zeroBitPlaneTree.SetValue(x, y, zeroBitPlanes);
            }

            List<PacketData> packets = new List<PacketData>(numLayers);

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
                    passesPerLayer[layer],
                    inclusionTree,
                    zeroBitPlaneTree);

                packets.Add(packet);
            }

            return packets.ToArray();
        }

        private PacketData EncodePacket(
            CodeBlockData[] codeBlocks,
            int codeBlocksWide,
            int codeBlocksHigh,
            int layer,
            int resolution,
            int component,
            int[] passesIncluded,
            bool[] firstInclusion,
            int targetPassesThisLayer,
            TagTree inclusionTree,
            TagTree zeroBitPlaneTree)
        {
            int numCodeBlocks = codeBlocksWide * codeBlocksHigh;

            // Reset bit writer
            _headerBuffer.Clear();
            _bitBuffer = 0;
            _bitsInBuffer = 0;
            _lastByteWasFF = false;

            // Collect contributions
            var contributions = new CodeBlockContribution[numCodeBlocks];

            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                var cb = codeBlocks[cbIdx];

                int alreadyIncluded = passesIncluded[cbIdx];
                int totalPasses = cb.NumPasses;
                int remaining = totalPasses - alreadyIncluded;

                if (remaining <= 0)
                {
                    contributions[cbIdx] = new CodeBlockContribution
                    {
                        CodeBlockIndex = cbIdx,
                        IsFirstInclusion = false,
                        ZeroBitPlanes = 0,
                        NumNewPasses = 0,
                        DataLength = 0,
                        Data = ReadOnlyMemory<byte>.Empty
                    };
                    continue;
                }

                int newPasses = Math.Min(remaining, Math.Max(1, targetPassesThisLayer - alreadyIncluded));
                if (newPasses <= 0)
                {
                    newPasses = 0;
                }

                // Calculate data length from cumulative PassLengths array
                int startLength = alreadyIncluded > 0 && cb.PassLengths.Length > 0
                    ? cb.PassLengths[Math.Min(alreadyIncluded - 1, cb.PassLengths.Length - 1)]
                    : 0;
                int endLength;
                if (alreadyIncluded + newPasses >= totalPasses)
                {
                    endLength = cb.Data.Length;
                }
                else if (alreadyIncluded + newPasses > 0 && cb.PassLengths.Length > 0)
                {
                    endLength = cb.PassLengths[Math.Min(alreadyIncluded + newPasses - 1, cb.PassLengths.Length - 1)];
                }
                else
                {
                    endLength = 0;
                }
                int dataLength = endLength - startLength;

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

                int zeroBitPlanes = cb.MsbPosition >= 0 ? (31 - cb.MsbPosition) : 0;

                contributions[cbIdx] = new CodeBlockContribution
                {
                    CodeBlockIndex = cbIdx,
                    IsFirstInclusion = firstInclusion[cbIdx] && newPasses > 0,
                    ZeroBitPlanes = zeroBitPlanes,
                    NumNewPasses = newPasses,
                    DataLength = data.Length,
                    Data = data
                };

                if (newPasses > 0)
                {
                    passesIncluded[cbIdx] += newPasses;
                    firstInclusion[cbIdx] = false;
                }
            }

            // Check if packet is empty
            bool hasContributions = false;
            for (int i = 0; i < numCodeBlocks; i++)
            {
                if (contributions[i].NumNewPasses > 0)
                {
                    hasContributions = true;
                    break;
                }
            }

            if (!hasContributions)
            {
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

            // Packet non-empty flag
            WriteBit(1);

            // Encode code-block headers per ITU-T T.800 B.10
            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                var contrib = contributions[cbIdx];
                int x = cbIdx % codeBlocksWide;
                int y = cbIdx / codeBlocksWide;

                // Determine if this code-block has never been included before this layer.
                // contrib.IsFirstInclusion is true only for code-blocks being included
                // for the first time in THIS layer. firstInclusion[cbIdx] is true for
                // code-blocks that are still not included (was true before, and since
                // newPasses==0 it wasn't set to false).
                bool neverIncluded = contrib.IsFirstInclusion || firstInclusion[cbIdx];

                if (neverIncluded)
                {
                    // Use tag tree to signal inclusion/non-inclusion (B.10.3)
                    inclusionTree.Encode(x, y, layer + 1, WriteBitAction);

                    if (contrib.NumNewPasses == 0)
                    {
                        // Tag tree signals "not included at this layer" - done for this CB
                        continue;
                    }

                    // First inclusion at this layer: encode zero bitplanes (B.10.4)
                    zeroBitPlaneTree.Encode(x, y, contrib.ZeroBitPlanes + 1, WriteBitAction);
                }
                else if (contrib.NumNewPasses > 0)
                {
                    // Already included in a previous layer, contributing again: 1-bit
                    WriteBit(1);
                }
                else
                {
                    // Already included in a previous layer, no contribution: 0-bit
                    WriteBit(0);
                    continue;
                }

                // Number of coding passes (B.10.5, Table B.4)
                WriteNumPasses(contrib.NumNewPasses);

                // Data length with Lblock (B.10.5)
                WriteLblock(cbIdx, contrib.NumNewPasses, contrib.DataLength);
            }

            FlushBits();

            // Append code-block data
            int totalDataSize = _headerBuffer.Count;
            for (int i = 0; i < numCodeBlocks; i++)
            {
                if (contributions[i].NumNewPasses > 0)
                    totalDataSize += contributions[i].Data.Length;
            }

            byte[] packetData = new byte[totalDataSize];
            _headerBuffer.CopyTo(packetData, 0);
            int offset = _headerBuffer.Count;

            for (int i = 0; i < numCodeBlocks; i++)
            {
                if (contributions[i].NumNewPasses > 0 && !contributions[i].Data.IsEmpty)
                {
                    contributions[i].Data.Span.CopyTo(packetData.AsSpan(offset));
                    offset += contributions[i].Data.Length;
                }
            }

            return new PacketData
            {
                Layer = layer,
                Resolution = resolution,
                Component = component,
                Position = 0,
                Data = packetData
            };
        }

        /// <summary>
        /// Calculates how many passes to include in each layer for rate control.
        /// </summary>
        private static int[] CalculatePassesPerLayer(CodeBlockData[] codeBlocks, int numCodeBlocks, int numLayers)
        {
            int maxPasses = 0;
            for (int i = 0; i < numCodeBlocks; i++)
            {
                if (codeBlocks[i].NumPasses > maxPasses)
                {
                    maxPasses = codeBlocks[i].NumPasses;
                }
            }

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

        // Action delegate for tag tree encoding
        private void WriteBitAction(int bit) => WriteBit(bit);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteBit(int bit)
        {
            _bitBuffer = (_bitBuffer << 1) | (bit & 1);
            _bitsInBuffer++;

            int maxBits = _lastByteWasFF ? 7 : 8;
            if (_bitsInBuffer == maxBits)
            {
                OutputByte();
            }
        }

        private void OutputByte()
        {
            byte b = (byte)_bitBuffer;
            _headerBuffer.Add(b);
            _lastByteWasFF = (b == 0xFF);
            _bitBuffer = 0;
            _bitsInBuffer = 0;
        }

        private void FlushBits()
        {
            if (_bitsInBuffer > 0)
            {
                int maxBits = _lastByteWasFF ? 7 : 8;
                _bitBuffer <<= (maxBits - _bitsInBuffer);
                OutputByte();
            }
            // If last byte was 0xFF, we already limited to 7 bits which ensures MSB=0
        }

        /// <summary>
        /// Writes number of coding passes using ITU-T T.800 Table B.4.
        /// </summary>
        private void WriteNumPasses(int passes)
        {
            if (passes == 1)
            {
                WriteBit(0);
            }
            else if (passes == 2)
            {
                WriteBit(1);
                WriteBit(0);
            }
            else if (passes <= 5)
            {
                WriteBit(1);
                WriteBit(1);
                int suffix = passes - 3;
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else if (passes <= 36)
            {
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                WriteBit(1);
                int suffix = passes - 6;
                WriteBit((suffix >> 4) & 1);
                WriteBit((suffix >> 3) & 1);
                WriteBit((suffix >> 2) & 1);
                WriteBit((suffix >> 1) & 1);
                WriteBit(suffix & 1);
            }
            else
            {
                for (int i = 0; i < 9; i++)
                {
                    WriteBit(1);
                }
                int suffix = passes - 37;
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
        /// Writes code-block data length using Lblock (ITU-T T.800 B.10.5).
        /// </summary>
        /// <remarks>
        /// The number of bits for the length field is Lblock + floor(log2(numPasses)).
        /// If the length doesn't fit, Lblock is incremented (signaled by writing 1-bits).
        /// </remarks>
        private void WriteLblock(int cbIdx, int numPasses, int dataLength)
        {
            int lblock = _lblock![cbIdx];

            // Number of bits contributed by numPasses: floor(log2(numPasses))
            int passContrib = FloorLog2(numPasses);

            // Total bits available for length = lblock + passContrib
            int lengthBits = lblock + passContrib;

            // Check if length fits; if not, increment Lblock
            while (dataLength >= (1 << lengthBits))
            {
                // Signal Lblock increment with a 1-bit
                WriteBit(1);
                lblock++;
                lengthBits = lblock + passContrib;
            }

            // Signal end of Lblock increment with a 0-bit
            WriteBit(0);

            _lblock[cbIdx] = lblock;

            // Write the length in 'lengthBits' bits, MSB first
            for (int i = lengthBits - 1; i >= 0; i--)
            {
                WriteBit((dataLength >> i) & 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorLog2(int value)
        {
            if (value <= 1) return 0;
            int log = 0;
            int v = value;
            while (v > 1)
            {
                v >>= 1;
                log++;
            }
            return log;
        }
    }
}
