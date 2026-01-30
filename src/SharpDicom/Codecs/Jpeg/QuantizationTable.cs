using System;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Represents a JPEG quantization table for DCT coefficient quantization.
    /// </summary>
    /// <remarks>
    /// Quantization tables define the divisors used to quantize DCT coefficients.
    /// Standard tables are defined in ITU-T T.81 Annex K (Tables K.1-K.2).
    /// The zigzag scan order is used to serialize the 8x8 block.
    /// </remarks>
    public sealed class QuantizationTable
    {
        // ITU-T T.81 Annex K Table K.1 - Luminance quantization table (quality 50)
        private static readonly int[] DefaultLuminanceValues =
        {
            16, 11, 10, 16,  24,  40,  51,  61,
            12, 12, 14, 19,  26,  58,  60,  55,
            14, 13, 16, 24,  40,  57,  69,  56,
            14, 17, 22, 29,  51,  87,  80,  62,
            18, 22, 37, 56,  68, 109, 103,  77,
            24, 35, 55, 64,  81, 104, 113,  92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103,  99
        };

        // ITU-T T.81 Annex K Table K.2 - Chrominance quantization table (quality 50)
        private static readonly int[] DefaultChrominanceValues =
        {
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99
        };

        /// <summary>
        /// Zigzag scan order for serializing 8x8 blocks.
        /// Maps sequential index (0-63) to 8x8 block position.
        /// </summary>
        /// <remarks>
        /// The zigzag pattern starts at the top-left corner and zigzags to the
        /// bottom-right, grouping coefficients by frequency (low to high).
        /// </remarks>
        public static readonly int[] ZigZagOrder =
        {
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63
        };

        /// <summary>
        /// Inverse zigzag scan order for deserializing 8x8 blocks.
        /// Maps 8x8 block position to sequential index (0-63).
        /// </summary>
        public static readonly int[] InverseZigZagOrder;

        /// <summary>
        /// Standard quantization table for luminance (Y) component (ITU-T T.81 Table K.1).
        /// </summary>
        public static readonly QuantizationTable LuminanceDefault;

        /// <summary>
        /// Standard quantization table for chrominance (Cb/Cr) components (ITU-T T.81 Table K.2).
        /// </summary>
        public static readonly QuantizationTable ChrominanceDefault;

        static QuantizationTable()
        {
            // Build inverse zigzag table
            InverseZigZagOrder = new int[64];
            for (int i = 0; i < 64; i++)
            {
                InverseZigZagOrder[ZigZagOrder[i]] = i;
            }

            // Initialize default tables
            LuminanceDefault = new QuantizationTable(DefaultLuminanceValues, 8, 0);
            ChrominanceDefault = new QuantizationTable(DefaultChrominanceValues, 8, 1);
        }

        private readonly int[] _table;  // 64 quantization values in zigzag order

        /// <summary>
        /// Gets the precision of the table values (8 or 16 bits).
        /// </summary>
        public byte Precision { get; }

        /// <summary>
        /// Gets the table identifier (0-3).
        /// </summary>
        public byte TableId { get; }

        /// <summary>
        /// Gets the quantization value at the specified zigzag index.
        /// </summary>
        /// <param name="index">The zigzag-order index (0-63).</param>
        /// <returns>The quantization divisor for that coefficient.</returns>
        public int this[int index] => _table[index];

        private QuantizationTable(int[] values, byte precision, byte tableId)
        {
            if (values.Length != 64)
            {
                throw new ArgumentException("Quantization table must have exactly 64 values.", nameof(values));
            }

            _table = (int[])values.Clone();
            Precision = precision;
            TableId = tableId;
        }

        /// <summary>
        /// Gets the quantization table values as a span.
        /// </summary>
        /// <returns>A read-only span of the 64 quantization values in zigzag order.</returns>
        public ReadOnlySpan<int> GetValues() => _table.AsSpan();

        /// <summary>
        /// Quantizes a DCT coefficient by dividing and rounding.
        /// </summary>
        /// <param name="coefficient">The DCT coefficient value.</param>
        /// <param name="index">The zigzag index of the coefficient (0-63).</param>
        /// <returns>The quantized coefficient.</returns>
        public int Quantize(int coefficient, int index)
        {
            int divisor = _table[index];
            // Round to nearest (per JPEG spec: add divisor/2 before integer division)
            if (coefficient >= 0)
            {
                return (coefficient + (divisor >> 1)) / divisor;
            }
            else
            {
                return (coefficient - (divisor >> 1)) / divisor;
            }
        }

        /// <summary>
        /// Dequantizes a coefficient by multiplying with the table value.
        /// </summary>
        /// <param name="quantizedCoefficient">The quantized coefficient value.</param>
        /// <param name="index">The zigzag index of the coefficient (0-63).</param>
        /// <returns>The dequantized coefficient.</returns>
        public int Dequantize(int quantizedCoefficient, int index)
        {
            return quantizedCoefficient * _table[index];
        }

        /// <summary>
        /// Attempts to parse a quantization table from a DQT segment.
        /// </summary>
        /// <param name="segment">The DQT segment payload (after marker and length).</param>
        /// <param name="table">When successful, contains the parsed quantization table.</param>
        /// <param name="bytesConsumed">When successful, contains the number of bytes consumed.</param>
        /// <returns>true if parsing was successful; otherwise, false.</returns>
        /// <remarks>
        /// DQT segment format (one or more tables):
        /// - 1 byte: Precision (high nibble, 0=8-bit, 1=16-bit) and table ID (low nibble)
        /// - 64 bytes (8-bit) or 128 bytes (16-bit): Quantization values in zigzag order
        /// </remarks>
        public static bool TryParseDQT(
            ReadOnlySpan<byte> segment,
            out QuantizationTable? table,
            out int bytesConsumed)
        {
            table = null;
            bytesConsumed = 0;

            if (segment.Length < 1)
            {
                return false;
            }

            byte header = segment[0];
            byte precision = (byte)(header >> 4);
            byte tableId = (byte)(header & 0x0F);

            if (precision > 1 || tableId > 3)
            {
                return false;
            }

            int valueCount = 64;
            int valueSize = precision == 0 ? 1 : 2;
            int expectedLength = 1 + (valueCount * valueSize);

            if (segment.Length < expectedLength)
            {
                return false;
            }

            int[] values = new int[64];
            int offset = 1;

            for (int i = 0; i < 64; i++)
            {
                if (precision == 0)
                {
                    values[i] = segment[offset++];
                }
                else
                {
                    values[i] = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(offset, 2));
                    offset += 2;
                }
            }

            table = new QuantizationTable(values, (byte)(precision == 0 ? 8 : 16), tableId);
            bytesConsumed = expectedLength;
            return true;
        }

        /// <summary>
        /// Creates a scaled quantization table based on quality factor.
        /// </summary>
        /// <param name="baseTable">The base quantization table (e.g., LuminanceDefault).</param>
        /// <param name="quality">Quality factor (1-100, where 50 = base table, 100 = minimum quantization).</param>
        /// <param name="tableId">The table identifier (0-3).</param>
        /// <returns>A new quantization table scaled for the specified quality.</returns>
        /// <remarks>
        /// The scaling formula follows the IJG (Independent JPEG Group) convention:
        /// - quality = 50: use base table unchanged
        /// - quality &lt; 50: scale up (more quantization, lower quality)
        /// - quality &gt; 50: scale down (less quantization, higher quality)
        /// </remarks>
        public static QuantizationTable CreateScaled(QuantizationTable baseTable, int quality, byte tableId)
        {
            // Clamp quality to valid range
            quality = Math.Max(1, Math.Min(100, quality));

            // Calculate scale factor (IJG formula)
            int scale;
            if (quality < 50)
            {
                scale = 5000 / quality;
            }
            else
            {
                scale = 200 - (quality * 2);
            }

            int[] values = new int[64];
            var baseValues = baseTable.GetValues();

            // Preserve base table precision and use appropriate max value
            byte precision = baseTable.Precision;
            int maxValue = precision == 16 ? 65535 : 255;

            for (int i = 0; i < 64; i++)
            {
                // Scale and clamp to valid range for the precision
                // Use long arithmetic to avoid overflow with extreme scale values
                long scaled = ((long)baseValues[i] * scale + 50) / 100;
                values[i] = (int)Math.Max(1, Math.Min(maxValue, scaled));
            }

            return new QuantizationTable(values, precision, tableId);
        }
    }
}
