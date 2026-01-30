using System;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Represents a Huffman coding table for JPEG encoding/decoding.
    /// </summary>
    /// <remarks>
    /// Huffman tables define the variable-length codes used to compress JPEG data.
    /// Standard tables are defined in ITU-T T.81 Annex K (Tables K.3-K.6).
    /// </remarks>
    public sealed class HuffmanTable
    {
        // ITU-T T.81 Annex K Table K.3 - Luminance DC Huffman table
        private static readonly byte[] LuminanceDCBits =
            { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] LuminanceDCValues =
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        // ITU-T T.81 Annex K Table K.4 - Chrominance DC Huffman table
        private static readonly byte[] ChrominanceDCBits =
            { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
        private static readonly byte[] ChrominanceDCValues =
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        // ITU-T T.81 Annex K Table K.5 - Luminance AC Huffman table
        private static readonly byte[] LuminanceACBits =
            { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125 };
        private static readonly byte[] LuminanceACValues =
        {
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
            0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
            0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16,
            0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
            0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
            0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
            0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4,
            0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
            0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea,
            0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa
        };

        // ITU-T T.81 Annex K Table K.6 - Chrominance AC Huffman table
        private static readonly byte[] ChrominanceACBits =
            { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119 };
        private static readonly byte[] ChrominanceACValues =
        {
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
            0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
            0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
            0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
            0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34,
            0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
            0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96,
            0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
            0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
            0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
            0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2,
            0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
            0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
            0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa
        };

        /// <summary>
        /// Standard Huffman table for luminance DC coefficients (ITU-T T.81 Table K.3).
        /// </summary>
        public static readonly HuffmanTable LuminanceDC = new(LuminanceDCBits, LuminanceDCValues, false);

        /// <summary>
        /// Standard Huffman table for luminance AC coefficients (ITU-T T.81 Table K.5).
        /// </summary>
        public static readonly HuffmanTable LuminanceAC = new(LuminanceACBits, LuminanceACValues, true);

        /// <summary>
        /// Standard Huffman table for chrominance DC coefficients (ITU-T T.81 Table K.4).
        /// </summary>
        public static readonly HuffmanTable ChrominanceDC = new(ChrominanceDCBits, ChrominanceDCValues, false);

        /// <summary>
        /// Standard Huffman table for chrominance AC coefficients (ITU-T T.81 Table K.6).
        /// </summary>
        public static readonly HuffmanTable ChrominanceAC = new(ChrominanceACBits, ChrominanceACValues, true);

        // Decoding structures
        private readonly int[] _minCode;    // Minimum code value for each bit length (1-indexed)
        private readonly int[] _maxCode;    // Maximum code value for each bit length (1-indexed)
        private readonly int[] _valPtr;     // Index to first value for each bit length (1-indexed)
        private readonly byte[] _values;    // Symbol values

        // Encoding structures
        private readonly ushort[] _encodeCode;  // Huffman codes indexed by symbol
        private readonly byte[] _encodeSize;    // Code sizes indexed by symbol

        /// <summary>
        /// Gets a value indicating whether this is an AC table (vs DC table).
        /// </summary>
        public bool IsAC { get; }

        /// <summary>
        /// Gets the number of symbols in this table.
        /// </summary>
        public int SymbolCount => _values.Length;

        private HuffmanTable(byte[] bits, byte[] values, bool isAC)
        {
            IsAC = isAC;
            _values = (byte[])values.Clone();

            // Allocate decoding arrays (16 bit lengths max, 1-indexed)
            _minCode = new int[17];
            _maxCode = new int[17];
            _valPtr = new int[17];

            // Build decoding tables
            BuildDecodeTables(bits);

            // Build encoding tables (max 256 symbols)
            _encodeCode = new ushort[256];
            _encodeSize = new byte[256];
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
                    byte symbol = _values[valueIndex];
                    _encodeCode[symbol] = (ushort)code;
                    _encodeSize[symbol] = (byte)length;
                    code++;
                    valueIndex++;
                }
                code <<= 1;
            }
        }

        /// <summary>
        /// Decodes a single Huffman symbol from the bit reader.
        /// </summary>
        /// <param name="reader">The bit reader to read from.</param>
        /// <returns>The decoded symbol value, or -1 if decoding failed.</returns>
        public int DecodeSymbol(ref HuffmanBitReader reader)
        {
            int code = 0;

            for (int length = 1; length <= 16; length++)
            {
                if (!reader.TryReadBit(out int bit))
                {
                    return -1; // Not enough bits
                }

                code = (code << 1) | bit;

                if (_maxCode[length] >= 0 && code <= _maxCode[length])
                {
                    int index = _valPtr[length] + (code - _minCode[length]);
                    return _values[index];
                }
            }

            return -1; // Invalid code
        }

        /// <summary>
        /// Gets the Huffman code and size for encoding a symbol.
        /// </summary>
        /// <param name="symbol">The symbol to encode.</param>
        /// <returns>A tuple of (code, size) for the symbol.</returns>
        public (ushort Code, byte Size) GetCode(byte symbol)
        {
            return (_encodeCode[symbol], _encodeSize[symbol]);
        }

        /// <summary>
        /// Attempts to parse a Huffman table from a DHT segment.
        /// </summary>
        /// <param name="segment">The DHT segment payload (after marker and length).</param>
        /// <param name="tableClass">When successful, contains 0 for DC or 1 for AC.</param>
        /// <param name="tableId">When successful, contains the table identifier (0-3).</param>
        /// <param name="table">When successful, contains the parsed Huffman table.</param>
        /// <param name="bytesConsumed">When successful, contains the number of bytes consumed.</param>
        /// <returns>true if parsing was successful; otherwise, false.</returns>
        /// <remarks>
        /// DHT segment format (one or more tables):
        /// - 1 byte: Table class (high nibble) and table identifier (low nibble)
        /// - 16 bytes: Number of codes for each bit length (BITS)
        /// - N bytes: Symbol values (HUFFVAL), where N = sum of BITS
        /// </remarks>
        public static bool TryParseDHT(
            ReadOnlySpan<byte> segment,
            out byte tableClass,
            out byte tableId,
            out HuffmanTable? table,
            out int bytesConsumed)
        {
            table = null;
            tableClass = 0;
            tableId = 0;
            bytesConsumed = 0;

            if (segment.Length < 17) // 1 byte header + 16 bytes BITS
            {
                return false;
            }

            byte header = segment[0];
            tableClass = (byte)(header >> 4);
            tableId = (byte)(header & 0x0F);

            if (tableClass > 1 || tableId > 3)
            {
                return false; // Invalid class or ID
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
                return false; // Not enough data for values
            }

            // Read HUFFVAL array
            byte[] values = new byte[totalValues];
            segment.Slice(17, totalValues).CopyTo(values);

            table = new HuffmanTable(bits, values, tableClass == 1);
            bytesConsumed = 17 + totalValues;
            return true;
        }
    }

    /// <summary>
    /// Bit reader for Huffman decoding with JPEG byte-stuffing handling.
    /// </summary>
    /// <remarks>
    /// JPEG uses byte-stuffing: 0xFF followed by 0x00 represents the byte 0xFF in the data stream.
    /// Any 0xFF followed by a non-zero byte is a marker (except RST markers which are skipped).
    /// </remarks>
    public ref struct HuffmanBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bytePosition;
        private int _bitPosition;
        private int _currentByte;
        private bool _hasMarker;
        private byte _markerCode;

        /// <summary>
        /// Gets a value indicating whether a marker was encountered.
        /// </summary>
        public bool HasMarker => _hasMarker;

        /// <summary>
        /// Gets the marker code if a marker was encountered.
        /// </summary>
        public byte MarkerCode => _markerCode;

        /// <summary>
        /// Gets the current byte position in the data.
        /// </summary>
        public int Position => _bytePosition;

        /// <summary>
        /// Initializes a new instance of the <see cref="HuffmanBitReader"/> struct.
        /// </summary>
        /// <param name="data">The compressed data to read from.</param>
        public HuffmanBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bytePosition = 0;
            _bitPosition = 0;
            _currentByte = 0;
            _hasMarker = false;
            _markerCode = 0;

            // Prime the first byte
            if (data.Length > 0)
            {
                FetchByte();
            }
        }

        private void FetchByte()
        {
            if (_bytePosition >= _data.Length)
            {
                return;
            }

            _currentByte = _data[_bytePosition++];

            // Handle byte-stuffing
            if (_currentByte == 0xFF && _bytePosition < _data.Length)
            {
                byte next = _data[_bytePosition];
                if (next == 0x00)
                {
                    // Byte-stuffed 0xFF
                    _bytePosition++;
                }
                else if (!JpegMarkers.IsRST(next))
                {
                    // Found a marker
                    _hasMarker = true;
                    _markerCode = next;
                    _bytePosition++;
                }
                // RST markers are skipped (entropy-coded data continues)
                else
                {
                    _bytePosition++;
                    FetchByte(); // Skip RST and continue
                }
            }

            _bitPosition = 8;
        }

        /// <summary>
        /// Attempts to read a single bit from the stream.
        /// </summary>
        /// <param name="bit">When successful, contains the bit value (0 or 1).</param>
        /// <returns>true if a bit was read; otherwise, false.</returns>
        public bool TryReadBit(out int bit)
        {
            if (_hasMarker)
            {
                bit = 0;
                return false;
            }

            if (_bitPosition == 0)
            {
                FetchByte();
                if (_bytePosition > _data.Length || _hasMarker)
                {
                    bit = 0;
                    return false;
                }
            }

            _bitPosition--;
            bit = (_currentByte >> _bitPosition) & 1;
            return true;
        }

        /// <summary>
        /// Attempts to read multiple bits from the stream.
        /// </summary>
        /// <param name="count">The number of bits to read (1-16).</param>
        /// <param name="value">When successful, contains the bits as an integer.</param>
        /// <returns>true if all bits were read; otherwise, false.</returns>
        public bool TryReadBits(int count, out int value)
        {
            value = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryReadBit(out int bit))
                {
                    return false;
                }
                value = (value << 1) | bit;
            }
            return true;
        }

        /// <summary>
        /// Reads additional bits and extends the sign for a DC/AC coefficient.
        /// </summary>
        /// <param name="category">The category (number of additional bits).</param>
        /// <param name="value">When successful, contains the signed coefficient value.</param>
        /// <returns>true if the value was read; otherwise, false.</returns>
        /// <remarks>
        /// JPEG uses a sign extension scheme where:
        /// - Category 0: value is 0
        /// - Category N: read N bits, if MSB is 0 then value is negative
        /// </remarks>
        public bool TryReadCoefficient(int category, out int value)
        {
            if (category == 0)
            {
                value = 0;
                return true;
            }

            if (!TryReadBits(category, out value))
            {
                return false;
            }

            // Sign extension: if MSB is 0, value is negative
            int threshold = 1 << (category - 1);
            if (value < threshold)
            {
                value = value - (1 << category) + 1;
            }

            return true;
        }
    }
}
