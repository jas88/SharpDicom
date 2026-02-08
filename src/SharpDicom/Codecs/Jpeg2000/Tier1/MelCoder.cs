using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// MEL (Modular Embedded Lossless) run-length coder for HT block coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MEL coder is an adaptive run-length coder with 13 states, derived from
    /// the LOCO-I algorithm used in JPEG-LS. It encodes runs of insignificant (all-zero)
    /// quads in the HT cleanup pass.
    /// </para>
    /// <para>
    /// State machine: Each state has a run-length exponent MelE[state], giving a run length
    /// of 2^MelE[state]. When a run completes (all quads in the run are insignificant),
    /// a 0-bit is emitted and the state transitions up (longer runs expected). When a run
    /// breaks (a significant quad appears before the run completes), a 1-bit is emitted
    /// and the state transitions down (shorter runs expected).
    /// </para>
    /// <para>
    /// The MEL stream grows backward from the end of the cleanup codeword segment.
    /// </para>
    /// </remarks>
    internal static class MelCoder
    {
        /// <summary>
        /// Run-length exponents for each of the 13 MEL states.
        /// Run length at state s = 2^MelE[s].
        /// </summary>
        /// <remarks>
        /// From ITU-T T.814 / OpenJPH ojph_block_common.cpp (BSD-2-Clause).
        /// State 0-2: exponent 0 (run length 1)
        /// State 3-5: exponent 1 (run length 2)
        /// State 6-8: exponent 2 (run length 4)
        /// State 9-10: exponent 3 (run length 8)
        /// State 11: exponent 4 (run length 16)
        /// State 12: exponent 5 (run length 32)
        /// </remarks>
        internal static ReadOnlySpan<int> MelE => new int[]
        {
            0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5
        };

        /// <summary>
        /// Number of MEL states.
        /// </summary>
        internal const int NumStates = 13;

        /// <summary>
        /// Maximum state index.
        /// </summary>
        internal const int MaxState = 12;
    }

    /// <summary>
    /// MEL run-length decoder for reading from a backward-growing MEL stream.
    /// </summary>
    /// <remarks>
    /// The MEL decoder reads bits from the MEL stream (which grows backward from
    /// the end of the cleanup segment) and produces a sequence of significance
    /// decisions for quads.
    /// </remarks>
    internal ref struct MelDecoder
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;            // Current byte position (reads backward)
        private uint _bitBuffer;     // Bit buffer for reading
        private int _bitsAvailable;  // Number of valid bits in buffer
        private int _run;            // Remaining run count
        private int _state;          // Current state (0-12)
        private bool _significantPending; // Whether a significant quad follows the current partial run
        private bool _prevWasFF;     // Whether the previously read byte was 0xFF (for bit-stuffing)

        /// <summary>
        /// Initializes a new MEL decoder.
        /// </summary>
        /// <param name="data">The MEL byte stream (backward-growing portion of cleanup segment).</param>
        /// <param name="endPos">Byte position of the last MEL byte (reads backward from here).</param>
        public MelDecoder(ReadOnlySpan<byte> data, int endPos)
        {
            _data = data;
            _pos = endPos;
            _bitBuffer = 0;
            _bitsAvailable = 0;
            _run = 0;
            _state = 0;
            _significantPending = false;
            _prevWasFF = false;
        }

        /// <summary>
        /// Gets the current MEL state (0-12).
        /// </summary>
        public readonly int State => _state;

        /// <summary>
        /// Gets the current run count remaining.
        /// </summary>
        public readonly int Run => _run;

        /// <summary>
        /// Decodes whether the next quad is significant (has any non-zero samples).
        /// </summary>
        /// <returns>
        /// <c>true</c> if the quad is significant (run broken);
        /// <c>false</c> if the quad is insignificant (part of a run).
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DecodeQuadSignificance()
        {
            if (_run > 0)
            {
                _run--;
                if (_run == 0 && _significantPending)
                {
                    // The partial run has ended; the next quad is significant
                    _significantPending = false;
                    _state = Math.Max(_state - 1, 0);
                    return true;
                }
                return false; // insignificant (part of run)
            }

            // Read one bit from MEL stream
            int bit = ReadBit();
            if (bit == 0)
            {
                // Full run: 2^MelE[state] insignificant quads follow
                int runLength = 1 << MelCoder.MelE[_state];
                _run = runLength - 1; // this quad counts as first
                _state = Math.Min(_state + 1, MelCoder.MaxState); // transition up
                return false; // this quad is insignificant
            }
            else
            {
                // Run broken: read MelE[state] bits for partial run count
                int numBits = MelCoder.MelE[_state];
                if (numBits > 0)
                {
                    int partialRun = ReadBits(numBits);
                    if (partialRun > 0)
                    {
                        // There are some insignificant quads before the significant one
                        _run = partialRun; // includes the significant quad at the end
                        _significantPending = true;
                        return false; // this quad is insignificant (first of partial run)
                    }
                }
                // No partial run: this quad is directly significant
                _state = Math.Max(_state - 1, 0);
                return true;
            }
        }

        /// <summary>
        /// Reads a single bit from the MEL stream (backward reading).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadBit()
        {
            if (_bitsAvailable == 0)
            {
                FillBitBuffer();
            }

            _bitsAvailable--;
            int bit = (int)((_bitBuffer >> _bitsAvailable) & 1);
            return bit;
        }

        /// <summary>
        /// Reads multiple bits from the MEL stream (MSB-first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadBits(int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
            {
                result = (result << 1) | ReadBit();
            }
            return result;
        }

        /// <summary>
        /// Fills the bit buffer by reading the next byte backward.
        /// </summary>
        /// <remarks>
        /// MEL bytes are read backward from the end of the segment.
        /// Per T.814 section F.4, bit-stuffing applies to the MEL stream: after a 0xFF
        /// byte, the next byte contributes only 7 valid bits.
        /// </remarks>
        private void FillBitBuffer()
        {
            if (_pos <= 0)
            {
                _bitBuffer = 0;
                _bitsAvailable = 8;
                return;
            }

            _pos--;
            byte b = _data[_pos];
            _bitBuffer = b;

            // T.814 bit-stuffing: after a 0xFF byte, the next byte contributes only 7 valid bits
            if (_prevWasFF)
            {
                _bitsAvailable = 7;
            }
            else
            {
                _bitsAvailable = 8;
            }
            _prevWasFF = (b == 0xFF);
        }
    }

    /// <summary>
    /// MEL run-length encoder for writing to a backward-growing MEL stream.
    /// </summary>
    /// <remarks>
    /// The MEL encoder writes bits to a backward-growing stream. The output
    /// is built in a temporary buffer and then reversed into the final cleanup segment.
    /// </remarks>
    internal ref struct MelEncoder
    {
        private byte[] _buffer;       // Output buffer (will be reversed into segment)
        private int _pos;             // Current write position
        private uint _bitBuffer;      // Bit accumulator
        private int _bitsInBuffer;    // Number of bits accumulated
        private int _maxBits;         // Max bits for current byte (7 after 0xFF, else 8)
        private int _run;             // Current run count
        private int _state;           // Current state (0-12)
        private bool _rented;         // Whether buffer was rented from ArrayPool

        /// <summary>
        /// Initializes a new MEL encoder.
        /// </summary>
        /// <param name="estimatedSize">Estimated output size in bytes.</param>
        public MelEncoder(int estimatedSize)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(estimatedSize, 64));
            _rented = true;
            _pos = 0;
            _bitBuffer = 0;
            _bitsInBuffer = 0;
            _maxBits = 8;
            _run = 0;
            _state = 0;
        }

        /// <summary>
        /// Gets the current MEL state (0-12).
        /// </summary>
        public readonly int State => _state;

        /// <summary>
        /// Gets the number of bytes written so far.
        /// </summary>
        public readonly int BytesWritten => _pos;

        /// <summary>
        /// Encodes the significance of a quad.
        /// </summary>
        /// <param name="isSignificant">
        /// <c>true</c> if the quad has any significant samples;
        /// <c>false</c> if all samples are zero (insignificant).
        /// </param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EncodeQuadSignificance(bool isSignificant)
        {
            int runLength = 1 << MelCoder.MelE[_state];

            if (!isSignificant)
            {
                _run++;
                if (_run >= runLength)
                {
                    // Run completed: emit 0-bit, transition up
                    WriteBit(0);
                    _run = 0;
                    _state = Math.Min(_state + 1, MelCoder.MaxState);
                }
                // Otherwise just accumulate run
            }
            else
            {
                // Run broken: emit 1-bit followed by MelE[state] bits encoding
                // the partial run count (how many insignificant quads preceded this
                // significant one in the current run).
                int numBits = MelCoder.MelE[_state];
                WriteBit(1);
                if (numBits > 0)
                {
                    WriteBits(_run, numBits);
                }
                _run = 0;
                _state = Math.Max(_state - 1, 0);
            }
        }

        /// <summary>
        /// Flushes any remaining bits and returns the encoded MEL data.
        /// </summary>
        /// <remarks>
        /// The returned data should be written backward (reversed) into the cleanup segment.
        /// </remarks>
        /// <returns>The encoded MEL bytes.</returns>
        public ReadOnlySpan<byte> Flush()
        {
            // If there's an incomplete run, it is implicit (decoder will pad with zeros)
            // Flush any remaining bits in the buffer
            if (_bitsInBuffer > 0)
            {
                // Pad remaining bits with zeros up to _maxBits (7 after 0xFF, else 8)
                _bitBuffer <<= (_maxBits - _bitsInBuffer);
                EmitByte((byte)_bitBuffer);
                _bitBuffer = 0;
                _bitsInBuffer = 0;
                _maxBits = 8;
            }

            return new ReadOnlySpan<byte>(_buffer, 0, _pos);
        }

        /// <summary>
        /// Returns rented buffer to the array pool.
        /// </summary>
        public void Dispose()
        {
            if (_rented && _buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = Array.Empty<byte>();
                _rented = false;
            }
        }

        /// <summary>
        /// Writes a single bit to the MEL stream.
        /// </summary>
        /// <remarks>
        /// Per T.814 section F.4, bit-stuffing applies: after emitting a 0xFF byte,
        /// the next byte can hold only 7 valid bits (the MSB is a stuffing bit).
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteBit(int bit)
        {
            _bitBuffer = (_bitBuffer << 1) | (uint)(bit & 1);
            _bitsInBuffer++;

            if (_bitsInBuffer >= _maxBits)
            {
                byte emitted = (byte)_bitBuffer;
                EmitByte(emitted);
                // After emitting 0xFF, the next byte has only 7 valid bits
                _maxBits = (emitted == 0xFF) ? 7 : 8;
                _bitBuffer = 0;
                _bitsInBuffer = 0;
            }
        }

        /// <summary>
        /// Writes multiple bits to the MEL stream.
        /// </summary>
        private void WriteBits(int value, int numBits)
        {
            for (int i = numBits - 1; i >= 0; i--)
            {
                WriteBit((value >> i) & 1);
            }
        }

        /// <summary>
        /// Emits a byte to the output buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitByte(byte b)
        {
            EnsureCapacity();
            _buffer[_pos++] = b;
        }

        /// <summary>
        /// Ensures the buffer has capacity for at least one more byte.
        /// </summary>
        private void EnsureCapacity()
        {
            if (_pos >= _buffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _pos);
                if (_rented)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
                _buffer = newBuffer;
                _rented = true;
            }
        }
    }
}
