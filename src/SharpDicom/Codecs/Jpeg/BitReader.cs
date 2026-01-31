using System;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Reads variable-length bit sequences from a JPEG bitstream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This ref struct provides efficient bit-level reading for JPEG Huffman decoding.
    /// It handles JPEG byte stuffing where 0xFF bytes are followed by 0x00 (which is skipped).
    /// Marker bytes (0xFF followed by non-zero) indicate end of scan data.
    /// </para>
    /// <para>
    /// The implementation uses a 32-bit buffer for efficient multi-bit reads, refilling
    /// from the byte stream when needed.
    /// </para>
    /// </remarks>
    public ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bytePosition;
        private uint _buffer;      // Bit buffer holding up to 32 bits
        private int _bitsInBuffer; // Number of valid bits in buffer (0-32)
        private bool _markerFound; // True if we hit a marker (0xFF followed by non-zero)

        /// <summary>
        /// Initializes a new BitReader for the given data.
        /// </summary>
        /// <param name="data">The JPEG bitstream data to read from.</param>
        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bytePosition = 0;
            _buffer = 0;
            _bitsInBuffer = 0;
            _markerFound = false;

            // Pre-fill the buffer
            Refill();
        }

        /// <summary>
        /// Gets the current byte position in the stream.
        /// </summary>
        public readonly int BytePosition => _bytePosition;

        /// <summary>
        /// Gets the number of bits remaining in the internal buffer.
        /// </summary>
        public readonly int BitsInBuffer => _bitsInBuffer;

        /// <summary>
        /// Returns true if there are no more bits available.
        /// </summary>
        public readonly bool IsEmpty => _bitsInBuffer == 0 && (_bytePosition >= _data.Length || _markerFound);

        /// <summary>
        /// Returns true if a marker byte was encountered (end of scan data).
        /// </summary>
        public readonly bool MarkerFound => _markerFound;

        /// <summary>
        /// Reads a single bit from the stream.
        /// </summary>
        /// <returns>0 or 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBit()
        {
            if (_bitsInBuffer == 0)
            {
                Refill();
            }

            if (_bitsInBuffer == 0)
            {
                throw new InvalidOperationException("No more bits available to read.");
            }

            _bitsInBuffer--;
            return (int)((_buffer >> _bitsInBuffer) & 1);
        }

        /// <summary>
        /// Reads the specified number of bits (1-25) from the stream.
        /// </summary>
        /// <param name="count">Number of bits to read (1-25).</param>
        /// <returns>The value read as an unsigned integer.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is not in range 1-25.</exception>
        /// <exception cref="InvalidOperationException">Thrown if not enough bits are available.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadBits(int count)
        {
            if ((uint)count > 25)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Bit count must be between 1 and 25.");
            }

            // Ensure we have enough bits
            while (_bitsInBuffer < count && !_markerFound && _bytePosition < _data.Length)
            {
                Refill();
            }

            if (_bitsInBuffer < count)
            {
                throw new InvalidOperationException($"Not enough bits available. Requested {count}, have {_bitsInBuffer}.");
            }

            _bitsInBuffer -= count;
            return (int)((_buffer >> _bitsInBuffer) & ((1u << count) - 1));
        }

        /// <summary>
        /// Peeks at the next bits without advancing the position.
        /// </summary>
        /// <param name="count">Number of bits to peek (1-25).</param>
        /// <returns>The value peeked as an unsigned integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PeekBits(int count)
        {
            if ((uint)count > 25)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Bit count must be between 1 and 25.");
            }

            // Ensure we have enough bits
            while (_bitsInBuffer < count && !_markerFound && _bytePosition < _data.Length)
            {
                Refill();
            }

            if (_bitsInBuffer < count)
            {
                // Return what we have, padded with zeros on the right
                // Mask after left shift to get correct count bits
                return (int)((_buffer << (count - _bitsInBuffer)) & ((1u << count) - 1));
            }

            return (int)((_buffer >> (_bitsInBuffer - count)) & ((1u << count) - 1));
        }

        /// <summary>
        /// Skips the specified number of bits.
        /// </summary>
        /// <param name="count">Number of bits to skip.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SkipBits(int count)
        {
            while (count > 0)
            {
                if (_bitsInBuffer == 0)
                {
                    Refill();
                    if (_bitsInBuffer == 0) return; // No more data
                }

                int skip = Math.Min(count, _bitsInBuffer);
                _bitsInBuffer -= skip;
                count -= skip;
            }
        }

        /// <summary>
        /// Aligns to the next byte boundary by discarding any remaining bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AlignToByte()
        {
            // Discard bits to align to byte boundary
            int bitsToDiscard = _bitsInBuffer % 8;
            _bitsInBuffer -= bitsToDiscard;
        }

        /// <summary>
        /// Gets the remaining data after the current bit position.
        /// Useful for extracting marker data after scan.
        /// </summary>
        /// <returns>The remaining bytes in the stream.</returns>
        public readonly ReadOnlySpan<byte> GetRemainingData()
        {
            if (_bytePosition >= _data.Length)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            // Account for buffered bytes
            int adjustedPosition = _bytePosition - (_bitsInBuffer / 8);
            return _data.Slice(Math.Max(0, adjustedPosition));
        }

        /// <summary>
        /// Refills the internal bit buffer from the byte stream.
        /// Handles JPEG byte stuffing (0xFF 0x00 sequences).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Refill()
        {
            // Fill the buffer with as many bytes as possible while keeping at least 8 bits free
            while (_bitsInBuffer <= 24 && _bytePosition < _data.Length && !_markerFound)
            {
                byte nextByte = _data[_bytePosition++];

                if (nextByte == 0xFF)
                {
                    // Check for byte stuffing
                    if (_bytePosition < _data.Length)
                    {
                        byte stuffByte = _data[_bytePosition];
                        if (stuffByte == 0x00)
                        {
                            // Byte stuffing: skip the 0x00 and use 0xFF
                            _bytePosition++;
                            _buffer = (_buffer << 8) | 0xFF;
                            _bitsInBuffer += 8;
                        }
                        else
                        {
                            // Marker found - stop reading
                            // Back up to leave the 0xFF in the stream
                            _bytePosition--;
                            _markerFound = true;
                            return;
                        }
                    }
                    else
                    {
                        // 0xFF at end of stream - treat as marker
                        _bytePosition--;
                        _markerFound = true;
                        return;
                    }
                }
                else
                {
                    // Normal byte
                    _buffer = (_buffer << 8) | nextByte;
                    _bitsInBuffer += 8;
                }
            }
        }

        /// <summary>
        /// Reads a signed value using JPEG's DC difference encoding.
        /// </summary>
        /// <param name="magnitude">The number of magnitude bits (0-15).</param>
        /// <returns>The signed value.</returns>
        /// <remarks>
        /// In JPEG, signed values are encoded with the MSB indicating sign:
        /// - If MSB is 1, the value is positive and equals the raw bits
        /// - If MSB is 0, the value is negative and equals raw bits minus (2^magnitude - 1)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSignedValue(int magnitude)
        {
            if (magnitude == 0)
            {
                return 0;
            }

            int value = ReadBits(magnitude);

            // If the MSB is 0, the value is negative
            // JPEG uses a signed magnitude representation
            if (value < (1 << (magnitude - 1)))
            {
                // Negative value
                value -= (1 << magnitude) - 1;
            }

            return value;
        }
    }
}
