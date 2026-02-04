using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// Golomb-Rice encoder for JPEG-LS entropy coding per ITU-T T.87 Section 4.5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Golomb-Rice coding encodes non-negative integers efficiently when they follow
    /// a geometric distribution. Each value is split into:
    /// - Unary quotient: value &gt;&gt; k (written as k zeros followed by 1)
    /// - Binary remainder: value &amp; ((1 &lt;&lt; k) - 1) (k bits)
    /// </para>
    /// <para>
    /// JPEG-LS uses bit-stuffing: after writing 0xFF byte, insert 0x00 byte to
    /// distinguish from JPEG markers (which start with 0xFF).
    /// </para>
    /// </remarks>
    internal ref struct GolombRiceEncoder
    {
        private List<byte> _output;
        private uint _buffer;
        private int _bitCount;

        /// <summary>
        /// Initializes a new Golomb-Rice encoder.
        /// </summary>
        /// <param name="output">The output byte list.</param>
        public GolombRiceEncoder(List<byte> output)
        {
            _output = output;
            _buffer = 0;
            _bitCount = 0;
        }

        /// <summary>
        /// Bits used for limit escape encoding (typically log2(range) + 1).
        /// For 16-bit: qbpp = 16.
        /// </summary>
        private int _qbpp = 16;

        /// <summary>
        /// Limit value for quotient (LIMIT - qbpp - 1 per ITU-T T.87).
        /// </summary>
        private int _limitMinusQbpp = 32 - 16 - 1;  // = 15 for 16-bit

        /// <summary>
        /// Sets the bits per pixel for limit escape encoding.
        /// </summary>
        public void SetBitsPerPixel(int bpp)
        {
            _qbpp = bpp;
            // LIMIT is 32, so LIMIT - qbpp - 1 = 31 - qbpp
            _limitMinusQbpp = 31 - bpp;
            if (_limitMinusQbpp < 0) _limitMinusQbpp = 0;
        }

        /// <summary>
        /// Encodes a mapped error value using Golomb-Rice coding.
        /// </summary>
        /// <param name="value">The mapped error value (non-negative).</param>
        /// <param name="k">The Golomb-Rice parameter.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteGolombRice(int value, int k)
        {
            // Split value into quotient and remainder
            int quotient = value >> k;
            int remainder = value & ((1 << k) - 1);

            // Check if we need limit escape per ITU-T T.87 Section A.5.3
            // Escape when quotient >= LIMIT - qbpp - 1
            if (quotient >= _limitMinusQbpp)
            {
                // Write (LIMIT - qbpp - 1) zeros followed by 1
                for (int i = 0; i < _limitMinusQbpp; i++)
                {
                    WriteBit(0);
                }
                WriteBit(1);

                // Write (qbpp + 1) bits: the value with offset
                // Per ITU-T T.87: write (value - 1) using (qbpp + 1) bits
                int escapedValue = value - 1;
                for (int i = _qbpp; i >= 0; i--)
                {
                    WriteBit((escapedValue >> i) & 1);
                }
            }
            else
            {
                // Write unary quotient (quotient zeros followed by 1)
                for (int i = 0; i < quotient; i++)
                {
                    WriteBit(0);
                }
                WriteBit(1);

                // Write k-bit binary remainder (MSB first)
                for (int i = k - 1; i >= 0; i--)
                {
                    WriteBit((remainder >> i) & 1);
                }
            }
        }

        /// <summary>
        /// Writes a single bit to the output stream.
        /// </summary>
        /// <param name="bit">The bit value (0 or 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBit(int bit)
        {
            // Accumulate bit into buffer
            _buffer = (_buffer << 1) | (uint)(bit & 1);
            _bitCount++;

            // Flush when buffer full
            if (_bitCount == 8)
            {
                byte b = (byte)_buffer;
                _output.Add(b);

                // JPEG bit-stuffing: insert 0x00 after 0xFF
                if (b == 0xFF)
                {
                    _output.Add(0x00);
                }

                _buffer = 0;
                _bitCount = 0;
            }
        }

        /// <summary>
        /// Flushes any remaining bits to the output stream.
        /// </summary>
        public void Flush()
        {
            if (_bitCount > 0)
            {
                // Pad with zeros to complete the byte
                _buffer <<= (8 - _bitCount);
                byte b = (byte)_buffer;
                _output.Add(b);

                // Apply bit-stuffing to final byte
                if (b == 0xFF)
                {
                    _output.Add(0x00);
                }

                _buffer = 0;
                _bitCount = 0;
            }
        }
    }

    /// <summary>
    /// Golomb-Rice decoder for JPEG-LS entropy decoding per ITU-T T.87 Section 4.5.
    /// </summary>
    /// <remarks>
    /// Decodes Golomb-Rice coded values from the bitstream, handling JPEG bit-stuffing
    /// (0x00 bytes after 0xFF are skipped).
    /// </remarks>
    internal ref struct GolombRiceDecoder
    {
        private ReadOnlySpan<byte> _data;
        private int _pos;
        private int _bitPos;
        private uint _buffer;

        /// <summary>
        /// Initializes a new Golomb-Rice decoder.
        /// </summary>
        /// <param name="data">The input data span.</param>
        public GolombRiceDecoder(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
            _bitPos = 0;
            _buffer = 0;
        }

        /// <summary>
        /// Bits used for limit escape encoding (typically log2(range) + 1).
        /// For 16-bit: qbpp = 16.
        /// </summary>
        private int _qbpp = 16;

        /// <summary>
        /// Limit value for quotient (LIMIT - qbpp - 1 per ITU-T T.87).
        /// </summary>
        private int _limitMinusQbpp = 32 - 16 - 1;  // = 15 for 16-bit

        /// <summary>
        /// Sets the bits per pixel for limit escape encoding.
        /// </summary>
        public void SetBitsPerPixel(int bpp)
        {
            _qbpp = bpp;
            // LIMIT is 32, so LIMIT - qbpp - 1 = 31 - qbpp
            _limitMinusQbpp = 31 - bpp;
            if (_limitMinusQbpp < 0) _limitMinusQbpp = 0;
        }

        /// <summary>
        /// Decodes a Golomb-Rice encoded value.
        /// </summary>
        /// <param name="k">The Golomb-Rice parameter.</param>
        /// <returns>The decoded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadGolombRice(int k)
        {
            // Read unary quotient (count zeros until 1)
            int quotient = 0;
            while (ReadBit() == 0)
            {
                quotient++;
            }

            // Check for limit escape per ITU-T T.87 Section A.5.3
            // Escape sequence: (LIMIT - qbpp - 1) zeros followed by 1
            if (quotient >= _limitMinusQbpp)
            {
                // Read (qbpp + 1) bits for the value
                int escapedValue = 0;
                for (int i = 0; i <= _qbpp; i++)
                {
                    escapedValue = (escapedValue << 1) | ReadBit();
                }
                // Per ITU-T T.87: value = escapedValue + 1
                return escapedValue + 1;
            }

            // Read k-bit binary remainder
            int remainder = 0;
            for (int i = 0; i < k; i++)
            {
                remainder = (remainder << 1) | ReadBit();
            }

            // Reconstruct value
            return (quotient << k) | remainder;
        }

        /// <summary>
        /// Reads a single bit from the input stream.
        /// </summary>
        /// <returns>The bit value (0 or 1).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            // Refill buffer when empty
            if (_bitPos == 0)
            {
                if (_pos >= _data.Length)
                {
                    return 0; // End of stream
                }

                _buffer = _data[_pos++];

                // JPEG bit-unstuffing: skip 0x00 after 0xFF
                if (_buffer == 0xFF && _pos < _data.Length && _data[_pos] == 0x00)
                {
                    _pos++;
                }

                _bitPos = 8;
            }

            // Extract bit from buffer
            _bitPos--;
            return (int)((_buffer >> _bitPos) & 1);
        }

        /// <summary>
        /// Gets the current byte position in the input stream.
        /// </summary>
        public int Position => _pos;
    }

    /// <summary>
    /// Error mapping functions for JPEG-LS per ITU-T T.87 Section 4.5.
    /// </summary>
    /// <remarks>
    /// Maps signed prediction errors to non-negative values for Golomb-Rice coding:
    /// - Even values: positive errors (0 → 0, 1 → 2, 2 → 4, ...)
    /// - Odd values: negative errors (-1 → 1, -2 → 3, -3 → 5, ...)
    /// </remarks>
    internal static class ErrorMapping
    {
        /// <summary>
        /// Maps a signed error to a non-negative value for encoding.
        /// </summary>
        public static int MapError(int error)
        {
            if (error >= 0)
            {
                return error << 1;
            }
            else
            {
                return ((-error) << 1) - 1;
            }
        }

        /// <summary>
        /// Unmaps a non-negative value to a signed error for decoding.
        /// </summary>
        public static int UnmapError(int mappedError)
        {
            if ((mappedError & 1) == 0)
            {
                // Even: positive error
                return mappedError >> 1;
            }
            else
            {
                // Odd: negative error
                return -((mappedError + 1) >> 1);
            }
        }
    }
}
