using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.Jpeg2000.Tier1
{
    /// <summary>
    /// HT MagRef (Magnitude Refinement) pass for the ITU-T T.814
    /// High-Throughput block coder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MagRef pass processes samples that ARE already significant (after Cleanup
    /// and optionally SigProp passes). For each significant sample, it encodes the
    /// next magnitude bit, refining the precision by one bitplane.
    /// </para>
    /// <para>
    /// This pass uses a simple byte-aligned bitstream. Each significant sample
    /// contributes exactly one bit (the magnitude bit at the specified bitplane).
    /// </para>
    /// <para>
    /// MagRef is the third coding pass in an HT Set (after Cleanup and SigProp).
    /// Together, the three passes (Cleanup + SigProp + MagRef) form one complete
    /// HT Set that processes one bitplane of refinement.
    /// </para>
    /// </remarks>
    internal static class HtMagRef
    {
        /// <summary>
        /// Encodes the MagRef refinement pass for already-significant samples.
        /// </summary>
        /// <param name="coefficients">
        /// Full-precision wavelet coefficients in row-major order.
        /// </param>
        /// <param name="sigState">
        /// Significance state after Cleanup and SigProp passes.
        /// Non-zero values indicate significant samples.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="bitplane">
        /// The bitplane being refined. The bit at this position of each
        /// significant sample's magnitude is encoded.
        /// </param>
        /// <returns>Encoded MagRef bitstream as a byte array.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when buffer lengths are inconsistent with dimensions.
        /// </exception>
        public static byte[] Encode(
            ReadOnlySpan<int> coefficients,
            ReadOnlySpan<byte> sigState,
            int width, int height,
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

            // Each significant sample contributes exactly 1 bit
            int estimatedBytes = Math.Max(8, (size + 7) / 8 + 4);
            byte[]? rented = null;
            Span<byte> buffer = estimatedBytes <= 1024
                ? stackalloc byte[estimatedBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(estimatedBytes)).AsSpan(0, estimatedBytes);
            buffer.Clear();

            int bitPos = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Only process already-significant samples
                    if (sigState[idx] == 0)
                    {
                        continue;
                    }

                    // Encode the magnitude bit at the specified bitplane
                    int absVal = Math.Abs(coefficients[idx]);
                    int bit = (absVal >> bitplane) & 1;
                    WriteBit(buffer, ref bitPos, bit);
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
        /// Decodes a MagRef refinement pass, updating coefficient magnitudes.
        /// </summary>
        /// <param name="data">Encoded MagRef bitstream.</param>
        /// <param name="coefficients">
        /// Coefficient buffer to update. Magnitude refinement bits are OR'd into
        /// existing significant sample magnitudes.
        /// </param>
        /// <param name="sigState">
        /// Significance state (read-only during MagRef). Determines which samples
        /// to refine.
        /// </param>
        /// <param name="width">Code-block width in samples.</param>
        /// <param name="height">Code-block height in samples.</param>
        /// <param name="bitplane">The bitplane being refined.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when buffer lengths are inconsistent with dimensions.
        /// </exception>
        public static void Decode(
            ReadOnlySpan<byte> data,
            Span<int> coefficients,
            ReadOnlySpan<byte> sigState,
            int width, int height,
            int bitplane)
        {
            int size = width * height;
            if (coefficients.Length < size)
            {
                throw new ArgumentException(
                    $"Coefficient buffer length {coefficients.Length} is less than {width}x{height}={size}.",
                    nameof(coefficients));
            }

            if (sigState.Length < size)
            {
                throw new ArgumentException(
                    $"Significance state length {sigState.Length} is less than {width}x{height}={size}.",
                    nameof(sigState));
            }

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

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;

                    // Only process already-significant samples
                    if (sigState[idx] == 0)
                    {
                        continue;
                    }

                    if (bitPos >= totalBits)
                    {
                        return;
                    }

                    // Read the magnitude refinement bit
                    int bit = ReadBit(bitstream, ref bitPos);

                    if (bit == 1)
                    {
                        // OR the refinement bit into the magnitude
                        int current = coefficients[idx];
                        int sign = current < 0 ? -1 : 1;
                        int magnitude = Math.Abs(current);
                        magnitude |= (1 << bitplane);
                        coefficients[idx] = magnitude * sign;
                    }
                }
            }
        }

        /// <summary>
        /// Writes a single bit to the buffer at the specified bit position.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBit(Span<byte> buffer, ref int bitPos, int bit)
        {
            int byteIdx = bitPos >> 3;
            int bitIdx = 7 - (bitPos & 7);

            if (byteIdx < buffer.Length)
            {
                if (bit != 0)
                {
                    buffer[byteIdx] |= (byte)(1 << bitIdx);
                }
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
