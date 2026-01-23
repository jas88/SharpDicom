using System;
using System.Buffers;
using System.Buffers.Binary;
#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics;
#endif

namespace SharpDicom.Codecs.Rle
{
    /// <summary>
    /// Static methods for encoding pixel data to DICOM RLE-compressed format using the TIFF PackBits algorithm.
    /// </summary>
    public static class RleEncoder
    {
        /// <summary>
        /// Maximum run length for both literal and replicate runs (PackBits spec).
        /// </summary>
        private const int MaxRunLength = 128;

        /// <summary>
        /// Minimum run length that benefits from replicate encoding.
        /// Runs shorter than this are included in literal runs.
        /// </summary>
        private const int MinReplicateRun = 3;

        /// <summary>
        /// Encodes a single segment using the PackBits algorithm.
        /// </summary>
        /// <param name="input">The raw segment data.</param>
        /// <param name="output">The destination buffer for compressed data.</param>
        /// <returns>The number of bytes written to the output buffer.</returns>
        /// <remarks>
        /// <para>
        /// The output is padded to an even length as required by DICOM.
        /// </para>
        /// <para>
        /// The worst-case expansion is approximately input length + (input length / 128) + 2.
        /// </para>
        /// </remarks>
        public static int EncodeSegment(ReadOnlySpan<byte> input, Span<byte> output)
        {
            if (input.IsEmpty)
            {
                return 0;
            }

            int srcPos = 0;
            int dstPos = 0;

            while (srcPos < input.Length)
            {
                // Find run length at current position
                int runLength = FindRunLength(input, srcPos);

                if (runLength >= MinReplicateRun)
                {
                    // Replicate run: -1 to -127 maps to 2-128 repetitions
                    int count = Math.Min(runLength, MaxRunLength);
                    output[dstPos++] = (byte)(-(count - 1));
                    output[dstPos++] = input[srcPos];
                    srcPos += count;
                }
                else
                {
                    // Literal run: find extent (up to MaxRunLength)
                    int literalStart = srcPos;
                    int literalLength = FindLiteralLength(input, srcPos);

                    // Header 0-127 maps to 1-128 literal bytes
                    output[dstPos++] = (byte)(literalLength - 1);
                    input.Slice(literalStart, literalLength).CopyTo(output.Slice(dstPos));
                    dstPos += literalLength;
                    srcPos += literalLength;
                }
            }

            // Pad to even length (DICOM requirement)
            if (dstPos % 2 != 0)
            {
                output[dstPos++] = 0;
            }

            return dstPos;
        }

        /// <summary>
        /// Encodes a complete frame to RLE-compressed format.
        /// </summary>
        /// <param name="pixelData">The raw pixel data for a single frame.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <returns>The compressed frame data including header.</returns>
        public static ReadOnlyMemory<byte> EncodeFrame(ReadOnlySpan<byte> pixelData, PixelDataInfo info)
        {
            // 1. Calculate segments needed
            int bytesPerSample = info.BitsAllocated / 8;
            int numberOfSegments = bytesPerSample * info.SamplesPerPixel;
            int pixelCount = info.Rows * info.Columns;

            // 2. Deinterleave pixels into byte segments (MSB first)
            byte[]? deinterleavedPool = null;
            byte[]? encodedPool = null;

            try
            {
                int segmentSize = pixelCount;
                int totalDeinterleavedSize = segmentSize * numberOfSegments;
                deinterleavedPool = ArrayPool<byte>.Shared.Rent(totalDeinterleavedSize);
                var deinterleaved = deinterleavedPool.AsSpan(0, totalDeinterleavedSize);

                DeinterleaveToSegments(pixelData, deinterleaved, info, pixelCount, numberOfSegments);

                // 3. RLE encode each segment
                // Worst case: input size + input/128 + 2 per segment
                int maxEncodedSegmentSize = segmentSize + (segmentSize / 128) + 2;
                int maxTotalEncoded = maxEncodedSegmentSize * numberOfSegments;
                encodedPool = ArrayPool<byte>.Shared.Rent(maxTotalEncoded);
                var encodedBuffer = encodedPool.AsSpan(0, maxTotalEncoded);

                Span<int> segmentLengths = stackalloc int[numberOfSegments];
                int encodedOffset = 0;

                for (int seg = 0; seg < numberOfSegments; seg++)
                {
                    var segmentInput = deinterleaved.Slice(seg * segmentSize, segmentSize);
                    var segmentOutput = encodedBuffer.Slice(encodedOffset, maxEncodedSegmentSize);

                    int encodedLength = EncodeSegment(segmentInput, segmentOutput);
                    segmentLengths[seg] = encodedLength;
                    encodedOffset += encodedLength;
                }

                // 4. Build output with header
                var header = RleSegmentHeader.Create(segmentLengths);
                int totalSize = RleSegmentHeader.HeaderSize + encodedOffset;
                var output = new byte[totalSize];

                // Write header
                header.WriteTo(output);

                // Write encoded segments
                encodedBuffer.Slice(0, encodedOffset).CopyTo(output.AsSpan(RleSegmentHeader.HeaderSize));

                return output;
            }
            finally
            {
                if (deinterleavedPool != null)
                {
                    ArrayPool<byte>.Shared.Return(deinterleavedPool);
                }
                if (encodedPool != null)
                {
                    ArrayPool<byte>.Shared.Return(encodedPool);
                }
            }
        }

        /// <summary>
        /// Deinterleaves pixel data into separate byte segments (MSB first).
        /// </summary>
        /// <remarks>
        /// <para>
        /// For 16-bit grayscale (little-endian storage):
        /// - Segment 0: High bytes (offset 1 in each pixel)
        /// - Segment 1: Low bytes (offset 0 in each pixel)
        /// </para>
        /// <para>
        /// For 8-bit RGB:
        /// - Segment 0: Red, Segment 1: Green, Segment 2: Blue
        /// </para>
        /// <para>
        /// For 16-bit RGB:
        /// - Segments 0,1: Red (high, low), 2,3: Green (high, low), 4,5: Blue (high, low)
        /// </para>
        /// </remarks>
        private static void DeinterleaveToSegments(
            ReadOnlySpan<byte> pixelData,
            Span<byte> segments,
            PixelDataInfo info,
            int pixelCount,
            int numberOfSegments)
        {
            int bytesPerSample = info.BitsAllocated / 8;
            int bytesPerPixel = bytesPerSample * info.SamplesPerPixel;
            int segmentSize = pixelCount;

            // For each pixel
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int segIndex = 0;

                // For each sample (color channel)
                for (int sample = 0; sample < info.SamplesPerPixel; sample++)
                {
                    // For each byte within the sample (MSB first to segments, from little-endian input)
                    for (int byteOffset = bytesPerSample - 1; byteOffset >= 0; byteOffset--)
                    {
                        int inputIndex = pixel * bytesPerPixel + sample * bytesPerSample + byteOffset;
                        int outputIndex = segIndex * segmentSize + pixel;
                        segments[outputIndex] = pixelData[inputIndex];
                        segIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the length of a run of identical bytes starting at the given index.
        /// </summary>
        private static int FindRunLength(ReadOnlySpan<byte> data, int startIndex)
        {
#if NET8_0_OR_GREATER
            if (Vector128.IsHardwareAccelerated && data.Length - startIndex >= Vector128<byte>.Count)
            {
                return FindRunLengthSimd(data, startIndex);
            }
#endif
            return FindRunLengthScalar(data, startIndex);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// SIMD-accelerated run length detection using Vector128.
        /// </summary>
        private static int FindRunLengthSimd(ReadOnlySpan<byte> data, int startIndex)
        {
            byte target = data[startIndex];
            var targetVector = Vector128.Create(target);
            int pos = startIndex;

            // Process 16 bytes at a time
            while (pos + Vector128<byte>.Count <= data.Length && pos - startIndex < MaxRunLength)
            {
                var chunk = Vector128.Create(data.Slice(pos, Vector128<byte>.Count));
                var comparison = Vector128.Equals(chunk, targetVector);

                // Check if all bytes match
                if (comparison != Vector128<byte>.AllBitsSet)
                {
                    // Find first non-matching byte
                    var mask = ~comparison.ExtractMostSignificantBits();
                    int firstDiff = BitOperations.TrailingZeroCount(mask);
                    return Math.Min(pos + firstDiff - startIndex, MaxRunLength);
                }

                pos += Vector128<byte>.Count;
            }

            // Handle remaining bytes (tail or near max run length)
            while (pos < data.Length && pos - startIndex < MaxRunLength && data[pos] == target)
            {
                pos++;
            }

            return Math.Min(pos - startIndex, MaxRunLength);
        }
#endif

        /// <summary>
        /// Scalar fallback for run length detection.
        /// </summary>
        private static int FindRunLengthScalar(ReadOnlySpan<byte> data, int startIndex)
        {
            byte target = data[startIndex];
            int length = 1;

            while (startIndex + length < data.Length && length < MaxRunLength && data[startIndex + length] == target)
            {
                length++;
            }

            return length;
        }

        /// <summary>
        /// Finds the length of a literal (non-repeating) sequence starting at the given index.
        /// </summary>
        private static int FindLiteralLength(ReadOnlySpan<byte> data, int startIndex)
        {
            int pos = startIndex;

            while (pos < data.Length && pos - startIndex < MaxRunLength)
            {
                // Check if starting a run of MinReplicateRun or more identical bytes
                if (pos + MinReplicateRun - 1 < data.Length)
                {
                    bool isRun = true;
                    byte first = data[pos];
                    for (int i = 1; i < MinReplicateRun; i++)
                    {
                        if (data[pos + i] != first)
                        {
                            isRun = false;
                            break;
                        }
                    }

                    if (isRun)
                    {
                        break; // End literal here, start replicate
                    }
                }

                pos++;
            }

            // Ensure we return at least 1 if we're not at end of data
            return Math.Max(1, pos - startIndex);
        }

        /// <summary>
        /// Calculates the maximum compressed size for a frame.
        /// </summary>
        /// <param name="info">Pixel data metadata.</param>
        /// <returns>The maximum possible compressed size including header.</returns>
        public static int GetMaxEncodedSize(PixelDataInfo info)
        {
            int bytesPerSample = info.BitsAllocated / 8;
            int numberOfSegments = bytesPerSample * info.SamplesPerPixel;
            int pixelCount = info.Rows * info.Columns;

            // Worst case per segment: input size + input/128 + 2 (padding)
            int maxPerSegment = pixelCount + (pixelCount / 128) + 2;
            return RleSegmentHeader.HeaderSize + (maxPerSegment * numberOfSegments);
        }
    }
}
