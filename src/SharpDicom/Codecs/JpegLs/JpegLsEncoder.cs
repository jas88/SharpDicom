using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SharpDicom.Codecs.JpegLs
{
    /// <summary>
    /// JPEG-LS encoder for ITU-T T.87 / ISO/IEC 14495-1 bitstreams.
    /// </summary>
    /// <remarks>
    /// This is a simplified managed implementation that handles basic JPEG-LS encoding.
    /// For production use, consider using a native library (CharLS) for better performance.
    /// </remarks>
    internal static class JpegLsEncoder
    {
        // JPEG markers
        private const ushort SOI = 0xFFD8;   // Start Of Image
        private const ushort EOI = 0xFFD9;   // End Of Image
        private const ushort SOF55 = 0xFFF7; // Start Of Frame (JPEG-LS)
        private const ushort SOS = 0xFFDA;   // Start Of Scan

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
            output.Add((byte)(SOI >> 8));
            output.Add((byte)(SOI & 0xFF));

            // Write SOF55 (Frame header)
            WriteFrameHeader(output, width, height, components, bitsPerSample);

            // Write SOS (Scan header)
            WriteScanHeader(output, components, near);

            // Encode pixel data
            EncodePixelData(output, pixelData, width, height, components, bitsPerSample, bytesPerSample, near);

            // Write EOI
            output.Add((byte)(EOI >> 8));
            output.Add((byte)(EOI & 0xFF));

            return output.ToArray();
        }

        private static void WriteFrameHeader(List<byte> output, int width, int height, int components, int bitsPerSample)
        {
            // SOF55 marker
            output.Add(0xFF);
            output.Add(0xF7);

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

        private static void WriteScanHeader(List<byte> output, int components, int near)
        {
            // SOS marker
            output.Add(0xFF);
            output.Add(0xDA);

            // Length
            int length = 6 + components * 2;
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));

            // Number of components in scan
            output.Add((byte)components);

            // Component selectors
            for (int i = 0; i < components; i++)
            {
                output.Add((byte)(i + 1));  // Component ID
                output.Add(0);               // Mapping table index
            }

            // NEAR parameter
            output.Add((byte)near);

            // Interleave mode (0 = non-interleaved for single component, line interleaved otherwise)
            output.Add((byte)(components > 1 ? 1 : 0));

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
            int near)
        {
            int maxVal = (1 << bitsPerSample) - 1;
            int range = maxVal + 1;

            // Initialize contexts
            var contexts = new JlsContextEncoder[365];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i].Initialize(range);
            }

            var writer = new JlsBitWriter(output);
            int inputPos = 0;
            int stride = width * components * bytesPerSample;

            // Encode line by line
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        // Get current sample
                        int sample;
                        if (bytesPerSample == 1)
                        {
                            sample = pixelData[inputPos++];
                        }
                        else
                        {
                            sample = pixelData[inputPos] | (pixelData[inputPos + 1] << 8);
                            inputPos += 2;
                        }

                        // Get neighboring samples
                        int a = GetSample(pixelData, stride, x, y, c, width, components, bytesPerSample, -1, 0);
                        int b = GetSample(pixelData, stride, x, y, c, width, components, bytesPerSample, 0, -1);
                        int cb = GetSample(pixelData, stride, x, y, c, width, components, bytesPerSample, -1, -1);
                        int d = GetSample(pixelData, stride, x, y, c, width, components, bytesPerSample, 1, -1);

                        // Compute gradients
                        int g1 = d - b;
                        int g2 = b - cb;
                        int g3 = cb - a;

                        // Quantize gradients
                        int q1 = QuantizeGradient(g1, near);
                        int q2 = QuantizeGradient(g2, near);
                        int q3 = QuantizeGradient(g3, near);

                        bool sign = false;
                        if (q1 < 0 || (q1 == 0 && q2 < 0) || (q1 == 0 && q2 == 0 && q3 < 0))
                        {
                            q1 = -q1;
                            q2 = -q2;
                            q3 = -q3;
                            sign = true;
                        }

                        int contextIndex = ComputeContextIndex(q1, q2, q3);

                        // Median prediction
                        int predicted;
                        if (cb >= Math.Max(a, b))
                            predicted = Math.Min(a, b);
                        else if (cb <= Math.Min(a, b))
                            predicted = Math.Max(a, b);
                        else
                            predicted = a + b - cb;

                        // Compute error
                        ref var ctx = ref contexts[contextIndex];
                        int rawError = sample - predicted;

                        // Apply context correction
                        int correctedError = rawError - (ctx.C + (ctx.N >> 1)) / ctx.N;
                        if (sign)
                            correctedError = -correctedError;

                        // Map error for encoding
                        int mappedError;
                        if (correctedError >= 0)
                            mappedError = correctedError << 1;
                        else
                            mappedError = -(correctedError << 1) - 1;

                        // Encode using Golomb-Rice
                        int k = ctx.ComputeK(32);
                        writer.WriteGolombRice(mappedError, k);

                        // Update context with original error
                        ctx.Update(rawError - (ctx.C + (ctx.N >> 1)) / ctx.N, 64, range);
                    }
                }
            }

            writer.Flush();
        }

        private static int GetSample(
            ReadOnlySpan<byte> data,
            int stride,
            int x,
            int y,
            int c,
            int width,
            int components,
            int bytesPerSample,
            int dx,
            int dy)
        {
            int nx = x + dx;
            int ny = y + dy;

            if (nx < 0 || ny < 0 || nx >= width)
                return 0;

            int samplePos = ny * stride + (nx * components + c) * bytesPerSample;

            if (samplePos < 0 || samplePos >= data.Length)
                return 0;

            if (bytesPerSample == 1)
                return data[samplePos];
            else
                return data[samplePos] | (data[samplePos + 1] << 8);
        }

        private static int QuantizeGradient(int g, int near)
        {
            int t1 = 3 + near;
            int t2 = 7 + near;
            int t3 = 21 + near;

            if (g <= -t3) return -4;
            if (g <= -t2) return -3;
            if (g <= -t1) return -2;
            if (g < -near) return -1;
            if (g <= near) return 0;
            if (g < t1) return 1;
            if (g < t2) return 2;
            if (g < t3) return 3;
            return 4;
        }

        private static int ComputeContextIndex(int q1, int q2, int q3)
        {
            return (q1 * 9 + q2) * 9 + q3 + (9 * 9 * 4);
        }

        /// <summary>
        /// Context state for JPEG-LS encoding.
        /// </summary>
        private struct JlsContextEncoder
        {
            public int A;
            public int B;
            public int C;
            public int N;

            public void Initialize(int range)
            {
                A = Math.Max(2, (range + 32) / 64);
                B = 0;
                C = 0;
                N = 1;
            }

            public int ComputeK(int limit)
            {
                int k = 0;
                int nTimesA = N * A;
                while ((N << k) < nTimesA && k < limit)
                    k++;
                return k;
            }

            public void Update(int error, int reset, int range)
            {
                int absError = error < 0 ? -error : error;
                A += absError;
                B += error;
                N++;

                if (N == reset)
                {
                    A = (A + 1) >> 1;
                    B = (B + 1) >> 1;
                    N = (N + 1) >> 1;
                }

                if (B <= -N)
                {
                    B = Math.Max(B + N, 1 - N);
                    if (C > -128) C--;
                }
                else if (B > 0)
                {
                    B = Math.Min(B - N, 0);
                    if (C < 127) C++;
                }
            }
        }

        /// <summary>
        /// Bit writer for Golomb-Rice coded data.
        /// </summary>
        private ref struct JlsBitWriter
        {
            private List<byte> _output;
            private uint _buffer;
            private int _bitCount;

            public JlsBitWriter(List<byte> output)
            {
                _output = output;
                _buffer = 0;
                _bitCount = 0;
            }

            public void WriteGolombRice(int value, int k)
            {
                // Unary part (quotient)
                int q = value >> k;
                for (int i = 0; i < q; i++)
                {
                    WriteBit(0);
                }
                WriteBit(1);

                // Binary part (remainder)
                for (int i = k - 1; i >= 0; i--)
                {
                    WriteBit((value >> i) & 1);
                }
            }

            private void WriteBit(int bit)
            {
                _buffer = (_buffer << 1) | (uint)(bit & 1);
                _bitCount++;

                if (_bitCount == 8)
                {
                    _output.Add((byte)_buffer);

                    // Bit stuffing: after 0xFF, insert 0x00
                    if (_buffer == 0xFF)
                    {
                        _output.Add(0x00);
                    }

                    _buffer = 0;
                    _bitCount = 0;
                }
            }

            public void Flush()
            {
                if (_bitCount > 0)
                {
                    _buffer <<= (8 - _bitCount);
                    _output.Add((byte)_buffer);

                    if (_buffer == 0xFF)
                    {
                        _output.Add(0x00);
                    }
                }
            }
        }
    }
}
