using System;
using System.Collections.Generic;
using System.IO;
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
    /// JPEG-LS bit-stuffing per ITU-T T.87 Section A.1: after writing a 0xFF byte,
    /// the next byte carries only 7 data bits with its MSB forced to 0 (the "stuff bit").
    /// No literal 0x00 byte is inserted — the reduced bit count naturally prevents
    /// ambiguity with JPEG markers.
    /// </para>
    /// </remarks>
    internal ref struct GolombRiceEncoder
    {
        private List<byte> _output;
        private uint _bitBuffer;   // Bits accumulated MSB-first at the top of the word
        private int _freeBitCount; // Free bits remaining in _bitBuffer (starts at 32)
        private bool _isFFWritten; // Whether last output byte was 0xFF

        /// <summary>
        /// Initializes a new Golomb-Rice encoder.
        /// </summary>
        /// <param name="output">The output byte list.</param>
        public GolombRiceEncoder(List<byte> output)
        {
            _output = output;
            _bitBuffer = 0;
            _freeBitCount = 32;
            _isFFWritten = false;
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
        /// Appends bits to the MSB-first bit buffer, flushing when full.
        /// Matches CharLS append_to_bit_stream() semantics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AppendBits(uint bits, int bitCount)
        {
            _freeBitCount -= bitCount;
            if (_freeBitCount >= 0)
            {
                _bitBuffer |= bits << _freeBitCount;
            }
            else
            {
                // Buffer overflow: add what fits, flush, then add the rest
                _bitBuffer |= bits >> (-_freeBitCount);
                DrainBuffer();

                if (_freeBitCount < 0)
                {
                    _bitBuffer |= bits >> (-_freeBitCount);
                    DrainBuffer();
                }

                _bitBuffer |= bits << _freeBitCount;
            }
        }

        /// <summary>
        /// Writes a single bit to the output stream with JPEG-LS bit-stuffing.
        /// </summary>
        /// <param name="bit">The bit value (0 or 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBit(int bit)
        {
            AppendBits((uint)(bit & 1), 1);
        }

        /// <summary>
        /// Drains complete bytes from the MSB end of the 32-bit buffer,
        /// applying JPEG-LS bit-stuffing (ITU-T T.87 A.1).
        /// After a 0xFF byte, the next byte extracts only 7 bits (MSB forced to 0).
        /// Matches CharLS flush() semantics.
        /// </summary>
        private void DrainBuffer()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_freeBitCount >= 32)
                {
                    _freeBitCount = 32;
                    break;
                }

                if (_isFFWritten)
                {
                    // After 0xFF: extract 7 bits from MSB, forcing output MSB to 0
                    byte b = (byte)(_bitBuffer >> 25);
                    _bitBuffer <<= 7;
                    _freeBitCount += 7;
                    _output.Add(b);
                    _isFFWritten = (b == 0xFF);
                }
                else
                {
                    // Normal: extract 8 bits from MSB
                    byte b = (byte)(_bitBuffer >> 24);
                    _bitBuffer <<= 8;
                    _freeBitCount += 8;
                    _output.Add(b);
                    _isFFWritten = (b == 0xFF);
                }
            }
        }

        /// <summary>
        /// Flushes remaining bits at end of scan.
        /// Matches CharLS end_scan(): flush, if last byte was 0xFF pad to fill
        /// the 7-bit post-FF byte, then flush again.
        /// </summary>
        public void Flush()
        {
            DrainBuffer();

            // If the last byte written was 0xFF, we must emit a properly stuffed byte.
            // Per CharLS end_scan(): append zero-fill bits so the 7-bit post-FF byte
            // gets fully emitted.
            if (_isFFWritten)
            {
                AppendBits(0, (_freeBitCount - 1) % 8);
            }

            DrainBuffer();
        }
    }

    /// <summary>
    /// Golomb-Rice decoder for JPEG-LS entropy decoding per ITU-T T.87 Section 4.5.
    /// </summary>
    /// <remarks>
    /// Decodes Golomb-Rice coded values from the bitstream, handling JPEG-LS bit-stuffing
    /// per ITU-T T.87 Section A.1: after a 0xFF byte, the next byte has only 7 valid data
    /// bits (MSB is a stuff bit forced to 0, which is discarded).
    /// </remarks>
    internal ref struct GolombRiceDecoder
    {
        private ReadOnlySpan<byte> _data;
        private int _pos;
        private int _validBits;
        private ulong _cache;

        /// <summary>
        /// Initializes a new Golomb-Rice decoder.
        /// </summary>
        /// <param name="data">The input data span.</param>
        public GolombRiceDecoder(ReadOnlySpan<byte> data)
        {
            _data = data;
            _pos = 0;
            _validBits = 0;
            _cache = 0;
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
        /// Maximum number of readable bits before needing to refill (64 - 8 = 56).
        /// </summary>
        private const int MaxReadableCacheBits = 56;

        /// <summary>
        /// Reads a single bit from the input stream with JPEG-LS bit-unstuffing.
        /// </summary>
        /// <returns>The bit value (0 or 1).</returns>
        /// <exception cref="InvalidDataException">Thrown when the input stream is exhausted prematurely.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            if (_validBits <= 0)
            {
                FillReadCache();
            }

            int bit = (int)((_cache >> 63) & 1);
            _cache <<= 1;
            _validBits--;
            return bit;
        }

        /// <summary>
        /// Fills the read cache from the input stream, handling JPEG-LS bit-unstuffing.
        /// After a 0xFF byte, valid_bits is decremented by 1 to discard the stuff bit
        /// (MSB of the following byte), per ITU-T T.87 Section A.1.
        /// </summary>
        private void FillReadCache()
        {
            while (_validBits < MaxReadableCacheBits)
            {
                if (_pos >= _data.Length)
                {
                    if (_validBits == 0)
                    {
                        throw new InvalidDataException("Truncated JPEG-LS stream");
                    }
                    return;
                }

                uint newByte = _data[_pos];

                // Marker detection: 0xFF followed by byte with MSB set = JPEG marker
                if (newByte == 0xFF &&
                    (_pos == _data.Length - 1 ||
                     (_data[_pos + 1] & 0x80) != 0))
                {
                    if (_validBits <= 0)
                    {
                        throw new InvalidDataException("Truncated JPEG-LS stream");
                    }
                    return;
                }

                // Place byte into MSB end of cache
                _cache |= (ulong)newByte << (MaxReadableCacheBits - _validBits);
                _validBits += 8;
                _pos++;

                // JPEG-LS bit-unstuffing: after a 0xFF byte, the stuff bit (MSB of next byte)
                // is discarded by counting only 7 valid bits instead of 8
                if (newByte == 0xFF)
                {
                    _validBits--;
                }
            }
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
