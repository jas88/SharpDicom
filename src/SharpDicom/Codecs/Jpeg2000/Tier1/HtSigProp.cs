using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT SigProp (Significance Propagation) refinement pass for the ITU-T T.814
    /// High-Throughput block coder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SigProp pass processes samples that are NOT yet significant (after the
    /// Cleanup pass) but have at least one significant neighbor. For each such sample:
    /// <list type="bullet">
    ///   <item>A significance bit is encoded (1 = becomes significant, 0 = remains insignificant)</item>
    ///   <item>If newly significant: sign bit and magnitude bits are encoded</item>
    /// </list>
    /// </para>
    /// <para>
    /// This pass uses a simple byte-aligned bitstream format for self-consistent
    /// encode/decode. The bitstream is not the three-stream VLC/MEL/MagSgn format
    /// used by the Cleanup pass.
    /// </para>
    /// <para>
    /// SigProp is the second coding pass in an HT Set (after Cleanup, before MagRef).
    /// It refines the coefficient representation by promoting samples with significant
    /// neighbors from insignificant to significant status.
    /// </para>
    /// </remarks>
    internal static class HtSigProp
    {
        /// <summary>
        /// Encodes the SigProp refinement pass for coefficients not yet marked significant.
        /// </summary>
        /// <param name="coefficients">
        /// Full-precision wavelet coefficients in row-major order.
        /// </param>
        /// <param name="sigState">
        /// Significance state from the Cleanup pass. Non-zero values indicate significant samples.
        /// Must be the same length as <paramref name="coefficients"/>.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=LH, 2=HL, 3=HH (reserved).</param>
        /// <param name="bitplane">
        /// The bitplane being refined. Only the bit at this position is considered
        /// for newly significant samples.
        /// </param>
        /// <returns>Encoded SigProp bitstream as a byte array.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when buffer lengths are inconsistent with dimensions.
        /// </exception>
        public static byte[] Encode(
            ReadOnlySpan<int> coefficients,
            ReadOnlySpan<byte> sigState,
            int width, int height,
            int subbandType,
            int bitplane)
        {
            int size = width * height;
            if (coefficients.Length < size)
            {
                throw new ArgumentException(
                    $"Coefficient count {coefficients.Length} is less than {width}x{height}={size}.",
                    nameof(coefficients));
            }

            if (sigState.Length < size)
            {
                throw new ArgumentException(
                    $"Significance state length {sigState.Length} is less than {width}x{height}={size}.",
                    nameof(sigState));
            }

            // Worst-case output: every sample is a SigProp candidate (not yet significant
            // but has a significant neighbor). Each candidate emits:
            //   1 significance bit
            //   + if newly significant: 1 sign bit + bitplane mantissa bits
            // = 1 + 1 + bitplane = 2 + bitplane bits per sample maximum.
            int bitsPerSample = 2 + Math.Max(bitplane, 0);
            long maxBits = (long)size * bitsPerSample;
            int bufferBytes = (int)Math.Min((maxBits + 7) / 8 + 4, int.MaxValue);
            bufferBytes = Math.Max(16, bufferBytes);
            byte[]? rented = null;
            Span<byte> buffer = bufferBytes <= 1024
                ? stackalloc byte[bufferBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(bufferBytes)).AsSpan(0, bufferBytes);
            buffer.Clear();

            int bitPos = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Skip already-significant samples
                    if (sigState[idx] != 0)
                    {
                        continue;
                    }

                    // Skip samples without a significant neighbor
                    if (!HasSignificantNeighbor(sigState, x, y, width, height))
                    {
                        continue;
                    }

                    // This sample is a candidate for SigProp
                    int absVal = Math.Abs(coefficients[idx]);
                    int bit = (absVal >> bitplane) & 1;

                    // Write significance bit
                    WriteBit(buffer, ref bitPos, bit);

                    if (bit == 1)
                    {
                        // Newly significant: encode sign (0=positive, 1=negative)
                        int signBit = coefficients[idx] < 0 ? 1 : 0;
                        WriteBit(buffer, ref bitPos, signBit);

                        // Encode magnitude mantissa bits below the current bitplane
                        for (int bp = bitplane - 1; bp >= 0; bp--)
                        {
                            int magBit = (absVal >> bp) & 1;
                            WriteBit(buffer, ref bitPos, magBit);
                        }
                    }
                }
            }

            // Calculate total bytes needed
            int totalBytes = (bitPos + 7) / 8;

            // Prefix with total bit count (4 bytes, little-endian)
            byte[] result = new byte[4 + totalBytes];
            result[0] = (byte)(bitPos & 0xFF);
            result[1] = (byte)((bitPos >> 8) & 0xFF);
            result[2] = (byte)((bitPos >> 16) & 0xFF);
            result[3] = (byte)((bitPos >> 24) & 0xFF);
            buffer.Slice(0, totalBytes).CopyTo(result.AsSpan(4));

            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            return result;
        }

        /// <summary>
        /// Decodes a SigProp refinement pass, updating coefficients and significance state.
        /// </summary>
        /// <param name="data">Encoded SigProp bitstream.</param>
        /// <param name="coefficients">
        /// Coefficient buffer to update. Newly significant samples will be written here.
        /// </param>
        /// <param name="sigState">
        /// Significance state to update. Newly significant samples will be marked.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="subbandType">Subband type: 0=LL, 1=LH, 2=HL, 3=HH (reserved).</param>
        /// <param name="bitplane">The bitplane being refined.</param>
        public static void Decode(
            ReadOnlySpan<byte> data,
            Span<int> coefficients,
            Span<byte> sigState,
            int width, int height,
            int subbandType,
            int bitplane)
        {
            if (data.Length < 4)
            {
                return; // No data
            }

            int totalBits = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
            if (totalBits == 0)
            {
                return;
            }

            ReadOnlySpan<byte> bitstream = data.Slice(4);
            int bitPos = 0;
            int size = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Skip already-significant samples
                    if (sigState[idx] != 0)
                    {
                        continue;
                    }

                    // Skip samples without a significant neighbor
                    if (!HasSignificantNeighbor(sigState, x, y, width, height))
                    {
                        continue;
                    }

                    if (bitPos >= totalBits)
                    {
                        return;
                    }

                    // Read significance bit
                    int bit = ReadBit(bitstream, ref bitPos);

                    if (bit == 1)
                    {
                        // Newly significant
                        sigState[idx] = 1;

                        // Read sign bit
                        int signBit = ReadBit(bitstream, ref bitPos);

                        // Read magnitude mantissa bits below current bitplane
                        int magnitude = 1 << bitplane;
                        for (int bp = bitplane - 1; bp >= 0; bp--)
                        {
                            int magBit = ReadBit(bitstream, ref bitPos);
                            if (magBit == 1)
                            {
                                magnitude |= (1 << bp);
                            }
                        }

                        coefficients[idx] = signBit == 1 ? -magnitude : magnitude;
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether a sample at (x, y) has at least one significant neighbor
        /// in the 8-connected neighborhood.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasSignificantNeighbor(
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

        /// <summary>
        /// Writes a single bit to the buffer at the specified bit position.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the bit position exceeds the buffer capacity.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBit(Span<byte> buffer, ref int bitPos, int bit)
        {
            int byteIdx = bitPos >> 3;
            int bitIdx = 7 - (bitPos & 7);

            if ((uint)byteIdx >= (uint)buffer.Length)
            {
                throw new InvalidOperationException(
                    $"SigProp bitstream buffer overflow at bit {bitPos} (buffer is {buffer.Length} bytes).");
            }

            if (bit != 0)
            {
                buffer[byteIdx] |= (byte)(1 << bitIdx);
            }

            bitPos++;
        }

        /// <summary>
        /// Reads a single bit from the buffer at the specified bit position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadBit(ReadOnlySpan<byte> buffer, ref int bitPos)
        {
            int byteIdx = bitPos >> 3;
            int bitIdx = 7 - (bitPos & 7);

            int bit = 0;
            if (byteIdx < buffer.Length)
            {
                bit = (buffer[byteIdx] >> bitIdx) & 1;
            }

            bitPos++;
            return bit;
        }
    }
}
