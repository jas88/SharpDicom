using System;
using System.Buffers;
using System.Runtime.CompilerServices;
#if !NETSTANDARD2_0
using System.Numerics;
#endif

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT Cleanup pass encoder and decoder for the ITU-T T.814 High-Throughput block coder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Cleanup pass is the primary coding pass in the HT algorithm. It processes
    /// wavelet coefficients in 2x2 quad groups, encoding:
    /// <list type="bullet">
    ///   <item>Quad significance patterns via VLC (Variable Length Codes)</item>
    ///   <item>Runs of insignificant quads via MEL (adaptive run-length coding)</item>
    ///   <item>Magnitude exponents and sign bits via MagSgn</item>
    /// </list>
    /// </para>
    /// <para>
    /// The Cleanup pass alone provides a valid "cleanup-only" fast encoding mode. In full
    /// HT Sets, it is followed by optional SigProp and MagRef refinement passes.
    /// </para>
    /// <para>
    /// Coefficients are arranged in row-major order. The pass processes them in 2x2 quads
    /// scanning left-to-right, top-to-bottom. Blocks with odd width or height are padded
    /// with implicit zeros.
    /// </para>
    /// </remarks>
    internal static class HtCleanup
    {
        /// <summary>
        /// Quad sample index layout within a 2x2 block:
        /// <code>
        /// bit 0: top-left      (row r, col c)
        /// bit 1: top-right     (row r, col c+1)
        /// bit 2: bottom-left   (row r+1, col c)
        /// bit 3: bottom-right  (row r+1, col c+1)
        /// </code>
        /// </summary>
        private const int TopLeft = 0;
        private const int TopRight = 1;
        private const int BottomLeft = 2;
        private const int BottomRight = 3;

        /// <summary>
        /// Number of VLC encode table contexts.
        /// </summary>
        private const int NumContexts = 8;

        /// <summary>
        /// Maximum number of significance patterns per context (4 bits = 16 patterns).
        /// </summary>
        private const int MaxPatternsPerContext = 16;

        /// <summary>
        /// VLC encode table for Table0: maps (context, sigPattern) to (codeword, length).
        /// Lazy-initialized for thread safety.
        /// </summary>
        private static readonly Lazy<VlcEncodeEntry[,]> _vlcEncode0 =
            new Lazy<VlcEncodeEntry[,]>(() => BuildVlcEncodeTable(VlcTable.Table0));

        /// <summary>
        /// VLC encode table for Table1: maps (context, sigPattern) to (codeword, length).
        /// </summary>
        private static readonly Lazy<VlcEncodeEntry[,]> _vlcEncode1 =
            new Lazy<VlcEncodeEntry[,]>(() => BuildVlcEncodeTable(VlcTable.Table1));

        /// <summary>
        /// Encodes wavelet coefficients using the HT Cleanup pass.
        /// </summary>
        /// <param name="coefficients">
        /// Wavelet coefficients in row-major order. Length must be width * height.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="subbandType">
        /// Subband type: 0=LL, 1=LH, 2=HL, 3=HH.
        /// Controls which VLC table is used for even vs odd quad rows in a stripe.
        /// </param>
        /// <returns>The cleanup codeword segment as a byte array.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when coefficient count does not match width * height.
        /// </exception>
        public static byte[] Encode(
            ReadOnlySpan<int> coefficients, int width, int height, int subbandType)
        {
            if (coefficients.Length != width * height)
            {
                throw new ArgumentException(
                    $"Coefficient count {coefficients.Length} does not match {width}x{height}={width * height}.",
                    nameof(coefficients));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Width and height must be positive.");
            }

            int quadW = (width + 1) >> 1;
            int quadH = (height + 1) >> 1;

            // Significance state array for context formation
            int sigStateSize = quadW * quadH;
            byte[]? rentedSigState = null;
            Span<byte> sigState = sigStateSize <= 1024
                ? stackalloc byte[sigStateSize]
                : (rentedSigState = ArrayPool<byte>.Shared.Rent(sigStateSize)).AsSpan(0, sigStateSize);
            sigState.Clear();

            // Estimate segment size (generous: ~4 bits/sample worst case + overhead)
            int estimatedSize = Math.Max(64, (width * height * 4) / 8 + 32);
            var writer = new HtCleanupWriter(estimatedSize);

            try
            {
                var encode0 = _vlcEncode0.Value;
                var encode1 = _vlcEncode1.Value;

                for (int qr = 0; qr < quadH; qr++)
                {
                    for (int qc = 0; qc < quadW; qc++)
                    {
                        int r = qr * 2;
                        int c = qc * 2;

                        // Extract quad coefficients (pad with zero for odd dimensions)
                        int v0 = GetCoefficient(coefficients, r, c, width, height);
                        int v1 = GetCoefficient(coefficients, r, c + 1, width, height);
                        int v2 = GetCoefficient(coefficients, r + 1, c, width, height);
                        int v3 = GetCoefficient(coefficients, r + 1, c + 1, width, height);

                        // Determine significance pattern
                        int sigPattern = 0;
                        if (v0 != 0) sigPattern |= (1 << TopLeft);
                        if (v1 != 0) sigPattern |= (1 << TopRight);
                        if (v2 != 0) sigPattern |= (1 << BottomLeft);
                        if (v3 != 0) sigPattern |= (1 << BottomRight);

                        bool isSignificant = sigPattern != 0;

                        // MEL encode: significant or insignificant quad
                        writer.EncodeMel(isSignificant);

                        if (isSignificant)
                        {
                            // Store significance state for context
                            sigState[qr * quadW + qc] = 1;

                            // Form 3-bit context from neighbours
                            int context = FormContext(sigState, qr, qc, quadW);

                            // Select VLC table based on quad row within stripe pair
                            bool useTable1 = ShouldUseTable1(qr, subbandType);
                            var encodeTable = useTable1 ? encode1 : encode0;

                            // VLC encode significance pattern
                            var vlcEntry = encodeTable[context, sigPattern];
                            writer.WriteVlcBits(vlcEntry.Codeword, vlcEntry.Length);

                            // Encode MagSgn for each significant sample
                            EncodeMagSgn(ref writer, v0, v1, v2, v3, sigPattern);
                        }
                    }
                }

                return writer.Finalize();
            }
            finally
            {
                writer.Dispose();
                if (rentedSigState != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedSigState);
                }
            }
        }

        /// <summary>
        /// Decodes a cleanup codeword segment to reconstruct wavelet coefficients.
        /// </summary>
        /// <param name="segment">The cleanup codeword segment bytes.</param>
        /// <param name="output">
        /// Output buffer for reconstructed coefficients. Must be at least width * height.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="subbandType">
        /// Subband type: 0=LL, 1=LH, 2=HL, 3=HH.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Thrown when output buffer is too small for the specified dimensions.
        /// </exception>
        public static void Decode(
            ReadOnlySpan<byte> segment, Span<int> output, int width, int height, int subbandType)
        {
            if (output.Length < width * height)
            {
                throw new ArgumentException(
                    $"Output buffer length {output.Length} is less than {width}x{height}={width * height}.",
                    nameof(output));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Width and height must be positive.");
            }

            // Clear output
            output.Slice(0, width * height).Clear();

            int quadW = (width + 1) >> 1;
            int quadH = (height + 1) >> 1;

            // Significance state for context formation
            int sigStateSize = quadW * quadH;
            byte[]? rentedSigState = null;
            Span<byte> sigState = sigStateSize <= 1024
                ? stackalloc byte[sigStateSize]
                : (rentedSigState = ArrayPool<byte>.Shared.Rent(sigStateSize)).AsSpan(0, sigStateSize);
            sigState.Clear();

            var reader = new HtCleanupReader(segment);

            try
            {
                for (int qr = 0; qr < quadH; qr++)
                {
                    for (int qc = 0; qc < quadW; qc++)
                    {
                        // MEL decode: is this quad significant?
                        bool isSignificant = reader.DecodeMelSignificance();

                        if (!isSignificant)
                        {
                            // Insignificant quad: all four samples are zero (already cleared)
                            continue;
                        }

                        // Mark as significant for context
                        sigState[qr * quadW + qc] = 1;

                        // Form context from neighbours
                        int context = FormContext(sigState, qr, qc, quadW);

                        // Select VLC table
                        bool useTable1 = ShouldUseTable1(qr, subbandType);

                        // Peek 7 VLC bits and decode
                        uint vlcBits = reader.PeekVlcBits(7);

                        byte sigPattern;
                        int codewordLen;

                        if (useTable1)
                        {
                            (sigPattern, _, codewordLen) =
                                VlcTable.DecodeTable1((int)vlcBits, context);
                        }
                        else
                        {
                            (sigPattern, _, codewordLen) =
                                VlcTable.DecodeTable0((int)vlcBits, context);
                        }

                        reader.AdvanceVlc(codewordLen);

                        // Decode MagSgn for each significant sample
                        int r = qr * 2;
                        int c = qc * 2;

                        DecodeMagSgnQuad(ref reader, output, sigPattern, r, c, width, height);
                    }
                }
            }
            finally
            {
                if (rentedSigState != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedSigState);
                }
            }
        }

        /// <summary>
        /// Determines whether to use VLC Table 1 (vs Table 0) for a quad row.
        /// </summary>
        /// <remarks>
        /// Within a stripe of 4 sample rows (2 quad rows), the first quad row uses Table 0
        /// and the second uses Table 1. The subband type does not affect table selection
        /// in the cleanup pass.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldUseTable1(int quadRow, int subbandType)
        {
            return (quadRow & 1) != 0;
        }

        /// <summary>
        /// Forms a 3-bit VLC context from neighbour quad significance.
        /// </summary>
        /// <remarks>
        /// Context bits:
        /// <list type="bullet">
        ///   <item>bit 0: left neighbour significant (quad at qc-1, same qr)</item>
        ///   <item>bit 1: above-left neighbour significant (quad at qc-1, qr-1)</item>
        ///   <item>bit 2: above neighbour significant (quad at qc, qr-1)</item>
        /// </list>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FormContext(
            ReadOnlySpan<byte> sigState, int qr, int qc, int quadW)
        {
            int ctx = 0;

            // Left neighbour
            if (qc > 0 && sigState[qr * quadW + (qc - 1)] != 0)
            {
                ctx |= 1;
            }

            // Above-left neighbour
            if (qr > 0 && qc > 0 && sigState[(qr - 1) * quadW + (qc - 1)] != 0)
            {
                ctx |= 2;
            }

            // Above neighbour
            if (qr > 0 && sigState[(qr - 1) * quadW + qc] != 0)
            {
                ctx |= 4;
            }

            return ctx;
        }

        /// <summary>
        /// Gets a coefficient value with bounds checking for odd-dimension padding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetCoefficient(
            ReadOnlySpan<int> coefficients, int row, int col, int width, int height)
        {
            if (row >= height || col >= width)
            {
                return 0;
            }
            return coefficients[row * width + col];
        }

        /// <summary>
        /// Encodes magnitude and sign for significant samples in a quad to MagSgn.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For each significant sample, the MagSgn stream encodes:
        /// <list type="number">
        ///   <item>1 sign bit: 0 for positive, 1 for negative</item>
        ///   <item>Magnitude exponent in unary: (E-1) one-bits followed by a 0-bit
        ///         (where E = floor(log2(|v|)) + 1)</item>
        ///   <item>Magnitude mantissa: the lower (E-1) bits of |v|</item>
        /// </list>
        /// Total bits per sample = 1 + E + (E-1) = 2E for E >= 1.
        /// For |v|=1 (E=1): 1 sign + 0 ones + 0-terminator = 2 bits.
        /// For |v|=2 (E=2): 1 sign + 1 one + 0-term + 1 mantissa = 4 bits.
        /// </para>
        /// </remarks>
        private static void EncodeMagSgn(
            ref HtCleanupWriter writer,
            int v0, int v1, int v2, int v3,
            int sigPattern)
        {
            if ((sigPattern & (1 << TopLeft)) != 0)
            {
                EncodeSampleMagSgn(ref writer, v0);
            }
            if ((sigPattern & (1 << TopRight)) != 0)
            {
                EncodeSampleMagSgn(ref writer, v1);
            }
            if ((sigPattern & (1 << BottomLeft)) != 0)
            {
                EncodeSampleMagSgn(ref writer, v2);
            }
            if ((sigPattern & (1 << BottomRight)) != 0)
            {
                EncodeSampleMagSgn(ref writer, v3);
            }
        }

        /// <summary>
        /// Encodes a single significant sample's magnitude and sign to MagSgn.
        /// </summary>
        /// <remarks>
        /// Format: [sign:1] [(E-1) one-bits] [0-terminator:1] [(E-1) mantissa bits]
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeSampleMagSgn(ref HtCleanupWriter writer, int value)
        {
            int absVal = Math.Abs(value);
            uint sign = value < 0 ? 1u : 0u;

            // Magnitude exponent E = floor(log2(|v|)) + 1
            int e = FloorLog2(absVal) + 1;

            // Sign bit
            writer.WriteMagSgnBits(sign, 1);

            // Unary exponent: (E-1) one-bits then 0-terminator
            for (int i = 0; i < e - 1; i++)
            {
                writer.WriteMagSgnBits(1, 1);
            }
            writer.WriteMagSgnBits(0, 1);

            // Mantissa: lower (E-1) bits of |v|
            int magBits = e - 1;
            if (magBits > 0)
            {
                uint mantissa = (uint)(absVal & ((1 << magBits) - 1));
                writer.WriteMagSgnBits(mantissa, magBits);
            }
        }

        /// <summary>
        /// Decodes magnitude and sign for all significant samples in a quad from MagSgn.
        /// </summary>
        private static void DecodeMagSgnQuad(
            ref HtCleanupReader reader, Span<int> output,
            byte sigPattern,
            int row, int col, int width, int height)
        {
            if ((sigPattern & (1 << TopLeft)) != 0)
            {
                int value = DecodeSampleMagSgn(ref reader);
                if (row < height && col < width)
                {
                    output[row * width + col] = value;
                }
            }
            if ((sigPattern & (1 << TopRight)) != 0)
            {
                int value = DecodeSampleMagSgn(ref reader);
                if (row < height && (col + 1) < width)
                {
                    output[row * width + (col + 1)] = value;
                }
            }
            if ((sigPattern & (1 << BottomLeft)) != 0)
            {
                int value = DecodeSampleMagSgn(ref reader);
                if ((row + 1) < height && col < width)
                {
                    output[(row + 1) * width + col] = value;
                }
            }
            if ((sigPattern & (1 << BottomRight)) != 0)
            {
                int value = DecodeSampleMagSgn(ref reader);
                if ((row + 1) < height && (col + 1) < width)
                {
                    output[(row + 1) * width + (col + 1)] = value;
                }
            }
        }

        /// <summary>
        /// Decodes a single significant sample's magnitude and sign from MagSgn.
        /// </summary>
        /// <remarks>
        /// Reads: [sign:1] [(E-1) one-bits] [0-terminator:1] [(E-1) mantissa bits]
        /// Reconstructs |v| = (1 &lt;&lt; (E-1)) | mantissa, then applies sign.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DecodeSampleMagSgn(ref HtCleanupReader reader)
        {
            // Sign bit
            uint signBit = reader.ReadMagSgnBits(1);

            // Unary exponent: count 1-bits until 0-bit terminator
            int e = 1;
            while (reader.ReadMagSgnBits(1) == 1)
            {
                e++;
            }
            // The 0-bit was consumed as the terminator

            // Mantissa: (E-1) bits
            int absVal;
            int magBits = e - 1;
            if (magBits > 0)
            {
                uint mantissa = reader.ReadMagSgnBits(magBits);
                absVal = (1 << magBits) | (int)mantissa;
            }
            else
            {
                absVal = 1;
            }

            return signBit == 1 ? -absVal : absVal;
        }

        /// <summary>
        /// Computes floor(log2(n)) for positive n.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorLog2(int n)
        {
#if !NETSTANDARD2_0
            return 31 - BitOperations.LeadingZeroCount((uint)n);
#else
            int result = 0;
            uint v = (uint)n;
            if (v >= 0x10000) { result += 16; v >>= 16; }
            if (v >= 0x100) { result += 8; v >>= 8; }
            if (v >= 0x10) { result += 4; v >>= 4; }
            if (v >= 0x4) { result += 2; v >>= 2; }
            if (v >= 0x2) { result += 1; }
            return result;
#endif
        }

        /// <summary>
        /// Builds a VLC encode table by inverting a VLC decode table.
        /// </summary>
        /// <remarks>
        /// For each context and significance pattern, finds the shortest codeword that
        /// decodes to that pattern, in MSB-first form for writing to the VLC stream.
        /// </remarks>
        private static VlcEncodeEntry[,] BuildVlcEncodeTable(ushort[] decodeTable)
        {
            var encodeTable = new VlcEncodeEntry[NumContexts, MaxPatternsPerContext];

            for (int ctx = 0; ctx < NumContexts; ctx++)
            {
                for (int cw = 0; cw < 128; cw++)
                {
                    int index = (ctx << 7) | cw;
                    ushort entry = decodeTable[index];

                    int sigPattern = entry & 0x0F;
                    int length = (entry >> 8) & 0x0F;

                    if (length == 0)
                    {
                        continue;
                    }

                    var existing = encodeTable[ctx, sigPattern];
                    if (existing.Length == 0 || length < existing.Length)
                    {
                        // Extract the significant bits of the codeword (LSB-first in table)
                        uint cwBits = (uint)(cw & ((1 << length) - 1));

                        // Reverse from LSB-first (table index) to MSB-first (stream write)
                        uint msbFirst = ReverseBits(cwBits, length);

                        encodeTable[ctx, sigPattern] = new VlcEncodeEntry(msbFirst, length);
                    }
                }
            }

            return encodeTable;
        }

        /// <summary>
        /// Reverses the lower N bits of a value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReverseBits(uint value, int numBits)
        {
            uint result = 0;
            for (int i = 0; i < numBits; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            return result;
        }

        /// <summary>
        /// A VLC encode table entry mapping a significance pattern to its codeword.
        /// </summary>
        internal readonly struct VlcEncodeEntry
        {
            /// <summary>VLC codeword bits in MSB-first order, right-aligned.</summary>
            public readonly uint Codeword;

            /// <summary>Length of the codeword in bits (1-7). 0 = invalid/uninitialized.</summary>
            public readonly int Length;

            public VlcEncodeEntry(uint codeword, int length)
            {
                Codeword = codeword;
                Length = length;
            }
        }
    }
}
