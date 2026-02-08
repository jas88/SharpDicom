using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Constants and shared state for MQ arithmetic coding.
    /// </summary>
    /// <remarks>
    /// The MQ-coder is a context-adaptive binary arithmetic coder used in JPEG 2000
    /// for bitplane coding (Tier-1 coding). It's based on the QM-coder from JBIG and JPEG.
    /// </remarks>
    public static class MqCoder
    {
        /// <summary>
        /// Number of coding contexts used for EBCOT bitplane coding.
        /// </summary>
        /// <remarks>
        /// EBCOT uses 19 contexts:
        /// - 9 for significance coding (based on neighbor significance)
        /// - 5 for sign coding
        /// - 3 for magnitude refinement
        /// - 1 for run-length coding
        /// - 1 for uniform (raw) coding
        /// </remarks>
        public const int NumContexts = 19;

        /// <summary>
        /// Initial state index for contexts (state 0, MPS=0).
        /// </summary>
        internal const int InitialState = 0;

        // MQ-coder probability estimation table (ITU-T T.800 Table C.2)
        // Format: (Qe, NMPS, NLPS, Switch)
        // Qe: probability value for LPS (16-bit fixed-point, normalized)
        // NMPS: next state after MPS
        // NLPS: next state after LPS
        // Switch: 1 if MPS/LPS should be exchanged after this state
        internal static readonly (ushort Qe, byte NMPS, byte NLPS, byte Switch)[] States = new (ushort, byte, byte, byte)[]
        {
            (0x5601, 1,  1,  1),   // State 0
            (0x3401, 2,  6,  0),   // State 1
            (0x1801, 3,  9,  0),   // State 2
            (0x0AC1, 4,  12, 0),   // State 3
            (0x0521, 5,  29, 0),   // State 4
            (0x0221, 38, 33, 0),   // State 5
            (0x5601, 7,  6,  1),   // State 6
            (0x5401, 8,  14, 0),   // State 7
            (0x4801, 9,  14, 0),   // State 8
            (0x3801, 10, 14, 0),   // State 9
            (0x3001, 11, 17, 0),   // State 10
            (0x2401, 12, 18, 0),   // State 11
            (0x1C01, 13, 20, 0),   // State 12
            (0x1601, 29, 21, 0),   // State 13
            (0x5601, 15, 14, 1),   // State 14
            (0x5401, 16, 14, 0),   // State 15
            (0x5101, 17, 15, 0),   // State 16
            (0x4801, 18, 16, 0),   // State 17
            (0x3801, 19, 17, 0),   // State 18
            (0x3401, 20, 18, 0),   // State 19
            (0x3001, 21, 19, 0),   // State 20
            (0x2801, 22, 19, 0),   // State 21
            (0x2401, 23, 20, 0),   // State 22
            (0x2201, 24, 21, 0),   // State 23
            (0x1C01, 25, 22, 0),   // State 24
            (0x1801, 26, 23, 0),   // State 25
            (0x1601, 27, 24, 0),   // State 26
            (0x1401, 28, 25, 0),   // State 27
            (0x1201, 29, 26, 0),   // State 28
            (0x1101, 30, 27, 0),   // State 29
            (0x0AC1, 31, 28, 0),   // State 30
            (0x09C1, 32, 29, 0),   // State 31
            (0x08A1, 33, 30, 0),   // State 32
            (0x0521, 34, 31, 0),   // State 33
            (0x0441, 35, 32, 0),   // State 34
            (0x02A1, 36, 33, 0),   // State 35
            (0x0221, 37, 34, 0),   // State 36
            (0x0141, 38, 35, 0),   // State 37
            (0x0111, 39, 36, 0),   // State 38
            (0x0085, 40, 37, 0),   // State 39
            (0x0049, 41, 38, 0),   // State 40
            (0x0025, 42, 39, 0),   // State 41
            (0x0015, 43, 40, 0),   // State 42
            (0x0009, 44, 41, 0),   // State 43
            (0x0005, 45, 42, 0),   // State 44
            (0x0001, 45, 43, 0),   // State 45
            (0x5601, 46, 46, 0),   // State 46 (uniform context)
        };

        /// <summary>
        /// Gets the number of states in the probability table.
        /// </summary>
        public const int NumStates = 47;
    }

    /// <summary>
    /// MQ arithmetic encoder for JPEG 2000 bitplane coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The encoder maintains context states and produces a compressed bitstream.
    /// Each context has its own probability estimation that adapts based on
    /// the symbols coded in that context.
    /// </para>
    /// <para>
    /// The encoder uses the following registers:
    /// - A: Interval size (normalized to [0x8000, 0x10000))
    /// - C: Code register (accumulated probability)
    /// - CT: Counter for shift operations
    /// </para>
    /// </remarks>
    public sealed class MqEncoder : IDisposable
    {
        private uint _a;           // Interval register
        private uint _c;           // Code register
        private int _ct;           // Counter
        private byte[] _buffer;    // Output buffer
        // _bp mirrors OpenJPEG's bp: points at the last written byte.
        // -1 means no byte written yet (equivalent to OpenJPEG's bp = start - 1).
        private int _bp;
        private bool _disposed;

        // Per-context state: index into States table and MPS value
        private readonly byte[] _contextState;
        private readonly byte[] _contextMps;

        /// <summary>
        /// Initializes a new MQ encoder with the specified buffer size.
        /// </summary>
        /// <param name="bufferSize">Initial output buffer size.</param>
        public MqEncoder(int bufferSize = 4096)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
            _contextState = new byte[MqCoder.NumContexts];
            _contextMps = new byte[MqCoder.NumContexts];
            Reset();
        }

        /// <summary>
        /// Resets the encoder state for a new code block per ITU-T T.800 C.2.8 (INITENC).
        /// </summary>
        public void Reset()
        {
            _a = 0x8000;
            _c = 0;
            _ct = 12;
            _bp = -1;  // No byte written yet (like OpenJPEG's bp = start - 1)

            // Initialize all contexts to state 0, MPS=0
            Array.Clear(_contextState, 0, _contextState.Length);
            Array.Clear(_contextMps, 0, _contextMps.Length);
        }

        /// <summary>
        /// Encodes a single bit using the specified context.
        /// </summary>
        /// <param name="context">Context index (0 to NumContexts-1).</param>
        /// <param name="bit">The bit to encode (0 or 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Encode(int context, int bit)
        {
            if ((uint)context >= MqCoder.NumContexts)
            {
                throw new ArgumentOutOfRangeException(nameof(context));
            }

            int state = _contextState[context];
            int mps = _contextMps[context];
            var (qe, nmps, nlps, swt) = MqCoder.States[state];

            if (bit == mps)
            {
                CodeMps(context, qe, nmps);
            }
            else
            {
                CodeLps(context, qe, nlps, swt);
            }
        }

        /// <summary>
        /// Encodes the most probable symbol per OpenJPEG opj_mqc_codemps.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CodeMps(int context, ushort qe, byte nmps)
        {
            _a -= qe;
            if ((_a & 0x8000) == 0)
            {
                // Need renormalization
                if (_a < qe)
                {
                    // Conditional exchange: a < qe means LPS sub-interval is larger
                    _a = qe;
                }
                else
                {
                    _c += qe;
                }
                _contextState[context] = nmps;
                Renormalize();
            }
            else
            {
                // No renormalization needed
                _c += qe;
            }
        }

        /// <summary>
        /// Encodes the least probable symbol per OpenJPEG opj_mqc_codelps.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CodeLps(int context, ushort qe, byte nlps, byte swt)
        {
            _a -= qe;
            if (_a < qe)
            {
                // Conditional exchange: MPS sub-interval is smaller
                _c += qe;
            }
            else
            {
                _a = qe;
            }
            if (swt == 1)
            {
                _contextMps[context] = (byte)(1 - _contextMps[context]);
            }
            _contextState[context] = nlps;
            Renormalize();
        }

        /// <summary>
        /// Encodes a bit using the uniform context (context 18, equal probability).
        /// </summary>
        /// <param name="bit">The bit to encode (0 or 1).</param>
        /// <remarks>
        /// Uses ITU-T T.800 uniform coding procedure for equal probability symbols.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EncodeUniform(int bit)
        {
            // For uniform coding, split interval in half
            _a >>= 1;

            if (bit != 0)
            {
                // Coding 1: add lower half to code register
                _c += _a;
            }

            // Always renormalize after uniform coding
            if (_a < 0x8000)
            {
                Renormalize();
            }
        }

        /// <summary>
        /// Flushes the encoder and returns the encoded data per ITU-T T.800 C.2.9 (FLUSH).
        /// </summary>
        /// <returns>The encoded byte sequence.</returns>
        public ReadOnlySpan<byte> Flush()
        {
            // Final renormalization per Figure C.11 – FLUSH procedure
            SetBits();

            // Output remaining bytes from code register
            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();

            // Per ITU-T T.800: coding pass must not end with 0xFF.
            // If last byte is not 0xFF, advance past it for length calculation.
            int length = _bp + 1;  // _bp points at last written byte

            // Remove trailing 0xFF if present
            while (length > 0 && _buffer[length - 1] == 0xFF)
            {
                length--;
            }

            return new ReadOnlySpan<byte>(_buffer, 0, length);
        }

        /// <summary>
        /// Gets the current encoded data without flushing.
        /// </summary>
        /// <returns>The encoded byte sequence so far.</returns>
        public ReadOnlySpan<byte> GetEncodedData()
        {
            int length = _bp + 1;  // _bp points at last written byte
            if (length <= 0) return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>(_buffer, 0, length);
        }

        /// <summary>
        /// Disposes the encoder and returns the buffer to the pool.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = Array.Empty<byte>();
                _disposed = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Renormalize()
        {
            do
            {
                _a <<= 1;
                _c <<= 1;
                _ct--;

                if (_ct == 0)
                {
                    ByteOut();
                }
            }
            while (_a < 0x8000);
        }

        /// <summary>
        /// Outputs a byte per ITU-T T.800 C.2.4 (BYTEOUT), matching OpenJPEG opj_mqc_byteout.
        /// _bp points at the last written byte (-1 if none written yet).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ByteOut()
        {
            EnsureCapacity();

            // Get current "last byte" value (0x00 if none written)
            byte lastByte = _bp >= 0 ? _buffer[_bp] : (byte)0;

            if (lastByte == 0xFF)
            {
                // After 0xFF: bit-stuffing — output 7 bits shifted by 20
                _bp++;
                _buffer[_bp] = (byte)(_c >> 20);
                _c &= 0xFFFFF;   // Keep lower 20 bits
                _ct = 7;
            }
            else
            {
                if ((_c & 0x8000000) == 0)
                {
                    // No carry: output 8 bits shifted by 19
                    _bp++;
                    _buffer[_bp] = (byte)(_c >> 19);
                    _c &= 0x7FFFF;  // Keep lower 19 bits
                    _ct = 8;
                }
                else
                {
                    // Carry: increment last byte
                    if (_bp >= 0)
                    {
                        _buffer[_bp]++;
                        lastByte = _buffer[_bp];
                    }

                    if (lastByte == 0xFF)
                    {
                        // Carry made last byte 0xFF: bit-stuffing
                        _c &= 0x7FFFFFF;  // Clear carry bit
                        _bp++;
                        _buffer[_bp] = (byte)(_c >> 20);
                        _c &= 0xFFFFF;
                        _ct = 7;
                    }
                    else
                    {
                        // Normal after carry
                        _bp++;
                        _buffer[_bp] = (byte)(_c >> 19);
                        _c &= 0x7FFFF;
                        _ct = 8;
                    }
                }
            }
        }

        /// <summary>
        /// Fills code register C with 1's for flushing, per OpenJPEG opj_mqc_setbits.
        /// </summary>
        private void SetBits()
        {
            uint tempc = _c + _a;
            _c |= 0xFFFF;
            if (_c >= tempc)
            {
                _c -= 0x8000;
            }
        }

        private void EnsureCapacity()
        {
            // _bp is the index of last written byte; ByteOut will write at _bp+1
            if (_bp + 2 >= _buffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                int copyLen = Math.Max(0, _bp + 1);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, copyLen);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }
        }
    }

    /// <summary>
    /// MQ arithmetic decoder for JPEG 2000 bitplane coding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decoder reads a compressed bitstream and produces decoded bits.
    /// It maintains context states that adapt during decoding, mirroring
    /// the encoder's state transitions.
    /// </para>
    /// <para>
    /// Matches OpenJPEG's opj_mqc implementation exactly:
    /// _bp points at the last consumed byte (like OpenJPEG's mqc->bp).
    /// ByteIn reads *(bp+1) and checks *bp for 0xFF bit-stuffing.
    /// </para>
    /// </remarks>
    public sealed class MqDecoder
    {
        private uint _a;           // Interval register
        private uint _c;           // Code register
        private int _ct;           // Counter
        // _bp: index of last consumed byte (like OpenJPEG's mqc->bp).
        // ByteIn reads data[_bp+1] and checks data[_bp] for 0xFF.
        private int _bp;
        private int _dataLength;
        private readonly ReadOnlyMemory<byte> _data;

        // Per-context state
        private readonly byte[] _contextState;
        private readonly byte[] _contextMps;

        /// <summary>
        /// Initializes a new MQ decoder with the specified data.
        /// </summary>
        /// <param name="data">The encoded data to decode.</param>
        public MqDecoder(ReadOnlyMemory<byte> data)
        {
            _data = data;
            _dataLength = data.Length;
            _contextState = new byte[MqCoder.NumContexts];
            _contextMps = new byte[MqCoder.NumContexts];
            Reset();
        }

        /// <summary>
        /// Resets the decoder state per ITU-T T.800 C.3.5 (INITDEC),
        /// matching OpenJPEG's opj_mqc_init_dec exactly.
        /// </summary>
        public void Reset()
        {
            _a = 0x8000;
            _dataLength = _data.Length;

            // Initialize all contexts
            Array.Clear(_contextState, 0, _contextState.Length);
            Array.Clear(_contextMps, 0, _contextMps.Length);

            // Initialize code register per OpenJPEG opj_mqc_init_dec:
            // c = *bp << 16 (first byte)
            // Then bytein (reads second byte)
            // Then c <<= 7, ct -= 7
            ReadOnlySpan<byte> span = _data.Span;
            if (span.Length == 0)
            {
                _c = 0xFF << 16;  // Match OpenJPEG: len==0 case
                _bp = 0;
                _ct = 0;
            }
            else
            {
                _c = (uint)(span[0] << 16);
                _bp = 0;  // bp points at byte 0 (the byte we just loaded)
            }

            // ByteIn reads *(bp+1) and checks *bp
            ByteIn();

            // Final init step
            _c <<= 7;
            _ct -= 7;
        }

        /// <summary>
        /// Decodes a single bit using the specified context per ITU-T T.800 C.3.2 (DECODE).
        /// Matches OpenJPEG opj_mqc_decode_macro exactly.
        /// </summary>
        /// <param name="context">Context index (0 to NumContexts-1).</param>
        /// <returns>The decoded bit (0 or 1).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Decode(int context)
        {
            if ((uint)context >= MqCoder.NumContexts)
            {
                throw new ArgumentOutOfRangeException(nameof(context));
            }

            int state = _contextState[context];
            int mps = _contextMps[context];
            var (qe, nmps, nlps, swt) = MqCoder.States[state];

            _a -= qe;

            int d;
            if ((_c >> 16) < qe)
            {
                // LPS exchange path
                if (_a < qe)
                {
                    // Exchange: a < qe means MPS sub-interval is smaller
                    _a = qe;
                    d = mps;
                    _contextState[context] = nmps;
                }
                else
                {
                    _a = qe;
                    d = 1 - mps;
                    if (swt == 1)
                    {
                        _contextMps[context] = (byte)(1 - mps);
                    }
                    _contextState[context] = nlps;
                }
                RenormalizeDecoder();
            }
            else
            {
                // MPS path: subtract qe from C
                _c -= (uint)(qe << 16);
                if ((_a & 0x8000) == 0)
                {
                    // MPS exchange path (renorm needed)
                    if (_a < qe)
                    {
                        // Exchange: a < qe means LPS sub-interval is larger
                        d = 1 - mps;
                        if (swt == 1)
                        {
                            _contextMps[context] = (byte)(1 - mps);
                        }
                        _contextState[context] = nlps;
                    }
                    else
                    {
                        d = mps;
                        _contextState[context] = nmps;
                    }
                    RenormalizeDecoder();
                }
                else
                {
                    // No renormalization needed
                    d = mps;
                }
            }

            return d;
        }

        /// <summary>
        /// Decodes a bit using uniform context (equal probability).
        /// </summary>
        /// <returns>The decoded bit (0 or 1).</returns>
        /// <remarks>
        /// Uses ITU-T T.800 uniform coding procedure for equal probability symbols.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DecodeUniform()
        {
            // For uniform coding, split interval in half
            _a >>= 1;

            int d;
            if ((_c >> 16) >= _a)
            {
                // Bit is 1
                d = 1;
                _c -= (uint)(_a << 16);
            }
            else
            {
                // Bit is 0
                d = 0;
            }

            // Always renormalize after uniform coding
            if (_a < 0x8000)
            {
                RenormalizeDecoder();
            }

            return d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RenormalizeDecoder()
        {
            do
            {
                if (_ct == 0)
                {
                    ByteIn();
                }
                _a <<= 1;
                _c <<= 1;
                _ct--;
            }
            while (_a < 0x8000);
        }

        /// <summary>
        /// Reads the next byte into the code register per ITU-T T.800 C.3.4,
        /// matching OpenJPEG's opj_mqc_bytein_macro exactly.
        /// _bp points at the last consumed byte. This reads data[_bp+1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ByteIn()
        {
            ReadOnlySpan<byte> span = _data.Span;

            // Read next byte (lookahead at bp+1)
            // If bp+1 is past end, use 0xFF padding (matching OpenJPEG's
            // artificial 0xFF 0xFF sentinel at end of data)
            int nextIdx = _bp + 1;
            byte nextByte = nextIdx < _dataLength ? span[nextIdx] : (byte)0xFF;

            // Check current byte (*bp) for 0xFF
            byte curByte = _bp >= 0 && _bp < _dataLength ? span[_bp] : (byte)0;

            if (curByte == 0xFF)
            {
                if (nextByte > 0x8F)
                {
                    // Marker detected: pad with 0xFF00, don't advance bp
                    _c += 0xFF00;
                    _ct = 8;
                }
                else
                {
                    // Bit-stuffed byte: 7 bits, shifted by 9
                    _bp++;
                    _c += (uint)(nextByte << 9);
                    _ct = 7;
                }
            }
            else
            {
                // Normal byte: 8 bits, shifted by 8
                _bp++;
                _c += (uint)(nextByte << 8);
                _ct = 8;
            }
        }
    }
}
