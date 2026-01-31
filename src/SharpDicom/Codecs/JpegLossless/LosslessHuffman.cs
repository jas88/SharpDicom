using System;
using System.Runtime.CompilerServices;
using SharpDicom.Codecs.Jpeg;

namespace SharpDicom.Codecs.JpegLossless
{
    /// <summary>
    /// Huffman coding for JPEG Lossless prediction residuals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JPEG Lossless uses the same Huffman coding structure as DCT-based JPEG,
    /// but only encodes DC-like difference values (prediction residuals).
    /// Each difference is encoded as:
    /// </para>
    /// <list type="number">
    /// <item>A Huffman code indicating the category (SSSS = number of additional bits)</item>
    /// <item>SSSS additional bits representing the signed difference magnitude</item>
    /// </list>
    /// <para>
    /// The category SSSS indicates how many bits follow to represent the difference:
    /// <list type="bullet">
    /// <item>SSSS=0: difference is 0 (no additional bits)</item>
    /// <item>SSSS=1: difference is -1 or 1</item>
    /// <item>SSSS=2: difference is -3..-2 or 2..3</item>
    /// <item>etc.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class LosslessHuffman
    {
        // Default Huffman table for lossless JPEG (extended to support 16-bit samples)
        // Based on ITU-T T.81 Annex K Table K.3 (Luminance DC) with additions for categories 12-16
        // BITS: number of codes of each length (1-16 bits)
        // Categories 0-16 are assigned codes of varying lengths
        private static readonly byte[] DefaultBits =
            { 0, 1, 5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        private static readonly byte[] DefaultValues =
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        /// <summary>
        /// Default DC Huffman table for lossless JPEG (supports up to 16-bit differences).
        /// </summary>
        public static readonly LosslessHuffman Default = new(DefaultBits, DefaultValues);

        // Decoding structures (1-indexed for bit lengths 1-16)
        private readonly int[] _minCode;   // Minimum code value for each bit length
        private readonly int[] _maxCode;   // Maximum code value for each bit length (-1 if none)
        private readonly int[] _valPtr;    // Index to first value for each bit length
        private readonly byte[] _values;   // Symbol values (categories 0-16)

        // Encoding structures (indexed by category 0-16)
        private readonly ushort[] _encodeCode;  // Huffman codes
        private readonly byte[] _encodeSize;    // Code sizes

        private LosslessHuffman(byte[] bits, byte[] values)
        {
            _values = (byte[])values.Clone();

            // Allocate decoding arrays (16 bit lengths max, 1-indexed)
            _minCode = new int[17];
            _maxCode = new int[17];
            _valPtr = new int[17];

            // Build decoding tables
            BuildDecodeTables(bits);

            // Build encoding tables (max 17 categories for 16-bit samples)
            _encodeCode = new ushort[17];
            _encodeSize = new byte[17];
            BuildEncodeTables(bits);
        }

        private void BuildDecodeTables(byte[] bits)
        {
            // Generate codes and build decode tables per ITU-T T.81 Annex C
            int code = 0;
            int valueIndex = 0;

            for (int length = 1; length <= 16; length++)
            {
                int count = bits[length - 1];
                if (count > 0)
                {
                    _minCode[length] = code;
                    _valPtr[length] = valueIndex;
                    _maxCode[length] = code + count - 1;
                    code += count;
                    valueIndex += count;
                }
                else
                {
                    _minCode[length] = -1;
                    _maxCode[length] = -1;
                }
                code <<= 1;
            }
        }

        private void BuildEncodeTables(byte[] bits)
        {
            // Generate codes for encoding per ITU-T T.81 Annex C
            int code = 0;
            int valueIndex = 0;

            for (int length = 1; length <= 16; length++)
            {
                int count = bits[length - 1];
                for (int i = 0; i < count; i++)
                {
                    if (valueIndex < _values.Length)
                    {
                        byte category = _values[valueIndex];
                        if (category < _encodeCode.Length)
                        {
                            _encodeCode[category] = (ushort)code;
                            _encodeSize[category] = (byte)length;
                        }
                        code++;
                        valueIndex++;
                    }
                }
                code <<= 1;
            }
        }

        /// <summary>
        /// Decodes a difference value from the bitstream.
        /// </summary>
        /// <param name="reader">The bit reader positioned at the start of a Huffman code.</param>
        /// <returns>The signed difference value.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the bitstream contains invalid Huffman codes or insufficient data.
        /// </exception>
        public int DecodeDifference(ref BitReader reader)
        {
            // 1. Decode SSSS (category) using Huffman
            int ssss = DecodeCategory(ref reader);
            if (ssss == 0)
            {
                return 0;
            }

            // 2. Read SSSS additional bits
            int additional = reader.ReadBits(ssss);

            // 3. Extend sign
            return ExtendSign(additional, ssss);
        }

        /// <summary>
        /// Encodes a difference value to the bitstream.
        /// </summary>
        /// <param name="writer">The bit writer to append to.</param>
        /// <param name="diff">The signed difference value to encode.</param>
        public void EncodeDifference(ref BitWriter writer, int diff)
        {
            // 1. Compute category SSSS
            int ssss = GetCategory(diff);

            // 2. Write Huffman code for category
            WriteCategory(ref writer, ssss);

            // 3. Write additional bits
            if (ssss > 0)
            {
                // For negative values, compute the encoded representation
                int additional = diff < 0 ? diff + (1 << ssss) - 1 : diff;
                writer.WriteBits(additional, ssss);
            }
        }

        /// <summary>
        /// Decodes a Huffman symbol (category) from the bitstream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int DecodeCategory(ref BitReader reader)
        {
            int code = 0;

            for (int length = 1; length <= 16; length++)
            {
                int bit = reader.ReadBit();
                code = (code << 1) | bit;

                if (_maxCode[length] >= 0 && code <= _maxCode[length])
                {
                    int index = _valPtr[length] + (code - _minCode[length]);
                    return _values[index];
                }
            }

            throw new InvalidOperationException("Invalid Huffman code in lossless JPEG bitstream.");
        }

        /// <summary>
        /// Writes a Huffman code for the specified category.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteCategory(ref BitWriter writer, int category)
        {
            if (category < 0 || category >= _encodeSize.Length || _encodeSize[category] == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(category),
                    $"Category {category} not supported by this Huffman table.");
            }

            writer.WriteBits(_encodeCode[category], _encodeSize[category]);
        }

        /// <summary>
        /// Extends the sign of a value based on its category.
        /// </summary>
        /// <param name="value">The raw bits read from the stream.</param>
        /// <param name="category">The category (number of bits).</param>
        /// <returns>The signed value.</returns>
        /// <remarks>
        /// In JPEG's signed magnitude encoding:
        /// - If the MSB of the additional bits is 1, the value is positive
        /// - If the MSB is 0, the value is negative (computed as value - 2^category + 1)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ExtendSign(int value, int category)
        {
            int threshold = 1 << (category - 1);
            return value < threshold ? value - (1 << category) + 1 : value;
        }

        /// <summary>
        /// Gets the category (number of bits) needed to represent a difference value.
        /// </summary>
        /// <param name="value">The difference value.</param>
        /// <returns>The category SSSS (0-16).</returns>
        /// <remarks>
        /// Category definitions:
        /// <list type="table">
        /// <item><term>0</term><description>value = 0</description></item>
        /// <item><term>1</term><description>value in [-1, 1]</description></item>
        /// <item><term>2</term><description>value in [-3, -2] or [2, 3]</description></item>
        /// <item><term>n</term><description>value in [-(2^n-1), -(2^(n-1))] or [2^(n-1), 2^n-1]</description></item>
        /// </list>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetCategory(int value)
        {
            if (value == 0)
            {
                return 0;
            }

            int absValue = value < 0 ? -value : value;

            // Count bits needed
            int category = 0;
            while (absValue > 0)
            {
                absValue >>= 1;
                category++;
            }

            return category;
        }

        /// <summary>
        /// Attempts to parse a DHT segment and create a LosslessHuffman table.
        /// </summary>
        /// <param name="segment">The DHT segment data (after the 0xFFC4 marker and length).</param>
        /// <param name="table">When successful, the parsed Huffman table.</param>
        /// <returns>true if parsing succeeded; otherwise, false.</returns>
        /// <remarks>
        /// DHT segment format:
        /// <list type="bullet">
        /// <item>1 byte: Table class (high nibble, should be 0 for DC) and ID (low nibble)</item>
        /// <item>16 bytes: Number of codes for each bit length (BITS)</item>
        /// <item>N bytes: Symbol values, where N = sum of BITS</item>
        /// </list>
        /// </remarks>
        public static bool TryParseDHT(ReadOnlySpan<byte> segment, out LosslessHuffman? table)
        {
            table = null;

            if (segment.Length < 17)
            {
                return false;
            }

            byte header = segment[0];
            byte tableClass = (byte)(header >> 4);
            // byte tableId = (byte)(header & 0x0F); // Unused but part of the format

            // For lossless JPEG, we only use DC tables (class 0)
            if (tableClass != 0)
            {
                return false;
            }

            // Read BITS array
            byte[] bits = new byte[16];
            int totalValues = 0;
            for (int i = 0; i < 16; i++)
            {
                bits[i] = segment[1 + i];
                totalValues += bits[i];
            }

            if (segment.Length < 17 + totalValues)
            {
                return false;
            }

            // Read values array
            byte[] values = new byte[totalValues];
            for (int i = 0; i < totalValues; i++)
            {
                values[i] = segment[17 + i];
            }

            table = new LosslessHuffman(bits, values);
            return true;
        }

        /// <summary>
        /// Gets the total size in bytes of the DHT segment payload for this table.
        /// </summary>
        /// <returns>Size in bytes (17 + number of symbols).</returns>
        public int GetDhtSegmentSize()
        {
            return 17 + _values.Length;
        }

        /// <summary>
        /// Writes this table as a DHT segment payload.
        /// </summary>
        /// <param name="destination">Destination buffer (must be at least GetDhtSegmentSize() bytes).</param>
        /// <param name="tableId">Table identifier (0-3, typically 0).</param>
        public void WriteDhtSegment(Span<byte> destination, byte tableId = 0)
        {
            // Header: class (0 for DC) and ID
            destination[0] = tableId;

            // Reconstruct BITS array from encode tables
            Span<byte> bits = destination.Slice(1, 16);
            bits.Clear();

            // Count symbols at each code length
            for (int i = 0; i < _values.Length; i++)
            {
                byte category = _values[i];
                if (category < _encodeSize.Length)
                {
                    byte size = _encodeSize[category];
                    if (size >= 1 && size <= 16)
                    {
                        bits[size - 1]++;
                    }
                }
            }

            // Write values
            _values.AsSpan().CopyTo(destination.Slice(17));
        }
    }
}
