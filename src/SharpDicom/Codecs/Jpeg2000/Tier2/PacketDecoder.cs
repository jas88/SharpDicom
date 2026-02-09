using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier2
{
    /// <summary>
    /// A segment of code-block data extracted from a packet layer.
    /// </summary>
    public readonly struct CodeBlockSegment
    {
        /// <summary>Gets the code-block index within the precinct.</summary>
        public int CodeBlockIndex { get; init; }

        /// <summary>Gets the number of new coding passes in this segment.</summary>
        public int NumNewPasses { get; init; }

        /// <summary>Gets the number of zero bitplanes (for first inclusion).</summary>
        public int ZeroBitPlanes { get; init; }

        /// <summary>Gets whether this is the first inclusion of the code-block.</summary>
        public bool IsFirstInclusion { get; init; }

        /// <summary>Gets the code-block data for this segment.</summary>
        public ReadOnlyMemory<byte> Data { get; init; }

        /// <summary>
        /// Creates an empty segment indicating no contribution.
        /// </summary>
        public static CodeBlockSegment Empty(int codeBlockIndex) => new()
        {
            CodeBlockIndex = codeBlockIndex,
            NumNewPasses = 0,
            ZeroBitPlanes = 0,
            IsFirstInclusion = false,
            Data = ReadOnlyMemory<byte>.Empty
        };
    }

    /// <summary>
    /// JPEG 2000 packet decoder for extracting code-block data from packets.
    /// Conforms to ITU-T T.800 Annex B (Tier-2 coding).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses tag trees for inclusion and zero bitplane decoding (B.10.2-B.10.4),
    /// Lblock-based data length decoding (B.10.5), and bit-unstuffing after 0xFF bytes.
    /// </para>
    /// </remarks>
    public sealed class PacketDecoder
    {
        private ReadOnlyMemory<byte> _data;
        private int _bytePosition;
        private int _bitBuffer;
        private int _bitsAvailable;
        private bool _lastByteWasFF;
        private int _totalBytesConsumed;

        // Per-code-block Lblock values (ITU-T T.800 B.10.5)
        private int[]? _lblock;

        // Tag trees - retained across packets for the same precinct
        private TagTree? _inclusionTree;
        private TagTree? _zeroBitPlaneTree;

        /// <summary>
        /// Initializes a new packet decoder.
        /// </summary>
        public PacketDecoder()
        {
        }

        /// <summary>
        /// Gets the total bytes consumed by the last DecodePacket call.
        /// </summary>
        public int BytesConsumed => _totalBytesConsumed;

        /// <summary>
        /// Initializes tag trees and Lblock state for a precinct.
        /// Must be called before the first DecodePacket call for a new precinct.
        /// </summary>
        /// <param name="codeBlocksWide">Number of code-blocks horizontally.</param>
        /// <param name="codeBlocksHigh">Number of code-blocks vertically.</param>
        public void InitPrecinct(int codeBlocksWide, int codeBlocksHigh)
        {
            int numCodeBlocks = codeBlocksWide * codeBlocksHigh;
            _lblock = new int[numCodeBlocks];
            for (int i = 0; i < numCodeBlocks; i++)
            {
                _lblock[i] = 3;
            }

            _inclusionTree = new TagTree(codeBlocksWide, codeBlocksHigh);
            _zeroBitPlaneTree = new TagTree(codeBlocksWide, codeBlocksHigh);
        }

        /// <summary>
        /// Decodes a packet and extracts code-block segments.
        /// </summary>
        /// <param name="packetData">The packet data to decode.</param>
        /// <param name="numCodeBlocks">Number of code-blocks in the precinct.</param>
        /// <param name="firstInclusion">Array tracking whether each code-block has been included before.</param>
        /// <returns>Code-block segments for this layer.</returns>
        public CodeBlockSegment[] DecodePacket(
            ReadOnlySpan<byte> packetData,
            int numCodeBlocks,
            bool[] firstInclusion)
        {
            return DecodePacket(packetData, numCodeBlocks, firstInclusion, numCodeBlocks, 1, 0);
        }

        /// <summary>
        /// Decodes a packet with tag tree support.
        /// </summary>
        /// <param name="packetData">The packet data to decode.</param>
        /// <param name="numCodeBlocks">Number of code-blocks in the precinct.</param>
        /// <param name="firstInclusion">Array tracking whether each code-block has been included before.</param>
        /// <param name="codeBlocksWide">Number of code-blocks horizontally.</param>
        /// <param name="codeBlocksHigh">Number of code-blocks vertically.</param>
        /// <param name="layer">Current quality layer index.</param>
        /// <returns>Code-block segments for this layer.</returns>
        public CodeBlockSegment[] DecodePacket(
            ReadOnlySpan<byte> packetData,
            int numCodeBlocks,
            bool[] firstInclusion,
            int codeBlocksWide,
            int codeBlocksHigh,
            int layer)
        {
            if (packetData.IsEmpty)
            {
                _totalBytesConsumed = 0;
                return CreateEmptySegments(numCodeBlocks);
            }

            // Auto-init tag trees if needed
            if (_inclusionTree == null || _zeroBitPlaneTree == null || _lblock == null)
            {
                InitPrecinct(codeBlocksWide, codeBlocksHigh);
            }

            // Initialize bit reader
            _data = packetData.ToArray();
            _bytePosition = 0;
            _bitBuffer = 0;
            _bitsAvailable = 0;
            _lastByteWasFF = false;

            // Read packet empty flag
            int nonEmpty = ReadBit();
            if (nonEmpty == 0)
            {
                _totalBytesConsumed = _bytePosition;
                return CreateEmptySegments(numCodeBlocks);
            }

            // Parse code-block information
            List<CodeBlockSegment> segments = new List<CodeBlockSegment>(numCodeBlocks);
            var cbInfo = new (int NumPasses, int DataLength, int ZeroBitPlanes, bool IsFirst)[numCodeBlocks];

            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                int x = cbIdx % codeBlocksWide;
                int y = cbIdx / codeBlocksWide;

                if (firstInclusion[cbIdx])
                {
                    // Use inclusion tag tree to determine if included at this layer
                    int inclusionValue = _inclusionTree!.Decode(x, y, layer + 1, ReadBitFunc);

                    if (inclusionValue > layer)
                    {
                        // Not included at this layer
                        cbInfo[cbIdx] = (0, 0, 0, false);
                        continue;
                    }

                    // First inclusion - decode zero bitplanes
                    int zeroBitPlanes = _zeroBitPlaneTree!.Decode(x, y, int.MaxValue - 1, ReadBitFunc);

                    // Read number of passes
                    int numPasses = ReadNumPasses();

                    // Read data length using Lblock
                    int dataLength = ReadLblock(cbIdx, numPasses);

                    cbInfo[cbIdx] = (numPasses, dataLength, zeroBitPlanes, true);
                    firstInclusion[cbIdx] = false;
                }
                else
                {
                    // Already included before - simple 1-bit flag
                    int included = ReadBit();
                    if (included == 0)
                    {
                        cbInfo[cbIdx] = (0, 0, 0, false);
                        continue;
                    }

                    // Read number of passes
                    int numPasses = ReadNumPasses();

                    // Read data length using Lblock
                    int dataLength = ReadLblock(cbIdx, numPasses);

                    cbInfo[cbIdx] = (numPasses, dataLength, 0, false);
                }
            }

            // Calculate header end position
            int headerEnd = _bytePosition;

            // Extract data segments
            int dataOffset = headerEnd;
            ReadOnlySpan<byte> packetSpan = _data.Span;

            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                var info = cbInfo[cbIdx];
                if (info.NumPasses == 0)
                {
                    segments.Add(CodeBlockSegment.Empty(cbIdx));
                    continue;
                }

                int safeOffset = Math.Min(dataOffset, packetSpan.Length);
                int safeLength = Math.Min(info.DataLength, packetSpan.Length - safeOffset);

                ReadOnlyMemory<byte> cbData = ReadOnlyMemory<byte>.Empty;
                if (safeLength > 0)
                {
                    cbData = _data.Slice(safeOffset, safeLength);
                }

                segments.Add(new CodeBlockSegment
                {
                    CodeBlockIndex = cbIdx,
                    NumNewPasses = info.NumPasses,
                    ZeroBitPlanes = info.ZeroBitPlanes,
                    IsFirstInclusion = info.IsFirst,
                    Data = cbData
                });

                dataOffset += info.DataLength;
            }

            _totalBytesConsumed = dataOffset;
            return segments.ToArray();
        }

        /// <summary>
        /// Decodes multiple packets and accumulates code-block data.
        /// </summary>
        public (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[] DecodeAllPackets(
            PacketData[] packets,
            int numCodeBlocks)
        {
            return DecodeAllPackets(packets, numCodeBlocks, numCodeBlocks, 1);
        }

        /// <summary>
        /// Decodes multiple packets and accumulates code-block data with tag tree support.
        /// </summary>
        public (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[] DecodeAllPackets(
            PacketData[] packets,
            int numCodeBlocks,
            int codeBlocksWide,
            int codeBlocksHigh)
        {
            var results = new (ReadOnlyMemory<byte>, int, int)[numCodeBlocks];
            bool[] firstInclusion = new bool[numCodeBlocks];
            List<byte>[] accumulatedData = new List<byte>[numCodeBlocks];

            for (int i = 0; i < numCodeBlocks; i++)
            {
                firstInclusion[i] = true;
                accumulatedData[i] = new List<byte>();
                results[i] = (ReadOnlyMemory<byte>.Empty, 0, 0);
            }

            // Initialize tag trees for the precinct
            InitPrecinct(codeBlocksWide, codeBlocksHigh);

            for (int layerIdx = 0; layerIdx < packets.Length; layerIdx++)
            {
                var segments = DecodePacket(
                    packets[layerIdx].Data.Span,
                    numCodeBlocks,
                    firstInclusion,
                    codeBlocksWide,
                    codeBlocksHigh,
                    layerIdx);

                for (int i = 0; i < numCodeBlocks; i++)
                {
                    var seg = segments[i];
                    if (seg.NumNewPasses > 0)
                    {
                        if (!seg.Data.IsEmpty)
                        {
                            accumulatedData[i].AddRange(seg.Data.ToArray());
                        }

                        var (_, totalPasses, zeroBitPlanes) = results[i];
                        if (seg.IsFirstInclusion)
                        {
                            zeroBitPlanes = seg.ZeroBitPlanes;
                        }
                        results[i] = (accumulatedData[i].ToArray(), totalPasses + seg.NumNewPasses, zeroBitPlanes);
                    }
                }
            }

            return results;
        }

        private static CodeBlockSegment[] CreateEmptySegments(int numCodeBlocks)
        {
            var segments = new CodeBlockSegment[numCodeBlocks];
            for (int i = 0; i < numCodeBlocks; i++)
            {
                segments[i] = CodeBlockSegment.Empty(i);
            }
            return segments;
        }

        // Func delegate for tag tree decoding
        private int ReadBitFunc() => ReadBit();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadBit()
        {
            if (_bitsAvailable == 0)
            {
                if (_bytePosition >= _data.Length)
                {
                    return 0;
                }

                _bitBuffer = _data.Span[_bytePosition++];
                _bitsAvailable = _lastByteWasFF ? 7 : 8;
                _lastByteWasFF = (_bitBuffer == 0xFF);

                // For 7-bit mode (after 0xFF), the MSB is stuffing and we only use bits 6..0
                // but our ReadBit reads from MSB down so we just reduce available count
            }

            _bitsAvailable--;
            return (_bitBuffer >> _bitsAvailable) & 1;
        }

        private int ReadBits(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | ReadBit();
            }
            return value;
        }

        /// <summary>
        /// Reads number of coding passes using ITU-T T.800 Table B.4.
        /// </summary>
        private int ReadNumPasses()
        {
            if (ReadBit() == 0)
            {
                return 1;
            }

            if (ReadBit() == 0)
            {
                return 2;
            }

            int next2 = ReadBits(2);
            if (next2 < 3)
            {
                return 3 + next2;
            }

            int suffix5 = ReadBits(5);
            if (suffix5 <= 30)
            {
                return 6 + suffix5;
            }

            return 37 + ReadBits(7);
        }

        /// <summary>
        /// Reads code-block data length using Lblock (ITU-T T.800 B.10.5).
        /// </summary>
        private int ReadLblock(int cbIdx, int numPasses)
        {
            int lblock = _lblock![cbIdx];

            // Read Lblock increment bits: count leading 1-bits until a 0-bit
            while (ReadBit() == 1)
            {
                lblock++;
            }

            _lblock[cbIdx] = lblock;

            // Number of bits for length = lblock + floor(log2(numPasses))
            int passContrib = FloorLog2(numPasses);
            int lengthBits = lblock + passContrib;

            return ReadBits(lengthBits);
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
