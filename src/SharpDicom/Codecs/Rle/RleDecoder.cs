using System;
using System.Buffers;
using SharpDicom.Data.Exceptions;

namespace SharpDicom.Codecs.Rle
{
    /// <summary>
    /// Static methods for decoding DICOM RLE-compressed pixel data using the TIFF PackBits algorithm.
    /// </summary>
    public static class RleDecoder
    {
        /// <summary>
        /// Decodes a single RLE segment using the PackBits algorithm.
        /// </summary>
        /// <param name="compressed">The compressed segment data.</param>
        /// <param name="output">The destination buffer for decompressed data.</param>
        /// <returns>The number of bytes written to the output buffer.</returns>
        /// <remarks>
        /// <para>
        /// The PackBits algorithm uses header bytes to indicate literal or replicate runs:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Header 0-127: Copy the next (header + 1) bytes literally (1-128 bytes).</description></item>
        /// <item><description>Header -127 to -1: Repeat the next byte (-header + 1) times (2-128 bytes).</description></item>
        /// <item><description>Header -128: No operation (reserved, skip).</description></item>
        /// </list>
        /// </remarks>
        public static int DecodeSegment(ReadOnlySpan<byte> compressed, Span<byte> output)
        {
            int srcPos = 0;
            int dstPos = 0;

            while (srcPos < compressed.Length && dstPos < output.Length)
            {
                sbyte header = (sbyte)compressed[srcPos++];

                if (header >= 0)
                {
                    // Literal run: copy next (header + 1) bytes
                    int count = header + 1;
                    if (srcPos + count > compressed.Length || dstPos + count > output.Length)
                    {
                        break; // Truncated data - stop gracefully
                    }

                    compressed.Slice(srcPos, count).CopyTo(output.Slice(dstPos));
                    srcPos += count;
                    dstPos += count;
                }
                else if (header != -128)
                {
                    // Replicate run: repeat next byte (-header + 1) times
                    int count = -header + 1;
                    if (srcPos >= compressed.Length || dstPos + count > output.Length)
                    {
                        break; // Truncated data - stop gracefully
                    }

                    output.Slice(dstPos, count).Fill(compressed[srcPos++]);
                    dstPos += count;
                }
                // header == -128 (-0x80): no-op, skip
            }

            return dstPos;
        }

        /// <summary>
        /// Decodes a complete RLE frame (all segments) to raw pixel data.
        /// </summary>
        /// <param name="compressedFrame">The compressed frame data including the 64-byte header.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="output">The destination buffer for decompressed pixel data.</param>
        /// <param name="frameIndex">The zero-based frame index (for error reporting).</param>
        /// <returns>A <see cref="DecodeResult"/> indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> compressedFrame,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex)
        {
            // 1. Parse header
            if (!RleSegmentHeader.TryParse(compressedFrame, out var header, out var headerError))
            {
                return DecodeResult.Fail(frameIndex, 0, headerError ?? "Invalid RLE header");
            }

            // 2. Validate segment count matches expected
            int expectedSegments = (info.BitsAllocated / 8) * info.SamplesPerPixel;
            if (header.NumberOfSegments != expectedSegments)
            {
                return DecodeResult.Fail(
                    frameIndex, 0,
                    $"Expected {expectedSegments} segments but header contains {header.NumberOfSegments}",
                    expectedSegments.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    header.NumberOfSegments.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // 3. Calculate expected output size
            int pixelCount = info.Rows * info.Columns;
            int expectedFrameSize = pixelCount * (info.BitsAllocated / 8) * info.SamplesPerPixel;

            if (output.Length < expectedFrameSize)
            {
                return DecodeResult.Fail(
                    frameIndex, 0,
                    $"Output buffer too small: need {expectedFrameSize} bytes, got {output.Length}",
                    expectedFrameSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    output.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // 4. Decode each segment to temporary buffers
            byte[]? pooledBuffer = null;
            try
            {
                int segmentSize = pixelCount;
                int totalTempSize = segmentSize * expectedSegments;
                pooledBuffer = ArrayPool<byte>.Shared.Rent(totalTempSize);
                var tempBuffer = pooledBuffer.AsSpan(0, totalTempSize);

                for (int seg = 0; seg < expectedSegments; seg++)
                {
                    uint offset = header.GetSegmentOffset(seg);
                    uint nextOffset;

                    if (seg + 1 < expectedSegments)
                    {
                        nextOffset = header.GetSegmentOffset(seg + 1);
                    }
                    else
                    {
                        nextOffset = (uint)compressedFrame.Length;
                    }

                    if (offset > compressedFrame.Length || nextOffset > compressedFrame.Length || nextOffset < offset)
                    {
                        return DecodeResult.Fail(
                            frameIndex, offset,
                            $"Segment {seg} has invalid offset range: {offset}-{nextOffset}");
                    }

                    var segmentData = compressedFrame.Slice((int)offset, (int)(nextOffset - offset));
                    var segmentOutput = tempBuffer.Slice(seg * segmentSize, segmentSize);

                    int decoded = DecodeSegment(segmentData, segmentOutput);
                    if (decoded != segmentSize)
                    {
                        return DecodeResult.Fail(
                            frameIndex, offset,
                            $"Segment {seg}: decoded {decoded} bytes, expected {segmentSize}",
                            segmentSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            decoded.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                // 5. Interleave segments back to output (MSB-first order)
                InterleaveSegments(tempBuffer, output, info, pixelCount, expectedSegments);

                return DecodeResult.Ok(expectedFrameSize);
            }
            finally
            {
                if (pooledBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(pooledBuffer);
                }
            }
        }

        /// <summary>
        /// Interleaves decoded byte segments back to pixel data.
        /// </summary>
        /// <remarks>
        /// <para>
        /// DICOM RLE uses MSB-first segment ordering:
        /// </para>
        /// <list type="bullet">
        /// <item><description>For 16-bit grayscale: Segment 0 = high bytes, Segment 1 = low bytes.</description></item>
        /// <item><description>For 8-bit RGB: Segment 0 = R, Segment 1 = G, Segment 2 = B.</description></item>
        /// <item><description>For 16-bit RGB: Segments 0,1 = R (high, low), 2,3 = G, 4,5 = B.</description></item>
        /// </list>
        /// <para>
        /// Output is in little-endian format (low byte first in memory).
        /// </para>
        /// </remarks>
        private static void InterleaveSegments(
            ReadOnlySpan<byte> segments,
            Span<byte> output,
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
                    // For each byte within the sample (MSB first in segments, convert to little-endian output)
                    for (int byteOffset = bytesPerSample - 1; byteOffset >= 0; byteOffset--)
                    {
                        int outputIndex = pixel * bytesPerPixel + sample * bytesPerSample + byteOffset;
                        int inputIndex = segIndex * segmentSize + pixel;
                        output[outputIndex] = segments[inputIndex];
                        segIndex++;
                    }
                }
            }
        }
    }
}
