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
    /// a 1-bit is emitted and the state transitions up (longer runs expected). When a run
    /// breaks (a significant quad appears before the run completes), a 0-bit is emitted
    /// and the state transitions down (shorter runs expected).
    /// </para>
    /// <para>
    /// The MEL stream is written forward and read forward from position (lcup - scup)
    /// within the cleanup codeword segment, with 0xFF bit-stuffing.
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
    /// MEL run-length decoder for reading from a forward-growing MEL stream
    /// within a cleanup codeword segment per ITU-T T.814.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MEL decoder reads bits forward from position (lcup - scup) within the segment,
    /// with 0xFF bit-stuffing (the MSB of a byte following 0xFF is a stuffing bit and
    /// must be ignored). Bits are read MSB-first.
    /// </para>
    /// <para>
    /// The decoder decodes runs of events from the bitstream. A 1-bit indicates a full
    /// run of 2^MelE[k] zero (insignificant) events. A 0-bit followed by MelE[k] bits
    /// indicates a partial run (the number of zeros before a 1 event).
    /// </para>
    /// </remarks>
    internal ref struct MelDecoder
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _pos;            // Current read position (forward)
        private int _size;           // Remaining bytes to read
        private ulong _tmp;          // Bit buffer (MSB-first)
        private int _bits;           // Number of valid bits in _tmp
        private bool _unstuff;       // Whether next byte needs unstuffing
        private int _k;              // Current state (0-12)

        // Queue of decoded runs
        private int _numRuns;        // Number of decoded runs in queue
        private ulong _runs;         // Packed runs (7 bits each)

        /// <summary>
        /// Initializes a new MEL decoder for reading from a cleanup codeword segment.
        /// </summary>
        /// <param name="data">The complete cleanup codeword segment.</param>
        /// <param name="lcup">Total length of the segment (MagSgn + MEL + VLC).</param>
        /// <param name="scup">Combined length of MEL + VLC data.</param>
        public MelDecoder(ReadOnlySpan<byte> data, int lcup, int scup)
        {
            _data = data;
            _pos = 0;
            _size = 0;
            _tmp = 0;
            _bits = 0;
            _unstuff = false;
            _k = 0;
            _numRuns = 0;
            _runs = 0;

            // MEL data starts at lcup - scup
            int melStart = lcup - scup;
            _data = data.Slice(melStart);
            _size = scup - 1; // scup - 1 bytes for MEL (last byte is shared with VLC)

            // Initialize by reading initial bytes to align to a 4-byte boundary
            // (following OpenJPH's mel_init approach for efficiency)
            InitialFill();
        }

        /// <summary>
        /// Gets the current MEL state (0-12).
        /// </summary>
        public readonly int State => _k;

        /// <summary>
        /// Gets the current run count remaining.
        /// </summary>
        // CA1822: Intentionally instance property for API compatibility with callers that
        // may reference it on a MelDecoder instance. Runs are consumed via the internal queue.
#pragma warning disable CA1822
        public readonly int Run => 0;
#pragma warning restore CA1822

        /// <summary>
        /// Decodes whether the next quad is significant (has any non-zero samples).
        /// </summary>
        /// <returns>
        /// <c>true</c> if the quad is significant;
        /// <c>false</c> if the quad is insignificant (part of a run).
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DecodeQuadSignificance()
        {
            // Get a decoded run from the queue
            int run = GetRun();
            // run is stored as: even = stretch of zeros not terminating in 1
            //                    odd = stretch of zeros terminating in 1
            bool terminatesInOne = (run & 1) != 0;
            int numZeros = run >> 1;

            if (numZeros > 0)
            {
                // There are zero events to consume first.
                // Put the rest back in the queue with decremented count.
                int remaining = ((numZeros - 1) << 1) | (terminatesInOne ? 1 : 0);
                // Push back remaining run at the head of the queue
                _runs = (_runs << 7) | ((uint)remaining & 0x7Fu);
                _numRuns++;
                return false; // insignificant (zero event)
            }

            if (terminatesInOne)
            {
                // This is the terminating 1 event
                return true; // significant
            }

            // Zero run with no terminating event (numZeros == 0, not terminating)
            // This shouldn't normally happen, but handle gracefully
            return false;
        }

        /// <summary>
        /// Performs initial read to fill the bit buffer, reading enough bytes
        /// to get the read address to a multiple of 4.
        /// </summary>
        private void InitialFill()
        {
            // Read up to 4 bytes to fill the initial buffer
            int num = 4;
            if (num > _size)
            {
                num = _size;
            }

            for (int i = 0; i < num; i++)
            {
                ulong d;
                if (_size > 0)
                {
                    d = _data[_pos++];
                    _size--;
                }
                else
                {
                    d = 0xFF; // feed 0xFF when exhausted
                }

                if (_size == 0 && _pos > 0)
                {
                    d |= 0xF; // MEL and VLC segments overlap on last byte
                }

                int dBits = 8 - (_unstuff ? 1 : 0);
                _tmp = (_tmp << dBits) | d;
                _bits += dBits;
                _unstuff = (d & 0xFF) == 0xFF;
            }

            _tmp <<= (64 - _bits); // push all the way up so first bit is MSB
        }

        /// <summary>
        /// Reads and unstuffs more data from the MEL bitstream into the buffer.
        /// </summary>
        private void MelRead()
        {
            if (_bits > 32)
            {
                return;
            }

            uint val = 0xFFFFFFFF; // feed 0xFF if exhausted
            if (_size > 4)
            {
                // Read up to 4 bytes
                val = (uint)_data[_pos]
                    | ((uint)_data[_pos + 1] << 8)
                    | ((uint)_data[_pos + 2] << 16)
                    | ((uint)_data[_pos + 3] << 24);
                _pos += 4;
                _size -= 4;
            }
            else if (_size > 0)
            {
                int i = 0;
                while (_size > 1)
                {
                    uint v = _data[_pos++];
                    uint m = ~(0xFFu << i);
                    val = (val & m) | (v << i);
                    _size--;
                    i += 8;
                }
                // Last byte: OR with 0xF for MEL/VLC overlap
                uint lastV = _data[_pos++];
                lastV |= 0xF;
                uint lastM = ~(0xFFu << i);
                val = (val & lastM) | (lastV << i);
                _size--;
            }

            // Unstuff and accumulate
            int bits = 32 - (_unstuff ? 1 : 0);

            uint t = val & 0xFF;
            bool unstuff = (val & 0xFF) == 0xFF;
            bits -= unstuff ? 1 : 0;
            t = t << (8 - (unstuff ? 1 : 0));

            t |= (val >> 8) & 0xFF;
            unstuff = ((val >> 8) & 0xFF) == 0xFF;
            bits -= unstuff ? 1 : 0;
            t = t << (8 - (unstuff ? 1 : 0));

            t |= (val >> 16) & 0xFF;
            unstuff = ((val >> 16) & 0xFF) == 0xFF;
            bits -= unstuff ? 1 : 0;
            t = t << (8 - (unstuff ? 1 : 0));

            t |= (val >> 24) & 0xFF;
            _unstuff = ((val >> 24) & 0xFF) == 0xFF;

            _tmp |= (ulong)t << (64 - bits - _bits);
            _bits += bits;
        }

        /// <summary>
        /// Decodes runs of MEL events from the bitstream into the run queue.
        /// </summary>
        private void MelDecode()
        {
            if (_bits < 6)
            {
                MelRead();
            }

            while (_bits >= 6 && _numRuns < 8)
            {
                int eval = MelCoder.MelE[_k];
                int run;

                if ((_tmp & (1UL << 63)) != 0)
                {
                    // 1-bit: full run of 2^eval zeros, not terminating
                    run = (1 << eval) - 1;
                    _k = Math.Min(12, _k + 1);
                    _tmp <<= 1;
                    _bits -= 1;
                    run = run << 1; // not terminating in 1
                }
                else
                {
                    // 0-bit: partial run, read eval bits for count, terminates in 1
                    run = (int)(_tmp >> (63 - eval)) & ((1 << eval) - 1);
                    _k = Math.Max(0, _k - 1);
                    _tmp <<= eval + 1;
                    _bits -= eval + 1;
                    run = (run << 1) + 1; // terminates in 1
                }

                int shift = _numRuns * 7;
                _runs &= ~((ulong)0x3F << shift);
                _runs |= (ulong)run << shift;
                _numRuns++;
            }
        }

        /// <summary>
        /// Retrieves one run from the run queue. If the queue is empty,
        /// decodes more runs from the MEL bitstream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetRun()
        {
            if (_numRuns == 0)
            {
                MelDecode();
            }

            int t = (int)(_runs & 0x7F);
            _runs >>= 7;
            _numRuns--;
            return t;
        }
    }

    /// <summary>
    /// MEL run-length encoder for writing to a forward-growing MEL stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MEL encoder writes bits forward with 0xFF bit-stuffing. The output bytes
    /// are written sequentially. This encoder is used for standalone testing.
    /// The actual HT cleanup pass uses the MEL encoding built into
    /// <see cref="HtCleanupWriter"/> which shares a buffer with VLC for efficient
    /// MEL/VLC termination and byte fusion.
    /// </para>
    /// </remarks>
    internal ref struct MelEncoder
    {
        private byte[] _buffer;       // Output buffer
        private int _pos;             // Current write position
        private int _remainingBits;   // Remaining bit slots in current byte
        private int _tmp;             // Current byte being built (MSB-first)
        private int _run;             // Current run count
        private int _state;           // Current state (0-12)
        private int _threshold;       // 1 << MelE[state]
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
            _remainingBits = 8;
            _tmp = 0;
            _run = 0;
            _state = 0;
            _threshold = 1; // 1 << MelCoder.MelE[0]
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
            if (!isSignificant)
            {
                _run++;
                if (_run >= _threshold)
                {
                    // Run completed: emit 1-bit, transition up
                    EmitBit(1);
                    _run = 0;
                    _state = Math.Min(_state + 1, MelCoder.MaxState);
                    _threshold = 1 << MelCoder.MelE[_state];
                }
                // Otherwise just accumulate run
            }
            else
            {
                // Run broken: emit 0-bit followed by MelE[state] bits encoding
                // the partial run count (MSB first)
                EmitBit(0);
                int numBits = MelCoder.MelE[_state];
                int run = _run;
                for (int i = numBits - 1; i >= 0; i--)
                {
                    EmitBit((run >> i) & 1);
                }

                _run = 0;
                _state = Math.Max(_state - 1, 0);
                _threshold = 1 << MelCoder.MelE[_state];
            }
        }

        /// <summary>
        /// Flushes any remaining bits and returns the encoded MEL data.
        /// </summary>
        /// <returns>The encoded MEL bytes.</returns>
        public ReadOnlySpan<byte> Flush()
        {
            // If there's an incomplete run, it is implicit (decoder will pad with zeros)
            // Flush any remaining bits in the buffer
            if (_remainingBits < 8)
            {
                // Pad remaining bits to fill the byte
                _tmp = _tmp << _remainingBits;
                EnsureCapacity();
                _buffer[_pos++] = (byte)_tmp;
                _tmp = 0;
                _remainingBits = 8;
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
        /// Emits a single bit to the MEL stream (MSB-first with 0xFF bit-stuffing).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitBit(int v)
        {
            _tmp = (_tmp << 1) + v;
            _remainingBits--;

            if (_remainingBits == 0)
            {
                EnsureCapacity();
                _buffer[_pos++] = (byte)_tmp;
                _remainingBits = (_tmp == 0xFF) ? 7 : 8;
                _tmp = 0;
            }
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
