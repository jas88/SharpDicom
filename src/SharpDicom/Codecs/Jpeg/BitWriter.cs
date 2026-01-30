using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Writes variable-length bit sequences to a JPEG bitstream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This ref struct provides efficient bit-level writing for JPEG Huffman encoding.
    /// It automatically handles JPEG byte stuffing: after writing 0xFF, it inserts 0x00
    /// to prevent accidental marker sequences.
    /// </para>
    /// <para>
    /// The implementation uses a 32-bit buffer for efficient multi-bit writes, flushing
    /// complete bytes to the output when the buffer is full.
    /// </para>
    /// </remarks>
    public ref struct BitWriter
    {
        private readonly Span<byte> _output;
        private int _bytePosition;
        private uint _buffer;      // Bit buffer holding accumulated bits
        private int _bitsInBuffer; // Number of valid bits in buffer (0-32)

        /// <summary>
        /// Initializes a new BitWriter for the given output buffer.
        /// </summary>
        /// <param name="output">The destination buffer for the JPEG bitstream.</param>
        public BitWriter(Span<byte> output)
        {
            _output = output;
            _bytePosition = 0;
            _buffer = 0;
            _bitsInBuffer = 0;
        }

        /// <summary>
        /// Gets the number of bytes written to the output buffer.
        /// </summary>
        public readonly int BytesWritten => _bytePosition;

        /// <summary>
        /// Gets the number of bits pending in the internal buffer (not yet written).
        /// </summary>
        public readonly int BitsInBuffer => _bitsInBuffer;

        /// <summary>
        /// Writes a single bit to the stream.
        /// </summary>
        /// <param name="bit">The bit value (0 or 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBit(int bit)
        {
            _buffer = (_buffer << 1) | ((uint)bit & 1);
            _bitsInBuffer++;

            if (_bitsInBuffer >= 8)
            {
                FlushByte();
            }
        }

        /// <summary>
        /// Writes the specified number of bits (1-25) to the stream.
        /// </summary>
        /// <param name="value">The value to write (only the lower <paramref name="count"/> bits are used).</param>
        /// <param name="count">Number of bits to write (1-25).</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is not in range 1-25.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBits(int value, int count)
        {
            if ((uint)count > 25)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Bit count must be between 1 and 25.");
            }

            // Mask the value to the specified number of bits
            uint maskedValue = (uint)value & ((1u << count) - 1);

            _buffer = (_buffer << count) | maskedValue;
            _bitsInBuffer += count;

            // Flush complete bytes
            while (_bitsInBuffer >= 8)
            {
                FlushByte();
            }
        }

        /// <summary>
        /// Writes a signed value using JPEG's DC difference encoding.
        /// </summary>
        /// <param name="value">The signed value to write.</param>
        /// <param name="magnitude">The number of magnitude bits (0-15).</param>
        /// <remarks>
        /// In JPEG, signed values are encoded with the MSB indicating sign:
        /// - Positive values are written as-is
        /// - Negative values are written as (value + 2^magnitude - 1)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSignedValue(int value, int magnitude)
        {
            if (magnitude == 0)
            {
                return;
            }

            int encodedValue;
            if (value < 0)
            {
                // Negative value: add (2^magnitude - 1) to get the encoded value
                encodedValue = value + (1 << magnitude) - 1;
            }
            else
            {
                encodedValue = value;
            }

            WriteBits(encodedValue, magnitude);
        }

        /// <summary>
        /// Flushes any remaining bits to the output, padding with 1s.
        /// </summary>
        /// <remarks>
        /// <para>
        /// JPEG requires that partial bytes at the end of entropy-coded data be
        /// padded with 1-bits. This ensures that 0xFF padding doesn't create
        /// false markers.
        /// </para>
        /// <para>
        /// After calling Flush, the BitWriter is reset and can continue writing,
        /// though this is typically only called at the end of a scan segment.
        /// </para>
        /// </remarks>
        public void Flush()
        {
            if (_bitsInBuffer > 0)
            {
                // Pad with 1s to complete the byte
                int padBits = 8 - _bitsInBuffer;
                _buffer = (_buffer << padBits) | ((1u << padBits) - 1);
                _bitsInBuffer = 8;
                FlushByte();
            }
        }

        /// <summary>
        /// Gets the span of bytes written so far.
        /// </summary>
        /// <returns>The written portion of the output buffer.</returns>
        public readonly ReadOnlySpan<byte> GetWrittenSpan()
        {
            return _output.Slice(0, _bytePosition);
        }

        /// <summary>
        /// Flushes one complete byte from the buffer to the output.
        /// Handles JPEG byte stuffing (inserts 0x00 after 0xFF).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushByte()
        {
            if (_bitsInBuffer < 8)
            {
                return;
            }

            _bitsInBuffer -= 8;
            byte outputByte = (byte)(_buffer >> _bitsInBuffer);

            // Check capacity
            if (_bytePosition >= _output.Length)
            {
                throw new InvalidOperationException("Output buffer is full.");
            }

            _output[_bytePosition++] = outputByte;

            // Handle byte stuffing: after writing 0xFF, insert 0x00
            if (outputByte == 0xFF)
            {
                if (_bytePosition >= _output.Length)
                {
                    throw new InvalidOperationException("Output buffer is full (byte stuffing).");
                }

                _output[_bytePosition++] = 0x00;
            }
        }

        /// <summary>
        /// Calculates the size category (number of bits) needed to represent a value.
        /// </summary>
        /// <param name="value">The value to categorize.</param>
        /// <returns>The number of bits needed (0-11 for JPEG AC, 0-15 for DC).</returns>
        /// <remarks>
        /// This is used to determine the "size" or "magnitude" category for
        /// Huffman encoding of DC differences and AC coefficients.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetMagnitudeCategory(int value)
        {
            if (value == 0)
            {
                return 0;
            }

            // Use absolute value for negative numbers
            if (value < 0)
            {
                value = -value;
            }

            // Count bits needed
            int category = 0;
            while (value > 0)
            {
                value >>= 1;
                category++;
            }

            return category;
        }
    }
}
