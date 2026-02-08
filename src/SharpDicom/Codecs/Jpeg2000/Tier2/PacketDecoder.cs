using System;
using System.Collections.Generic;
using System.IO;
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
        private int _totalBytesConsumed;

        /// <summary>
        /// Initializes a new packet decoder.
        /// </summary>
        public PacketDecoder()
        {
        }

        /// <summary>
        /// Gets the total bytes consumed by the last <see cref="DecodePacket"/> call
        /// (header + all code-block data).
        /// </summary>
        public int BytesConsumed => _totalBytesConsumed;

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
            if (packetData.IsEmpty)
            {
                _totalBytesConsumed = 0;
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
                _totalBytesConsumed = _bytePosition;
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

            _totalBytesConsumed = dataOffset;
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

                        // Update totals
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
        /// Reads number of coding passes using ITU-T T.800 Table B.4.
        /// </summary>
        /// <remarks>
        /// Both EBCOT and HTJ2K use the same variable-length encoding:
        /// | Passes | Coding                        |
        /// |--------|-------------------------------|
        /// | 1      | 0                             |
        /// | 2      | 10                            |
        /// | 3-5    | 11xx (00=3, 01=4, 10=5)       |
        /// | 6-36   | 1111 + 5-bit (0-30)           |
        /// | 37-164 | 1111 1111 + 7-bit (0-127)     |
        /// </remarks>
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

            // At this point we've read "11"
            // Read 2 more bits to determine which case
            int next2 = ReadBits(2);
            if (next2 < 3)
            {
                // 11xx where xx = 00, 01, 10 -> passes 3, 4, 5
                return 3 + next2;
            }

            // next2 == 3 means we read "1111"
            // Now distinguish between:
            //   1111 + 5-bit suffix (passes 6-36, suffix 0-30)
            //   1111 1111 + 7-bit suffix (passes 37-164, suffix 0-127)
            // Read the 5-bit suffix. If it's 31 (all ones), we've actually
            // read 1111 11111 and the 9th bit is the start of the long form.
            int suffix5 = ReadBits(5);
            if (suffix5 <= 30)
            {
                return 6 + suffix5;
            }

            // suffix5 == 31 means we read "1111 11111" (9 ones total).
            // The encoder format for 37-164 is 0xff80 | (n-37) in 16 bits:
            //   bits[15:12] = 1111, bits[11:7] = 11111, bits[6:0] = suffix
            // We consumed bits[15:7]. Now read the remaining 7 bits.
            return 37 + ReadBits(7);
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
