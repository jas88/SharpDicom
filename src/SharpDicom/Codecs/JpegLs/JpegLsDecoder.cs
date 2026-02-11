using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// JPEG-LS header information parsed from the codestream.
    /// </summary>
    public readonly struct JpegLsHeader
    {
        /// <summary>Image width in pixels.</summary>
        public int Width { get; init; }
        /// <summary>Image height in pixels.</summary>
        public int Height { get; init; }
        /// <summary>Number of components.</summary>
        public int Components { get; init; }
        /// <summary>Bits per sample.</summary>
        public int BitsPerSample { get; init; }
        /// <summary>NEAR parameter (0=lossless).</summary>
        public int Near { get; init; }
        /// <summary>Interleave mode.</summary>
        public JlsInterleaveMode InterleaveMode { get; init; }
    }

    /// <summary>
    /// JPEG-LS decoder for ITU-T T.87 / ISO/IEC 14495-1 bitstreams.
    /// </summary>
    /// <remarks>
    /// Complete managed implementation matching CharLS reference behavior including
    /// run mode decoding, modulo range reduction, and correct edge pixel initialization.
    /// </remarks>
    internal static class JpegLsDecoder
    {
        private const ushort SOI = 0xFFD8;
        private const ushort EOI = 0xFFD9;
        private const ushort SOF55 = 0xFFF7;
        private const ushort SOS = 0xFFDA;

        // Run length J table per ITU-T T.87, A.2.1
        private static readonly int[] J = {
            0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
            4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15
        };

        /// <summary>
        /// Attempts to parse only the header from a JPEG-LS stream.
        /// </summary>
        public static bool TryParseHeader(ReadOnlySpan<byte> data, out JpegLsHeader header, out string? error)
        {
            header = default;
            error = null;

            if (data.Length < 4)
            {
                error = "Data too short for JPEG-LS header";
                return false;
            }

            if (BinaryPrimitives.ReadUInt16BigEndian(data) != SOI)
            {
                error = "Missing SOI marker";
                return false;
            }

            int pos = 2;
            int near = 0;
            int interleave = 0;
            int width = 0, height = 0, components = 0, precision = 0;
            bool foundSof55 = false;

            while (pos + 4 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;

                if (marker == EOI) break;

                if (marker == SOS)
                {
                    int sosLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                    if (pos + sosLen <= data.Length && sosLen >= 6)
                    {
                        int numComponents = data[pos + 2];
                        if (sosLen >= 6 + numComponents * 2)
                        {
                            int sosDataStart = pos + 2 + 1 + numComponents * 2;
                            if (sosDataStart + 2 < data.Length)
                            {
                                near = data[sosDataStart];
                                interleave = data[sosDataStart + 1];
                            }
                        }
                    }
                    break;
                }

                if ((marker & 0xFF00) != 0xFF00)
                {
                    error = $"Invalid marker at position {pos - 2}";
                    return false;
                }

                int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;

                if (marker == SOF55)
                {
                    if (segLen < 8) { error = "SOF55 segment too short"; return false; }
                    precision = data[pos];
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 1));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 3));
                    components = data[pos + 5];
                    foundSof55 = true;
                }

                pos += segLen - 2;
            }

            if (!foundSof55) { error = "SOF55 marker not found"; return false; }

            header = new JpegLsHeader
            {
                Width = width, Height = height, Components = components,
                BitsPerSample = precision, Near = near,
                InterleaveMode = (JlsInterleaveMode)interleave
            };
            return true;
        }

        /// <summary>
        /// Decodes a JPEG-LS frame.
        /// </summary>
        public static DecodeResult TryDecode(
            ReadOnlySpan<byte> data,
            PixelDataInfo info,
            Span<byte> destination,
            int frameIndex)
        {
            if (!TryParseHeader(data, out var header, out var error))
                return DecodeResult.Fail(frameIndex, 0, error ?? "Failed to parse JPEG-LS header");

            if (header.Width != info.Columns || header.Height != info.Rows)
                return DecodeResult.Fail(frameIndex, 0,
                    $"Dimension mismatch: header={header.Width}x{header.Height}, expected={info.Columns}x{info.Rows}");

            int bytesPerSample = (header.BitsPerSample + 7) / 8;
            int stride = header.Width * header.Components * bytesPerSample;

            if (destination.Length < header.Height * stride)
                return DecodeResult.Fail(frameIndex, 0,
                    $"Destination buffer too small: {destination.Length} < {header.Height * stride}");

            try
            {
                if (header.Components == 1 || header.InterleaveMode != JlsInterleaveMode.None)
                {
                    int scanDataStart = FindScanDataStart(data, 0);
                    if (scanDataStart < 0)
                        return DecodeResult.Fail(frameIndex, 0, "Could not find scan data start");

                    DecodeScanComponent(data.Slice(scanDataStart), destination,
                        header.Width, header.Height, header.Components, 0,
                        header.BitsPerSample, header.Near);
                    return DecodeResult.Ok(header.Height * stride);
                }
                else
                {
                    int searchFrom = 0;
                    for (int c = 0; c < header.Components; c++)
                    {
                        int scanDataStart = FindScanDataStart(data, searchFrom);
                        if (scanDataStart < 0)
                            return DecodeResult.Fail(frameIndex, 0, $"Could not find scan data start for component {c}");

                        DecodeScanComponent(data.Slice(scanDataStart), destination,
                            header.Width, header.Height, header.Components, c,
                            header.BitsPerSample, header.Near);

                        searchFrom = scanDataStart;
                        for (int j = searchFrom; j < data.Length - 1; j++)
                        {
                            if (data[j] == 0xFF && (data[j + 1] & 0x80) != 0)
                            {
                                searchFrom = j;
                                break;
                            }
                        }
                    }
                    return DecodeResult.Ok(header.Height * stride);
                }
            }
            catch (Exception ex)
            {
                return DecodeResult.Fail(frameIndex, 0, $"Decode error: {ex.Message}");
            }
        }

        private static int FindScanDataStart(ReadOnlySpan<byte> data, int searchFrom)
        {
            int pos = searchFrom == 0 ? 2 : searchFrom;
            while (pos + 4 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;
                if (marker == SOS)
                {
                    int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                    return pos + segLen;
                }
                if (marker == EOI) return -1;
                if ((marker & 0xFF00) == 0xFF00 && marker != 0xFF00)
                {
                    int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                    pos += segLen;
                }
            }
            return -1;
        }

        /// <summary>
        /// Decodes a single component scan using CharLS-compatible line buffer approach.
        /// </summary>
        private static void DecodeScanComponent(
            ReadOnlySpan<byte> scanData,
            Span<byte> output,
            int width,
            int height,
            int components,
            int componentIndex,
            int bitsPerSample,
            int near)
        {
            int maxVal = (1 << bitsPerSample) - 1;
            int range = ComputeRange(maxVal, near);
            int bytesPerSample = (bitsPerSample + 7) / 8;

            var contexts = new JlsContext[365];
            for (int i = 0; i < contexts.Length; i++)
                contexts[i].Initialize(range);

            var runContexts = new JlsRunModeContext[2];
            runContexts[0].Initialize(0, range);
            runContexts[1].Initialize(1, range);

            var decoder = new GolombRiceDecoder(scanData);
            decoder.SetBitsPerPixel(bitsPerSample);

            JpegLsPredictor.ComputeDefaultThresholds(maxVal, near, out int t1, out int t2, out int t3);

            int stride = width * components * bytesPerSample;

            // Line buffers: width + 2 elements
            int lineWidth = width + 2;
            int[] previousLine = new int[lineWidth];
            int[] currentLine = new int[lineWidth];

            int runIndex = 0;

            for (int y = 0; y < height; y++)
            {
                // Edge pixel initialization per CharLS
                previousLine[width + 1] = previousLine[width];
                currentLine[0] = previousLine[1];

                // Decode the line (matching CharLS decode_sample_line)
                int index = 1;
                int rb = previousLine[0];
                int rd = previousLine[1];

                while (index <= width)
                {
                    int ra = currentLine[index - 1];
                    int rc = rb;
                    rb = rd;
                    rd = previousLine[index + 1];

                    int q1 = QuantizeGradient(rd - rb, near, t1, t2, t3);
                    int q2 = QuantizeGradient(rb - rc, near, t1, t2, t3);
                    int q3 = QuantizeGradient(rc - ra, near, t1, t2, t3);
                    int qs = (q1 * 9 + q2) * 9 + q3;

                    if (qs != 0)
                    {
                        currentLine[index] = DecodeRegular(qs,
                            JpegLsPredictor.MedianEdgeDetection(ra, rb, rc),
                            maxVal, near, bitsPerSample, contexts, ref decoder);
                        index++;
                    }
                    else
                    {
                        int consumed = DecodeRunMode(currentLine, previousLine, index, width, ra,
                            maxVal, near, bitsPerSample, runContexts, ref runIndex, ref decoder);
                        index += consumed;
                        rb = previousLine[index - 1];
                        rd = previousLine[index];
                    }
                }

                // Write decoded line to output
                for (int x = 0; x < width; x++)
                {
                    int dstPos = y * stride + (x * components + componentIndex) * bytesPerSample;
                    int sample = currentLine[x + 1];
                    if (bytesPerSample == 1)
                    {
                        output[dstPos] = (byte)sample;
                    }
                    else
                    {
                        output[dstPos] = (byte)(sample & 0xFF);
                        output[dstPos + 1] = (byte)(sample >> 8);
                    }
                }

                // Swap line buffers
                var temp = previousLine;
                previousLine = currentLine;
                currentLine = temp;
            }
        }

        /// <summary>
        /// Decodes a sample in regular mode, matching CharLS decode_regular exactly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DecodeRegular(
            int qs,
            int predicted,
            int maxVal,
            int near,
            int bitsPerSample,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            int sign = qs >> 31; // 0 or -1
            int contextIndex = (sign ^ qs) - sign; // abs(qs)
            ref var ctx = ref contexts[contextIndex];

            // Apply bias correction to prediction
            int correctedPrediction = predicted + ((ctx.C ^ sign) - sign);
            correctedPrediction = CorrectPrediction(correctedPrediction, maxVal);

            int k = ctx.ComputeK();

            // Read and unmap error value
            int mappedError = decoder.ReadGolombRice(k);
            int errorValue = UnmapErrorValue(mappedError);

            // Apply error correction XOR (only when k == 0)
            if (k == 0)
            {
                errorValue = errorValue ^ ctx.GetErrorCorrection(near);
            }

            // Update context BEFORE applying sign (CharLS ordering)
            ctx.Update(errorValue, near, 64);

            // Apply sign to error value
            errorValue = (errorValue ^ sign) - sign;

            // Reconstruct sample
            return ComputeReconstructedSample(correctedPrediction, errorValue, maxVal);
        }

        /// <summary>
        /// Decodes a run of pixels per ITU-T T.87, A.7.
        /// Returns the number of pixels consumed.
        /// </summary>
        private static int DecodeRunMode(
            int[] currentLine,
            int[] previousLine,
            int startIndex,
            int width,
            int ra,
            int maxVal,
            int near,
            int bitsPerSample,
            JlsRunModeContext[] runContexts,
            ref int runIndex,
            ref GolombRiceDecoder decoder)
        {
            int countRemain = width - (startIndex - 1);
            int runLength = DecodeRunPixels(countRemain, ref runIndex, ref decoder);

            // Fill the run
            for (int i = 0; i < runLength; i++)
            {
                currentLine[startIndex + i] = ra;
            }

            if (runLength == countRemain)
                return runLength;

            // Run interruption: decode the interruption pixel
            int ix = startIndex + runLength;
            int rbVal = previousLine[ix];

            currentLine[ix] = DecodeRunInterruptionPixel(ra, rbVal,
                maxVal, near, bitsPerSample, runContexts, ref runIndex, ref decoder);

            if (runIndex > 0) runIndex--;

            return runLength + 1;
        }

        /// <summary>
        /// Decodes run length from the bitstream per ITU-T T.87, A.7.1.
        /// </summary>
        private static int DecodeRunPixels(int countRemain, ref int runIndex, ref GolombRiceDecoder decoder)
        {
            int runLength = 0;

            while (runLength < countRemain)
            {
                // Read one bit
                if (decoder.ReadBit() == 1)
                {
                    // Run continues for 2^J[runIndex] pixels
                    runLength += (1 << J[runIndex]);
                    if (runIndex < 31) runIndex++;
                }
                else
                {
                    // Run ends: read J[runIndex] bits for the remaining run length
                    int remaining = 0;
                    for (int i = 0; i < J[runIndex]; i++)
                    {
                        remaining = (remaining << 1) | decoder.ReadBit();
                    }
                    runLength += remaining;
                    break;
                }

                if (runLength >= countRemain)
                {
                    runLength = countRemain;
                    break;
                }
            }

            return Math.Min(runLength, countRemain);
        }

        /// <summary>
        /// Decodes a run interruption pixel per ITU-T T.87, A.7.2.
        /// </summary>
        private static int DecodeRunInterruptionPixel(
            int ra, int rb,
            int maxVal, int near, int bitsPerSample,
            JlsRunModeContext[] runContexts,
            ref int runIndex,
            ref GolombRiceDecoder decoder)
        {
            int limit = ComputeLimit(bitsPerSample);

            if (Math.Abs(ra - rb) <= near)
            {
                ref var ctx = ref runContexts[1];
                int k = ctx.ComputeK();
                int eMapped = decoder.ReadGolombRiceWithLimit(k, limit - J[runIndex] - 1, bitsPerSample);
                int errorValue = ctx.ComputeErrorValue(eMapped + ctx.RunInterruptionType, k);
                ctx.UpdateVariables(errorValue, eMapped, 64);
                return ComputeReconstructedSample(ra, errorValue, maxVal);
            }
            else
            {
                ref var ctx = ref runContexts[0];
                int k = ctx.ComputeK();
                int eMapped = decoder.ReadGolombRiceWithLimit(k, limit - J[runIndex] - 1, bitsPerSample);
                int errorValue = ctx.ComputeErrorValue(eMapped + ctx.RunInterruptionType, k);
                ctx.UpdateVariables(errorValue, eMapped, 64);
                int signVal = Sign(rb - ra);
                return ComputeReconstructedSample(rb, errorValue * signVal, maxVal);
            }
        }

        /// <summary>
        /// Unmaps a non-negative mapped error to a signed error.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int UnmapErrorValue(int mappedError)
        {
            if ((mappedError & 1) == 0)
                return mappedError >> 1;
            return -((mappedError + 1) >> 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CorrectPrediction(int predicted, int maxVal)
        {
            if ((predicted & maxVal) == predicted)
                return predicted;
            return (~(predicted >> 31)) & maxVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeReconstructedSample(int predicted, int errorValue, int maxVal)
        {
            return maxVal & (predicted + errorValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int QuantizeGradient(int gradient, int near, int t1, int t2, int t3)
        {
            if (gradient <= -t3) return -4;
            if (gradient <= -t2) return -3;
            if (gradient <= -t1) return -2;
            if (gradient < -near) return -1;
            if (gradient <= near) return 0;
            if (gradient < t1) return 1;
            if (gradient < t2) return 2;
            if (gradient < t3) return 3;
            return 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Sign(int value)
        {
            if (value > 0) return 1;
            if (value < 0) return -1;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeRange(int maxVal, int near)
        {
            if (near == 0)
                return maxVal + 1;
            return (maxVal + 2 * near) / (2 * near + 1) + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeLimit(int bitsPerSample)
        {
            return 2 * (bitsPerSample + Math.Max(8, bitsPerSample));
        }
    }
}
