using System;
using System.Buffers;
using System.Buffers.Binary;

namespace SharpDicom.Codecs.Jpeg
{
    /// <summary>
    /// Decodes JPEG Baseline (Process 1) compressed pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This decoder handles the JPEG Baseline DCT process (SOF0) which is the most
    /// common JPEG variant used in DICOM. It supports 8-bit grayscale and RGB images.
    /// </para>
    /// <para>
    /// The decoding process involves:
    /// 1. Parsing JPEG markers (SOI, DQT, DHT, SOF0, SOS, EOI)
    /// 2. Huffman decoding of entropy-coded scan data
    /// 3. Dequantization of DCT coefficients
    /// 4. Inverse DCT to recover pixel values
    /// 5. Color conversion (YCbCr to RGB for color images)
    /// </para>
    /// </remarks>
    public static class JpegBaselineDecoder
    {
        /// <summary>
        /// Decodes a single JPEG frame to raw pixel data.
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

                    case JpegMarkers.SOF0:
                        if (!JpegFrameInfo.TryParse(segmentPayload, markerCode, out context.FrameInfo))
                        {
                            return DecodeResult.Fail(frameIndex, position, "Failed to parse SOF0");
                        }

                        // Validate against PixelDataInfo
                        if (context.FrameInfo.Width != info.Columns || context.FrameInfo.Height != info.Rows)
                        {
                            return DecodeResult.Fail(frameIndex, position, "Dimension mismatch",
                                $"{info.Columns}x{info.Rows}",
                                $"{context.FrameInfo.Width}x{context.FrameInfo.Height}");
                        }

                        if (context.FrameInfo.Precision != 8)
                        {
                            return DecodeResult.Fail(frameIndex, position,
                                "Only 8-bit precision supported for JPEG Baseline",
                                "8", context.FrameInfo.Precision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        break;

                    case JpegMarkers.SOF1:
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
                            $"Unsupported SOF type: 0x{markerCode:X2}. Only SOF0 (Baseline) supported.");

                    case JpegMarkers.DRI:
                        if (segmentPayload.Length >= 2)
                        {
                            context.RestartInterval = BinaryPrimitives.ReadUInt16BigEndian(segmentPayload);
                        }
                        break;

                    case JpegMarkers.SOS:
                        // Validate SOF was parsed before SOS
                        if (context.FrameInfo.Width == 0 || context.FrameInfo.Height == 0)
                        {
                            return DecodeResult.Fail(frameIndex, position,
                                "SOS marker found before SOF (frame header missing)");
                        }

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

                        // Successfully decoded - find EOI and return
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

            // Spectral selection and successive approximation (baseline ignores these)
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
            byte[]? componentBuffer = null;

            try
            {
                // Rent buffers for each component
                int bufferSize = pixelCount * componentCount;
                componentBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                var components = componentBuffer.AsSpan(0, bufferSize);

                // Initialize DC predictors (one per component)
                Span<int> dcPredictors = stackalloc int[4];
                dcPredictors.Clear();

                // Create bit reader
                var bitReader = new HuffmanBitReader(scanData);

                // DCT block buffer (8x8 = 64 coefficients)
                Span<float> dctBlock = stackalloc float[64];
                Span<int> quantizedBlock = stackalloc int[64];

                int mcuIndex = 0;
                int restartCounter = 0;

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
                            // Bit reader handles RST markers automatically
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
                                        dctBlock[zigzagIndex] = (float)quantizedBlock[i] * quantTable[i];
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

                                            // Level shift (+128) and clamp
                                            int pixelValue = (int)(dctBlock[py * 8 + px] + 128.5f);
                                            pixelValue = Math.Max(0, Math.Min(255, pixelValue));

                                            int outputIndex = compIdx * pixelCount + y * width + x;
                                            components[outputIndex] = (byte)pixelValue;
                                        }
                                    }
                                }
                            }
                        }

                        restartCounter++;
                        mcuIndex++;
                    }
                }

                // Convert from planar component storage to interleaved output
                if (componentCount == 1)
                {
                    // Grayscale: direct copy
                    components.Slice(0, pixelCount).CopyTo(output);
                }
                else if (componentCount == 3)
                {
                    // YCbCr to RGB conversion
                    var yPlane = components.Slice(0, pixelCount);
                    var cbPlane = components.Slice(pixelCount, pixelCount);
                    var crPlane = components.Slice(pixelCount * 2, pixelCount);

                    // Check if upsampling is needed
                    if (maxH > 1 || maxV > 1)
                    {
                        // Need to upsample chroma components
                        byte[]? upsampledCb = null;
                        byte[]? upsampledCr = null;
                        try
                        {
                            upsampledCb = ArrayPool<byte>.Shared.Rent(pixelCount);
                            upsampledCr = ArrayPool<byte>.Shared.Rent(pixelCount);

                            UpsampleComponent(cbPlane, upsampledCb.AsSpan(0, pixelCount), width, height, maxH, maxV,
                                context.FrameInfo.Components[1].HorizontalSampling,
                                context.FrameInfo.Components[1].VerticalSampling);
                            UpsampleComponent(crPlane, upsampledCr.AsSpan(0, pixelCount), width, height, maxH, maxV,
                                context.FrameInfo.Components[2].HorizontalSampling,
                                context.FrameInfo.Components[2].VerticalSampling);

                            ColorConversion.YCbCrToRgb(
                                yPlane,
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
                        // No subsampling - direct conversion
                        ColorConversion.YCbCrToRgb(yPlane, cbPlane, crPlane, output);
                    }
                }
                else
                {
                    return $"Unsupported component count: {componentCount}";
                }

                return null; // Success
            }
            finally
            {
                if (componentBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(componentBuffer);
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

            // The input component was stored with full image width as stride,
            // even though only a portion of each row contains valid data.
            // This is because the component buffer uses row-major storage.
            int inputStride = width;
            int maxSrcX = (width + scaleX - 1) / scaleX - 1;
            int maxSrcY = (height + scaleY - 1) / scaleY - 1;

            // Simple nearest-neighbor upsampling
            for (int y = 0; y < height; y++)
            {
                int srcY = Math.Min(y / scaleY, maxSrcY);

                for (int x = 0; x < width; x++)
                {
                    int srcX = Math.Min(x / scaleX, maxSrcX);
                    output[y * width + x] = input[srcY * inputStride + srcX];
                }
            }
        }

        /// <summary>
        /// Internal context for JPEG decoding.
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
