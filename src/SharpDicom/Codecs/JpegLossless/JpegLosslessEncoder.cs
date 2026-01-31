using System;
using System.Buffers;
using System.Buffers.Binary;
using SharpDicom.Codecs.Jpeg;

namespace SharpDicom.Codecs.JpegLossless
{
    /// <summary>
    /// Encodes raw pixel data to JPEG Lossless (Process 14) format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This encoder implements JPEG Lossless compression as defined in ITU-T T.81 (Annex H)
    /// for the DICOM Transfer Syntax 1.2.840.10008.1.2.4.70 (JPEG Lossless, Non-Hierarchical,
    /// First-Order Prediction, Process 14, Selection Value 1).
    /// </para>
    /// <para>
    /// The encoding process:
    /// <list type="number">
    /// <item>Write SOI marker</item>
    /// <item>Write DHT marker with Huffman table</item>
    /// <item>Write SOF3 marker with frame parameters</item>
    /// <item>Write SOS marker with scan parameters</item>
    /// <item>For each pixel: compute prediction, encode difference</item>
    /// <item>Write EOI marker</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class JpegLosslessEncoder
    {
        /// <summary>
        /// Encodes a single frame to JPEG Lossless format.
        /// </summary>
        /// <param name="pixelData">The uncompressed pixel data in native (little-endian) format.</param>
        /// <param name="info">Pixel data metadata (dimensions, bit depth).</param>
        /// <param name="selectionValue">DPCM predictor selection (1-7, default 1 for DICOM).</param>
        /// <returns>The JPEG Lossless encoded data as a byte array.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="selectionValue"/> is not in range 1-7.
        /// </exception>
        /// <remarks>
        /// DICOM Transfer Syntax 1.2.840.10008.1.2.4.70 requires Selection Value 1 (horizontal prediction).
        /// </remarks>
        public static ReadOnlyMemory<byte> EncodeFrame(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            int selectionValue = 1)
        {
            if (selectionValue < 1 || selectionValue > 7)
            {
                throw new ArgumentOutOfRangeException(nameof(selectionValue),
                    "Selection value must be 1-7 for JPEG Lossless encoding.");
            }

            int width = info.Columns;
            int height = info.Rows;
            int precision = info.BitsStored;
            int components = info.SamplesPerPixel;
            int bytesPerSample = info.BytesPerSample;

            // Read samples from input
            int[] samples = ReadSamplesFromInput(pixelData, width * height * components, bytesPerSample);

            // Estimate output size: in worst case (random data), output may exceed input
            // For each sample: Huffman code (up to 16 bits) + category bits (up to 16 bits)
            // So worst case is ~4 bytes per sample. Add header overhead.
            int totalSamples = width * height * components;
            int estimatedSize = (totalSamples * 4) + 1024;
            byte[] outputBuffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

            try
            {
                int outputPosition = 0;

                // 1. Write SOI marker
                outputBuffer[outputPosition++] = JpegMarkers.Prefix;
                outputBuffer[outputPosition++] = JpegMarkers.SOI;

                // 2. Write DHT marker with Huffman table
                WriteHuffmanTable(outputBuffer, ref outputPosition, LosslessHuffman.Default);

                // 3. Write SOF3 marker (lossless frame header)
                WriteSOF3(outputBuffer, ref outputPosition, precision, height, width, components);

                // 4. Write SOS marker (start of scan)
                WriteSOS(outputBuffer, ref outputPosition, components, selectionValue);

                // 5. Encode entropy data
                int defaultValue = Predictor.GetDefaultValue(precision, 0);
                int entropyBytesWritten = EncodeEntropyData(
                    samples,
                    outputBuffer.AsSpan(outputPosition),
                    width, height, components,
                    precision, selectionValue, defaultValue,
                    LosslessHuffman.Default);
                outputPosition += entropyBytesWritten;

                // 6. Write EOI marker
                outputBuffer[outputPosition++] = JpegMarkers.Prefix;
                outputBuffer[outputPosition++] = JpegMarkers.EOI;

                // 7. Ensure even length (DICOM requirement)
                if (outputPosition % 2 != 0)
                {
                    outputBuffer[outputPosition++] = 0x00;
                }

                // Copy to exact-sized result
                byte[] result = new byte[outputPosition];
                Buffer.BlockCopy(outputBuffer, 0, result, 0, outputPosition);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(outputBuffer);
            }
        }

        private static int[] ReadSamplesFromInput(ReadOnlySpan<byte> pixelData, int sampleCount, int bytesPerSample)
        {
            int[] samples = new int[sampleCount];

            if (bytesPerSample == 1)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = pixelData[i];
                }
            }
            else
            {
                // Little-endian 16-bit input (DICOM native format)
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] = BinaryPrimitives.ReadUInt16LittleEndian(pixelData.Slice(i * 2));
                }
            }

            return samples;
        }

        private static void WriteHuffmanTable(byte[] output, ref int position, LosslessHuffman table)
        {
            // DHT marker
            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.DHT;

            int segmentStart = position;
            position += 2; // Reserve space for length

            // Write table
            int tableSize = table.GetDhtSegmentSize();
            table.WriteDhtSegment(output.AsSpan(position), 0);
            position += tableSize;

            // Write length (includes length field itself)
            int segmentLength = position - segmentStart;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(segmentStart), (ushort)segmentLength);
        }

        private static void WriteSOF3(byte[] output, ref int position, int precision, int height, int width, int components)
        {
            // SOF3 marker (lossless, Huffman)
            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.SOF3;

            // Segment length: 8 + 3 * Nf
            int segmentLength = 8 + 3 * components;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(position), (ushort)segmentLength);
            position += 2;

            // Sample precision P
            output[position++] = (byte)precision;

            // Number of lines Y (height)
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(position), (ushort)height);
            position += 2;

            // Number of samples per line X (width)
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(position), (ushort)width);
            position += 2;

            // Number of components Nf
            output[position++] = (byte)components;

            // Component specifications
            for (int c = 0; c < components; c++)
            {
                output[position++] = (byte)(c + 1);  // Component identifier Ci
                output[position++] = 0x11;           // Hi:Vi (1:1 sampling factors)
                output[position++] = 0;              // Tqi (quantization table selector, unused for lossless)
            }
        }

        private static void WriteSOS(byte[] output, ref int position, int components, int selectionValue)
        {
            // SOS marker
            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.SOS;

            // Segment length: 6 + 2 * Ns
            int segmentLength = 6 + 2 * components;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(position), (ushort)segmentLength);
            position += 2;

            // Number of components in scan Ns
            output[position++] = (byte)components;

            // Component specifications
            for (int c = 0; c < components; c++)
            {
                output[position++] = (byte)(c + 1);  // Component selector Csj
                output[position++] = 0x00;           // Tdj:Taj (Huffman table selectors)
            }

            // Ss (predictor selection value, 1-7)
            output[position++] = (byte)selectionValue;

            // Se (should be 0 for lossless)
            output[position++] = 0;

            // Ah:Al (successive approximation bit positions, Al = point transform)
            // For initial scan: Ah = 0, Al = 0
            output[position++] = 0x00;
        }

        private static int EncodeEntropyData(
            int[] samples,
            Span<byte> output,
            int width,
            int height,
            int components,
            int precision,
            int selectionValue,
            int defaultValue,
            LosslessHuffman huffmanTable)
        {
            // Use a large buffer for bit writing
            var writer = new BitWriter(output);

            for (int component = 0; component < components; component++)
            {
                int componentOffset = component * width * height;
                var componentSamples = samples.AsSpan().Slice(componentOffset, width * height);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        int sample = componentSamples[index];

                        // Get neighbors
                        Predictor.GetNeighbors(
                            componentSamples,
                            x, y, width, defaultValue,
                            out int a, out int b, out int c);

                        // Compute prediction
                        int prediction = Predictor.Predict(selectionValue, a, b, c);

                        // Compute difference
                        int diff = sample - prediction;

                        // Encode difference
                        huffmanTable.EncodeDifference(ref writer, diff);
                    }
                }
            }

            // Flush remaining bits (pads with 1s per JPEG spec)
            writer.Flush();

            return writer.BytesWritten;
        }
    }
}
