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
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tier-2 decoding parses packet headers to extract code-block inclusion
    /// information and data lengths, then extracts the code-block data segments.
    /// </para>
    /// <para>
    /// Reference: ITU-T T.800 Annex B (Tier-2 coding).
    /// </para>
    /// </remarks>
    public sealed class PacketDecoder
    {
        private ReadOnlyMemory<byte> _data;
        private int _bytePosition;
        private int _bitBuffer;
        private int _bitsAvailable;

        /// <summary>
        /// Initializes a new packet decoder.
        /// </summary>
        public PacketDecoder()
        {
        }

        /// <summary>
        /// Decodes a packet and extracts code-block segments.
        /// </summary>
        /// <param name="packetData">The packet data to decode.</param>
        /// <param name="numCodeBlocks">Number of code-blocks in the precinct.</param>
        /// <param name="firstInclusion">Array where true indicates the code-block has NOT been included
        /// in any previous layer yet (i.e., this would be its first inclusion).</param>
        /// <returns>Code-block segments for this layer.</returns>
        public CodeBlockSegment[] DecodePacket(
            ReadOnlySpan<byte> packetData,
            int numCodeBlocks,
            bool[] firstInclusion)
        {
            if (packetData.IsEmpty)
            {
                return CreateEmptySegments(numCodeBlocks);
            }

            // Initialize bit reader
            _data = packetData.ToArray();
            _bytePosition = 0;
            _bitBuffer = 0;
            _bitsAvailable = 0;

            // Read packet empty flag
            int nonEmpty = ReadBit();
            if (nonEmpty == 0)
            {
                // Empty packet - all code-blocks have no contribution
                return CreateEmptySegments(numCodeBlocks);
            }

            // Parse code-block information
            List<CodeBlockSegment> segments = new List<CodeBlockSegment>(numCodeBlocks);
            List<(int NumPasses, int DataLength, int ZeroBitPlanes, bool IsFirst)> cbInfo =
                new List<(int, int, int, bool)>(numCodeBlocks);

            for (int cbIdx = 0; cbIdx < numCodeBlocks; cbIdx++)
            {
                if (firstInclusion[cbIdx])
                {
                    // First potential inclusion
                    int included = ReadBit();
                    if (included == 0)
                    {
                        // Not included yet
                        cbInfo.Add((0, 0, 0, false));
                        continue;
                    }

                    // Read zero bitplanes
                    int zeroBitPlanes = ReadZeroBitPlanes();

                    // Read number of passes
                    int numPasses = ReadNumPasses();

                    // Read data length
                    int dataLength = ReadLength();

                    cbInfo.Add((numPasses, dataLength, zeroBitPlanes, true));
                    firstInclusion[cbIdx] = false;
                }
                else
                {
                    // Already included before
                    int included = ReadBit();
                    if (included == 0)
                    {
                        // No contribution this layer
                        cbInfo.Add((0, 0, 0, false));
                        continue;
                    }

                    // Read number of passes
                    int numPasses = ReadNumPasses();

                    // Read data length
                    int dataLength = ReadLength();

                    cbInfo.Add((numPasses, dataLength, 0, false));
                }
            }

            // Calculate header end position
            int headerEnd = _bytePosition;
            if (_bitsAvailable > 0 && _bitsAvailable < 8)
            {
                // Partial byte was consumed
                headerEnd = _bytePosition;
            }

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

                // Extract data
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

            return segments.ToArray();
        }

        /// <summary>
        /// Decodes multiple packets and accumulates code-block data.
        /// </summary>
        /// <param name="packets">Array of packets in layer order.</param>
        /// <param name="numCodeBlocks">Number of code-blocks.</param>
        /// <returns>Accumulated data for each code-block.</returns>
        public (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[] DecodeAllPackets(
            PacketData[] packets,
            int numCodeBlocks)
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

            foreach (var packet in packets)
            {
                var segments = DecodePacket(packet.Data.Span, numCodeBlocks, firstInclusion);

                for (int i = 0; i < numCodeBlocks; i++)
                {
                    var seg = segments[i];
                    if (seg.NumNewPasses > 0)
                    {
                        // Accumulate data
                        if (!seg.Data.IsEmpty)
                        {
                            accumulatedData[i].AddRange(seg.Data.ToArray());
                        }

                        // Update pass count and zero bitplanes (defer ToArray until after all packets)
                        var (_, totalPasses, zeroBitPlanes) = results[i];
                        if (seg.IsFirstInclusion)
                        {
                            zeroBitPlanes = seg.ZeroBitPlanes;
                        }
                        // Store null placeholder for data - will convert to array after loop
                        results[i] = (Array.Empty<byte>(), totalPasses + seg.NumNewPasses, zeroBitPlanes);
                    }
                }
            }

            // Convert accumulated data to arrays (deferred to avoid O(n^2) copying)
            for (int i = 0; i < numCodeBlocks; i++)
            {
                var (_, totalPasses, zeroBitPlanes) = results[i];
                results[i] = (accumulatedData[i].ToArray(), totalPasses, zeroBitPlanes);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadBit()
        {
            if (_bitsAvailable == 0)
            {
                if (_bytePosition >= _data.Length)
                {
                    return 0; // EOF - return 0
                }

                _bitBuffer = _data.Span[_bytePosition++];
                _bitsAvailable = 8;
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
        /// Reads zero bitplane count.
        /// </summary>
        private int ReadZeroBitPlanes()
        {
            // Read 3 bits
            int value = ReadBits(3);

            if (value < 7)
            {
                return value;
            }

            // Extended: read 5 more bits
            return ReadBits(5);
        }

        /// <summary>
        /// Reads number of coding passes.
        /// </summary>
        private int ReadNumPasses()
        {
            // ITU-T T.800 Table B.4
            if (ReadBit() == 0)
            {
                return 1;
            }

            if (ReadBit() == 0)
            {
                return 2;
            }

            // 11xx
            int next2 = ReadBits(2);
            if (next2 < 3)
            {
                return 3 + next2;
            }

            // 1111xxxxx or 11111111xxxxxxx
            if (ReadBit() == 0)
            {
                // 11110xxxx
                return 6 + ReadBits(4);
            }

            if (ReadBit() == 0)
            {
                // 111110xxxx
                return 22 + ReadBits(4);
            }

            if (ReadBit() == 0)
            {
                // 1111110xxxxx
                return 38 + ReadBits(5);
            }

            if (ReadBit() == 0)
            {
                // 11111110xxxxxx
                return 70 + ReadBits(6);
            }

            // 11111111xxxxxxx
            return 134 + ReadBits(7);
        }

        /// <summary>
        /// Reads code-block data length.
        /// </summary>
        private int ReadLength()
        {
            // Match encoder's scheme
            if (ReadBit() == 0)
            {
                // Short: 4 bits
                return ReadBits(4);
            }

            if (ReadBit() == 0)
            {
                // Medium: 8 bits
                return ReadBits(8);
            }

            // Long: 16 bits
            return ReadBits(16);
        }
    }
}
