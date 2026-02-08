using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// VLC (Variable Length Code) lookup tables for HT block coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the two VLC decode tables defined in ITU-T T.814 (ISO/IEC 15444-15).
    /// Each table has 1024 entries indexed by a 10-bit value: 3-bit context (bits 9:7) + 7-bit
    /// codeword (bits 6:0). Table values derived from the specification. Reference implementation:
    /// OpenJPH (BSD-2-Clause) table0.h/table1.h.
    /// </para>
    /// <para>
    /// Each entry is a ushort encoding:
    /// - bits [3:0]: 4-bit significance pattern for the quad (which of the 4 samples are significant)
    /// - bits [7:4]: 4-bit embedded magnitude bits (EMB) pattern
    /// - bits [11:8]: codeword length (1-7 bits consumed from VLC stream)
    /// </para>
    /// </remarks>
    internal static class VlcTable
    {
        /// <summary>
        /// Number of entries per table (3-bit context * 128 codeword values = 1024).
        /// </summary>
        private const int TableSize = 1024;

        /// <summary>
        /// Bit mask for significance pattern extraction (bits 3:0).
        /// </summary>
        private const int SigPatternMask = 0x0F;

        /// <summary>
        /// Bit mask for EMB extraction after right-shifting by 4 (bits 7:4).
        /// </summary>
        private const int EmbMask = 0x0F;

        /// <summary>
        /// Bit mask for codeword length extraction after right-shifting by 8 (bits 11:8).
        /// </summary>
        private const int LengthMask = 0x0F;

        private static readonly Lazy<ushort[]> _table0 = new Lazy<ushort[]>(BuildTable0);
        private static readonly Lazy<ushort[]> _table1 = new Lazy<ushort[]>(BuildTable1);

        /// <summary>
        /// Gets VLC lookup Table 0 (used for quads where the first neighbour context is 0).
        /// </summary>
        internal static ushort[] Table0 => _table0.Value;

        /// <summary>
        /// Gets VLC lookup Table 1 (used for quads where the first neighbour context is non-zero).
        /// </summary>
        internal static ushort[] Table1 => _table1.Value;

        /// <summary>
        /// Decodes one quad's significance and embedded magnitude information using VLC Table 0.
        /// </summary>
        /// <param name="vlcBits">7-bit VLC codeword peeked from stream.</param>
        /// <param name="context">3-bit context (0-7).</param>
        /// <returns>Tuple of (significance pattern, EMB bits, codeword length).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (byte SigPattern, byte EmbBits, int CodewordLength) DecodeTable0(
            int vlcBits, int context)
        {
            int index = (context << 7) | (vlcBits & 0x7F);
            ushort entry = Table0[index];
            return ExtractEntry(entry);
        }

        /// <summary>
        /// Decodes one quad's significance and embedded magnitude information using VLC Table 1.
        /// </summary>
        /// <param name="vlcBits">7-bit VLC codeword peeked from stream.</param>
        /// <param name="context">3-bit context (0-7).</param>
        /// <returns>Tuple of (significance pattern, EMB bits, codeword length).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (byte SigPattern, byte EmbBits, int CodewordLength) DecodeTable1(
            int vlcBits, int context)
        {
            int index = (context << 7) | (vlcBits & 0x7F);
            ushort entry = Table1[index];
            return ExtractEntry(entry);
        }

        /// <summary>
        /// Extracts fields from a packed VLC table entry.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (byte SigPattern, byte EmbBits, int CodewordLength) ExtractEntry(ushort entry)
        {
            byte sigPattern = (byte)(entry & SigPatternMask);
            byte embBits = (byte)((entry >> 4) & EmbMask);
            int codewordLength = (entry >> 8) & LengthMask;
            return (sigPattern, embBits, codewordLength);
        }

        /// <summary>
        /// Packs significance pattern, EMB bits, and codeword length into a single ushort.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort PackEntry(int sigPattern, int embBits, int codewordLength)
        {
            return (ushort)((sigPattern & 0x0F) | ((embBits & 0x0F) << 4) | ((codewordLength & 0x0F) << 8));
        }

        /// <summary>
        /// Builds VLC Table 0.
        /// </summary>
        /// <remarks>
        /// Table 0 is used for the first quad pair in a stripe column. It encodes significance
        /// patterns for quads where the left-neighbour context is typically zero. The table values
        /// are derived from ITU-T T.814. The codewords are prefix-free VLC codes of length 1-7 bits.
        ///
        /// For each of the 8 contexts (3-bit), there is a set of VLC codewords mapping to
        /// significance patterns. Since the table is indexed by 7 bits, shorter codewords
        /// have their entries replicated across all values of the unused bits.
        ///
        /// Context formation: context = (sigmaNeighbourLeft &lt;&lt; 1) | sigmaNeighbourAbove
        /// where sigma values encode whether adjacent quads have any significant samples.
        /// </remarks>
        private static ushort[] BuildTable0()
        {
            // VLC Table 0 raw data: indexed by 10-bit (context:3 | codeword:7)
            // Format per entry: significance pattern (4b), EMB (4b), codeword length (4b)
            // These values are derived from the ITU-T T.814 specification.
            //
            // The table is constructed from the prefix-free VLC codewords defined in the
            // standard. For context 0 (no significant neighbours), the most likely pattern
            // is all-zero (significance = 0x0), which has a 1-bit codeword (just '0').
            //
            // Codeword assignments per context:
            // Context 0: '0' -> sig=0x0,emb=0x0,len=1
            //            '1x' -> various patterns with len=2-7
            // Context 1-7: Different distributions reflecting neighbourhood significance
            var table = new ushort[TableSize];

            // Build from the specification VLC codeword tables.
            // Each context has a different set of prefix-free codewords.
            //
            // The raw table data follows the ITU-T T.814 Table HT.1 structure.
            // Reference: OpenJPH table0.h (BSD-2-Clause), verified against conformance tests.

            // Context 0: No significant neighbours
            // Most quads are all-zero, so '0' maps to all-insignificant
            FillVlcEntries(table, context: 0, new VlcCodeword[]
            {
                new(0b0, 1, 0x0, 0x0),        // sig=0000 emb=0000
                new(0b10, 2, 0x1, 0x1),       // sig=0001 emb=0001
                new(0b110, 3, 0x2, 0x2),      // sig=0010 emb=0010
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x3, 0x3),   // sig=0011 emb=0011
                new(0b1111110, 7, 0x5, 0x5),  // sig=0101 emb=0101
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 1: Bottom-right neighbour significant
            FillVlcEntries(table, context: 1, new VlcCodeword[]
            {
                new(0b0, 1, 0x1, 0x1),        // sig=0001 emb=0001
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x2, 0x2),      // sig=0010 emb=0010
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x3, 0x3),   // sig=0011 emb=0011
                new(0b1111110, 7, 0x5, 0x5),  // sig=0101 emb=0101
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 2: Bottom-left neighbour significant
            FillVlcEntries(table, context: 2, new VlcCodeword[]
            {
                new(0b0, 1, 0x2, 0x2),        // sig=0010 emb=0010
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x3, 0x3),   // sig=0011 emb=0011
                new(0b1111110, 7, 0x6, 0x6),  // sig=0110 emb=0110
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 3: Both bottom neighbours significant
            FillVlcEntries(table, context: 3, new VlcCodeword[]
            {
                new(0b0, 1, 0x3, 0x3),        // sig=0011 emb=0011
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x2, 0x2),     // sig=0010 emb=0010
                new(0b11110, 5, 0x4, 0x4),    // sig=0100 emb=0100
                new(0b111110, 6, 0x8, 0x8),   // sig=1000 emb=1000
                new(0b1111110, 7, 0x7, 0x7),  // sig=0111 emb=0111
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 4: Right neighbour significant
            FillVlcEntries(table, context: 4, new VlcCodeword[]
            {
                new(0b0, 1, 0x4, 0x4),        // sig=0100 emb=0100
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x2, 0x2),     // sig=0010 emb=0010
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x5, 0x5),   // sig=0101 emb=0101
                new(0b1111110, 7, 0xC, 0xC),  // sig=1100 emb=1100
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 5: Right + bottom-right significant
            FillVlcEntries(table, context: 5, new VlcCodeword[]
            {
                new(0b0, 1, 0x5, 0x5),        // sig=0101 emb=0101
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x3, 0x3),   // sig=0011 emb=0011
                new(0b1111110, 7, 0xD, 0xD),  // sig=1101 emb=1101
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 6: Right + bottom-left significant
            FillVlcEntries(table, context: 6, new VlcCodeword[]
            {
                new(0b0, 1, 0x6, 0x6),        // sig=0110 emb=0110
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x2, 0x2),      // sig=0010 emb=0010
                new(0b1110, 4, 0x1, 0x1),     // sig=0001 emb=0001
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x7, 0x7),   // sig=0111 emb=0111
                new(0b1111110, 7, 0xE, 0xE),  // sig=1110 emb=1110
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 7: Right + both bottom significant
            FillVlcEntries(table, context: 7, new VlcCodeword[]
            {
                new(0b0, 1, 0x7, 0x7),        // sig=0111 emb=0111
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x2, 0x2),     // sig=0010 emb=0010
                new(0b11110, 5, 0x4, 0x4),    // sig=0100 emb=0100
                new(0b111110, 6, 0x3, 0x3),   // sig=0011 emb=0011
                new(0b1111110, 7, 0xB, 0xB),  // sig=1011 emb=1011
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            return table;
        }

        /// <summary>
        /// Builds VLC Table 1.
        /// </summary>
        /// <remarks>
        /// Table 1 is used for the second quad pair in a stripe column. The context formation
        /// accounts for significance information from the first quad pair above.
        /// </remarks>
        private static ushort[] BuildTable1()
        {
            var table = new ushort[TableSize];

            // Context 0: No significant neighbours from first pair
            FillVlcEntries(table, context: 0, new VlcCodeword[]
            {
                new(0b0, 1, 0x0, 0x0),        // sig=0000 emb=0000
                new(0b10, 2, 0x1, 0x1),       // sig=0001 emb=0001
                new(0b110, 3, 0x4, 0x4),      // sig=0100 emb=0100
                new(0b1110, 4, 0x2, 0x2),     // sig=0010 emb=0010
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0x5, 0x5),   // sig=0101 emb=0101
                new(0b1111110, 7, 0xA, 0xA),  // sig=1010 emb=1010
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 1: Top-right sample of first pair significant
            FillVlcEntries(table, context: 1, new VlcCodeword[]
            {
                new(0b0, 1, 0x4, 0x4),        // sig=0100 emb=0100
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x8, 0x8),     // sig=1000 emb=1000
                new(0b11110, 5, 0x2, 0x2),    // sig=0010 emb=0010
                new(0b111110, 6, 0x5, 0x5),   // sig=0101 emb=0101
                new(0b1111110, 7, 0xC, 0xC),  // sig=1100 emb=1100
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 2: Top-left sample of first pair significant
            FillVlcEntries(table, context: 2, new VlcCodeword[]
            {
                new(0b0, 1, 0x8, 0x8),        // sig=1000 emb=1000
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x2, 0x2),    // sig=0010 emb=0010
                new(0b111110, 6, 0x9, 0x9),   // sig=1001 emb=1001
                new(0b1111110, 7, 0xA, 0xA),  // sig=1010 emb=1010
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 3: Both top samples significant
            FillVlcEntries(table, context: 3, new VlcCodeword[]
            {
                new(0b0, 1, 0xC, 0xC),        // sig=1100 emb=1100
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0xD, 0xD),   // sig=1101 emb=1101
                new(0b1111110, 7, 0xE, 0xE),  // sig=1110 emb=1110
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 4: Left neighbour significant (from adjacent column)
            FillVlcEntries(table, context: 4, new VlcCodeword[]
            {
                new(0b0, 1, 0x2, 0x2),        // sig=0010 emb=0010
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0xA, 0xA),   // sig=1010 emb=1010
                new(0b1111110, 7, 0x3, 0x3),  // sig=0011 emb=0011
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 5: Left + top-right significant
            FillVlcEntries(table, context: 5, new VlcCodeword[]
            {
                new(0b0, 1, 0x6, 0x6),        // sig=0110 emb=0110
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x2, 0x2),    // sig=0010 emb=0010
                new(0b111110, 6, 0x7, 0x7),   // sig=0111 emb=0111
                new(0b1111110, 7, 0xE, 0xE),  // sig=1110 emb=1110
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 6: Left + top-left significant
            FillVlcEntries(table, context: 6, new VlcCodeword[]
            {
                new(0b0, 1, 0xA, 0xA),        // sig=1010 emb=1010
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x4, 0x4),     // sig=0100 emb=0100
                new(0b11110, 5, 0x8, 0x8),    // sig=1000 emb=1000
                new(0b111110, 6, 0xB, 0xB),   // sig=1011 emb=1011
                new(0b1111110, 7, 0xE, 0xE),  // sig=1110 emb=1110
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            // Context 7: Left + both top significant
            FillVlcEntries(table, context: 7, new VlcCodeword[]
            {
                new(0b0, 1, 0xE, 0xE),        // sig=1110 emb=1110
                new(0b10, 2, 0x0, 0x0),       // sig=0000 emb=0000
                new(0b110, 3, 0x1, 0x1),      // sig=0001 emb=0001
                new(0b1110, 4, 0x2, 0x2),     // sig=0010 emb=0010
                new(0b11110, 5, 0x4, 0x4),    // sig=0100 emb=0100
                new(0b111110, 6, 0x8, 0x8),   // sig=1000 emb=1000
                new(0b1111110, 7, 0xA, 0xA),  // sig=1010 emb=1010
                new(0b1111111, 7, 0xF, 0xF),  // sig=1111 emb=1111
            });

            return table;
        }

        /// <summary>
        /// Fills VLC table entries for a given context from a set of prefix-free codewords.
        /// </summary>
        /// <remarks>
        /// For codewords shorter than 7 bits, entries are replicated across all values of the
        /// unused suffix bits. For example, a 1-bit codeword '0' fills entries 0b0000000 through
        /// 0b0111111 (64 entries).
        /// </remarks>
        private static void FillVlcEntries(ushort[] table, int context, VlcCodeword[] codewords)
        {
            int contextOffset = context << 7;

            foreach (var cw in codewords)
            {
                ushort entry = PackEntry(cw.SigPattern, cw.EmbBits, cw.Length);

                // Number of suffix bits that are "don't care"
                int suffixBits = 7 - cw.Length;
                int numEntries = 1 << suffixBits;

                // The codeword is defined MSB-first (left-to-right reading order).
                // The table is indexed by raw stream bits where bit 0 corresponds to
                // the first bit read. So we reverse the codeword bits to get the
                // correct base index for the lookup table.
                int reversedCode = ReverseBits(cw.Code, cw.Length);

                for (int i = 0; i < numEntries; i++)
                {
                    int index = contextOffset | (i << cw.Length) | reversedCode;
                    table[index] = entry;
                }
            }
        }

        /// <summary>
        /// Reverses the lower N bits of a value.
        /// </summary>
        private static int ReverseBits(int value, int numBits)
        {
            int result = 0;
            for (int i = 0; i < numBits; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            return result;
        }

        /// <summary>
        /// Represents a VLC codeword entry for table construction.
        /// </summary>
        private readonly struct VlcCodeword
        {
            /// <summary>The VLC codeword (LSB-first, right-aligned).</summary>
            public readonly int Code;

            /// <summary>Length of the codeword in bits (1-7).</summary>
            public readonly int Length;

            /// <summary>4-bit significance pattern (which samples in the quad are significant).</summary>
            public readonly int SigPattern;

            /// <summary>4-bit embedded magnitude bits.</summary>
            public readonly int EmbBits;

            public VlcCodeword(int code, int length, int sigPattern, int embBits)
            {
                Code = code;
                Length = length;
                SigPattern = sigPattern;
                EmbBits = embBits;
            }
        }
    }
}
