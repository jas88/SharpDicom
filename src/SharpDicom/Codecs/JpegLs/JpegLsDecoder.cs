using System;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// JPEG-LS header information parsed from the codestream.
    /// </summary>
    public readonly struct JpegLsHeader
    {
        /// <summary>
        /// Image width in pixels.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Image height in pixels.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Number of components (1=grayscale, 3=color).
        /// </summary>
        public int Components { get; init; }

        /// <summary>
        /// Bits per sample.
        /// </summary>
        public int BitsPerSample { get; init; }

        /// <summary>
        /// NEAR parameter (0=lossless).
        /// </summary>
        public int Near { get; init; }

        /// <summary>
        /// Interleave mode.
        /// </summary>
        public JlsInterleaveMode InterleaveMode { get; init; }
    }

    /// <summary>
    /// JPEG-LS decoder for ITU-T T.87 / ISO/IEC 14495-1 bitstreams.
    /// </summary>
    /// <remarks>
    /// Complete managed implementation of JPEG-LS lossless and near-lossless decoding.
    /// Supports all interleave modes, 8-bit and 16-bit samples, and context-based prediction.
    /// </remarks>
    internal static class JpegLsDecoder
    {
        // JPEG markers
        private const ushort SOI = 0xFFD8;   // Start Of Image
        private const ushort EOI = 0xFFD9;   // End Of Image
        private const ushort SOF55 = 0xFFF7; // Start Of Frame (JPEG-LS)
        private const ushort LSE = 0xFFF8;   // JPEG-LS Extension
        private const ushort SOS = 0xFFDA;   // Start Of Scan

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

            // Check SOI marker
            if (BinaryPrimitives.ReadUInt16BigEndian(data) != SOI)
            {
                error = "Missing SOI marker";
                return false;
            }

            int pos = 2;
            int near = 0;
            int interleave = 0;
            int width = 0;
            int height = 0;
            int components = 0;
            int precision = 0;
            bool foundSof55 = false;

            while (pos + 4 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;

                if (marker == EOI)
                {
                    break;
                }

                if (marker == SOS)
                {
                    // Parse SOS to get NEAR and interleave mode
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
                    // After SOS comes entropy data, stop parsing markers
                    break;
                }

                if ((marker & 0xFF00) != 0xFF00)
                {
                    error = $"Invalid marker at position {pos - 2}";
                    return false;
                }

                // Read segment length
                int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;

                if (marker == SOF55)
                {
                    // Parse SOF55 (Frame header)
                    if (segLen < 8)
                    {
                        error = "SOF55 segment too short";
                        return false;
                    }

                    precision = data[pos];
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 1));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 3));
                    components = data[pos + 5];
                    foundSof55 = true;
                }

                pos += segLen - 2;
            }

            if (!foundSof55)
            {
                error = "SOF55 marker not found";
                return false;
            }

            // Build header after parsing both SOF55 and SOS markers
            header = new JpegLsHeader
            {
                Width = width,
                Height = height,
                Components = components,
                BitsPerSample = precision,
                Near = near,
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
            {
                return DecodeResult.Fail(frameIndex, 0, error ?? "Failed to parse JPEG-LS header");
            }

            // Validate dimensions match
            if (header.Width != info.Columns || header.Height != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Dimension mismatch: header={header.Width}x{header.Height}, expected={info.Columns}x{info.Rows}");
            }

            // Find the start of scan data (after SOS marker and its parameters)
            int scanDataStart = FindScanDataStart(data);
            if (scanDataStart < 0)
            {
                return DecodeResult.Fail(frameIndex, 0, "Could not find scan data start");
            }

            int bytesPerSample = (header.BitsPerSample + 7) / 8;
            int stride = header.Width * header.Components * bytesPerSample;

            if (destination.Length < header.Height * stride)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Destination buffer too small: {destination.Length} < {header.Height * stride}");
            }

            try
            {
                // Decode scan data using context-based prediction
                int bytesWritten = DecodeScanData(
                    data.Slice(scanDataStart),
                    destination,
                    header.Width,
                    header.Height,
                    header.Components,
                    header.BitsPerSample,
                    header.Near,
                    header.InterleaveMode);

                return DecodeResult.Ok(bytesWritten);
            }
            catch (Exception ex)
            {
                return DecodeResult.Fail(frameIndex, scanDataStart, $"Decode error: {ex.Message}");
            }
        }

        private static int FindScanDataStart(ReadOnlySpan<byte> data)
        {
            int pos = 2; // Skip SOI

            while (pos + 4 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                pos += 2;

                if (marker == SOS)
                {
                    int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                    return pos + segLen; // Start of entropy-coded data
                }

                if ((marker & 0xFF00) == 0xFF00 && marker != 0xFF00)
                {
                    int segLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos));
                    pos += segLen;
                }
            }

            return -1;
        }

        private static int DecodeScanData(
            ReadOnlySpan<byte> scanData,
            Span<byte> output,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int near,
            JlsInterleaveMode interleaveMode)
        {
            int bytesPerSample = (bitsPerSample + 7) / 8;
            int stride = width * components * bytesPerSample;
            int maxVal = (1 << bitsPerSample) - 1;
            int range = maxVal + 1;

            // Initialize 365 contexts per ITU-T T.87
            var contexts = new JlsContext[365];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i].Initialize(range);
            }

            var decoder = new GolombRiceDecoder(scanData);
            decoder.SetBitsPerPixel(bitsPerSample);

            // Decode based on interleave mode
            switch (interleaveMode)
            {
                case JlsInterleaveMode.None:
                    return DecodeNonInterleaved(output, width, height, components, bytesPerSample, near, maxVal, range, contexts, ref decoder);
                case JlsInterleaveMode.Line:
                    return DecodeLineInterleaved(output, width, height, components, bytesPerSample, near, maxVal, range, contexts, ref decoder);
                case JlsInterleaveMode.Sample:
                    return DecodeSampleInterleaved(output, width, height, components, bytesPerSample, near, maxVal, range, contexts, ref decoder);
                default:
                    return DecodeNonInterleaved(output, width, height, components, bytesPerSample, near, maxVal, range, contexts, ref decoder);
            }
        }

        private static int DecodeNonInterleaved(
            Span<byte> output,
            int width,
            int height,
            int components,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            // Decode each component separately into a temp buffer, then interleave
            int stride = width * components * bytesPerSample;
            int componentSize = width * height * bytesPerSample;

            // Use a temporary buffer for each component
            byte[] componentBuffer = new byte[componentSize];
            int componentStride = width * bytesPerSample;

            for (int c = 0; c < components; c++)
            {
                int componentPos = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sample = DecodeSampleSingleComponent(componentBuffer, componentPos, x, y, width, bytesPerSample, componentStride, near, maxVal, range, contexts, ref decoder);
                        WriteSampleAt(componentBuffer, componentPos, sample, bytesPerSample);
                        componentPos += bytesPerSample;
                    }
                }

                // Copy component data to output at correct interleaved positions
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcPos = y * componentStride + x * bytesPerSample;
                        int dstPos = y * stride + (x * components + c) * bytesPerSample;
                        for (int b = 0; b < bytesPerSample; b++)
                        {
                            output[dstPos + b] = componentBuffer[srcPos + b];
                        }
                    }
                }
            }

            return height * stride;
        }

        private static int DecodeSampleSingleComponent(
            byte[] componentBuffer,
            int currentPos,
            int x,
            int y,
            int width,
            int bytesPerSample,
            int stride,
            int near,
            int maxVal,
            int range,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            // Get neighboring samples for prediction (single component, no interleaving)
            int a = GetSampleSingleComponent(componentBuffer, currentPos, x, y, width, bytesPerSample, stride, -1, 0);  // left
            int b = GetSampleSingleComponent(componentBuffer, currentPos, x, y, width, bytesPerSample, stride, 0, -1);  // above
            int c_diag = GetSampleSingleComponent(componentBuffer, currentPos, x, y, width, bytesPerSample, stride, -1, -1); // above-left
            int d = GetSampleSingleComponent(componentBuffer, currentPos, x, y, width, bytesPerSample, stride, 1, -1);  // above-right

            // Compute gradients for context selection
            int g1 = d - b;
            int g2 = b - c_diag;
            int g3 = c_diag - a;

            // Quantize gradients
            int q1 = JpegLsPredictor.QuantizeGradient(g1, near);
            int q2 = JpegLsPredictor.QuantizeGradient(g2, near);
            int q3 = JpegLsPredictor.QuantizeGradient(g3, near);

            // Normalize gradients and track sign
            bool sign = JpegLsPredictor.NormalizeGradients(ref q1, ref q2, ref q3);

            // Compute context index
            int contextIndex = JpegLsPredictor.ComputeContextIndex(q1, q2, q3);

            // Median edge detection prediction
            int predicted = JpegLsPredictor.MedianEdgeDetection(a, b, c_diag);

            // Clamp prediction to valid range
            predicted = Clamp(predicted, 0, maxVal);

            // Read error from bitstream
            ref var ctx = ref contexts[contextIndex];
            int k = ctx.ComputeK(32);
            int mappedError = decoder.ReadGolombRice(k);

            // Unmap error value
            int correctedError = ErrorMapping.UnmapError(mappedError);

            // Apply sign from gradient normalization
            if (sign)
            {
                correctedError = -correctedError;
            }

            // Apply bias correction
            int biasCorrection = ctx.GetBiasCorrection();
            int rawError = correctedError + biasCorrection;

            // Reconstruct sample
            int sample = predicted + rawError;
            sample = Clamp(sample, 0, maxVal);

            // Update context
            ctx.Update(rawError, 64, range);

            return sample;
        }

        private static int GetSampleSingleComponent(
            byte[] buffer,
            int currentPos,
            int x,
            int y,
            int width,
            int bytesPerSample,
            int stride,
            int dx,
            int dy)
        {
            int nx = x + dx;
            int ny = y + dy;

            // Out of bounds - return 0
            if (nx < 0 || ny < 0 || nx >= width)
                return 0;

            // Calculate position in component buffer
            int samplePos = ny * stride + nx * bytesPerSample;

            // Sample not yet decoded
            if (samplePos < 0 || samplePos >= currentPos)
                return 0;

            // Read sample value
            if (bytesPerSample == 1)
            {
                return buffer[samplePos];
            }
            else
            {
                // 16-bit sample (little-endian)
                if (samplePos + 1 >= currentPos)
                    return 0;
                return buffer[samplePos] | (buffer[samplePos + 1] << 8);
            }
        }

        private static void WriteSampleAt(byte[] buffer, int pos, int sample, int bytesPerSample)
        {
            if (bytesPerSample == 1)
            {
                buffer[pos] = (byte)sample;
            }
            else
            {
                // 16-bit sample (little-endian)
                buffer[pos] = (byte)(sample & 0xFF);
                buffer[pos + 1] = (byte)(sample >> 8);
            }
        }

        private static int DecodeLineInterleaved(
            Span<byte> output,
            int width,
            int height,
            int components,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            // Decode line by line, all components per line
            int stride = width * components * bytesPerSample;
            int outputPos = 0;

            for (int y = 0; y < height; y++)
            {
                for (int c = 0; c < components; c++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sample = DecodeSample(output, outputPos, x, y, c, width, components, bytesPerSample, stride, near, maxVal, range, contexts, ref decoder);
                        WriteSample(output, ref outputPos, sample, bytesPerSample);
                    }
                }
            }

            return outputPos;
        }

        private static int DecodeSampleInterleaved(
            Span<byte> output,
            int width,
            int height,
            int components,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            // Decode sample by sample (pixel by pixel, all components per pixel)
            int stride = width * components * bytesPerSample;
            int outputPos = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        int sample = DecodeSample(output, outputPos, x, y, c, width, components, bytesPerSample, stride, near, maxVal, range, contexts, ref decoder);
                        WriteSample(output, ref outputPos, sample, bytesPerSample);
                    }
                }
            }

            return outputPos;
        }

        private static int DecodeSample(
            Span<byte> output,
            int currentPos,
            int x,
            int y,
            int c,
            int width,
            int components,
            int bytesPerSample,
            int stride,
            int near,
            int maxVal,
            int range,
            JlsContext[] contexts,
            ref GolombRiceDecoder decoder)
        {
            // Get neighboring samples for prediction
            int a = GetSample(output, currentPos, x, y, c, width, components, bytesPerSample, stride, -1, 0);  // left
            int b = GetSample(output, currentPos, x, y, c, width, components, bytesPerSample, stride, 0, -1);  // above
            int c_diag = GetSample(output, currentPos, x, y, c, width, components, bytesPerSample, stride, -1, -1); // above-left
            int d = GetSample(output, currentPos, x, y, c, width, components, bytesPerSample, stride, 1, -1);  // above-right

            // Compute gradients for context selection
            int g1 = d - b;
            int g2 = b - c_diag;
            int g3 = c_diag - a;

            // Quantize gradients
            int q1 = JpegLsPredictor.QuantizeGradient(g1, near);
            int q2 = JpegLsPredictor.QuantizeGradient(g2, near);
            int q3 = JpegLsPredictor.QuantizeGradient(g3, near);

            // Normalize gradients and track sign
            bool sign = JpegLsPredictor.NormalizeGradients(ref q1, ref q2, ref q3);

            // Compute context index
            int contextIndex = JpegLsPredictor.ComputeContextIndex(q1, q2, q3);

            // Median edge detection prediction
            int predicted = JpegLsPredictor.MedianEdgeDetection(a, b, c_diag);

            // Clamp prediction to valid range
            predicted = Clamp(predicted, 0, maxVal);

            // Read error from bitstream
            ref var ctx = ref contexts[contextIndex];
            int k = ctx.ComputeK(32);
            int mappedError = decoder.ReadGolombRice(k);

            // Unmap error value
            int correctedError = ErrorMapping.UnmapError(mappedError);

            // Apply sign from gradient normalization
            if (sign)
            {
                correctedError = -correctedError;
            }

            // Apply bias correction
            int biasCorrection = ctx.GetBiasCorrection();
            int rawError = correctedError + biasCorrection;

            // Reconstruct sample
            int sample = predicted + rawError;
            sample = Clamp(sample, 0, maxVal);

            // Update context
            ctx.Update(rawError, 64, range);

            return sample;
        }

        private static int GetSample(
            Span<byte> output,
            int currentPos,
            int x,
            int y,
            int c,
            int width,
            int components,
            int bytesPerSample,
            int stride,
            int dx,
            int dy)
        {
            int nx = x + dx;
            int ny = y + dy;

            // Out of bounds - return 0
            if (nx < 0 || ny < 0 || nx >= width)
                return 0;

            // Calculate position
            int samplePos = ny * stride + (nx * components + c) * bytesPerSample;

            // Sample not yet decoded
            if (samplePos < 0 || samplePos >= currentPos)
                return 0;

            // Read sample value
            if (bytesPerSample == 1)
            {
                return output[samplePos];
            }
            else
            {
                // 16-bit sample (little-endian)
                if (samplePos + 1 >= currentPos)
                    return 0;
                return output[samplePos] | (output[samplePos + 1] << 8);
            }
        }

        private static void WriteSample(Span<byte> output, ref int pos, int sample, int bytesPerSample)
        {
            if (bytesPerSample == 1)
            {
                output[pos++] = (byte)sample;
            }
            else
            {
                // 16-bit sample (little-endian)
                output[pos++] = (byte)(sample & 0xFF);
                output[pos++] = (byte)(sample >> 8);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
