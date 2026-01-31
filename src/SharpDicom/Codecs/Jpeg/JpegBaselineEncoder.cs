using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Encodes raw pixel data to JPEG Baseline (Process 1) format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This encoder produces JPEG Baseline DCT output (SOF0) compatible with DICOM
    /// Transfer Syntax 1.2.840.10008.1.2.4.50. It supports 8-bit grayscale and RGB images.
    /// </para>
    /// <para>
    /// The encoding process involves:
    /// 1. Color conversion (RGB to YCbCr for color images)
    /// 2. Level shift (-128)
    /// 3. Forward DCT
    /// 4. Quantization using quality-scaled tables
    /// 5. Huffman encoding
    /// 6. Marker segment generation (SOI, DQT, SOF0, DHT, SOS, EOI)
    /// </para>
    /// </remarks>
    public static class JpegBaselineEncoder
    {
        /// <summary>
        /// Encodes a single frame to JPEG Baseline format.
        /// </summary>
        /// <param name="pixelData">The raw pixel data for a single frame.</param>
        /// <param name="info">Pixel data metadata.</param>
        /// <param name="options">JPEG encoding options (quality, subsampling, etc.).</param>
        /// <returns>The JPEG-encoded frame data.</returns>
        public static ReadOnlyMemory<byte> EncodeFrame(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            JpegCodecOptions options)
        {
            int width = info.Columns;
            int height = info.Rows;
            int componentCount = info.SamplesPerPixel;

            // Validate
            if (info.BitsAllocated != 8)
            {
                throw new ArgumentException("JPEG Baseline only supports 8-bit samples.", nameof(info));
            }

            // Estimate output size (typically < original for lossy)
            int estimatedSize = pixelData.Length + 1024; // Extra for headers
            byte[]? outputBuffer = null;

            try
            {
                outputBuffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
                var output = outputBuffer.AsSpan();
                int position = 0;

                // 1. Write SOI marker
                output[position++] = JpegMarkers.Prefix;
                output[position++] = JpegMarkers.SOI;

                // 2. Optional JFIF APP0 marker
                if (options.IncludeJfifMarker)
                {
                    position = WriteJfifMarker(output, position);
                }

                // 3. Write DQT markers (quantization tables)
                var quantTables = CreateQuantizationTables(options.Quality);
                position = WriteDqtMarkers(output, position, quantTables, componentCount);

                // 4. Write SOF0 marker
                position = WriteSof0Marker(output, position, width, height, componentCount, options.Subsampling);

                // 5. Write DHT markers (Huffman tables)
                position = WriteDhtMarkers(output, position, componentCount);

                // 6. Write SOS marker and entropy-coded data
                position = WriteSosMarkerAndScanData(output, position, pixelData, info, options, quantTables);

                // 7. Write EOI marker
                output[position++] = JpegMarkers.Prefix;
                output[position++] = JpegMarkers.EOI;

                // 8. Ensure even length for DICOM
                if (position % 2 != 0)
                {
                    output[position++] = 0x00;
                }

                // Return the encoded data
                return outputBuffer.AsMemory(0, position).ToArray();
            }
            finally
            {
                if (outputBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(outputBuffer);
                }
            }
        }

        private static QuantizationTable[] CreateQuantizationTables(int quality)
        {
            var luminance = QuantizationTable.CreateScaled(QuantizationTable.LuminanceDefault, quality, 0);
            var chrominance = QuantizationTable.CreateScaled(QuantizationTable.ChrominanceDefault, quality, 1);
            return new[] { luminance, chrominance };
        }

        private static int WriteJfifMarker(Span<byte> output, int position)
        {
            // APP0 marker
            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.APP0;

            // Length (16 bytes including length field)
            output[position++] = 0x00;
            output[position++] = 0x10;

            // JFIF identifier
            output[position++] = (byte)'J';
            output[position++] = (byte)'F';
            output[position++] = (byte)'I';
            output[position++] = (byte)'F';
            output[position++] = 0x00;

            // Version 1.01
            output[position++] = 0x01;
            output[position++] = 0x01;

            // Aspect ratio units (0 = no units)
            output[position++] = 0x00;

            // X and Y density (1:1)
            output[position++] = 0x00;
            output[position++] = 0x01;
            output[position++] = 0x00;
            output[position++] = 0x01;

            // No thumbnail
            output[position++] = 0x00;
            output[position++] = 0x00;

            return position;
        }

        private static int WriteDqtMarkers(
            Span<byte> output,
            int position,
            QuantizationTable[] tables,
            int componentCount)
        {
            // Write one DQT marker containing all needed tables
            int tableCount = componentCount == 1 ? 1 : 2;

            // Calculate total segment length: 2 (length) + tableCount * (1 + 64)
            int segmentLength = 2 + tableCount * 65;

            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.DQT;
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)segmentLength);
            position += 2;

            for (int i = 0; i < tableCount; i++)
            {
                // Table header: precision (0 = 8-bit) in high nibble, table ID in low nibble
                output[position++] = (byte)i;

                // 64 quantization values in zigzag order
                var values = tables[i].GetValues();
                for (int j = 0; j < 64; j++)
                {
                    output[position++] = (byte)Math.Max(1, Math.Min(255, values[j]));
                }
            }

            return position;
        }

        private static int WriteSof0Marker(
            Span<byte> output,
            int position,
            int width,
            int height,
            int componentCount,
            ChromaSubsampling subsampling)
        {
            // Segment length: 8 + 3 * componentCount
            int segmentLength = 8 + 3 * componentCount;

            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.SOF0;
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)segmentLength);
            position += 2;

            // Precision (8 bits)
            output[position++] = 8;

            // Height and width (big-endian)
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)height);
            position += 2;
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)width);
            position += 2;

            // Component count
            output[position++] = (byte)componentCount;

            // Component specifications
            if (componentCount == 1)
            {
                // Grayscale: component 1, 1x1 sampling, quant table 0
                output[position++] = 1;       // Component ID
                output[position++] = 0x11;    // 1x1 sampling
                output[position++] = 0;       // Quant table 0
            }
            else
            {
                // Color: Y, Cb, Cr components
                byte ySampling = subsampling switch
                {
                    ChromaSubsampling.None => 0x11,        // 1x1
                    ChromaSubsampling.Horizontal => 0x21, // 2x1
                    ChromaSubsampling.Both => 0x22,       // 2x2
                    _ => 0x11
                };

                // Y component
                output[position++] = 1;           // Component ID
                output[position++] = ySampling;   // Sampling factors
                output[position++] = 0;           // Quant table 0

                // Cb component
                output[position++] = 2;           // Component ID
                output[position++] = 0x11;        // 1x1 sampling
                output[position++] = 1;           // Quant table 1

                // Cr component
                output[position++] = 3;           // Component ID
                output[position++] = 0x11;        // 1x1 sampling
                output[position++] = 1;           // Quant table 1
            }

            return position;
        }

        private static int WriteDhtMarkers(Span<byte> output, int position, int componentCount)
        {
            // Write DC and AC Huffman tables
            // For grayscale: only luminance tables
            // For color: luminance + chrominance tables

            // Luminance DC table
            position = WriteSingleDhtTable(output, position, HuffmanTable.LuminanceDC, 0, 0);

            // Luminance AC table
            position = WriteSingleDhtTable(output, position, HuffmanTable.LuminanceAC, 1, 0);

            if (componentCount > 1)
            {
                // Chrominance DC table
                position = WriteSingleDhtTable(output, position, HuffmanTable.ChrominanceDC, 0, 1);

                // Chrominance AC table
                position = WriteSingleDhtTable(output, position, HuffmanTable.ChrominanceAC, 1, 1);
            }

            return position;
        }

        private static int WriteSingleDhtTable(
            Span<byte> output,
            int position,
            HuffmanTable table,
            byte tableClass,
            byte tableId)
        {
            // Get the bits and values from the table
            // We need to reconstruct these from the table - use standard tables

            byte[] bits;
            byte[] values;

            if (tableClass == 0) // DC
            {
                if (tableId == 0)
                {
                    bits = new byte[] { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
                    values = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
                }
                else
                {
                    bits = new byte[] { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
                    values = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
                }
            }
            else // AC
            {
                if (tableId == 0)
                {
                    bits = new byte[] { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125 };
                    values = new byte[]
                    {
                        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
                        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
                        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
                        0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
                        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16,
                        0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
                        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
                        0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
                        0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
                        0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
                        0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
                        0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
                        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
                        0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
                        0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
                        0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
                        0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4,
                        0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
                        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea,
                        0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                        0xf9, 0xfa
                    };
                }
                else
                {
                    bits = new byte[] { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119 };
                    values = new byte[]
                    {
                        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
                        0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
                        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
                        0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
                        0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34,
                        0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
                        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38,
                        0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                        0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
                        0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
                        0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
                        0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96,
                        0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
                        0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
                        0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
                        0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2,
                        0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
                        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
                        0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                        0xf9, 0xfa
                    };
                }
            }

            // Segment length: 2 + 1 + 16 + values.Length
            int segmentLength = 19 + values.Length;

            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.DHT;
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)segmentLength);
            position += 2;

            // Table header: class in high nibble, ID in low nibble
            output[position++] = (byte)((tableClass << 4) | tableId);

            // 16 bytes: count of codes for each length
            for (int i = 0; i < 16; i++)
            {
                output[position++] = bits[i];
            }

            // Symbol values
            for (int i = 0; i < values.Length; i++)
            {
                output[position++] = values[i];
            }

            return position;
        }

        private static int WriteSosMarkerAndScanData(
            Span<byte> output,
            int position,
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            JpegCodecOptions options,
            QuantizationTable[] quantTables)
        {
            int width = info.Columns;
            int height = info.Rows;
            int componentCount = info.SamplesPerPixel;

            // Write SOS header
            int sosLength = 6 + 2 * componentCount;
            output[position++] = JpegMarkers.Prefix;
            output[position++] = JpegMarkers.SOS;
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(position, 2), (ushort)sosLength);
            position += 2;

            // Component count
            output[position++] = (byte)componentCount;

            // Component specifications
            if (componentCount == 1)
            {
                // Grayscale: component 1, DC table 0, AC table 0
                output[position++] = 1;     // Component ID
                output[position++] = 0x00;  // DC table 0, AC table 0
            }
            else
            {
                // Y: DC table 0, AC table 0
                output[position++] = 1;
                output[position++] = 0x00;

                // Cb: DC table 1, AC table 1
                output[position++] = 2;
                output[position++] = 0x11;

                // Cr: DC table 1, AC table 1
                output[position++] = 3;
                output[position++] = 0x11;
            }

            // Spectral selection start/end (baseline: 0-63)
            output[position++] = 0;   // Ss
            output[position++] = 63;  // Se

            // Successive approximation (baseline: 0)
            output[position++] = 0;   // Ah/Al

            // Now write the entropy-coded scan data
            position = EncodeEntropyCoded(output, position, pixelData, info, options, quantTables);

            return position;
        }

        private static int EncodeEntropyCoded(
            Span<byte> output,
            int position,
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            JpegCodecOptions options,
            QuantizationTable[] quantTables)
        {
            int width = info.Columns;
            int height = info.Rows;
            int componentCount = info.SamplesPerPixel;
            int pixelCount = width * height;

            // Create bit writer for entropy coding
            var bitWriter = new BitWriter(output.Slice(position));

            // Convert to component planes and color space
            byte[]? componentBuffer = null;

            try
            {
                int bufferSize = pixelCount * componentCount;
                componentBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                var components = componentBuffer.AsSpan(0, bufferSize);

                if (componentCount == 1)
                {
                    // Grayscale: direct copy
                    pixelData.Slice(0, pixelCount).CopyTo(components);
                }
                else
                {
                    // RGB to YCbCr conversion
                    var yPlane = components.Slice(0, pixelCount);
                    var cbPlane = components.Slice(pixelCount, pixelCount);
                    var crPlane = components.Slice(pixelCount * 2, pixelCount);

                    // Call conversion utility
                    byte[]? tempY = null;
                    byte[]? tempCb = null;
                    byte[]? tempCr = null;

                    try
                    {
                        tempY = ArrayPool<byte>.Shared.Rent(pixelCount);
                        tempCb = ArrayPool<byte>.Shared.Rent(pixelCount);
                        tempCr = ArrayPool<byte>.Shared.Rent(pixelCount);

                        ColorConversion.RgbToYCbCr(pixelData, tempY.AsSpan(0, pixelCount),
                            tempCb.AsSpan(0, pixelCount), tempCr.AsSpan(0, pixelCount));

                        tempY.AsSpan(0, pixelCount).CopyTo(yPlane);
                        tempCb.AsSpan(0, pixelCount).CopyTo(cbPlane);
                        tempCr.AsSpan(0, pixelCount).CopyTo(crPlane);
                    }
                    finally
                    {
                        if (tempY != null) ArrayPool<byte>.Shared.Return(tempY);
                        if (tempCb != null) ArrayPool<byte>.Shared.Return(tempCb);
                        if (tempCr != null) ArrayPool<byte>.Shared.Return(tempCr);
                    }
                }

                // Calculate MCU dimensions
                int mcuWidth = 8;
                int mcuHeight = 8;

                // For subsampling (simplified - no subsampling for now)
                int mcuCountX = (width + mcuWidth - 1) / mcuWidth;
                int mcuCountY = (height + mcuHeight - 1) / mcuHeight;

                // DC predictors (one per component) - use array to avoid stackalloc/ref struct issues
                int[] dcPredictors = new int[4];

                // DCT block buffers - use array to avoid stackalloc/ref struct issues
                float[] dctBlock = new float[64];
                int[] quantizedBlock = new int[64];

                // Encode MCUs
                for (int mcuY = 0; mcuY < mcuCountY; mcuY++)
                {
                    for (int mcuX = 0; mcuX < mcuCountX; mcuX++)
                    {
                        for (int compIdx = 0; compIdx < componentCount; compIdx++)
                        {
                            var componentPlane = components.Slice(compIdx * pixelCount, pixelCount);
                            var quantTable = quantTables[compIdx == 0 ? 0 : 1];
                            var dcTable = compIdx == 0 ? HuffmanTable.LuminanceDC : HuffmanTable.ChrominanceDC;
                            var acTable = compIdx == 0 ? HuffmanTable.LuminanceAC : HuffmanTable.ChrominanceAC;

                            // Extract 8x8 block with level shift
                            int blockX = mcuX * 8;
                            int blockY = mcuY * 8;

                            for (int py = 0; py < 8; py++)
                            {
                                int y = blockY + py;
                                if (y >= height) y = height - 1;

                                for (int px = 0; px < 8; px++)
                                {
                                    int x = blockX + px;
                                    if (x >= width) x = width - 1;

                                    // Level shift (-128)
                                    dctBlock[py * 8 + px] = componentPlane[y * width + x] - 128.0f;
                                }
                            }

                            // Forward DCT
                            DctTransform.Forward(dctBlock);

                            // Quantize (zigzag order)
                            for (int i = 0; i < 64; i++)
                            {
                                int zigzagIndex = QuantizationTable.ZigZagOrder[i];
                                int value = (int)Math.Round(dctBlock[zigzagIndex] / quantTable[i]);
                                quantizedBlock[i] = value;
                            }

                            // Encode DC coefficient (differential)
                            int dcValue = quantizedBlock[0];
                            int dcDiff = dcValue - dcPredictors[compIdx];
                            dcPredictors[compIdx] = dcValue;

                            EncodeDcCoefficient(ref bitWriter, dcDiff, dcTable);

                            // Encode AC coefficients (pass full block, skip DC at index 0)
                            EncodeAcCoefficients(ref bitWriter, quantizedBlock, acTable);
                        }
                    }
                }

                // Flush remaining bits
                bitWriter.Flush();

                return position + bitWriter.BytesWritten;
            }
            finally
            {
                if (componentBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(componentBuffer);
                }
            }
        }

        private static void EncodeDcCoefficient(ref BitWriter writer, int dcDiff, HuffmanTable dcTable)
        {
            int category = BitWriter.GetMagnitudeCategory(dcDiff);
            var (code, size) = dcTable.GetCode((byte)category);

            writer.WriteBits(code, size);

            if (category > 0)
            {
                writer.WriteSignedValue(dcDiff, category);
            }
        }

        private static void EncodeAcCoefficients(ref BitWriter writer, int[] quantizedBlock, HuffmanTable acTable)
        {
            int zeroRun = 0;

            // AC coefficients are indices 1-63 (skip DC at index 0)
            for (int i = 1; i < 64; i++)
            {
                int value = quantizedBlock[i];

                if (value == 0)
                {
                    zeroRun++;
                    if (zeroRun == 16)
                    {
                        // ZRL (16 zeros)
                        var (zrlCode, zrlSize) = acTable.GetCode(0xF0);
                        writer.WriteBits(zrlCode, zrlSize);
                        zeroRun = 0;
                    }
                }
                else
                {
                    // Encode any remaining zeros
                    while (zeroRun >= 16)
                    {
                        var (zrlCode, zrlSize) = acTable.GetCode(0xF0);
                        writer.WriteBits(zrlCode, zrlSize);
                        zeroRun -= 16;
                    }

                    // Encode the coefficient
                    int category = BitWriter.GetMagnitudeCategory(value);
                    byte symbol = (byte)((zeroRun << 4) | category);

                    var (code, size) = acTable.GetCode(symbol);
                    writer.WriteBits(code, size);
                    writer.WriteSignedValue(value, category);

                    zeroRun = 0;
                }
            }

            // EOB if we didn't end on a non-zero coefficient
            if (zeroRun > 0)
            {
                var (eobCode, eobSize) = acTable.GetCode(0x00);
                writer.WriteBits(eobCode, eobSize);
            }
        }

        /// <summary>
        /// Gets the maximum encoded size for a frame.
        /// </summary>
        /// <param name="info">Pixel data metadata.</param>
        /// <returns>The maximum possible encoded size including headers.</returns>
        public static int GetMaxEncodedSize(PixelDataInfo info)
        {
            // Worst case: no compression + headers + byte stuffing
            int pixelSize = info.FrameSize;
            int headerOverhead = 1024; // Conservative estimate for all markers
            int byteStuffing = pixelSize / 255; // Worst case stuffing
            return pixelSize + headerOverhead + byteStuffing;
        }
    }
}
