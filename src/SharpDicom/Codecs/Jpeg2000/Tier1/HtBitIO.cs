using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Three-stream reader for HT cleanup pass codeword segments per ITU-T T.814.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The HT cleanup pass packs three byte-streams into a single codeword segment:
    /// <list type="bullet">
    ///   <item>MagSgn: reads forward from byte 0 with 0xFF bit-stuffing</item>
    ///   <item>MEL: reads forward from the MagSgn/MEL boundary with 0xFF bit-stuffing</item>
    ///   <item>VLC: reads backward from the end of the segment with >0x8F bit-stuffing</item>
    /// </list>
    /// </para>
    /// <para>
    /// The ILW (Interface Locator Word) is a 12-bit value stored across the last two bytes
    /// of the segment. It encodes the combined length of MEL + VLC data (scup).
    /// The MagSgn data occupies the first (totalLength - scup) bytes.
    /// </para>
    /// <para>
    /// Segment layout:
    /// <code>
    /// [MagSgn bytes...] [MEL bytes...] [VLC bytes (reversed)]
    ///  ^-- byte 0        ^-- lcup-scup  ^-- reads backward from lcup-1
    /// </code>
    /// </para>
    /// </remarks>
    internal ref struct HtCleanupReader
    {
        // MagSgn stream state (forward reader with 0xFF bit-stuffing)
        private readonly ReadOnlySpan<byte> _segment;
        private int _msPos;
        private int _msSize;
        private ulong _msTmp;
        private int _msBits;
        private bool _msUnstuff;

        // VLC stream state (backward reader with >0x8F bit-stuffing)
        private int _vlcPos;       // Points into _segment, reads backward
        private int _vlcSize;      // Remaining bytes
        private ulong _vlcTmp;
        private int _vlcBits;
        private bool _vlcUnstuff;

        // MEL decoder
        private MelDecoder _melDecoder;

        // Stream boundaries
        private readonly int _lcup;
        private readonly int _scup;

        /// <summary>
        /// Initializes a new HT cleanup reader by parsing the ILW and locating stream boundaries.
        /// </summary>
        /// <param name="segment">The cleanup codeword segment bytes.</param>
        /// <exception cref="ArgumentException">Thrown when segment is too short or ILW is invalid.</exception>
        public HtCleanupReader(ReadOnlySpan<byte> segment)
        {
            // Empty segment is valid for all-zero codeblocks
            if (segment.IsEmpty)
            {
                _segment = segment;
                _lcup = 0;
                _scup = 0;
                _msPos = 0;
                _msSize = 0;
                _msTmp = 0;
                _msBits = 0;
                _msUnstuff = false;
                _vlcPos = 0;
                _vlcSize = 0;
                _vlcTmp = 0;
                _vlcBits = 0;
                _vlcUnstuff = false;
                _melDecoder = default;
                return;
            }

            if (segment.Length == 1)
            {
                // Single-byte segment: treat as degenerate case with no streams
                _segment = segment;
                _lcup = 1;
                _scup = 0;
                _msPos = 0;
                _msSize = 0;
                _msTmp = 0;
                _msBits = 0;
                _msUnstuff = false;
                _vlcPos = 0;
                _vlcSize = 0;
                _vlcTmp = 0;
                _vlcBits = 0;
                _vlcUnstuff = false;
                _melDecoder = default;
                return;
            }

            _segment = segment;
            _lcup = segment.Length;

            // Parse ILW (scup) from the last 2 bytes
            // scup = (last_byte << 4) | (second_to_last_byte & 0xF)
            _scup = (segment[_lcup - 1] << 4) + (segment[_lcup - 2] & 0x0F);

            if (_scup < 2 || _scup > _lcup || _scup > 4079)
            {
                throw new ArgumentException(
                    $"Invalid ILW value {_scup} (lcup={_lcup}).", nameof(segment));
            }

            int msLen = _lcup - _scup; // MagSgn length

            // Initialize MagSgn forward reader
            _msPos = 0;
            _msSize = msLen;
            _msTmp = 0;
            _msBits = 0;
            _msUnstuff = false;

            // Initialize VLC backward reader
            // VLC data starts at the end and reads backward.
            // The first byte read is segment[lcup-2], and the top 4 bits are discarded (ILW).
            _vlcPos = 0;
            _vlcSize = 0;
            _vlcTmp = 0;
            _vlcBits = 0;
            _vlcUnstuff = false;

            // Initialize MEL decoder
            _melDecoder = new MelDecoder(segment, _lcup, _scup);

            // Initialize VLC stream (reads backward from segment end)
            InitVlc();

            // Pre-fill MagSgn buffer
            FillMagSgn();
        }

        /// <summary>
        /// Gets the total segment length (lcup).
        /// </summary>
        public readonly int Lcup => _lcup;

        /// <summary>
        /// Gets the MEL+VLC combined length (scup).
        /// </summary>
        public readonly int Scup => _scup;

        /// <summary>
        /// Initializes the VLC backward reader, consuming the ILW bits from the first byte.
        /// </summary>
        private void InitVlc()
        {
            // VLC reads backward from segment[lcup-2] (the second-to-last byte).
            // The first half-byte (lower nibble) is the ILW, so we start with the upper 4 bits.
            int startPos = _lcup - 2;
            _vlcSize = _scup - 2; // remaining bytes after this initial read

            byte d = _segment[startPos];
            _vlcTmp = (ulong)(d >> 4);
            _vlcBits = 4 - ((_vlcTmp & 7) == 7 ? 1 : 0);
            _vlcUnstuff = (d | 0xF) > 0x8F;

            // Set position to read backward from startPos - 1
            _vlcPos = startPos - 1;

            // Fill initial bits
            FillVlcBuffer();
        }

        /// <summary>
        /// Reads bits from the MagSgn stream (forward, with 0xFF bit-stuffing).
        /// </summary>
        /// <param name="count">Number of bits to read (0-32).</param>
        /// <returns>The read bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadMagSgnBits(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            if (_msBits < 32)
            {
                FillMagSgn();
                if (_msBits < 32)
                {
                    FillMagSgn();
                }
            }

            uint result = (uint)_msTmp & ((1u << count) - 1);
            _msTmp >>= count;
            _msBits -= count;
            return result;
        }

        /// <summary>
        /// Peeks bits from the VLC stream without advancing.
        /// </summary>
        /// <param name="count">Number of bits to peek.</param>
        /// <returns>The peeked bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekVlcBits(int count)
        {
            if (_vlcBits < 32)
            {
                FillVlcBuffer();
                if (_vlcBits < 32)
                {
                    FillVlcBuffer();
                }
            }

            return (uint)_vlcTmp & ((1u << count) - 1);
        }

        /// <summary>
        /// Advances the VLC stream by the specified number of bits.
        /// </summary>
        /// <param name="bits">Number of bits to advance.</param>
        /// <returns>The new head of the VLC buffer after advancement.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint AdvanceVlc(int bits)
        {
            _vlcTmp >>= bits;
            _vlcBits -= bits;
            return (uint)_vlcTmp;
        }

        /// <summary>
        /// Reads VLC bits: peeks and then advances.
        /// </summary>
        /// <param name="count">Number of bits to read.</param>
        /// <returns>The read bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVlcBits(int count)
        {
            uint val = PeekVlcBits(count);
            AdvanceVlc(count);
            return val;
        }

        /// <summary>
        /// Decodes MEL quad significance.
        /// </summary>
        /// <returns>
        /// <c>true</c> if quad is significant; <c>false</c> if insignificant (part of run).
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DecodeMelSignificance()
        {
            return _melDecoder.DecodeQuadSignificance();
        }

        /// <summary>
        /// Fills the MagSgn bit buffer by reading bytes forward with 0xFF bit-stuffing.
        /// When the MagSgn stream is exhausted, 0xFF bytes are fed in (per ITU-T T.814).
        /// </summary>
        private void FillMagSgn()
        {
            if (_msBits > 32)
            {
                return;
            }

            // Read up to 4 bytes
            for (int i = 0; i < 4; i++)
            {
                ulong d;
                if (_msPos < _msSize)
                {
                    d = _segment[_msPos++];
                }
                else
                {
                    d = 0xFF; // pad with 0xFF when exhausted
                }

                _msTmp |= d << _msBits;
                _msBits += 8 - (_msUnstuff ? 1 : 0);
                _msUnstuff = (d & 0xFF) == 0xFF;
            }
        }

        /// <summary>
        /// Fills the VLC bit buffer by reading bytes backward with >0x8F bit-stuffing.
        /// </summary>
        private void FillVlcBuffer()
        {
            if (_vlcBits > 32)
            {
                return;
            }

            // Read up to 4 bytes backward
            for (int i = 0; i < 4; i++)
            {
                if (_vlcSize <= 0)
                {
                    break;
                }

                ulong d = _segment[_vlcPos];
                _vlcPos--;
                _vlcSize--;

                // Unstuff: if last byte was >0x8F and this byte is 0x7F, skip MSB
                int dBits = 8 - ((_vlcUnstuff && ((d & 0x7F) == 0x7F)) ? 1 : 0);
                _vlcTmp |= d << _vlcBits;
                _vlcBits += dBits;
                _vlcUnstuff = d > 0x8F;
            }
        }
    }

    /// <summary>
    /// Three-stream writer for constructing HT cleanup pass codeword segments per ITU-T T.814.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Builds a cleanup segment by writing three streams independently:
    /// <list type="bullet">
    ///   <item>MagSgn (ms): written forward from byte 0 with 0xFF bit-stuffing</item>
    ///   <item>MEL: written forward with 0xFF bit-stuffing (shared buffer with VLC)</item>
    ///   <item>VLC: written backward from end of buffer with >0x8F bit-stuffing</item>
    /// </list>
    /// </para>
    /// <para>
    /// During <see cref="Finalize"/>, the streams are terminated and merged. The MEL and VLC
    /// streams are fused at termination per the ITU-T T.814 specification. The final segment
    /// layout is [MagSgn][MEL][VLC] with the ILW encoding mel.pos + vlc.pos.
    /// </para>
    /// </remarks>
    internal ref struct HtCleanupWriter
    {
        // MagSgn stream (forward with 0xFF bit-stuffing)
        private byte[] _msBuffer;
        private int _msPos;
        private int _msMaxBits;
        private int _msUsedBits;
        private uint _msTmp;
        private bool _msRented;

        // MEL/VLC shared buffer
        private byte[] _melVlcBuffer;
        private bool _melVlcRented;

        // MEL encoder state (forward in _melVlcBuffer)
        private int _melPos;
        private int _melRemainingBits;
        private int _melTmp;
        private int _melRun;
        private int _melK;
        private int _melThreshold;

        // VLC encoder state (backward in _melVlcBuffer)
        // vlc.buf points to last byte of vlc region; vlc writes at *(buf - pos)
        private int _vlcBufOffset; // offset within _melVlcBuffer of the "last byte"
        private int _vlcPos;
        private int _vlcUsedBits;
        private int _vlcTmp;
        private bool _vlcLastGreaterThan8F;

        /// <summary>
        /// MEL exponent table for states 0-12.
        /// </summary>
        private static ReadOnlySpan<int> MelExp => new int[]
        {
            0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5
        };

        /// <summary>
        /// Initializes a new HT cleanup writer with separate MagSgn and MEL/VLC buffers.
        /// </summary>
        /// <param name="estimatedSize">Estimated total segment size in bytes.</param>
        public HtCleanupWriter(int estimatedSize)
        {
            int bufSize = Math.Max(estimatedSize, 128);

            // MagSgn buffer
            _msBuffer = ArrayPool<byte>.Shared.Rent(bufSize);
            _msRented = true;
            _msPos = 0;
            _msMaxBits = 8;
            _msUsedBits = 0;
            _msTmp = 0;

            // MEL/VLC shared buffer
            int melVlcSize = Math.Max(bufSize, 256);
            _melVlcBuffer = ArrayPool<byte>.Shared.Rent(melVlcSize);
            _melVlcRented = true;

            // MEL state (forward from byte 0 of _melVlcBuffer)
            _melPos = 0;
            _melRemainingBits = 8;
            _melTmp = 0;
            _melRun = 0;
            _melK = 0;
            _melThreshold = 1; // 1 << MelExp[0]

            // VLC state (backward from end of _melVlcBuffer)
            _vlcBufOffset = _melVlcBuffer.Length - 1;
            _melVlcBuffer[_vlcBufOffset] = 0xFF;
            _vlcPos = 1;
            _vlcUsedBits = 4;
            _vlcTmp = 0xF;
            _vlcLastGreaterThan8F = true;
        }

        /// <summary>
        /// Writes bits to the MagSgn stream with 0xFF bit-stuffing.
        /// </summary>
        /// <param name="cwd">Codeword bits to write, right-aligned.</param>
        /// <param name="cwdLen">Number of bits to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteMagSgnBits(uint cwd, int cwdLen)
        {
            while (cwdLen > 0)
            {
                int t = Math.Min(_msMaxBits - _msUsedBits, cwdLen);
                _msTmp |= (cwd & ((1u << t) - 1)) << _msUsedBits;
                _msUsedBits += t;
                cwd >>= t;
                cwdLen -= t;

                if (_msUsedBits >= _msMaxBits)
                {
                    EnsureMsCapacity();
                    _msBuffer[_msPos++] = (byte)_msTmp;
                    _msMaxBits = (_msTmp == 0xFF) ? 7 : 8;
                    _msTmp = 0;
                    _msUsedBits = 0;
                }
            }
        }

        /// <summary>
        /// Writes bits to the VLC stream (backward with >0x8F bit-stuffing).
        /// </summary>
        /// <param name="cwd">Codeword bits to write, right-aligned.</param>
        /// <param name="cwdLen">Number of bits to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVlcBits(uint cwd, int cwdLen)
        {
            int cw = (int)cwd;
            int len = cwdLen;
            while (len > 0)
            {
                int availBits = 8 - (_vlcLastGreaterThan8F ? 1 : 0) - _vlcUsedBits;
                int t = Math.Min(availBits, len);
                _vlcTmp |= (cw & ((1 << t) - 1)) << _vlcUsedBits;
                _vlcUsedBits += t;
                availBits -= t;
                len -= t;
                cw >>= t;

                if (availBits == 0)
                {
                    if (_vlcLastGreaterThan8F && _vlcTmp != 0x7F)
                    {
                        _vlcLastGreaterThan8F = false;
                        continue; // one empty bit remaining
                    }

                    EnsureVlcCapacity();
                    _melVlcBuffer[_vlcBufOffset - _vlcPos] = (byte)_vlcTmp;
                    _vlcPos++;
                    _vlcLastGreaterThan8F = _vlcTmp > 0x8F;
                    _vlcTmp = 0;
                    _vlcUsedBits = 0;
                }
            }
        }

        /// <summary>
        /// Encodes a MEL event (significance of a quad).
        /// </summary>
        /// <param name="isSignificant">Whether the quad is significant.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EncodeMel(bool isSignificant)
        {
            if (!isSignificant)
            {
                _melRun++;
                if (_melRun >= _melThreshold)
                {
                    MelEmitBit(1);
                    _melRun = 0;
                    _melK = Math.Min(12, _melK + 1);
                    _melThreshold = 1 << MelExp[_melK];
                }
            }
            else
            {
                MelEmitBit(0);
                int e = MelExp[_melK];
                int run = _melRun;
                for (int i = e - 1; i >= 0; i--)
                {
                    MelEmitBit((run >> i) & 1);
                }

                _melRun = 0;
                _melK = Math.Max(0, _melK - 1);
                _melThreshold = 1 << MelExp[_melK];
            }
        }

        /// <summary>
        /// Finalizes the segment by terminating MEL/VLC, terminating MagSgn,
        /// and assembling the final segment with the ILW.
        /// </summary>
        /// <returns>The complete cleanup codeword segment as a byte array.</returns>
        public byte[] Finalize()
        {
            // Terminate MEL and VLC together (fuse last bytes if possible)
            TerminateMelVlc();

            // Terminate MagSgn
            TerminateMagSgn();

            // Assemble final segment: [MagSgn][MEL][VLC]
            int totalLen = _msPos + _melPos + _vlcPos;
            byte[] segment = new byte[totalLen];

            // Copy MagSgn bytes (forward from byte 0)
            if (_msPos > 0)
            {
                Buffer.BlockCopy(_msBuffer, 0, segment, 0, _msPos);
            }

            // Copy MEL bytes (forward from _melVlcBuffer[0..melPos])
            if (_melPos > 0)
            {
                Buffer.BlockCopy(_melVlcBuffer, 0, segment, _msPos, _melPos);
            }

            // Copy VLC bytes (backward from _melVlcBuffer[vlcBufOffset])
            // VLC data is at _melVlcBuffer[vlcBufOffset - vlcPos + 1 .. vlcBufOffset]
            if (_vlcPos > 0)
            {
                int vlcSrcStart = _vlcBufOffset - _vlcPos + 1;
                Buffer.BlockCopy(_melVlcBuffer, vlcSrcStart, segment, _msPos + _melPos, _vlcPos);
            }

            // Write the ILW (Interface Locator Word) into the last 2 bytes
            int numBytes = _melPos + _vlcPos; // scup = mel + vlc length
            if (numBytes > 4079)
            {
                throw new InvalidOperationException(
                    $"MEL+VLC combined length {numBytes} exceeds the 12-bit ILW maximum of 4079.");
            }

            // ILW: last byte = numBytes >> 4, second-to-last byte low nibble = numBytes & 0xF
            segment[totalLen - 1] = (byte)(numBytes >> 4);
            segment[totalLen - 2] = (byte)((segment[totalLen - 2] & 0xF0) | (numBytes & 0xF));

            return segment;
        }

        /// <summary>
        /// Releases all rented buffers.
        /// </summary>
        public void Dispose()
        {
            if (_msRented && _msBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_msBuffer);
                _msBuffer = Array.Empty<byte>();
                _msRented = false;
            }

            if (_melVlcRented && _melVlcBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_melVlcBuffer);
                _melVlcBuffer = Array.Empty<byte>();
                _melVlcRented = false;
            }
        }

        /// <summary>
        /// Emits a single bit to the MEL stream (forward with 0xFF bit-stuffing).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MelEmitBit(int v)
        {
            _melTmp = (_melTmp << 1) + v;
            _melRemainingBits--;

            if (_melRemainingBits == 0)
            {
                EnsureMelCapacity();
                _melVlcBuffer[_melPos++] = (byte)_melTmp;
                _melRemainingBits = (_melTmp == 0xFF) ? 7 : 8;
                _melTmp = 0;
            }
        }

        /// <summary>
        /// Terminates MagSgn stream per ITU-T T.814: pads remaining bits with 1s,
        /// emits final byte unless it would be 0xFF.
        /// </summary>
        private void TerminateMagSgn()
        {
            if (_msUsedBits > 0)
            {
                int t = _msMaxBits - _msUsedBits; // unused bits
                _msTmp |= (0xFFu & ((1u << t) - 1)) << _msUsedBits;
                _msUsedBits += t;

                if (_msTmp != 0xFF)
                {
                    EnsureMsCapacity();
                    _msBuffer[_msPos++] = (byte)_msTmp;
                }
            }
            else if (_msMaxBits == 7)
            {
                // Previous byte was 0xFF, which should not end the stream.
                // Back up one position per the reference implementation.
                _msPos--;
            }
        }

        /// <summary>
        /// Terminates MEL and VLC streams together per ITU-T T.814.
        /// Attempts to fuse the final MEL and VLC bytes into a single byte.
        /// </summary>
        private void TerminateMelVlc()
        {
            // If there is an incomplete MEL run, emit a 1-bit to signal it
            if (_melRun > 0)
            {
                MelEmitBit(1);
            }

            // Shift MEL tmp to fill remaining bits
            _melTmp = _melTmp << _melRemainingBits;
            int melMask = (0xFF << _melRemainingBits) & 0xFF;
            int vlcMask = 0xFF >> (8 - _vlcUsedBits);

            if ((melMask | vlcMask) == 0)
            {
                // No remaining bits to write
                return;
            }

            int fuse = _melTmp | _vlcTmp;
            if ((((fuse ^ _melTmp) & melMask) | ((fuse ^ _vlcTmp) & vlcMask)) == 0
                && fuse != 0xFF && _vlcPos > 1)
            {
                // MEL and VLC can be fused into one byte
                EnsureMelCapacity();
                _melVlcBuffer[_melPos++] = (byte)fuse;
            }
            else
            {
                // Cannot fuse; write them separately
                EnsureMelCapacity();
                _melVlcBuffer[_melPos++] = (byte)_melTmp; // melTmp cannot be 0xFF

                EnsureVlcCapacity();
                _melVlcBuffer[_vlcBufOffset - _vlcPos] = (byte)_vlcTmp;
                _vlcPos++;
            }
        }

        /// <summary>
        /// Ensures the MagSgn buffer has capacity for at least one more byte.
        /// </summary>
        private void EnsureMsCapacity()
        {
            if (_msPos >= _msBuffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_msBuffer.Length * 2);
                Buffer.BlockCopy(_msBuffer, 0, newBuffer, 0, _msPos);
                if (_msRented)
                {
                    ArrayPool<byte>.Shared.Return(_msBuffer);
                }

                _msBuffer = newBuffer;
                _msRented = true;
            }
        }

        /// <summary>
        /// Ensures the MEL region of the shared buffer has capacity.
        /// MEL writes forward from byte 0; VLC writes backward from the end.
        /// If they would collide, we grow the buffer.
        /// </summary>
        private void EnsureMelCapacity()
        {
            // MEL writes at _melPos; VLC has written _vlcPos bytes from the end.
            // Check if they would overlap.
            int vlcStart = _vlcBufOffset - _vlcPos + 1;
            if (_melPos >= vlcStart)
            {
                GrowMelVlcBuffer();
            }
        }

        /// <summary>
        /// Ensures the VLC region of the shared buffer has capacity.
        /// </summary>
        private void EnsureVlcCapacity()
        {
            int vlcWritePos = _vlcBufOffset - _vlcPos;
            if (vlcWritePos < 0 || vlcWritePos <= _melPos)
            {
                GrowMelVlcBuffer();
            }
        }

        /// <summary>
        /// Grows the shared MEL/VLC buffer, preserving both ends.
        /// MEL data stays at the beginning; VLC data stays at the end.
        /// </summary>
        private void GrowMelVlcBuffer()
        {
            int newSize = _melVlcBuffer.Length * 2;
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

            // Copy MEL data (from beginning)
            if (_melPos > 0)
            {
                Buffer.BlockCopy(_melVlcBuffer, 0, newBuffer, 0, _melPos);
            }

            // Copy VLC data (from end).
            // VLC bytes are at _melVlcBuffer[_vlcBufOffset - _vlcPos + 1 .. _vlcBufOffset]
            int newVlcBufOffset = newBuffer.Length - 1;
            if (_vlcPos > 0)
            {
                int oldVlcStart = _vlcBufOffset - _vlcPos + 1;
                int newVlcStart = newVlcBufOffset - _vlcPos + 1;
                Buffer.BlockCopy(_melVlcBuffer, oldVlcStart, newBuffer, newVlcStart, _vlcPos);
            }

            if (_melVlcRented)
            {
                ArrayPool<byte>.Shared.Return(_melVlcBuffer);
            }

            _melVlcBuffer = newBuffer;
            _melVlcRented = true;
            _vlcBufOffset = newVlcBufOffset;
        }
    }
}
