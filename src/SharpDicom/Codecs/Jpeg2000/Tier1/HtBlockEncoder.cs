using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
#if !NETSTANDARD2_0
using System.Numerics;
#endif

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT (High-Throughput) block encoder implementing <see cref="IBlockCoder"/>
    /// for the ITU-T T.814 block coding algorithm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Combines the three HT coding passes into complete HT Sets:
    /// <list type="bullet">
    ///   <item>Pass 1 (Cleanup): <see cref="HtCleanup"/> encodes significance patterns
    ///     and coefficient values at the primary bitplane.</item>
    ///   <item>Pass 2 (SigProp): <see cref="HtSigProp"/> promotes insignificant samples
    ///     with significant neighbors.</item>
    ///   <item>Pass 3 (MagRef): <see cref="HtMagRef"/> refines magnitude precision for
    ///     already-significant samples.</item>
    /// </list>
    /// </para>
    /// <para>
    /// The number of passes controls quality:
    /// <list type="bullet">
    ///   <item>1 pass: Cleanup only (fast preset, already lossless for most data).</item>
    ///   <item>3 passes: One complete HT Set (Cleanup + SigProp + MagRef).</item>
    ///   <item>6 passes: Two HT Sets (for two bitplanes of refinement).</item>
    /// </list>
    /// </para>
    /// <para>
    /// For lossless compression, a single cleanup pass is sufficient because
    /// <see cref="HtCleanup"/> encodes full-precision coefficients. Additional passes
    /// provide progressive refinement capability for truncatable bitstreams.
    /// </para>
    /// <para>
    /// Data format for multi-pass encoding: When <c>NumPasses &gt; 1</c>, the
    /// <see cref="CodeBlockData.Data"/> contains a header with cumulative pass
    /// lengths (4 bytes each, little-endian) followed by concatenated pass data.
    /// When <c>NumPasses == 1</c>, the data is the raw cleanup segment with no header.
    /// </para>
    /// </remarks>
    public sealed class HtBlockEncoder : IBlockCoder
    {
        /// <summary>
        /// Gets the shared singleton instance for sequential (non-concurrent) use.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The HT block encoder is stateless: all state is local to each
        /// encode or <see cref="DecodeBlock"/> call.
        /// The singleton is safe for both sequential and concurrent use.
        /// </para>
        /// </remarks>
        public static HtBlockEncoder Instance { get; } = new HtBlockEncoder();

        /// <summary>
        /// Maximum number of coding passes supported by the HT decoder.
        /// HT mode uses 1, 3, or 6 passes (1 or 2 HT Sets).
        /// </summary>
        internal const int MaxPasses = 6;

        /// <summary>
        /// Number of coding passes per HT Set.
        /// </summary>
        internal const int PassesPerSet = 3;

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// Encoding strategy based on MSB position:
        /// <list type="bullet">
        ///   <item>All-zero block (msbPosition &lt; 0): returns <see cref="CodeBlockData.Empty"/>.</item>
        ///   <item>msbPosition = 0: 1 pass (cleanup only, no refinement possible).</item>
        ///   <item>msbPosition = 1: up to 3 passes (1 HT Set).</item>
        ///   <item>msbPosition >= 2: up to 6 passes (2 HT Sets).</item>
        /// </list>
        /// </para>
        /// </remarks>
        public CodeBlockData EncodeBlock(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int subbandType,
            int msbPosition)
        {
            return EncodeBlock(coefficients, width, height, subbandType, msbPosition, 1);
        }

        /// <summary>
        /// Encodes a single code-block with a specified number of coding passes.
        /// </summary>
        /// <param name="coefficients">Wavelet coefficients in row-major order.</param>
        /// <param name="width">Code-block width in coefficients.</param>
        /// <param name="height">Code-block height in coefficients.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</param>
        /// <param name="msbPosition">MSB hint, or -1 for auto-detect.</param>
        /// <param name="requestedPasses">
        /// Requested number of coding passes (1, 3, or 6). The actual number may be
        /// fewer if the MSB position does not support additional passes.
        /// </param>
        /// <returns>Encoded code-block data with pass and length information.</returns>
#pragma warning disable CA1822 // Instance method: called via typed reference from J2kEncoder
        public CodeBlockData EncodeBlock(
            ReadOnlySpan<int> coefficients,
            int width, int height,
            int subbandType,
            int msbPosition,
            int requestedPasses)
#pragma warning restore CA1822
        {
            int size = width * height;
            if (coefficients.Length < size)
            {
                throw new ArgumentException(
                    $"Coefficient buffer length {coefficients.Length} is less than {width}x{height}={size}.",
                    nameof(coefficients));
            }

            // Determine MSB position if not provided
            int msb = msbPosition;
            if (msb < 0)
            {
                msb = FindMsbPosition(coefficients, size);
            }

            if (msb < 0)
            {
                // All-zero block
                return CodeBlockData.Empty;
            }

            // HT cleanup pass encodes full-precision coefficients.
            byte[] cleanupData = HtCleanup.Encode(coefficients, width, height, subbandType);

            // Determine actual pass count based on MSB and request
            int passes = requestedPasses;
            if (msb < 1 || passes <= 1)
            {
                // Cleanup only: MSB too low for refinement, or single pass requested
                return new CodeBlockData
                {
                    Data = cleanupData,
                    NumPasses = 1,
                    PassLengths = new[] { cleanupData.Length },
                    MsbPosition = msb
                };
            }

            // Cap at 3 passes if MSB only allows 1 HT Set
            if (msb < 2)
            {
                passes = Math.Min(passes, PassesPerSet);
            }
            else
            {
                passes = Math.Min(passes, MaxPasses);
            }

            // Build significance state from coefficients (after cleanup, non-zero = significant)
            byte[]? rentedSig = null;
            byte[] sigState;
            if (size <= 1024)
            {
                sigState = new byte[size];
            }
            else
            {
                rentedSig = ArrayPool<byte>.Shared.Rent(size);
                sigState = rentedSig;
            }

            try
            {
                for (int i = 0; i < size; i++)
                {
                    sigState[i] = coefficients[i] != 0 ? (byte)1 : (byte)0;
                }

                // HT Set 1: SigProp + MagRef at bitplane 0
                byte[] sigPropData = HtSigProp.Encode(
                    coefficients, sigState, width, height, subbandType, 0);
                byte[] magRefData = HtMagRef.Encode(
                    coefficients, sigState, width, height, 0);

                if (passes <= PassesPerSet)
                {
                    return BuildMultiPassResult(
                        cleanupData, sigPropData, magRefData,
                        null, null, msb, PassesPerSet);
                }

                // HT Set 2: Update significance state, refine at bitplane 1
                // Re-derive significance (SigProp may have promoted samples)
                for (int i = 0; i < size; i++)
                {
                    sigState[i] = coefficients[i] != 0 ? (byte)1 : (byte)0;
                }

                byte[] sigPropData2 = HtSigProp.Encode(
                    coefficients, sigState, width, height, subbandType, 1);
                byte[] magRefData2 = HtMagRef.Encode(
                    coefficients, sigState, width, height, 1);

                return BuildMultiPassResult(
                    cleanupData, sigPropData, magRefData,
                    sigPropData2, magRefData2, msb, MaxPasses);
            }
            finally
            {
                if (rentedSig != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedSig);
                }
            }
        }

        /// <inheritdoc />
        public void DecodeBlock(
            ReadOnlySpan<byte> data,
            int numPasses,
            Span<int> output,
            int width, int height,
            int msbPosition,
            int subbandType)
        {
            int size = width * height;
            if (output.Length < size)
            {
                throw new ArgumentException(
                    $"Output buffer length {output.Length} is less than {width}x{height}={size}.",
                    nameof(output));
            }

            output.Slice(0, size).Clear();

            if (numPasses == 0 || data.IsEmpty || msbPosition < 0)
            {
                return;
            }

            if (numPasses < 1 || numPasses > MaxPasses)
            {
                throw new ArgumentException(
                    $"Number of passes must be between 1 and {MaxPasses}, got {numPasses}.",
                    nameof(numPasses));
            }

            if (numPasses == 1)
            {
                // Raw cleanup segment, no header
                HtCleanup.Decode(data, output, width, height, subbandType);
                return;
            }

            // Multi-pass: parse embedded pass length header
            int headerSize = numPasses * 4;
            if (data.Length < headerSize)
            {
                throw new ArgumentException(
                    "Data too short for pass length header.", nameof(data));
            }

            Span<int> passLengths = stackalloc int[numPasses];
            for (int i = 0; i < numPasses; i++)
            {
                int offset = i * 4;
                passLengths[i] = data[offset] | (data[offset + 1] << 8) |
                                 (data[offset + 2] << 16) | (data[offset + 3] << 24);
            }

            // Validate pass lengths are monotonically non-decreasing and within bounds
            ReadOnlySpan<byte> passData = data.Slice(headerSize);
            for (int i = 0; i < numPasses; i++)
            {
                if (passLengths[i] < 0 || passLengths[i] > passData.Length)
                {
                    throw new InvalidDataException(
                        $"Pass length [{i}]={passLengths[i]} is out of bounds (data length={passData.Length}).");
                }
                if (i > 0 && passLengths[i] < passLengths[i - 1])
                {
                    throw new InvalidDataException(
                        $"Pass lengths are not monotonically non-decreasing: [{i-1}]={passLengths[i-1]}, [{i}]={passLengths[i]}.");
                }
            }

            // Pass 1: Cleanup
            int cleanupLen = passLengths[0];
            HtCleanup.Decode(passData.Slice(0, cleanupLen), output, width, height, subbandType);

            if (numPasses < 2)
            {
                return;
            }

            // Build significance state from cleanup output
            byte[]? rentedSig = null;
            Span<byte> sigState = size <= 1024
                ? stackalloc byte[size]
                : (rentedSig = ArrayPool<byte>.Shared.Rent(size)).AsSpan(0, size);

            try
            {
                for (int i = 0; i < size; i++)
                {
                    sigState[i] = output[i] != 0 ? (byte)1 : (byte)0;
                }

                // Pass 2: SigProp at bitplane 0
                int sigPropStart = passLengths[0];
                int sigPropLen = passLengths[1] - passLengths[0];
                HtSigProp.Decode(
                    passData.Slice(sigPropStart, sigPropLen),
                    output, sigState, width, height, subbandType, 0);

                if (numPasses < 3)
                {
                    return;
                }

                // Pass 3: MagRef at bitplane 0
                int magRefStart = passLengths[1];
                int magRefLen = passLengths[2] - passLengths[1];
                HtMagRef.Decode(
                    passData.Slice(magRefStart, magRefLen),
                    output, sigState, width, height, 0);

                if (numPasses < 4)
                {
                    return;
                }

                // HT Set 2: Update sigState and refine at bitplane 1
                for (int i = 0; i < size; i++)
                {
                    sigState[i] = output[i] != 0 ? (byte)1 : (byte)0;
                }

                // Pass 4: SigProp at bitplane 1
                int sigProp2Start = passLengths[2];
                int sigProp2Len = passLengths[3] - passLengths[2];
                HtSigProp.Decode(
                    passData.Slice(sigProp2Start, sigProp2Len),
                    output, sigState, width, height, subbandType, 1);

                if (numPasses < 5)
                {
                    return;
                }

                // Pass 5: MagRef at bitplane 1
                int magRef2Start = passLengths[3];
                int magRef2Len = passLengths[4] - passLengths[3];
                HtMagRef.Decode(
                    passData.Slice(magRef2Start, magRef2Len),
                    output, sigState, width, height, 1);

                // Pass 6: Placeholder (no additional data)
            }
            finally
            {
                if (rentedSig != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedSig);
                }
            }
        }

        /// <summary>
        /// Builds a multi-pass <see cref="CodeBlockData"/> with an embedded pass-length header.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The header consists of <paramref name="numPasses"/> cumulative pass lengths,
        /// each stored as 4-byte little-endian integers. The pass data follows immediately
        /// after the header.
        /// </para>
        /// </remarks>
        private static CodeBlockData BuildMultiPassResult(
            byte[] cleanupData,
            byte[] sigPropData,
            byte[] magRefData,
            byte[]? sigPropData2,
            byte[]? magRefData2,
            int msbPosition,
            int numPasses)
        {
            // Calculate cumulative pass lengths (relative to start of pass data, not header)
            int pass1End = cleanupData.Length;
            int pass2End = pass1End + sigPropData.Length;
            int pass3End = pass2End + magRefData.Length;

            int[] passLengths;
            int totalPassData;

            if (numPasses == 3)
            {
                passLengths = new[] { pass1End, pass2End, pass3End };
                totalPassData = pass3End;
            }
            else
            {
                // 6 passes
                int pass4End = pass3End + (sigPropData2?.Length ?? 0);
                int pass5End = pass4End + (magRefData2?.Length ?? 0);
                passLengths = new[] { pass1End, pass2End, pass3End, pass4End, pass5End, pass5End };
                totalPassData = pass5End;
            }

            // Build data with header
            int headerSize = numPasses * 4;
            byte[] combinedData = new byte[headerSize + totalPassData];

            // Write pass length header (little-endian)
            for (int i = 0; i < numPasses; i++)
            {
                int offset = i * 4;
                int len = passLengths[i];
                combinedData[offset] = (byte)(len & 0xFF);
                combinedData[offset + 1] = (byte)((len >> 8) & 0xFF);
                combinedData[offset + 2] = (byte)((len >> 16) & 0xFF);
                combinedData[offset + 3] = (byte)((len >> 24) & 0xFF);
            }

            // Copy pass data
            int dst = headerSize;
            Buffer.BlockCopy(cleanupData, 0, combinedData, dst, cleanupData.Length);
            dst += cleanupData.Length;
            Buffer.BlockCopy(sigPropData, 0, combinedData, dst, sigPropData.Length);
            dst += sigPropData.Length;
            Buffer.BlockCopy(magRefData, 0, combinedData, dst, magRefData.Length);
            dst += magRefData.Length;

            if (sigPropData2 != null)
            {
                Buffer.BlockCopy(sigPropData2, 0, combinedData, dst, sigPropData2.Length);
                dst += sigPropData2.Length;
            }

            if (magRefData2 != null)
            {
                Buffer.BlockCopy(magRefData2, 0, combinedData, dst, magRefData2.Length);
            }

            // PassLengths in CodeBlockData are cumulative including header
            int[] externalPassLengths = new int[numPasses];
            for (int i = 0; i < numPasses; i++)
            {
                externalPassLengths[i] = headerSize + passLengths[i];
            }

            return new CodeBlockData
            {
                Data = combinedData,
                NumPasses = numPasses,
                PassLengths = externalPassLengths,
                MsbPosition = msbPosition
            };
        }

        /// <summary>
        /// Finds the most significant bit position across all coefficients.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindMsbPosition(ReadOnlySpan<int> coefficients, int count)
        {
            int maxMag = 0;
            for (int i = 0; i < count; i++)
            {
                int mag = Math.Abs(coefficients[i]);
                if (mag > maxMag)
                {
                    maxMag = mag;
                }
            }

            if (maxMag == 0)
            {
                return -1;
            }

#if !NETSTANDARD2_0
            return 31 - BitOperations.LeadingZeroCount((uint)maxMag);
#else
            int msb = 0;
            uint v = (uint)maxMag;
            if (v >= 0x10000) { msb += 16; v >>= 16; }
            if (v >= 0x100) { msb += 8; v >>= 8; }
            if (v >= 0x10) { msb += 4; v >>= 4; }
            if (v >= 0x4) { msb += 2; v >>= 2; }
            if (v >= 0x2) { msb += 1; }
            return msb;
#endif
        }

        /// <summary>
        /// Updates significance state after a SigProp pass by checking which
        /// previously-insignificant samples with significant neighbors have become
        /// significant at the specified bitplane.
        /// </summary>
        private static void UpdateSigStateFromSigProp(
            ReadOnlySpan<int> coefficients,
            Span<byte> sigState,
            int width, int height,
            int bitplane)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    if (sigState[idx] != 0)
                    {
                        continue;
                    }

                    if (!HasSignificantNeighborInState(sigState, x, y, width, height))
                    {
                        continue;
                    }

                    int absVal = Math.Abs(coefficients[idx]);
                    if (((absVal >> bitplane) & 1) != 0)
                    {
                        sigState[idx] = 1;
                    }
                }
            }
        }

        /// <summary>
        /// Checks for significant neighbors in the significance state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasSignificantNeighborInState(
            ReadOnlySpan<byte> sigState, int x, int y, int width, int height)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                {
                    continue;
                }

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    if (nx < 0 || nx >= width)
                    {
                        continue;
                    }

                    if (sigState[ny * width + nx] != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
