using System;
using System.Buffers;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Decodes JPEG Extended (Process 2,4) compressed pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This decoder handles the JPEG Extended Sequential DCT process (SOF1) which supports
    /// both 8-bit and 12-bit sample precision. It is used for DICOM Transfer Syntax
    /// 1.2.840.10008.1.2.4.51 (JPEG Extended, Process 2 and 4).
    /// </para>
    /// <para>
    /// Key differences from baseline (SOF0):
    /// <list type="bullet">
    /// <item>Parses SOF1 marker (0xFFC1) instead of SOF0 (0xFFC0)</item>
    /// <item>Sample precision can be 8 or 12 bits</item>
    /// <item>Huffman categories extend to 12 for 12-bit precision</item>
    /// <item>Quantization tables may use 16-bit entries</item>
    /// <item>12-bit output is written as ushort (little-endian) to the destination buffer</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class JpegExtendedDecoder
    {
        /// <summary>
        /// Decodes a single JPEG Extended frame to raw pixel data.
        /// </summary>
        /// <param name="compressedFrame">The JPEG-compressed frame data.</param>
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
            // 1. Validate minimum length (SOI + minimal content + EOI)
            if (compressedFrame.Length < 4)
            {
                return DecodeResult.Fail(frameIndex, 0, "Compressed frame too short");
            }

            // 2. Check for SOI marker (0xFFD8)
            if (compressedFrame[0] != JpegMarkers.Prefix || compressedFrame[1] != JpegMarkers.SOI)
            {
                return DecodeResult.Fail(frameIndex, 0, "Missing SOI marker", "0xFFD8",
                    $"0x{compressedFrame[0]:X2}{compressedFrame[1]:X2}");
            }

            // 3. Parse markers and collect tables
            var context = new DecodeContext();
            int position = 2; // After SOI

            while (position < compressedFrame.Length - 1)
            {
                // Find next marker
                if (compressedFrame[position] != JpegMarkers.Prefix)
                {
                    return DecodeResult.Fail(frameIndex, position, "Expected marker prefix 0xFF");
                }

                byte markerCode = compressedFrame[position + 1];
                position += 2;

                // Skip padding 0xFF bytes
                while (markerCode == JpegMarkers.Prefix && position < compressedFrame.Length)
                {
                    markerCode = compressedFrame[position++];
                }

                // End of Image
                if (markerCode == JpegMarkers.EOI)
                {
                    break;
                }

                // Markers without payload
                if (markerCode == JpegMarkers.SOI || JpegMarkers.IsRST(markerCode))
                {
                    continue;
                }

                // Read segment length (big-endian, includes length bytes)
                if (position + 2 > compressedFrame.Length)
                {
                    return DecodeResult.Fail(frameIndex, position, "Truncated marker segment");
                }

                ushort segmentLength = BinaryPrimitives.ReadUInt16BigEndian(compressedFrame.Slice(position, 2));
                if (segmentLength < 2 || position + segmentLength > compressedFrame.Length)
                {
                    return DecodeResult.Fail(frameIndex, position, "Invalid segment length");
                }

                var segmentPayload = compressedFrame.Slice(position + 2, segmentLength - 2);

                // Process marker
                switch (markerCode)
                {
                    case JpegMarkers.DQT:
                        if (!ParseDqtSegment(segmentPayload, context, out var dqtError))
                        {
                            return DecodeResult.Fail(frameIndex, position, dqtError ?? "Failed to parse DQT");
                        }
                        break;

                    case JpegMarkers.DHT:
                        if (!ParseDhtSegment(segmentPayload, context, out var dhtError))
                        {
                            return DecodeResult.Fail(frameIndex, position, dhtError ?? "Failed to parse DHT");
                        }
                        break;

                    case JpegMarkers.SOF1: // Extended sequential DCT
                        if (!JpegFrameInfo.TryParse(segmentPayload, markerCode, out context.FrameInfo))
                        {
                            return DecodeResult.Fail(frameIndex, position, "Failed to parse SOF1");
                        }

                        // Validate against PixelDataInfo
                        if (context.FrameInfo.Width != info.Columns || context.FrameInfo.Height != info.Rows)
                        {
                            return DecodeResult.Fail(frameIndex, position, "Dimension mismatch",
                                $"{info.Columns}x{info.Rows}",
                                $"{context.FrameInfo.Width}x{context.FrameInfo.Height}");
                        }

                        // SOF1 supports 8-bit and 12-bit precision
                        if (context.FrameInfo.Precision != 8 && context.FrameInfo.Precision != 12)
                        {
                            return DecodeResult.Fail(frameIndex, position,
                                "JPEG Extended supports 8-bit or 12-bit precision",
                                "8 or 12", context.FrameInfo.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        break;

                    case JpegMarkers.SOF0:
                        // Also accept SOF0 for compatibility (8-bit data in extended codec)
                        if (!JpegFrameInfo.TryParse(segmentPayload, markerCode, out context.FrameInfo))
                        {
                            return DecodeResult.Fail(frameIndex, position, "Failed to parse SOF0");
                        }

                        if (context.FrameInfo.Width != info.Columns || context.FrameInfo.Height != info.Rows)
                        {
                            return DecodeResult.Fail(frameIndex, position, "Dimension mismatch",
                                $"{info.Columns}x{info.Rows}",
                                $"{context.FrameInfo.Width}x{context.FrameInfo.Height}");
                        }

                        if (context.FrameInfo.Precision != 8)
                        {
                            return DecodeResult.Fail(frameIndex, position,
                                "Only 8-bit precision supported for SOF0",
                                "8", context.FrameInfo.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        break;

                    case JpegMarkers.SOF2:
                    case JpegMarkers.SOF3:
                    case JpegMarkers.SOF5:
                    case JpegMarkers.SOF6:
                    case JpegMarkers.SOF7:
                    case JpegMarkers.SOF9:
                    case JpegMarkers.SOF10:
                    case JpegMarkers.SOF11:
                    case JpegMarkers.SOF13:
                    case JpegMarkers.SOF14:
                    case JpegMarkers.SOF15:
                        return DecodeResult.Fail(frameIndex, position,
                            $"Unsupported SOF type: 0x{markerCode:X2}. Only SOF0 and SOF1 (Extended) supported.");

                    case JpegMarkers.DRI:
                        if (segmentPayload.Length >= 2)
                        {
                            context.RestartInterval = BinaryPrimitives.ReadUInt16BigEndian(segmentPayload);
                        }
                        break;

                    case JpegMarkers.SOS:
                        // Parse SOS header
                        if (!ParseSosHeader(segmentPayload, context, out var sosError))
                        {
                            return DecodeResult.Fail(frameIndex, position, sosError ?? "Failed to parse SOS");
                        }

                        // Use default tables if none provided
                        EnsureDefaultTables(context);

                        // Scan data follows immediately after SOS segment
                        int scanDataStart = position + segmentLength;
                        var scanData = compressedFrame.Slice(scanDataStart);

                        // Decode the entropy-coded scan data
                        var decodeError = DecodeScanData(scanData, context, info, output);
                        if (decodeError != null)
                        {
                            return DecodeResult.Fail(frameIndex, scanDataStart, decodeError);
                        }

                        // Successfully decoded - return
                        return DecodeResult.Ok(info.FrameSize);

                    default:
                        // Skip unknown markers (APP segments, COM, etc.)
                        break;
                }

                position += segmentLength;
            }

            return DecodeResult.Fail(frameIndex, position, "No SOS marker found");
        }

        private static bool ParseDqtSegment(ReadOnlySpan<byte> data, DecodeContext context, out string? error)
        {
            error = null;
            int offset = 0;

            while (offset < data.Length)
            {
                if (!QuantizationTable.TryParseDQT(data.Slice(offset), out var table, out int consumed))
                {
                    error = "Invalid DQT data";
                    return false;
                }

                if (table!.TableId < 4)
                {
                    context.QuantTables[table.TableId] = table;
                }

                offset += consumed;
            }

            return true;
        }

        private static bool ParseDhtSegment(ReadOnlySpan<byte> data, DecodeContext context, out string? error)
        {
            error = null;
            int offset = 0;

            while (offset < data.Length)
            {
                if (!HuffmanTable.TryParseDHT(data.Slice(offset), out byte tableClass, out byte tableId,
                    out var table, out int consumed))
                {
                    error = "Invalid DHT data";
                    return false;
                }

                if (tableId < 4 && table != null)
                {
                    if (tableClass == 0)
                    {
                        context.DcTables[tableId] = table;
                    }
                    else
                    {
                        context.AcTables[tableId] = table;
                    }
                }

                offset += consumed;
            }

            return true;
        }

        private static bool ParseSosHeader(ReadOnlySpan<byte> data, DecodeContext context, out string? error)
        {
            error = null;

            if (data.Length < 1)
            {
                error = "SOS segment too short";
                return false;
            }

            int componentCount = data[0];
            if (data.Length < 1 + componentCount * 2 + 3)
            {
                error = "SOS segment truncated";
                return false;
            }

            context.ScanComponents = new ScanComponentInfo[componentCount];

            for (int i = 0; i < componentCount; i++)
            {
                byte componentSelector = data[1 + i * 2];
                byte tableSelectors = data[1 + i * 2 + 1];

                context.ScanComponents[i] = new ScanComponentInfo
                {
                    ComponentId = componentSelector,
                    DcTableId = (byte)(tableSelectors >> 4),
                    AcTableId = (byte)(tableSelectors & 0x0F)
                };
            }

            // Spectral selection and successive approximation
            int ssIndex = 1 + componentCount * 2;
            context.SpectralStart = data[ssIndex];
            context.SpectralEnd = data[ssIndex + 1];
            context.SuccessiveApprox = data[ssIndex + 2];

            return true;
        }

        private static void EnsureDefaultTables(DecodeContext context)
        {
            // Use standard Huffman tables if none provided
            context.DcTables[0] ??= HuffmanTable.LuminanceDC;
            context.DcTables[1] ??= HuffmanTable.ChrominanceDC;
            context.AcTables[0] ??= HuffmanTable.LuminanceAC;
            context.AcTables[1] ??= HuffmanTable.ChrominanceAC;

            // Use default quantization tables if none provided
            context.QuantTables[0] ??= QuantizationTable.LuminanceDefault;
            context.QuantTables[1] ??= QuantizationTable.ChrominanceDefault;
        }

        private static string? DecodeScanData(
            ReadOnlySpan<byte> scanData,
            DecodeContext context,
            PixelDataInfo info,
            Span<byte> output)
        {
            int width = context.FrameInfo.Width;
            int height = context.FrameInfo.Height;
            int componentCount = context.FrameInfo.ComponentCount;
            int precision = context.FrameInfo.Precision;
            bool is12Bit = precision == 12;

            // Calculate MCU dimensions
            int mcuWidth = 8;
            int mcuHeight = 8;

            // Check for subsampling
            int maxH = 1, maxV = 1;
            foreach (var comp in context.FrameInfo.Components)
            {
                maxH = Math.Max(maxH, comp.HorizontalSampling);
                maxV = Math.Max(maxV, comp.VerticalSampling);
            }

            mcuWidth *= maxH;
            mcuHeight *= maxV;

            int mcuCountX = (width + mcuWidth - 1) / mcuWidth;
            int mcuCountY = (height + mcuHeight - 1) / mcuHeight;

            // Allocate temporary buffers for decoded components
            int pixelCount = width * height;
            int[]? componentBuffer = null;

            try
            {
                // Rent buffers for each component (use int[] for 12-bit precision)
                int bufferSize = pixelCount * componentCount;
                componentBuffer = ArrayPool<int>.Shared.Rent(bufferSize);
                var components = componentBuffer.AsSpan(0, bufferSize);
                components.Clear();

                // Initialize DC predictors (one per component)
                Span<int> dcPredictors = stackalloc int[4];
                dcPredictors.Clear();

                // Create bit reader
                var bitReader = new HuffmanBitReader(scanData);

                // DCT block buffer (8x8 = 64 coefficients)
                Span<float> dctBlock = stackalloc float[64];
                Span<int> quantizedBlock = stackalloc int[64];

                int restartCounter = 0;

                // Level shift value: 2^(precision-1)
                int levelShift = 1 << (precision - 1);
                int maxSample = (1 << precision) - 1;

                // Decode each MCU
                for (int mcuY = 0; mcuY < mcuCountY; mcuY++)
                {
                    for (int mcuX = 0; mcuX < mcuCountX; mcuX++)
                    {
                        // Handle restart interval
                        if (context.RestartInterval > 0 && restartCounter > 0 &&
                            restartCounter % context.RestartInterval == 0)
                        {
                            dcPredictors.Clear();
                        }

                        // Decode each component's blocks within this MCU
                        for (int compIdx = 0; compIdx < componentCount; compIdx++)
                        {
                            var compInfo = context.FrameInfo.Components[compIdx];
                            var scanComp = FindScanComponent(context.ScanComponents, compInfo.ComponentId);
                            if (scanComp == null)
                            {
                                return $"Component {compInfo.ComponentId} not in scan";
                            }

                            var dcTable = context.DcTables[scanComp.DcTableId];
                            var acTable = context.AcTables[scanComp.AcTableId];
                            var quantTable = context.QuantTables[compInfo.QuantizationTableId];

                            if (dcTable == null || acTable == null || quantTable == null)
                            {
                                return "Missing Huffman or quantization table";
                            }

                            int blocksH = compInfo.HorizontalSampling;
                            int blocksV = compInfo.VerticalSampling;

                            // Decode all blocks for this component in this MCU
                            for (int blockY = 0; blockY < blocksV; blockY++)
                            {
                                for (int blockX = 0; blockX < blocksH; blockX++)
                                {
                                    // Clear the block
                                    quantizedBlock.Clear();

                                    // Decode DC coefficient
                                    int dcCategory = dcTable.DecodeSymbol(ref bitReader);
                                    if (dcCategory < 0)
                                    {
                                        return "Failed to decode DC coefficient";
                                    }

                                    int dcDiff = 0;
                                    if (dcCategory > 0)
                                    {
                                        if (!bitReader.TryReadCoefficient(dcCategory, out dcDiff))
                                        {
                                            return "Failed to read DC difference";
                                        }
                                    }

                                    dcPredictors[compIdx] += dcDiff;
                                    quantizedBlock[0] = dcPredictors[compIdx];

                                    // Decode AC coefficients
                                    int acIndex = 1;
                                    while (acIndex < 64)
                                    {
                                        int acSymbol = acTable.DecodeSymbol(ref bitReader);
                                        if (acSymbol < 0)
                                        {
                                            return "Failed to decode AC coefficient";
                                        }

                                        if (acSymbol == 0x00) // EOB
                                        {
                                            break;
                                        }

                                        int runLength = acSymbol >> 4;
                                        int acCategory = acSymbol & 0x0F;

                                        if (acSymbol == 0xF0) // ZRL - 16 zeros
                                        {
                                            acIndex += 16;
                                            continue;
                                        }

                                        acIndex += runLength;

                                        if (acIndex >= 64)
                                        {
                                            return "AC coefficient index out of range";
                                        }

                                        if (acCategory > 0)
                                        {
                                            if (!bitReader.TryReadCoefficient(acCategory, out int acValue))
                                            {
                                                return "Failed to read AC coefficient";
                                            }
                                            quantizedBlock[acIndex] = acValue;
                                        }

                                        acIndex++;
                                    }

                                    // Dequantize and reorder from zigzag
                                    for (int i = 0; i < 64; i++)
                                    {
                                        int zigzagIndex = QuantizationTable.ZigZagOrder[i];
                                        dctBlock[zigzagIndex] = quantizedBlock[i] * quantTable[i];
                                    }

                                    // Inverse DCT
                                    DctTransform.Inverse(dctBlock);

                                    // Level shift and store to component buffer
                                    int blockPixelX = mcuX * mcuWidth / maxH * compInfo.HorizontalSampling + blockX * 8;
                                    int blockPixelY = mcuY * mcuHeight / maxV * compInfo.VerticalSampling + blockY * 8;

                                    for (int py = 0; py < 8; py++)
                                    {
                                        int y = blockPixelY + py;
                                        if (y >= height) break;

                                        for (int px = 0; px < 8; px++)
                                        {
                                            int x = blockPixelX + px;
                                            if (x >= width) break;

                                            // Level shift (+levelShift) and clamp to [0, maxSample]
                                            int pixelValue = (int)(dctBlock[py * 8 + px] + levelShift + 0.5f);
                                            pixelValue = Math.Max(0, Math.Min(maxSample, pixelValue));

                                            int outputIndex = compIdx * pixelCount + y * width + x;
                                            components[outputIndex] = pixelValue;
                                        }
                                    }
                                }
                            }
                        }

                        restartCounter++;
                    }
                }

                // Convert from planar component storage to interleaved output
                if (is12Bit)
                {
                    WriteOutput12Bit(components, output, pixelCount, componentCount, width, height, maxH, maxV, context.FrameInfo);
                }
                else
                {
                    WriteOutput8Bit(components, output, pixelCount, componentCount, width, height, maxH, maxV, context.FrameInfo);
                }

                return null; // Success
            }
            finally
            {
                if (componentBuffer != null)
                {
                    ArrayPool<int>.Shared.Return(componentBuffer);
                }
            }
        }

        private static void WriteOutput8Bit(
            ReadOnlySpan<int> components,
            Span<byte> output,
            int pixelCount,
            int componentCount,
            int width,
            int height,
            int maxH,
            int maxV,
            JpegFrameInfo frameInfo)
        {
            if (componentCount == 1)
            {
                // Grayscale: direct copy
                for (int i = 0; i < pixelCount; i++)
                {
                    output[i] = (byte)components[i];
                }
            }
            else if (componentCount == 3)
            {
                // Convert int[] component planes to byte[] for color conversion
                byte[]? yBuf = null;
                byte[]? cbBuf = null;
                byte[]? crBuf = null;
                try
                {
                    yBuf = ArrayPool<byte>.Shared.Rent(pixelCount);
                    cbBuf = ArrayPool<byte>.Shared.Rent(pixelCount);
                    crBuf = ArrayPool<byte>.Shared.Rent(pixelCount);

                    for (int i = 0; i < pixelCount; i++)
                    {
                        yBuf[i] = (byte)components[i];
                        cbBuf[i] = (byte)components[pixelCount + i];
                        crBuf[i] = (byte)components[pixelCount * 2 + i];
                    }

                    if (maxH > 1 || maxV > 1)
                    {
                        // Need to upsample chroma components
                        byte[]? upsampledCb = null;
                        byte[]? upsampledCr = null;
                        try
                        {
                            upsampledCb = ArrayPool<byte>.Shared.Rent(pixelCount);
                            upsampledCr = ArrayPool<byte>.Shared.Rent(pixelCount);

                            UpsampleComponent(cbBuf.AsSpan(0, pixelCount), upsampledCb.AsSpan(0, pixelCount),
                                width, height, maxH, maxV,
                                frameInfo.Components[1].HorizontalSampling,
                                frameInfo.Components[1].VerticalSampling);
                            UpsampleComponent(crBuf.AsSpan(0, pixelCount), upsampledCr.AsSpan(0, pixelCount),
                                width, height, maxH, maxV,
                                frameInfo.Components[2].HorizontalSampling,
                                frameInfo.Components[2].VerticalSampling);

                            ColorConversion.YCbCrToRgb(
                                yBuf.AsSpan(0, pixelCount),
                                upsampledCb.AsSpan(0, pixelCount),
                                upsampledCr.AsSpan(0, pixelCount),
                                output);
                        }
                        finally
                        {
                            if (upsampledCb != null) ArrayPool<byte>.Shared.Return(upsampledCb);
                            if (upsampledCr != null) ArrayPool<byte>.Shared.Return(upsampledCr);
                        }
                    }
                    else
                    {
                        ColorConversion.YCbCrToRgb(
                            yBuf.AsSpan(0, pixelCount),
                            cbBuf.AsSpan(0, pixelCount),
                            crBuf.AsSpan(0, pixelCount),
                            output);
                    }
                }
                finally
                {
                    if (yBuf != null) ArrayPool<byte>.Shared.Return(yBuf);
                    if (cbBuf != null) ArrayPool<byte>.Shared.Return(cbBuf);
                    if (crBuf != null) ArrayPool<byte>.Shared.Return(crBuf);
                }
            }
        }

        private static void WriteOutput12Bit(
            ReadOnlySpan<int> components,
            Span<byte> output,
            int pixelCount,
            int componentCount,
            int width,
            int height,
            int maxH,
            int maxV,
            JpegFrameInfo frameInfo)
        {
            if (componentCount == 1)
            {
                // Grayscale 12-bit: write each sample as little-endian ushort
                for (int i = 0; i < pixelCount; i++)
                {
                    ushort value = (ushort)components[i];
                    BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(i * 2), value);
                }
            }
            else if (componentCount == 3)
            {
                // 12-bit color: write interleaved as ushort values
                // For 12-bit color images, YCbCr to RGB conversion uses ushort
                ushort[]? yBuf = null;
                ushort[]? cbBuf = null;
                ushort[]? crBuf = null;
                ushort[]? rgbBuf = null;
                try
                {
                    yBuf = ArrayPool<ushort>.Shared.Rent(pixelCount);
                    cbBuf = ArrayPool<ushort>.Shared.Rent(pixelCount);
                    crBuf = ArrayPool<ushort>.Shared.Rent(pixelCount);
                    rgbBuf = ArrayPool<ushort>.Shared.Rent(pixelCount * 3);

                    for (int i = 0; i < pixelCount; i++)
                    {
                        yBuf[i] = (ushort)components[i];
                        cbBuf[i] = (ushort)components[pixelCount + i];
                        crBuf[i] = (ushort)components[pixelCount * 2 + i];
                    }

                    if (maxH > 1 || maxV > 1)
                    {
                        // Need to upsample chroma components
                        ushort[]? upsampledCb = null;
                        ushort[]? upsampledCr = null;
                        try
                        {
                            upsampledCb = ArrayPool<ushort>.Shared.Rent(pixelCount);
                            upsampledCr = ArrayPool<ushort>.Shared.Rent(pixelCount);

                            UpsampleComponent16(cbBuf.AsSpan(0, pixelCount), upsampledCb.AsSpan(0, pixelCount),
                                width, height, maxH, maxV,
                                frameInfo.Components[1].HorizontalSampling,
                                frameInfo.Components[1].VerticalSampling);
                            UpsampleComponent16(crBuf.AsSpan(0, pixelCount), upsampledCr.AsSpan(0, pixelCount),
                                width, height, maxH, maxV,
                                frameInfo.Components[2].HorizontalSampling,
                                frameInfo.Components[2].VerticalSampling);

                            ColorConversion.YCbCrToRgb(
                                yBuf.AsSpan(0, pixelCount),
                                upsampledCb.AsSpan(0, pixelCount),
                                upsampledCr.AsSpan(0, pixelCount),
                                rgbBuf.AsSpan(0, pixelCount * 3),
                                4095); // 12-bit max
                        }
                        finally
                        {
                            if (upsampledCb != null) ArrayPool<ushort>.Shared.Return(upsampledCb);
                            if (upsampledCr != null) ArrayPool<ushort>.Shared.Return(upsampledCr);
                        }
                    }
                    else
                    {
                        ColorConversion.YCbCrToRgb(
                            yBuf.AsSpan(0, pixelCount),
                            cbBuf.AsSpan(0, pixelCount),
                            crBuf.AsSpan(0, pixelCount),
                            rgbBuf.AsSpan(0, pixelCount * 3),
                            4095); // 12-bit max
                    }

                    // Write RGB values as little-endian ushorts
                    for (int i = 0; i < pixelCount * 3; i++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(i * 2), rgbBuf[i]);
                    }
                }
                finally
                {
                    if (yBuf != null) ArrayPool<ushort>.Shared.Return(yBuf);
                    if (cbBuf != null) ArrayPool<ushort>.Shared.Return(cbBuf);
                    if (crBuf != null) ArrayPool<ushort>.Shared.Return(crBuf);
                    if (rgbBuf != null) ArrayPool<ushort>.Shared.Return(rgbBuf);
                }
            }
        }

        private static ScanComponentInfo? FindScanComponent(ScanComponentInfo[]? components, byte componentId)
        {
            if (components == null) return null;

            foreach (var comp in components)
            {
                if (comp.ComponentId == componentId)
                {
                    return comp;
                }
            }

            return null;
        }

        private static void UpsampleComponent(
            ReadOnlySpan<byte> input,
            Span<byte> output,
            int width,
            int height,
            int maxH,
            int maxV,
            int compH,
            int compV)
        {
            int scaleX = maxH / compH;
            int scaleY = maxV / compV;

            int inputWidth = (width + scaleX - 1) / scaleX;
            int inputHeight = (height + scaleY - 1) / scaleY;

            for (int y = 0; y < height; y++)
            {
                int srcY = Math.Min(y / scaleY, inputHeight - 1);

                for (int x = 0; x < width; x++)
                {
                    int srcX = Math.Min(x / scaleX, inputWidth - 1);
                    output[y * width + x] = input[srcY * width + srcX];
                }
            }
        }

        private static void UpsampleComponent16(
            ReadOnlySpan<ushort> input,
            Span<ushort> output,
            int width,
            int height,
            int maxH,
            int maxV,
            int compH,
            int compV)
        {
            int scaleX = maxH / compH;
            int scaleY = maxV / compV;

            int inputHeight = (height + scaleY - 1) / scaleY;
            int inputWidth = (width + scaleX - 1) / scaleX;

            for (int y = 0; y < height; y++)
            {
                int srcY = Math.Min(y / scaleY, inputHeight - 1);

                for (int x = 0; x < width; x++)
                {
                    int srcX = Math.Min(x / scaleX, inputWidth - 1);
                    output[y * width + x] = input[srcY * width + srcX];
                }
            }
        }

        /// <summary>
        /// Internal context for JPEG Extended decoding.
        /// </summary>
        private sealed class DecodeContext
        {
            public JpegFrameInfo FrameInfo;
            public QuantizationTable?[] QuantTables { get; } = new QuantizationTable?[4];
            public HuffmanTable?[] DcTables { get; } = new HuffmanTable?[4];
            public HuffmanTable?[] AcTables { get; } = new HuffmanTable?[4];
            public ScanComponentInfo[]? ScanComponents { get; set; }
            public int RestartInterval { get; set; }
            public int SpectralStart { get; set; }
            public int SpectralEnd { get; set; } = 63;
            public int SuccessiveApprox { get; set; }
        }

        /// <summary>
        /// Information about a component within a scan.
        /// </summary>
        private sealed class ScanComponentInfo
        {
            public byte ComponentId { get; init; }
            public byte DcTableId { get; init; }
            public byte AcTableId { get; init; }
        }
    }
}
