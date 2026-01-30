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
        private int _bp;           // Current byte position
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
        /// Resets the encoder state for a new code block.
        /// </summary>
        public void Reset()
        {
            _a = 0x8000;  // Interval starts at 0x8000
            _c = 0;
            _ct = 12;     // Initial shift count
            _bp = 0;

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

            _a -= qe;

            if (bit == mps)
            {
                // Coding MPS
                if (_a < 0x8000)
                {
                    // Need to renormalize
                    if (_a < qe)
                    {
                        // LPS becomes MPS
                        _c += _a;
                        _a = qe;
                    }
                    _contextState[context] = nmps;
                    Renormalize();
                }
            }
            else
            {
                // Coding LPS
                if (_a >= qe)
                {
                    _c += _a;
                    _a = qe;
                }
                if (swt == 1)
                {
                    _contextMps[context] = (byte)(1 - mps);
                }
                _contextState[context] = nlps;
                Renormalize();
            }
        }

        /// <summary>
        /// Encodes a bit using the uniform context (context 18, equal probability).
        /// </summary>
        /// <param name="bit">The bit to encode (0 or 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EncodeUniform(int bit)
        {
            // Uniform coding uses a simplified procedure
            _a -= 0x5601; // Qe for uniform context

            if (bit == 0)
            {
                // Coding 0 (MPS)
                if (_a < 0x8000)
                {
                    if (_a < 0x5601)
                    {
                        _c += _a;
                        _a = 0x5601;
                    }
                    Renormalize();
                }
            }
            else
            {
                // Coding 1 (LPS)
                if (_a >= 0x5601)
                {
                    _c += _a;
                    _a = 0x5601;
                }
                Renormalize();
            }
        }

        /// <summary>
        /// Flushes the encoder and returns the encoded data.
        /// </summary>
        /// <returns>The encoded byte sequence.</returns>
        public ReadOnlySpan<byte> Flush()
        {
            // Final renormalization
            SetBits();

            // Output remaining bytes from code register
            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();

            // Remove trailing 0xFF if present
            int length = _bp;
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
            return new ReadOnlySpan<byte>(_buffer, 0, _bp);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ByteOut()
        {
            EnsureCapacity();

            uint t = _c >> 19;

            if (t > 0xFF)
            {
                // Carry propagation
                if (_bp > 0)
                {
                    _buffer[_bp - 1]++;
                }
                _c &= 0x7FFFF;
                t = _c >> 19;
            }

            if (t == 0xFF)
            {
                // Stuff byte
                _buffer[_bp++] = 0xFF;
                _c &= 0x7FFFF;
                _ct = 7;
            }
            else
            {
                if (_bp > 0 && _buffer[_bp - 1] == 0xFF)
                {
                    _ct = 7;
                }
                _buffer[_bp++] = (byte)t;
                _c &= 0x7FFFF;
                _ct = 8;
            }
        }

        private void SetBits()
        {
            uint t = _a + _c - 1;
            t &= 0xFFFF0000;
            if (t < _c)
            {
                t += 0x8000;
            }
            _c = t;
        }

        private void EnsureCapacity()
        {
            if (_bp >= _buffer.Length - 2)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _bp);
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
    /// </remarks>
    public sealed class MqDecoder
    {
        private uint _a;           // Interval register
        private uint _c;           // Code register
        private int _ct;           // Counter
        private int _bp;           // Current byte position
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
            _contextState = new byte[MqCoder.NumContexts];
            _contextMps = new byte[MqCoder.NumContexts];
            Reset();
        }

        /// <summary>
        /// Resets the decoder state for a new code block.
        /// </summary>
        public void Reset()
        {
            _a = 0x8000;
            _bp = 0;

            // Initialize all contexts
            Array.Clear(_contextState, 0, _contextState.Length);
            Array.Clear(_contextMps, 0, _contextMps.Length);

            // Initialize code register by reading first bytes
            ReadOnlySpan<byte> span = _data.Span;
            _c = 0;

            // Read first byte
            if (_bp < span.Length)
            {
                byte b = span[_bp++];
                if (b == 0xFF)
                {
                    // Check for stuffing
                    if (_bp < span.Length && span[_bp] == 0x00)
                    {
                        _bp++;
                    }
                }
                _c = (uint)(b << 16);
            }

            // Read second byte
            ByteIn();
            _c <<= 7;
            _ct -= 7;
        }

        /// <summary>
        /// Decodes a single bit using the specified context.
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
            if ((_c >> 16) < _a)
            {
                // MPS path
                if (_a < 0x8000)
                {
                    // Need to renormalize
                    if (_a < qe)
                    {
                        // LPS actually
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
                    d = mps;
                }
            }
            else
            {
                // LPS path
                _c -= (uint)(_a << 16);
                if (_a < qe)
                {
                    // Actually MPS
                    d = mps;
                    _contextState[context] = nmps;
                }
                else
                {
                    d = 1 - mps;
                    if (swt == 1)
                    {
                        _contextMps[context] = (byte)(1 - mps);
                    }
                    _contextState[context] = nlps;
                }
                _a = qe;
                RenormalizeDecoder();
            }

            return d;
        }

        /// <summary>
        /// Decodes a bit using uniform context (equal probability).
        /// </summary>
        /// <returns>The decoded bit (0 or 1).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DecodeUniform()
        {
            const uint qe = 0x5601;

            _a -= qe;

            int d;
            if ((_c >> 16) < _a)
            {
                // MPS path (0)
                if (_a < 0x8000)
                {
                    if (_a < qe)
                    {
                        d = 1;
                    }
                    else
                    {
                        d = 0;
                    }
                    RenormalizeDecoder();
                }
                else
                {
                    d = 0;
                }
            }
            else
            {
                // LPS path (1)
                _c -= (uint)(_a << 16);
                if (_a < qe)
                {
                    d = 0;
                }
                else
                {
                    d = 1;
                }
                _a = qe;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ByteIn()
        {
            ReadOnlySpan<byte> span = _data.Span;

            if (_bp >= span.Length)
            {
                // Past end of data, use padding
                _ct = 8;
                return;
            }

            byte b = span[_bp++];

            if (b == 0xFF)
            {
                // Check next byte
                if (_bp < span.Length)
                {
                    byte b1 = span[_bp];
                    if (b1 > 0x8F)
                    {
                        // Marker - don't consume it, use 0xFF as data
                        _c |= 0xFF00;
                        _ct = 8;
                    }
                    else
                    {
                        // Stuffed byte - skip 0x00 or continue
                        if (b1 == 0x00)
                        {
                            _bp++;
                        }
                        _c |= 0xFF00;
                        _ct = 8;
                    }
                }
                else
                {
                    _c |= 0xFF00;
                    _ct = 8;
                }
            }
            else
            {
                _c |= (uint)(b << 8);
                _ct = 8;
            }
        }
    }
}
