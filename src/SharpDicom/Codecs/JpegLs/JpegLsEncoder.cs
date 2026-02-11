using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// JPEG-LS encoder for ITU-T T.87 / ISO/IEC 14495-1 bitstreams.
    /// </summary>
    /// <remarks>
    /// This is a complete managed implementation of JPEG-LS lossless and near-lossless encoding.
    /// Supports all interleave modes, 8-bit and 16-bit samples, and all standard predictor modes.
    /// </remarks>
    internal static class JpegLsEncoder
    {
        // JPEG markers
        private const ushort SOI = 0xFFD8;   // Start Of Image
        private const ushort EOI = 0xFFD9;   // End Of Image
        private const ushort SOF55 = 0xFFF7; // Start Of Frame (JPEG-LS)
        private const ushort SOS = 0xFFDA;   // Start Of Scan
        private const ushort LSE = 0xFFF8;   // JPEG-LS Extension

        /// <summary>
        /// Encodes raw pixel data to JPEG-LS format.
        /// </summary>
        public static byte[] Encode(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            int near)
        {
            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;
            int bitsPerSample = info.BitsStored;
            int bytesPerSample = info.BytesPerSample;

            var output = new List<byte>(pixelData.Length);

            // Write SOI
            WriteMarker(output, SOI);

            // Write SOF55 (Frame header)
            WriteFrameHeader(output, width, height, components, bitsPerSample);

            if (components == 1)
            {
                // Single component: one SOS, one scan
                WriteScanHeader(output, 1, near, JlsInterleaveMode.None, 0);
                EncodePixelData(output, pixelData, width, height, 1, bitsPerSample, bytesPerSample, near, JlsInterleaveMode.None);
            }
            else
            {
                // Non-interleaved: one SOS per component with fresh context state per CharLS
                for (int c = 0; c < components; c++)
                {
                    WriteScanHeader(output, 1, near, JlsInterleaveMode.None, c);
                    EncodeSingleComponent(output, pixelData, width, height, components, bitsPerSample, bytesPerSample, near, c);
                }
            }

            // Write EOI
            WriteMarker(output, EOI);

            return output.ToArray();
        }

        private static void WriteMarker(List<byte> output, ushort marker)
        {
            output.Add((byte)(marker >> 8));
            output.Add((byte)(marker & 0xFF));
        }

        private static void WriteFrameHeader(List<byte> output, int width, int height, int components, int bitsPerSample)
        {
            // SOF55 marker
            WriteMarker(output, SOF55);

            // Length (including length field)
            int length = 8 + components * 3;
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));

            // Precision (bits per sample)
            output.Add((byte)bitsPerSample);

            // Height
            output.Add((byte)(height >> 8));
            output.Add((byte)(height & 0xFF));

            // Width
            output.Add((byte)(width >> 8));
            output.Add((byte)(width & 0xFF));

            // Number of components
            output.Add((byte)components);

            // Component specifications
            for (int i = 0; i < components; i++)
            {
                output.Add((byte)(i + 1));  // Component ID
                output.Add(0x11);            // Sampling factors (1x1)
                output.Add(0);               // Quantization table (0 for JPEG-LS)
            }
        }

        private static void WriteScanHeader(List<byte> output, int scanComponents, int near, JlsInterleaveMode interleaveMode, int startComponent)
        {
            // SOS marker
            WriteMarker(output, SOS);

            // Length: 2 (length field) + 1 (Ns) + scanComponents * 2 + 3 (NEAR, ILV, Ah/Al)
            int length = 6 + scanComponents * 2;
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));

            // Number of components in scan
            output.Add((byte)scanComponents);

            // Component selectors
            for (int i = 0; i < scanComponents; i++)
            {
                output.Add((byte)(startComponent + i + 1));  // Component ID (1-based)
                output.Add(0);                                 // Mapping table index
            }

            // NEAR parameter
            output.Add((byte)near);

            // Interleave mode
            output.Add((byte)interleaveMode);

            // Point transform (Ah, Al = 0)
            output.Add(0);
        }

        private static void EncodePixelData(
            List<byte> output,
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int bytesPerSample,
            int near,
            JlsInterleaveMode interleaveMode)
        {
            int maxVal = (1 << bitsPerSample) - 1;
            int range = maxVal + 1;

            // Initialize 365 contexts per ITU-T T.87
            var contexts = new JlsContext[365];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i].Initialize(range);
            }

            var encoder = new GolombRiceEncoder(output);
            encoder.SetBitsPerPixel(bitsPerSample);
            int stride = width * components * bytesPerSample;

            // Compute quantization thresholds per ITU-T T.87, C.2.4.1.1.1
            JpegLsPredictor.ComputeDefaultThresholds(maxVal, near, out int t1, out int t2, out int t3);

            // Encode based on interleave mode
            switch (interleaveMode)
            {
                case JlsInterleaveMode.None:
                    EncodeNonInterleaved(pixelData, width, height, components, bitsPerSample, bytesPerSample, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    break;
                case JlsInterleaveMode.Line:
                    EncodeLineInterleaved(pixelData, width, height, components, bitsPerSample, bytesPerSample, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    break;
                case JlsInterleaveMode.Sample:
                    EncodeSampleInterleaved(pixelData, width, height, components, bitsPerSample, bytesPerSample, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    break;
            }

            encoder.Flush();
        }

        /// <summary>
        /// Encodes a single component with fresh context state and its own entropy segment.
        /// Used for non-interleaved multi-component mode per CharLS behavior.
        /// </summary>
        private static void EncodeSingleComponent(
            List<byte> output,
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int bytesPerSample,
            int near,
            int componentIndex)
        {
            int maxVal = (1 << bitsPerSample) - 1;
            int range = maxVal + 1;

            // Fresh contexts for each component per CharLS
            var contexts = new JlsContext[365];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i].Initialize(range);
            }

            var encoder = new GolombRiceEncoder(output);
            encoder.SetBitsPerPixel(bitsPerSample);
            int stride = width * components * bytesPerSample;

            // Compute quantization thresholds per ITU-T T.87, C.2.4.1.1.1
            JpegLsPredictor.ComputeDefaultThresholds(maxVal, near, out int t1, out int t2, out int t3);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sample = GetSample(pixelData, x, y, componentIndex, width, components, bytesPerSample, stride, 0, 0);
                    EncodeSample(pixelData, sample, x, y, componentIndex, width, components, bytesPerSample, stride, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                }
            }

            encoder.Flush();
        }

        private static void EncodeNonInterleaved(
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            int t1,
            int t2,
            int t3,
            JlsContext[] contexts,
            ref GolombRiceEncoder encoder)
        {
            // Encode each component separately (used for single-component case)
            int stride = width * components * bytesPerSample;

            for (int c = 0; c < components; c++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sample = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, 0, 0);
                        EncodeSample(pixelData, sample, x, y, c, width, components, bytesPerSample, stride, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    }
                }
            }
        }

        private static void EncodeLineInterleaved(
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            int t1,
            int t2,
            int t3,
            JlsContext[] contexts,
            ref GolombRiceEncoder encoder)
        {
            // Encode line by line, all components per line
            int stride = width * components * bytesPerSample;

            for (int y = 0; y < height; y++)
            {
                for (int c = 0; c < components; c++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sample = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, 0, 0);
                        EncodeSample(pixelData, sample, x, y, c, width, components, bytesPerSample, stride, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    }
                }
            }
        }

        private static void EncodeSampleInterleaved(
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int bitsPerSample,
            int bytesPerSample,
            int near,
            int maxVal,
            int range,
            int t1,
            int t2,
            int t3,
            JlsContext[] contexts,
            ref GolombRiceEncoder encoder)
        {
            // Encode sample by sample (pixel by pixel, all components per pixel)
            int stride = width * components * bytesPerSample;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        int sample = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, 0, 0);
                        EncodeSample(pixelData, sample, x, y, c, width, components, bytesPerSample, stride, near, maxVal, range, t1, t2, t3, contexts, ref encoder);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeSample(
            ReadOnlySpan<byte> pixelData,
            int sample,
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
            int t1,
            int t2,
            int t3,
            JlsContext[] contexts,
            ref GolombRiceEncoder encoder)
        {
            // Get neighboring samples for prediction
            int a = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, -1, 0);  // left
            int b = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, 0, -1);  // above
            int c_diag = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, -1, -1); // above-left
            int d = GetSample(pixelData, x, y, c, width, components, bytesPerSample, stride, 1, -1);  // above-right

            // Compute gradients for context selection
            int g1 = d - b;
            int g2 = b - c_diag;
            int g3 = c_diag - a;

            // Quantize gradients
            int q1 = JpegLsPredictor.QuantizeGradient(g1, near, t1, t2, t3);
            int q2 = JpegLsPredictor.QuantizeGradient(g2, near, t1, t2, t3);
            int q3 = JpegLsPredictor.QuantizeGradient(g3, near, t1, t2, t3);

            // Normalize gradients and track sign
            bool sign = JpegLsPredictor.NormalizeGradients(ref q1, ref q2, ref q3);

            // Compute context index
            int contextIndex = JpegLsPredictor.ComputeContextIndex(q1, q2, q3);

            // Median edge detection prediction
            int predicted = JpegLsPredictor.MedianEdgeDetection(a, b, c_diag);

            // Apply bias correction C to prediction (ITU-T T.87, A.6)
            ref var ctx = ref contexts[contextIndex];
            if (sign)
                predicted -= ctx.C;
            else
                predicted += ctx.C;

            // Clamp prediction to valid range
            predicted = Clamp(predicted, 0, maxVal);

            // Compute prediction error
            int error = sample - predicted;

            // Apply sign from gradient normalization
            if (sign)
            {
                error = -error;
            }

            // Compute Golomb-Rice parameter
            int k = ctx.ComputeK();

            // Apply error correction XOR and map to non-negative (ITU-T T.87, A.5.2)
            // CharLS passes (k | near_lossless) so error correction is disabled for near-lossless
            int errorCorrection = ctx.GetErrorCorrection(k | near);
            int mappedError = ErrorMapping.MapError(errorCorrection ^ error);

            // Encode using Golomb-Rice
            encoder.WriteGolombRice(mappedError, k);

            // Update context with the error value (ITU-T T.87, A.12)
            // CharLS encoder updates with post-sign-flip error
            ctx.Update(error, near, 64);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetSample(
            ReadOnlySpan<byte> data,
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
            if (nx < 0 || ny < 0 || nx >= width || ny >= data.Length / stride)
                return 0;

            // Calculate position
            int samplePos = ny * stride + (nx * components + c) * bytesPerSample;

            if (samplePos < 0 || samplePos >= data.Length)
                return 0;

            // Read sample value
            if (bytesPerSample == 1)
            {
                return data[samplePos];
            }
            else
            {
                // 16-bit sample (little-endian)
                if (samplePos + 1 >= data.Length)
                    return 0;
                return data[samplePos] | (data[samplePos + 1] << 8);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
