using System;
using System.Buffers.Binary;
using SharpDicom.Codecs.Jpeg;

namespace SharpDicom.Codecs.JpegLossless
{
    /// <summary>
    /// Decodes JPEG Lossless (Process 14) compressed pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This decoder implements JPEG Lossless compression as defined in ITU-T T.81 (Annex H)
    /// for the DICOM Transfer Syntax 1.2.840.10008.1.2.4.70 (JPEG Lossless, Non-Hierarchical,
    /// First-Order Prediction, Process 14, Selection Value 1).
    /// </para>
    /// <para>
    /// The decoding process:
    /// <list type="number">
    /// <item>Parse SOI marker</item>
    /// <item>Parse frame markers (SOF3, DHT, SOS)</item>
    /// <item>Initialize first pixel with 2^(P-Pt-1)</item>
    /// <item>For each pixel: predict + decode difference = reconstructed value</item>
    /// <item>Handle point transform if present</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class JpegLosslessDecoder
    {
        /// <summary>
        /// Decodes a single JPEG Lossless frame.
        /// </summary>
        /// <param name="compressedFrame">The JPEG Lossless compressed data (SOI to EOI).</param>
        /// <param name="info">Pixel data metadata (dimensions, bit depth).</param>
        /// <param name="output">Destination buffer for decompressed pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <returns>A DecodeResult indicating success or failure with diagnostics.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> compressedFrame,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex)
        {
            int position = 0;

            // 1. Find and validate SOI marker
            if (!FindMarker(compressedFrame, ref position, JpegMarkers.SOI))
            {
                return DecodeResult.Fail(frameIndex, 0, "Missing SOI marker");
            }

            // Parse frame parameters
            int precision = 0;
            int width = 0;
            int height = 0;
            int components = 0;
            int selectionValue = 1;
            int pointTransform = 0;
            LosslessHuffman? huffmanTable = null;
            int scanDataStart = 0;

            // 2. Parse markers until SOS
            while (position < compressedFrame.Length - 1)
            {
                if (compressedFrame[position] != JpegMarkers.Prefix)
                {
                    position++;
                    continue;
                }

                byte marker = compressedFrame[position + 1];
                position += 2;

                if (marker == JpegMarkers.EOI)
                {
                    break;
                }

                if (marker == JpegMarkers.SOI || marker == 0x00)
                {
                    continue;
                }

                // Markers with segments have 2-byte length following
                if (position + 2 > compressedFrame.Length)
                {
                    return DecodeResult.Fail(frameIndex, position, "Unexpected end of data");
                }

                int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(compressedFrame.Slice(position));

                // Validate segment length doesn't exceed remaining data
                if (segmentLength < 2 || position + segmentLength > compressedFrame.Length)
                {
                    return DecodeResult.Fail(frameIndex, position, $"Invalid segment length: {segmentLength}");
                }

                var segment = compressedFrame.Slice(position, segmentLength);
                position += segmentLength;

                switch (marker)
                {
                    case JpegMarkers.SOF3: // Lossless (Huffman)
                        if (!ParseSOF3(segment, out precision, out height, out width, out components))
                        {
                            return DecodeResult.Fail(frameIndex, position, "Invalid SOF3 marker");
                        }
                        break;

                    case JpegMarkers.DHT:
                        if (!LosslessHuffman.TryParseDHT(segment.Slice(2), out huffmanTable))
                        {
                            // Use default table if parsing fails
                            huffmanTable = LosslessHuffman.Default;
                        }
                        break;

                    case JpegMarkers.SOS:
                        if (!ParseSOS(segment, out selectionValue, out pointTransform))
                        {
                            return DecodeResult.Fail(frameIndex, position, "Invalid SOS marker");
                        }
                        scanDataStart = position;
                        goto DecodeEntropy;
                }
            }

        DecodeEntropy:
            // Validate we have required information
            if (width == 0 || height == 0 || precision == 0)
            {
                return DecodeResult.Fail(frameIndex, 0, "Missing frame information (SOF3)");
            }

            if (scanDataStart == 0)
            {
                return DecodeResult.Fail(frameIndex, 0, "Missing scan data (SOS marker not found)");
            }

            // Use default Huffman table if none provided
            huffmanTable ??= LosslessHuffman.Default;

            // Override dimensions from PixelDataInfo if available (more reliable)
            if (info.Columns > 0)
            {
                width = info.Columns;
            }
            if (info.Rows > 0)
            {
                height = info.Rows;
            }
            if (info.BitsStored > 0)
            {
                precision = info.BitsStored;
            }

            // 3. Decode entropy-coded data
            var scanData = compressedFrame.Slice(scanDataStart);
            return DecodeEntropyData(
                scanData,
                huffmanTable,
                width,
                height,
                components,
                precision,
                selectionValue,
                pointTransform,
                output,
                frameIndex);
        }

        private static bool FindMarker(ReadOnlySpan<byte> data, ref int position, byte marker)
        {
            while (position < data.Length - 1)
            {
                if (data[position] == JpegMarkers.Prefix && data[position + 1] == marker)
                {
                    position += 2;
                    return true;
                }
                position++;
            }
            return false;
        }

        private static bool ParseSOF3(ReadOnlySpan<byte> segment, out int precision, out int height, out int width, out int components)
        {
            precision = 0;
            height = 0;
            width = 0;
            components = 0;

            // SOF3 segment: Lh(2) + P(1) + Y(2) + X(2) + Nf(1) + component specs
            if (segment.Length < 8)
            {
                return false;
            }

            precision = segment[2];
            height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3));
            width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(5));
            components = segment[7];

            return precision >= 2 && precision <= 16 && width > 0 && height > 0 && components >= 1;
        }

        private static bool ParseSOS(ReadOnlySpan<byte> segment, out int selectionValue, out int pointTransform)
        {
            selectionValue = 1;
            pointTransform = 0;

            // SOS segment: Ls(2) + Ns(1) + component specs + Ss(1) + Se(1) + Ah:Al(1)
            // For lossless: Ss = predictor selection, Se = 0, Al = point transform
            if (segment.Length < 6)
            {
                return false;
            }

            int ns = segment[2]; // Number of components in scan
            int specOffset = 3 + ns * 2; // Skip component specifications

            if (segment.Length < specOffset + 3)
            {
                return false;
            }

            selectionValue = segment[specOffset];     // Ss (predictor selection)
            // segment[specOffset + 1] is Se (should be 0 for lossless)
            pointTransform = segment[specOffset + 2] & 0x0F; // Al (low nibble)

            return selectionValue >= 0 && selectionValue <= 7;
        }

        private static DecodeResult DecodeEntropyData(
            ReadOnlySpan<byte> scanData,
            LosslessHuffman huffmanTable,
            int width,
            int height,
            int components,
            int precision,
            int selectionValue,
            int pointTransform,
            Span<byte> output,
            int frameIndex)
        {
            // Allocate temporary buffer for decoded samples
            int totalSamples = width * height * components;
            int[] samples = new int[totalSamples];

            // Default prediction value: 2^(P-Pt-1)
            int defaultValue = Predictor.GetDefaultValue(precision, pointTransform);

            var reader = new BitReader(scanData);

            try
            {
                for (int component = 0; component < components; component++)
                {
                    int componentOffset = component * width * height;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int index = componentOffset + y * width + x;

                            // Get neighbors for this sample
                            Predictor.GetNeighbors(
                                samples.AsSpan().Slice(componentOffset, width * height),
                                x, y, width, defaultValue,
                                out int a, out int b, out int c);

                            // Compute prediction
                            int prediction = Predictor.Predict(selectionValue, a, b, c);

                            // Decode difference
                            int diff = huffmanTable.DecodeDifference(ref reader);

                            // Reconstruct sample
                            int sample = prediction + diff;

                            // Apply point transform (left shift)
                            if (pointTransform > 0)
                            {
                                sample <<= pointTransform;
                            }

                            // Clamp to valid range
                            int maxValue = (1 << precision) - 1;
                            sample = Math.Max(0, Math.Min(maxValue, sample));

                            samples[index] = sample;
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                return DecodeResult.Fail(frameIndex, reader.BytePosition, ex.Message);
            }

            // Write samples to output buffer
            int bytesPerSample = (precision + 7) / 8;
            int bytesWritten = WriteSamplesToOutput(samples, output, bytesPerSample);

            return DecodeResult.Ok(bytesWritten);
        }

        private static int WriteSamplesToOutput(int[] samples, Span<byte> output, int bytesPerSample)
        {
            int outputIndex = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                int sample = samples[i];

                if (bytesPerSample == 1)
                {
                    output[outputIndex++] = (byte)sample;
                }
                else
                {
                    // Little-endian 16-bit output (DICOM native format)
                    output[outputIndex++] = (byte)(sample & 0xFF);
                    output[outputIndex++] = (byte)((sample >> 8) & 0xFF);
                }
            }

            return outputIndex;
        }
    }
}
