using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// Three-stream reader for HT cleanup pass codeword segments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The HT cleanup pass packs three byte-streams into a single codeword segment:
    /// <list type="bullet">
    ///   <item>MagSgn: reads forward from byte 0 (magnitude and sign bits)</item>
    ///   <item>VLC: reads forward from a boundary offset (variable-length codes)</item>
    ///   <item>MEL: reads backward from the end of the segment (run-length codes)</item>
    /// </list>
    /// </para>
    /// <para>
    /// The ILW (Interface Locator Word) is stored in the last 2 bytes of the segment.
    /// It is a 12-bit value encoding the byte offset where VLC data begins (also marking
    /// the end of MagSgn data). The MEL data starts just before the ILW bytes and grows
    /// backward.
    /// </para>
    /// <para>
    /// Segment layout:
    /// <code>
    /// [MagSgn bytes...] [VLC bytes...] [MEL bytes (reversed)] [ILW (2 bytes)]
    ///  ^-- forward        ^-- vlcStart     ^-- backward           ^-- segment end
    /// </code>
    /// </para>
    /// </remarks>
    internal ref struct HtCleanupReader
    {
        // MagSgn stream state (forward reader)
        private readonly ReadOnlySpan<byte> _segment;
        private int _magSgnBytePos;
        private uint _magSgnBitBuffer;
        private int _magSgnBitsAvailable;

        // VLC stream state (forward reader from vlcStart)
        private int _vlcBytePos;
        private uint _vlcBitBuffer;
        private int _vlcBitsAvailable;
        private readonly int _vlcEnd;    // End of VLC data (start of MEL backwards region)

        // MEL decoder (backward reader)
        private MelDecoder _melDecoder;

        // Stream boundaries
        private readonly int _vlcStart;
        private readonly int _melEnd;    // Last MEL byte position (just before ILW)

        /// <summary>
        /// Initializes a new HT cleanup reader by parsing the segment and locating stream boundaries.
        /// </summary>
        /// <param name="segment">The cleanup codeword segment bytes.</param>
        /// <exception cref="ArgumentException">Thrown when segment is too short (minimum 2 bytes for ILW).</exception>
        public HtCleanupReader(ReadOnlySpan<byte> segment)
        {
            if (segment.Length < 2)
            {
                throw new ArgumentException("Cleanup segment must be at least 2 bytes (ILW).", nameof(segment));
            }

            _segment = segment;

            // Parse ILW from the last 2 bytes
            // ILW is a 12-bit value: upper 8 bits from second-to-last byte,
            // lower 4 bits from upper nibble of last byte
            int ilwByte0 = segment[segment.Length - 2];
            int ilwByte1 = segment[segment.Length - 1];
            int vlcOffset = (ilwByte0 << 4) | (ilwByte1 >> 4);

            _vlcStart = vlcOffset;

            // MEL data ends just before the ILW (2 bytes from end)
            _melEnd = segment.Length - 2;

            // VLC data goes from vlcStart to just before MEL backward region
            // The VLC and MEL share the middle region; VLC reads forward, MEL backward
            _vlcEnd = _melEnd;

            // Initialize MagSgn forward reader at byte 0
            _magSgnBytePos = 0;
            _magSgnBitBuffer = 0;
            _magSgnBitsAvailable = 0;

            // Initialize VLC forward reader at vlcStart
            _vlcBytePos = _vlcStart;
            _vlcBitBuffer = 0;
            _vlcBitsAvailable = 0;

            // Initialize MEL backward reader
            _melDecoder = new MelDecoder(segment, _melEnd);
        }

        /// <summary>
        /// Gets the VLC start offset parsed from the ILW.
        /// </summary>
        public readonly int VlcStart => _vlcStart;

        /// <summary>
        /// Gets the MEL end position (last byte before ILW).
        /// </summary>
        public readonly int MelEnd => _melEnd;

        /// <summary>
        /// Reads bits from the MagSgn stream (forward).
        /// </summary>
        /// <param name="count">Number of bits to read (1-25).</param>
        /// <returns>The read bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadMagSgnBits(int count)
        {
            while (_magSgnBitsAvailable < count)
            {
                FillMagSgnBuffer();
            }

            _magSgnBitsAvailable -= count;
            uint bits = (_magSgnBitBuffer >> _magSgnBitsAvailable) & ((1u << count) - 1);
            return bits;
        }

        /// <summary>
        /// Peeks bits from the VLC stream without advancing the position.
        /// </summary>
        /// <param name="count">Number of bits to peek (1-7, typically 7).</param>
        /// <returns>The peeked bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekVlcBits(int count)
        {
            while (_vlcBitsAvailable < count)
            {
                FillVlcBuffer();
            }

            return (_vlcBitBuffer >> (_vlcBitsAvailable - count)) & ((1u << count) - 1);
        }

        /// <summary>
        /// Advances the VLC stream position by the specified number of bits.
        /// </summary>
        /// <param name="bits">Number of bits to advance.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AdvanceVlc(int bits)
        {
            _vlcBitsAvailable -= bits;
        }

        /// <summary>
        /// Reads VLC bits: peeks and then advances.
        /// </summary>
        /// <param name="count">Number of bits to read.</param>
        /// <returns>The read bits, right-aligned.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVlcBits(int count)
        {
            uint bits = PeekVlcBits(count);
            AdvanceVlc(count);
            return bits;
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
        /// Fills the MagSgn bit buffer from the next byte in the MagSgn region.
        /// </summary>
        private void FillMagSgnBuffer()
        {
            if (_magSgnBytePos >= _vlcStart)
            {
                // Past MagSgn region - pad with zeros
                _magSgnBitBuffer = (_magSgnBitBuffer << 8);
                _magSgnBitsAvailable += 8;
                return;
            }

            byte b = _segment[_magSgnBytePos++];
            _magSgnBitBuffer = (_magSgnBitBuffer << 8) | b;
            _magSgnBitsAvailable += 8;
        }

        /// <summary>
        /// Fills the VLC bit buffer from the next byte in the VLC region.
        /// </summary>
        private void FillVlcBuffer()
        {
            if (_vlcBytePos >= _vlcEnd)
            {
                // Past VLC region - pad with zeros
                _vlcBitBuffer = (_vlcBitBuffer << 8);
                _vlcBitsAvailable += 8;
                return;
            }

            byte b = _segment[_vlcBytePos++];
            _vlcBitBuffer = (_vlcBitBuffer << 8) | b;
            _vlcBitsAvailable += 8;
        }
    }

    /// <summary>
    /// Three-stream writer for constructing HT cleanup pass codeword segments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Builds a cleanup segment by writing three streams independently:
    /// <list type="bullet">
    ///   <item>MagSgn: written forward from byte 0</item>
    ///   <item>VLC: written forward into a separate buffer</item>
    ///   <item>MEL: written forward into a separate buffer (reversed during finalization)</item>
    /// </list>
    /// </para>
    /// <para>
    /// During <see cref="Finalize"/>, the three streams are merged into a single segment
    /// with the ILW (Interface Locator Word) appended at the end.
    /// </para>
    /// </remarks>
    internal ref struct HtCleanupWriter
    {
        // MagSgn stream (forward, built in place)
        private byte[] _magSgnBuffer;
        private int _magSgnPos;
        private uint _magSgnBitBuffer;
        private int _magSgnBitsInBuffer;
        private bool _magSgnRented;

        // VLC stream (forward, separate buffer)
        private byte[] _vlcBuffer;
        private int _vlcPos;
        private uint _vlcBitBuffer;
        private int _vlcBitsInBuffer;
        private bool _vlcRented;

        // MEL encoder
        private MelEncoder _melEncoder;

        /// <summary>
        /// Initializes a new HT cleanup writer.
        /// </summary>
        /// <param name="estimatedSize">Estimated total segment size in bytes.</param>
        public HtCleanupWriter(int estimatedSize)
        {
            int bufSize = Math.Max(estimatedSize, 64);
            _magSgnBuffer = ArrayPool<byte>.Shared.Rent(bufSize);
            _magSgnRented = true;
            _magSgnPos = 0;
            _magSgnBitBuffer = 0;
            _magSgnBitsInBuffer = 0;

            _vlcBuffer = ArrayPool<byte>.Shared.Rent(bufSize);
            _vlcRented = true;
            _vlcPos = 0;
            _vlcBitBuffer = 0;
            _vlcBitsInBuffer = 0;

            _melEncoder = new MelEncoder(bufSize / 4);
        }

        /// <summary>
        /// Writes bits to the MagSgn stream.
        /// </summary>
        /// <param name="bits">Bit value to write, right-aligned.</param>
        /// <param name="count">Number of bits to write (1-25).</param>
        public void WriteMagSgnBits(uint bits, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                _magSgnBitBuffer = (_magSgnBitBuffer << 1) | ((bits >> i) & 1);
                _magSgnBitsInBuffer++;

                if (_magSgnBitsInBuffer == 8)
                {
                    EnsureMagSgnCapacity();
                    _magSgnBuffer[_magSgnPos++] = (byte)_magSgnBitBuffer;
                    _magSgnBitBuffer = 0;
                    _magSgnBitsInBuffer = 0;
                }
            }
        }

        /// <summary>
        /// Writes bits to the VLC stream.
        /// </summary>
        /// <param name="bits">Bit value to write, right-aligned.</param>
        /// <param name="count">Number of bits to write (1-7).</param>
        public void WriteVlcBits(uint bits, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                _vlcBitBuffer = (_vlcBitBuffer << 1) | ((bits >> i) & 1);
                _vlcBitsInBuffer++;

                if (_vlcBitsInBuffer == 8)
                {
                    EnsureVlcCapacity();
                    _vlcBuffer[_vlcPos++] = (byte)_vlcBitBuffer;
                    _vlcBitBuffer = 0;
                    _vlcBitsInBuffer = 0;
                }
            }
        }

        /// <summary>
        /// Encodes a MEL quad significance value.
        /// </summary>
        /// <param name="isSignificant">Whether the quad is significant.</param>
        public void EncodeMel(bool isSignificant)
        {
            _melEncoder.EncodeQuadSignificance(isSignificant);
        }

        /// <summary>
        /// Finalizes the segment by merging all three streams and appending the ILW.
        /// </summary>
        /// <returns>The complete cleanup codeword segment.</returns>
        /// <remarks>
        /// <para>
        /// Segment layout after finalization:
        /// <code>
        /// [MagSgn bytes] [VLC bytes] [MEL bytes (reversed)] [ILW (2 bytes)]
        /// </code>
        /// </para>
        /// <para>
        /// The ILW encodes the byte offset where VLC data starts (= MagSgn length).
        /// </para>
        /// </remarks>
        public byte[] Finalize()
        {
            // Flush remaining MagSgn bits
            if (_magSgnBitsInBuffer > 0)
            {
                _magSgnBitBuffer <<= (8 - _magSgnBitsInBuffer);
                EnsureMagSgnCapacity();
                _magSgnBuffer[_magSgnPos++] = (byte)_magSgnBitBuffer;
                _magSgnBitBuffer = 0;
                _magSgnBitsInBuffer = 0;
            }

            // Flush remaining VLC bits
            if (_vlcBitsInBuffer > 0)
            {
                _vlcBitBuffer <<= (8 - _vlcBitsInBuffer);
                EnsureVlcCapacity();
                _vlcBuffer[_vlcPos++] = (byte)_vlcBitBuffer;
                _vlcBitBuffer = 0;
                _vlcBitsInBuffer = 0;
            }

            // Flush MEL encoder
            ReadOnlySpan<byte> melData = _melEncoder.Flush();

            // Calculate ILW value (VLC start offset = MagSgn length)
            int vlcOffset = _magSgnPos;

            if (vlcOffset > 4095)
            {
                throw new InvalidOperationException(
                    $"VLC start offset {vlcOffset} exceeds the 12-bit ILW maximum of 4095. " +
                    "The code-block is too large to encode in a single HT cleanup pass segment.");
            }

            // Total segment size: MagSgn + VLC + MEL (reversed) + 2 (ILW)
            int totalSize = _magSgnPos + _vlcPos + melData.Length + 2;
            byte[] segment = new byte[totalSize];

            // Copy MagSgn (forward)
            Buffer.BlockCopy(_magSgnBuffer, 0, segment, 0, _magSgnPos);

            // Copy VLC (forward, starting at vlcOffset)
            Buffer.BlockCopy(_vlcBuffer, 0, segment, _magSgnPos, _vlcPos);

            // Copy MEL (reversed into segment)
            int melDst = _magSgnPos + _vlcPos;
            for (int i = 0; i < melData.Length; i++)
            {
                segment[melDst + i] = melData[melData.Length - 1 - i];
            }

            // Write ILW at the end (12-bit value in 2 bytes)
            // ILW byte 0: upper 8 bits of vlcOffset
            // ILW byte 1: lower 4 bits of vlcOffset in upper nibble, lower nibble = 0
            segment[totalSize - 2] = (byte)(vlcOffset >> 4);
            segment[totalSize - 1] = (byte)((vlcOffset & 0x0F) << 4);

            return segment;
        }

        /// <summary>
        /// Releases all rented buffers.
        /// </summary>
        public void Dispose()
        {
            if (_magSgnRented && _magSgnBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_magSgnBuffer);
                _magSgnBuffer = Array.Empty<byte>();
                _magSgnRented = false;
            }
            if (_vlcRented && _vlcBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_vlcBuffer);
                _vlcBuffer = Array.Empty<byte>();
                _vlcRented = false;
            }
            _melEncoder.Dispose();
        }

        private void EnsureMagSgnCapacity()
        {
            if (_magSgnPos >= _magSgnBuffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_magSgnBuffer.Length * 2);
                Buffer.BlockCopy(_magSgnBuffer, 0, newBuffer, 0, _magSgnPos);
                if (_magSgnRented) ArrayPool<byte>.Shared.Return(_magSgnBuffer);
                _magSgnBuffer = newBuffer;
                _magSgnRented = true;
            }
        }

        private void EnsureVlcCapacity()
        {
            if (_vlcPos >= _vlcBuffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(_vlcBuffer.Length * 2);
                Buffer.BlockCopy(_vlcBuffer, 0, newBuffer, 0, _vlcPos);
                if (_vlcRented) ArrayPool<byte>.Shared.Return(_vlcBuffer);
                _vlcBuffer = newBuffer;
                _vlcRented = true;
            }
        }
    }
}
