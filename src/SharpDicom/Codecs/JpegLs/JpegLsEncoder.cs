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
    /// Complete managed implementation matching CharLS reference behavior including
    /// run mode encoding, modulo range reduction, and correct edge pixel initialization.
    /// </remarks>
    internal static class JpegLsEncoder
    {
        // JPEG markers
        private const ushort SOI = 0xFFD8;   // Start Of Image
        private const ushort EOI = 0xFFD9;   // End Of Image
        private const ushort SOF55 = 0xFFF7; // Start Of Frame (JPEG-LS)
        private const ushort SOS = 0xFFDA;   // Start Of Scan

        // Run length J table per ITU-T T.87, A.2.1
        private static readonly int[] J = {
            0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
            4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15
        };

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
                EncodeScanComponent(output, pixelData, width, height, 1, 0, bitsPerSample, bytesPerSample, near);
            }
            else
            {
                // Non-interleaved: one SOS per component with fresh context state per CharLS
                for (int c = 0; c < components; c++)
                {
                    WriteScanHeader(output, 1, near, JlsInterleaveMode.None, c);
                    EncodeScanComponent(output, pixelData, width, height, components, c, bitsPerSample, bytesPerSample, near);
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
            WriteMarker(output, SOF55);
            int length = 8 + components * 3;
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));
            output.Add((byte)bitsPerSample);
            output.Add((byte)(height >> 8));
            output.Add((byte)(height & 0xFF));
            output.Add((byte)(width >> 8));
            output.Add((byte)(width & 0xFF));
            output.Add((byte)components);
            for (int i = 0; i < components; i++)
            {
                output.Add((byte)(i + 1));
                output.Add(0x11);
                output.Add(0);
            }
        }

        private static void WriteScanHeader(List<byte> output, int scanComponents, int near, JlsInterleaveMode interleaveMode, int startComponent)
        {
            WriteMarker(output, SOS);
            int length = 6 + scanComponents * 2;
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));
            output.Add((byte)scanComponents);
            for (int i = 0; i < scanComponents; i++)
            {
                output.Add((byte)(startComponent + i + 1));
                output.Add(0);
            }
            output.Add((byte)near);
            output.Add((byte)interleaveMode);
            output.Add(0);
        }

        /// <summary>
        /// Encodes a single component scan using CharLS-compatible line buffer approach.
        /// </summary>
        private static void EncodeScanComponent(
            List<byte> output,
            ReadOnlySpan<byte> pixelData,
            int width,
            int height,
            int components,
            int componentIndex,
            int bitsPerSample,
            int bytesPerSample,
            int near)
        {
            int maxVal = (1 << bitsPerSample) - 1;
            int range = ComputeRange(maxVal, near);
            int qbpp = near == 0 ? bitsPerSample : Log2Ceiling(range);

            // Initialize 365 regular mode contexts
            var contexts = new JlsContext[365];
            for (int i = 0; i < contexts.Length; i++)
                contexts[i].Initialize(range);

            // Initialize 2 run mode contexts
            var runContexts = new JlsRunModeContext[2];
            runContexts[0].Initialize(0, range);
            runContexts[1].Initialize(1, range);

            var encoder = new GolombRiceEncoder(output);
            encoder.SetBitsPerPixel(bitsPerSample, qbpp);

            JpegLsPredictor.ComputeDefaultThresholds(maxVal, near, out int t1, out int t2, out int t3);

            int stride = width * components * bytesPerSample;

            // Line buffers: width + 2 elements (index 0 and width+1 are edge pixels)
            int lineWidth = width + 2;
            int[] previousLine = new int[lineWidth];
            int[] currentLine = new int[lineWidth];

            int runIndex = 0;

            for (int y = 0; y < height; y++)
            {
                // Copy source pixels into currentLine[1..width]
                for (int x = 0; x < width; x++)
                {
                    currentLine[x + 1] = ReadSample(pixelData, x, y, componentIndex, width, components, bytesPerSample, stride);
                }

                // Edge pixel initialization per CharLS
                // previous_line[width + 1] = previous_line[width] (Rd at last col = Rb at last col)
                previousLine[width + 1] = previousLine[width];
                // current_line[0] = previous_line[1] (Ra at first col = Rb at first col)
                currentLine[0] = previousLine[1];

                // Encode the line (matching CharLS encode_sample_line exactly)
                int index = 1;
                int rb = previousLine[0];
                int rd = previousLine[1];

                while (index <= width)
                {
                    int ra = currentLine[index - 1];
                    int rc = rb;
                    rb = rd;
                    rd = previousLine[index + 1];

                    // Compute context ID
                    int q1 = QuantizeGradient(rd - rb, near, t1, t2, t3);
                    int q2 = QuantizeGradient(rb - rc, near, t1, t2, t3);
                    int q3 = QuantizeGradient(rc - ra, near, t1, t2, t3);
                    int qs = (q1 * 9 + q2) * 9 + q3;

                    if (qs != 0)
                    {
                        // Regular mode
                        currentLine[index] = EncodeRegular(qs, currentLine[index],
                            JpegLsPredictor.MedianEdgeDetection(ra, rb, rc),
                            maxVal, near, range, bitsPerSample, qbpp, contexts, ref encoder);
                        index++;
                    }
                    else
                    {
                        // Run mode
                        int runLength = EncodeRunMode(currentLine, previousLine, index, width, ra,
                            maxVal, near, range, bitsPerSample, qbpp, contexts, runContexts, ref runIndex, ref encoder);
                        index += runLength;
                        rb = previousLine[index - 1];
                        rd = previousLine[index];
                    }
                }

                // Swap line buffers
                var temp = previousLine;
                previousLine = currentLine;
                currentLine = temp;
            }

            encoder.Flush();
        }

        /// <summary>
        /// Encodes a sample in regular mode, matching CharLS encode_regular exactly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int EncodeRegular(
            int qs,
            int x,
            int predicted,
            int maxVal,
            int near,
            int range,
            int bitsPerSample,
            int qbpp,
            JlsContext[] contexts,
            ref GolombRiceEncoder encoder)
        {
            // Extract sign from context ID (CharLS: bit_wise_sign(qs))
            int sign = qs >> 31; // 0 or -1

            // Get context by absolute value of qs
            int contextIndex = (sign ^ qs) - sign; // abs(qs)
            ref var ctx = ref contexts[contextIndex];

            int k = ctx.ComputeK();

            // Apply bias correction to prediction (CharLS: predicted + apply_sign(C, sign))
            // apply_sign(i, sign) = (i ^ sign) - sign
            int correctedPrediction = predicted + ((ctx.C ^ sign) - sign);

            // Clamp prediction (CharLS: correct_prediction)
            correctedPrediction = CorrectPrediction(correctedPrediction, maxVal);

            // Compute error with sign flip
            // CharLS: compute_error_value(apply_sign(x - predicted_value, sign))
            int rawError = x - correctedPrediction;
            int signedError = (rawError ^ sign) - sign; // apply_sign

            int errorValue;
            if (near == 0)
            {
                // Lossless: bit-shift modulo range reduction
                errorValue = ModuloRange(signedError, bitsPerSample);
            }
            else
            {
                // Near-lossless: quantize then range-based modulo
                errorValue = ModuloRangeNearLossless(Quantize(signedError, near), range);
            }

            // Map error value with XOR error correction
            int errorCorrection = ctx.GetErrorCorrection(k | near);
            int mappedError = MapErrorValue(errorCorrection ^ errorValue);

            // Encode
            encoder.WriteGolombRice(mappedError, k);

            // Update context
            ctx.Update(errorValue, near, 64);

            // Compute reconstructed sample for the reference line
            int reconstructedError = (errorValue ^ sign) - sign;
            if (near == 0)
            {
                return ComputeReconstructedSample(correctedPrediction, reconstructedError, maxVal);
            }
            else
            {
                return FixReconstructedValue(
                    correctedPrediction + Dequantize(reconstructedError, near),
                    near, maxVal, range);
            }
        }

        /// <summary>
        /// Encodes a run of pixels per ITU-T T.87, A.7.
        /// Returns the number of pixels consumed (run_length or run_length + 1 if interrupted).
        /// </summary>
        private static int EncodeRunMode(
            int[] currentLine,
            int[] previousLine,
            int startIndex,
            int width,
            int ra,
            int maxVal,
            int near,
            int range,
            int bitsPerSample,
            int qbpp,
            JlsContext[] contexts,
            JlsRunModeContext[] runContexts,
            ref int runIndex,
            ref GolombRiceEncoder encoder)
        {
            int countRemain = width - (startIndex - 1);
            int runLength = 0;

            // Count run length (lossless: exact match; near-lossless: within NEAR)
            while (runLength < countRemain)
            {
                if (near == 0)
                {
                    if (currentLine[startIndex + runLength] != ra)
                        break;
                }
                else
                {
                    if (Math.Abs(currentLine[startIndex + runLength] - ra) > near)
                        break;
                }
                currentLine[startIndex + runLength] = ra; // Replace with prediction for reference
                runLength++;
            }

            bool endOfLine = (runLength == countRemain);

            // Encode run pixels (ITU-T T.87, A.7.1)
            EncodeRunPixels(runLength, endOfLine, ref runIndex, ref encoder);

            if (endOfLine)
                return runLength;

            // Run interruption: encode the interruption pixel
            int ix = startIndex + runLength;
            int rxVal = currentLine[ix];
            int rbVal = previousLine[ix];

            currentLine[ix] = EncodeRunInterruptionPixel(rxVal, ra, rbVal,
                maxVal, near, range, bitsPerSample, qbpp, runContexts, ref runIndex, ref encoder);

            // Decrement run index
            if (runIndex > 0) runIndex--;

            return runLength + 1;
        }

        /// <summary>
        /// Encodes run length per ITU-T T.87, A.7.1.
        /// </summary>
        private static void EncodeRunPixels(int runLength, bool endOfLine, ref int runIndex, ref GolombRiceEncoder encoder)
        {
            while (runLength >= (1 << J[runIndex]))
            {
                encoder.AppendOnesToBitStream(1);
                runLength -= (1 << J[runIndex]);
                if (runIndex < 31) runIndex++;
            }

            if (endOfLine)
            {
                if (runLength != 0)
                {
                    encoder.AppendOnesToBitStream(1);
                }
            }
            else
            {
                // Write leading 0 + remaining run length in J[runIndex] bits
                encoder.AppendBitsPublic((uint)runLength, J[runIndex] + 1);
            }
        }

        /// <summary>
        /// Encodes a run interruption pixel per ITU-T T.87, A.7.2.
        /// </summary>
        private static int EncodeRunInterruptionPixel(
            int x, int ra, int rb,
            int maxVal, int near, int range, int bitsPerSample, int qbpp,
            JlsRunModeContext[] runContexts,
            ref int runIndex,
            ref GolombRiceEncoder encoder)
        {
            if (Math.Abs(ra - rb) <= near)
            {
                int errorValue;
                if (near == 0)
                {
                    errorValue = ModuloRange(x - ra, bitsPerSample);
                }
                else
                {
                    errorValue = ModuloRangeNearLossless(Quantize(x - ra, near), range);
                }
                EncodeRunInterruptionError(ref runContexts[1], errorValue, near, bitsPerSample, qbpp, runIndex, ref encoder);
                if (near == 0)
                {
                    return ComputeReconstructedSample(ra, errorValue, maxVal);
                }
                else
                {
                    return FixReconstructedValue(ra + Dequantize(errorValue, near), near, maxVal, range);
                }
            }
            else
            {
                int signVal = Sign(rb - ra);
                int errorValue;
                if (near == 0)
                {
                    errorValue = ModuloRange((x - rb) * signVal, bitsPerSample);
                }
                else
                {
                    errorValue = ModuloRangeNearLossless(Quantize((x - rb) * signVal, near), range);
                }
                EncodeRunInterruptionError(ref runContexts[0], errorValue, near, bitsPerSample, qbpp, runIndex, ref encoder);
                if (near == 0)
                {
                    return ComputeReconstructedSample(rb, errorValue * signVal, maxVal);
                }
                else
                {
                    return FixReconstructedValue(rb + Dequantize(errorValue, near) * signVal, near, maxVal, range);
                }
            }
        }

        /// <summary>
        /// Encodes a run interruption error per ITU-T T.87, A.7.2.
        /// </summary>
        private static void EncodeRunInterruptionError(
            ref JlsRunModeContext context,
            int errorValue,
            int near,
            int bitsPerSample,
            int qbpp,
            int runIndex,
            ref GolombRiceEncoder encoder)
        {
            int k = context.ComputeK();
            bool map = context.ComputeMap(errorValue, k);
            int eMappedErrorValue = 2 * Math.Abs(errorValue) - context.RunInterruptionType - (map ? 1 : 0);

            // LIMIT for run interruption = LIMIT - J[runIndex] - 1
            int limit = ComputeLimit(bitsPerSample);
            encoder.WriteGolombRiceWithLimit(eMappedErrorValue, k, limit - J[runIndex] - 1, qbpp);

            context.UpdateVariables(errorValue, eMappedErrorValue, 64);
        }

        /// <summary>
        /// Sign function matching CharLS: returns -1, 0, or 1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Sign(int value)
        {
            if (value > 0) return 1;
            if (value < 0) return -1;
            return 0;
        }

        /// <summary>
        /// Modulo range reduction per CharLS lossless_traits::modulo_range.
        /// For 8-bit: equivalent to (int8_t)cast, wraps to [-128, 127].
        /// For 16-bit: equivalent to (int16_t)cast, wraps to [-32768, 32767].
        /// General: sign-extend from bitsPerSample bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ModuloRange(int errorValue, int bitsPerSample)
        {
            // CharLS: (int32_t)((uint32_t)error << (32 - bpp)) >> (32 - bpp)
            int shift = 32 - bitsPerSample;
            return ((int)((uint)errorValue << shift)) >> shift;
        }

        /// <summary>
        /// Corrects prediction to [0, maxVal] range per CharLS correct_prediction.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CorrectPrediction(int predicted, int maxVal)
        {
            if ((predicted & maxVal) == predicted)
                return predicted;
            return (~(predicted >> 31)) & maxVal;
        }

        /// <summary>
        /// Computes reconstructed sample per CharLS compute_reconstructed_sample.
        /// For lossless: (maxVal &amp; (predicted + error)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeReconstructedSample(int predicted, int errorValue, int maxVal)
        {
            return maxVal & (predicted + errorValue);
        }

        /// <summary>
        /// Maps a signed error to non-negative per ITU-T T.87, A.5.2.
        /// Same as ErrorMapping.MapError but inlined for encoder use.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MapErrorValue(int errorValue)
        {
            if (errorValue >= 0)
                return errorValue << 1;
            return ((-errorValue) << 1) - 1;
        }

        /// <summary>
        /// Quantizes a gradient for context selection.
        /// This matches CharLS quantize_gradient_org.
        /// </summary>
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

        /// <summary>
        /// Reads a sample value from pixel data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReadSample(ReadOnlySpan<byte> data, int x, int y, int c, int width, int components, int bytesPerSample, int stride)
        {
            int pos = y * stride + (x * components + c) * bytesPerSample;
            if (bytesPerSample == 1)
                return data[pos];
            return data[pos] | (data[pos + 1] << 8);
        }

        /// <summary>
        /// Computes RANGE parameter per ITU-T T.87.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeRange(int maxVal, int near)
        {
            if (near == 0)
                return maxVal + 1;
            return (maxVal + 2 * near) / (2 * near + 1) + 1;
        }

        /// <summary>
        /// Computes LIMIT parameter per CharLS.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ComputeLimit(int bitsPerSample)
        {
            return 2 * (bitsPerSample + Math.Max(8, bitsPerSample));
        }

        /// <summary>
        /// Computes ceil(log2(n)) matching CharLS log2_ceiling exactly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Log2Ceiling(int n)
        {
            int k = 0;
            int v = 1;
            while (v < n)
            {
                k++;
                v *= 2;
            }
            return k;
        }

        /// <summary>
        /// Quantizes a prediction error for near-lossless mode.
        /// Matches CharLS default_traits::quantize_sample_value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Quantize(int errorValue, int near)
        {
            if (errorValue > 0)
                return (errorValue + near) / (2 * near + 1);
            return -(near - errorValue) / (2 * near + 1);
        }

        /// <summary>
        /// Dequantizes an error value for near-lossless reconstruction.
        /// Matches CharLS default_traits::dequantize_sample_value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Dequantize(int errorValue, int near)
        {
            return errorValue * (2 * near + 1);
        }

        /// <summary>
        /// Range-based modulo reduction for near-lossless mode.
        /// Matches CharLS default_traits::modulo_range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ModuloRangeNearLossless(int errorValue, int range)
        {
            if (errorValue < 0)
                errorValue += range;
            if (errorValue >= (range + 1) / 2)
                errorValue -= range;
            return errorValue;
        }

        /// <summary>
        /// Fixes a reconstructed sample for near-lossless mode.
        /// Matches CharLS default_traits::fix_reconstructed_value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FixReconstructedValue(int value, int near, int maxVal, int range)
        {
            if (value < -near)
                value += range * (2 * near + 1);
            else if (value > maxVal + near)
                value -= range * (2 * near + 1);
            return CorrectPrediction(value, maxVal);
        }
    }
}
