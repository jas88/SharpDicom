using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharpDicom.Codecs.Jpeg2000.Subband;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// Decodes JPEG 2000 codestreams to raw pixel data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This decoder handles JPEG 2000 Part 1 codestreams (ITU-T T.800) as used
    /// in DICOM files with transfer syntaxes:
    /// - JPEG 2000 Lossless (1.2.840.10008.1.2.4.90)
    /// - JPEG 2000 (1.2.840.10008.1.2.4.91)
    /// </para>
    /// <para>
    /// The decoder supports both lossless (5/3 wavelet) and lossy (9/7 wavelet) modes.
    /// Multi-tile codestreams are decoded in parallel when MaxDegreeOfParallelism > 1.
    /// </para>
    /// </remarks>
    public static class J2kDecoder
    {
        /// <summary>
        /// Decodes a JPEG 2000 codestream to raw pixel data.
        /// </summary>
        /// <param name="codestream">The JPEG 2000 codestream data.</param>
        /// <param name="info">Expected pixel data information.</param>
        /// <param name="output">Destination buffer for decoded pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <returns>Decode result indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> codestream,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex)
        {
            return DecodeFrame(codestream, info, output, frameIndex, null, 1);
        }

        /// <summary>
        /// Decodes a JPEG 2000 codestream to raw pixel data using a specific block coder.
        /// </summary>
        /// <param name="codestream">The JPEG 2000 codestream data.</param>
        /// <param name="info">Expected pixel data information.</param>
        /// <param name="output">Destination buffer for decoded pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <param name="blockCoder">
        /// Block coder to use for decoding. If null, auto-detects from the CAP marker:
        /// uses HtBlockEncoder for HTJ2K codestreams, EbcotBlockCoder for standard J2K.
        /// </param>
        /// <returns>Decode result indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> codestream,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex,
            IBlockCoder? blockCoder)
        {
            return DecodeFrame(codestream, info, output, frameIndex, blockCoder, 1);
        }

        /// <summary>
        /// Decodes a JPEG 2000 codestream to raw pixel data with parallel tile decoding.
        /// </summary>
        /// <param name="codestream">The JPEG 2000 codestream data.</param>
        /// <param name="info">Expected pixel data information.</param>
        /// <param name="output">Destination buffer for decoded pixel data.</param>
        /// <param name="frameIndex">Frame index for error reporting.</param>
        /// <param name="blockCoder">
        /// Block coder to use for decoding. If null, auto-detects from the CAP marker.
        /// </param>
        /// <param name="maxDegreeOfParallelism">
        /// Maximum number of tiles to decode in parallel. Use 1 for sequential decoding.
        /// </param>
        /// <returns>Decode result indicating success or failure.</returns>
        public static DecodeResult DecodeFrame(
            ReadOnlySpan<byte> codestream,
            PixelDataInfo info,
            Span<byte> output,
            int frameIndex,
            IBlockCoder? blockCoder,
            int maxDegreeOfParallelism)
        {
            // 1. Parse codestream header
            if (!J2kCodestream.TryParse(codestream, out var header, out var error))
            {
                return DecodeResult.Fail(frameIndex, 0, error ?? "Invalid J2K header");
            }

            // 2. Validate against PixelDataInfo
            if (header!.ImageWidth != info.Columns || header.ImageHeight != info.Rows)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Dimension mismatch: J2K {header.ImageWidth}x{header.ImageHeight} vs expected {info.Columns}x{info.Rows}");
            }

            if (header.ComponentCount != info.SamplesPerPixel)
            {
                return DecodeResult.Fail(frameIndex, 0,
                    $"Component count mismatch: J2K {header.ComponentCount} vs expected {info.SamplesPerPixel}");
            }

            // 3. Auto-detect block coder from CAP marker if not specified
            IBlockCoder effectiveCoder = blockCoder ?? (header.IsHtj2k
                ? Tier1.HtBlockEncoder.Instance
                : (IBlockCoder)EbcotBlockCoder.Instance);

            // 4. Determine tile layout from SIZ marker
            int imageW = header.ImageWidth;
            int imageH = header.ImageHeight;
            int tileW = header.TileWidth;
            int tileH = header.TileHeight;

            // Default tile size = image size when not specified
            if (tileW <= 0)
            {
                tileW = imageW;
            }

            if (tileH <= 0)
            {
                tileH = imageH;
            }

            int tileCols = (imageW + tileW - 1) / tileW;
            int tileRows = (imageH + tileH - 1) / tileH;
            int numTiles = tileCols * tileRows;

            // 5. Find all tile data offsets
            var tileDataEntries = FindAllTileDataOffsets(codestream, numTiles);

            if (tileDataEntries.Count == 0)
            {
                return DecodeResult.Fail(frameIndex, 0, "Could not find any tile data");
            }

            // 6. Decode tiles
            int components = header.ComponentCount;
            bool lossless = header.UsesReversibleTransform;
            int levels = header.DecompositionLevels;
            int cbWidth = header.CodeBlockWidth;
            int cbHeight = header.CodeBlockHeight;

            // For single tile: use the existing efficient path
            if (numTiles == 1 && tileDataEntries.Count >= 1)
            {
                var entry = tileDataEntries[0];
                ReadOnlySpan<byte> tileData = codestream.Slice(entry.DataOffset, entry.DataLength);

                // Allocate component arrays
                int[][] componentData = new int[components][];
                for (int c = 0; c < components; c++)
                {
                    componentData[c] = new int[imageW * imageH];
                }

                DecodeTileData(tileData, componentData, imageW, imageH, components,
                    lossless, levels, cbWidth, cbHeight, effectiveCoder, header);

                // Apply inverse DWT
                for (int c = 0; c < components; c++)
                {
                    DwtTransform.Inverse(componentData[c], imageW, imageH, levels, lossless);
                }

                // Apply inverse color transform if used
                if (components >= 3 && header.UsesMct)
                {
                    ApplyInverseColorTransform(componentData, imageW, imageH, lossless);
                }

                // Write output
                WriteOutput(componentData, info, output);
                return DecodeResult.Ok(info.FrameSize);
            }

            // Multi-tile decode
            // We need to decode each tile independently into its own component arrays,
            // then stitch them into the full image.
            var tileComponentData = new int[numTiles][][];

            // Copy codestream data for parallel access (Span cannot be captured in closures)
            byte[] codestreamArray = codestream.ToArray();

            if (maxDegreeOfParallelism > 1 && numTiles > 1)
            {
                // Parallel tile decode
                Parallel.For(0, numTiles, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    tileIdx =>
                    {
                        DecodeTileAtIndex(codestreamArray, tileDataEntries, tileIdx,
                            tileW, tileH, imageW, imageH, tileCols,
                            components, lossless, levels, cbWidth, cbHeight,
                            effectiveCoder, header, tileComponentData);
                    });
            }
            else
            {
                // Sequential tile decode
                for (int tileIdx = 0; tileIdx < numTiles; tileIdx++)
                {
                    DecodeTileAtIndex(codestreamArray, tileDataEntries, tileIdx,
                        tileW, tileH, imageW, imageH, tileCols,
                        components, lossless, levels, cbWidth, cbHeight,
                        effectiveCoder, header, tileComponentData);
                }
            }

            // Stitch tiles into full image
            int[][] fullComponentData = new int[components][];
            for (int c = 0; c < components; c++)
            {
                fullComponentData[c] = new int[imageW * imageH];
            }

            for (int tileIdx = 0; tileIdx < numTiles; tileIdx++)
            {
                if (tileComponentData[tileIdx] == null)
                {
                    continue;
                }

                int tileRow = tileIdx / tileCols;
                int tileCol = tileIdx % tileCols;
                int tx0 = tileCol * tileW;
                int ty0 = tileRow * tileH;
                int actualTileW = Math.Min(tileW, imageW - tx0);
                int actualTileH = Math.Min(tileH, imageH - ty0);

                for (int c = 0; c < components; c++)
                {
                    for (int y = 0; y < actualTileH; y++)
                    {
                        int dstStart = (ty0 + y) * imageW + tx0;
                        int srcStart = y * actualTileW;
                        Array.Copy(tileComponentData[tileIdx][c], srcStart, fullComponentData[c], dstStart, actualTileW);
                    }
                }
            }

            // Apply inverse color transform on the full image
            if (components >= 3 && header.UsesMct)
            {
                ApplyInverseColorTransform(fullComponentData, imageW, imageH, lossless);
            }

            // Write output
            WriteOutput(fullComponentData, info, output);
            return DecodeResult.Ok(info.FrameSize);
        }

        /// <summary>
        /// Decodes a single tile at the given index.
        /// Thread-safe: no shared mutable state between tiles.
        /// </summary>
        private static void DecodeTileAtIndex(
            byte[] codestreamArray,
            List<TileDataEntry> tileDataEntries,
            int tileIdx,
            int nominalTileW, int nominalTileH,
            int imageW, int imageH, int tileCols,
            int components, bool lossless, int levels,
            int cbWidth, int cbHeight,
            IBlockCoder blockCoder,
            J2kCodestream header,
            int[][][] tileComponentData)
        {
            // Find this tile's data
            TileDataEntry? entry = null;
            foreach (var e in tileDataEntries)
            {
                if (e.TileIndex == tileIdx)
                {
                    entry = e;
                    break;
                }
            }

            int tileRow = tileIdx / tileCols;
            int tileCol = tileIdx % tileCols;
            int tx0 = tileCol * nominalTileW;
            int ty0 = tileRow * nominalTileH;
            int actualTileW = Math.Min(nominalTileW, imageW - tx0);
            int actualTileH = Math.Min(nominalTileH, imageH - ty0);

            // Allocate tile component arrays
            int[][] tileComponents = new int[components][];
            for (int c = 0; c < components; c++)
            {
                tileComponents[c] = new int[actualTileW * actualTileH];
            }

            if (entry != null)
            {
                ReadOnlySpan<byte> tileData = new ReadOnlySpan<byte>(
                    codestreamArray, entry.Value.DataOffset, entry.Value.DataLength);

                // Use a separate block coder for thread safety when using EBCOT
                // (EbcotBlockCoder.Instance is NOT thread-safe for concurrent use)
                IBlockCoder localCoder = blockCoder;
                if (blockCoder is EbcotBlockCoder)
                {
                    localCoder = new EbcotBlockCoder();
                }

                DecodeTileData(tileData, tileComponents, actualTileW, actualTileH, components,
                    lossless, levels, cbWidth, cbHeight, localCoder, header);
            }

            // Apply inverse DWT per tile
            for (int c = 0; c < components; c++)
            {
                DwtTransform.Inverse(tileComponents[c], actualTileW, actualTileH, levels, lossless);
            }

            tileComponentData[tileIdx] = tileComponents;
        }

        /// <summary>
        /// Decodes the raw tile data (packets/code-blocks) into coefficient arrays
        /// using per-subband code-block iteration via <see cref="TileComponent"/>.
        /// Does NOT apply inverse DWT or color transform.
        /// </summary>
        private static void DecodeTileData(
            ReadOnlySpan<byte> tileData,
            int[][] componentData,
            int width, int height,
            int components,
            bool lossless, int levels,
            int cbWidth, int cbHeight,
            IBlockCoder blockCoder,
            J2kCodestream header)
        {
            // Compute subband layout and total code blocks
            var subbands = SubbandPartitioner.GetSubbands(
                width, height, levels, cbWidth, cbHeight);

            int totalCodeBlocks = 0;
            for (int s = 0; s < subbands.Length; s++)
            {
                totalCodeBlocks += subbands[s].TotalCodeBlocks;
            }

            var packetDecoder = new PacketDecoder();
            packetDecoder.IsHtMode = header.IsHtj2k;

            int dataOffset = 0;

            for (int c = 0; c < components; c++)
            {
                bool[] firstInclusion = new bool[totalCodeBlocks];
                for (int i = 0; i < totalCodeBlocks; i++)
                {
                    firstInclusion[i] = true;
                }

                var segments = DecodeComponentPackets(
                    tileData.Slice(dataOffset),
                    totalCodeBlocks,
                    header.NumberOfLayers,
                    packetDecoder,
                    firstInclusion);

                // Use TileComponent to place decoded coefficients in correct subband positions
                using var tileComp = new TileComponent(0, c, width, height, levels, cbWidth, cbHeight);
                int cbIdx = 0;

                // Iterate subbands in the SAME canonical order as encoder
                for (int s = 0; s < subbands.Length; s++)
                {
                    var sb = subbands[s];
                    int subbandType = (int)sb.Type;

                    for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                    {
                        for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                        {
                            var (data, totalPasses, zeroBitPlanes) = segments[cbIdx];

                            if (totalPasses > 0 && !data.IsEmpty)
                            {
                                // Compute actual code block dimensions within this subband
                                int startX = cbX * cbWidth;
                                int startY = cbY * cbHeight;
                                int actualW = Math.Min(cbWidth, sb.Width - startX);
                                int actualH = Math.Min(cbHeight, sb.Height - startY);

                                int msbPosition = Math.Max(0, 31 - zeroBitPlanes);

                                // Decode into tightly-packed buffer with actual dimensions
                                int[] packed = new int[actualW * actualH];
                                blockCoder.DecodeBlock(
                                    data.Span,
                                    totalPasses,
                                    packed,
                                    actualW, actualH,
                                    msbPosition,
                                    subbandType);

                                // Unpack into cbWidth-stride buffer for SetCodeBlockCoefficients
                                int[] cbBuffer = new int[cbWidth * cbHeight];
                                for (int y = 0; y < actualH; y++)
                                {
                                    for (int x = 0; x < actualW; x++)
                                    {
                                        cbBuffer[y * cbWidth + x] = packed[y * actualW + x];
                                    }
                                }

                                // Place decoded coefficients into correct subband position
                                tileComp.SetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);
                            }

                            cbIdx++;
                        }
                    }
                }

                // Copy TileComponent's coefficient array back to componentData
                tileComp.Coefficients.CopyTo(componentData[c].AsSpan(0, width * height));
            }
        }

        /// <summary>
        /// Checks if data starts with a valid JPEG 2000 SOC marker.
        /// </summary>
        /// <param name="data">Data to check.</param>
        /// <returns>True if data appears to be a JPEG 2000 codestream.</returns>
        public static bool IsJpeg2000(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2)
            {
                return false;
            }

            ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data);
            return marker == J2kMarkers.SOC;
        }

        /// <summary>
        /// Information about a tile's data location within the codestream.
        /// </summary>
        private struct TileDataEntry
        {
            /// <summary>Zero-based tile index.</summary>
            public int TileIndex;

            /// <summary>Byte offset of tile data (after SOD marker).</summary>
            public int DataOffset;

            /// <summary>Length of tile data in bytes.</summary>
            public int DataLength;
        }

        /// <summary>
        /// Finds all tile data offsets within a codestream.
        /// </summary>
        private static List<TileDataEntry> FindAllTileDataOffsets(ReadOnlySpan<byte> data, int expectedTiles)
        {
            var entries = new List<TileDataEntry>(expectedTiles);

            if (data.Length < 4)
            {
                return entries;
            }

            int position = 0;
            int currentTileIndex = -1;
            int currentTilePartLength = 0;

            while (position + 2 <= data.Length)
            {
                ushort marker = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                if (marker == J2kMarkers.EOC)
                {
                    break;
                }

                if (!J2kMarkers.HasSegment(marker))
                {
                    if (marker == J2kMarkers.SOD)
                    {
                        // Data follows SOD marker
                        // Calculate data length from tile-part length
                        int dataLength;
                        if (currentTilePartLength > 0)
                        {
                            // SOT told us the total tile-part length
                            // We need to figure out how much has been consumed since the SOT marker
                            // The data length extends to the end of the tile-part
                            // Since we may have had markers between SOT and SOD, use remaining length
                            // For simplicity: scan for next SOT or EOC to determine end
                            int endPos = FindNextTileOrEnd(data, position);
                            dataLength = endPos - position;
                        }
                        else
                        {
                            // No tile-part length info, scan for next marker
                            int endPos = FindNextTileOrEnd(data, position);
                            dataLength = endPos - position;
                        }

                        if (currentTileIndex >= 0)
                        {
                            entries.Add(new TileDataEntry
                            {
                                TileIndex = currentTileIndex,
                                DataOffset = position,
                                DataLength = dataLength
                            });
                        }

                        position += dataLength;
                        currentTileIndex = -1;
                        currentTilePartLength = 0;
                    }

                    continue;
                }

                if (position + 2 > data.Length)
                {
                    break;
                }

                int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                position += 2;

                if (segmentLength < 2)
                {
                    break;
                }

                if (marker == J2kMarkers.SOT)
                {
                    if (position + 4 <= data.Length)
                    {
                        currentTileIndex = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position));
                        if (position + 6 <= data.Length)
                        {
                            currentTilePartLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 2));
                        }
                    }
                }

                position += segmentLength - 2;
            }

            return entries;
        }

        /// <summary>
        /// Finds the byte position of the next SOT marker or EOC marker after the given position.
        /// </summary>
        private static int FindNextTileOrEnd(ReadOnlySpan<byte> data, int startPos)
        {
            int pos = startPos;
            while (pos + 1 < data.Length)
            {
                if (data[pos] == 0xFF)
                {
                    byte next = data[pos + 1];
                    // SOT (0xFF90) or EOC (0xFFD9)
                    if (next == 0x90 || next == 0xD9)
                    {
                        return pos;
                    }
                }

                pos++;
            }

            return data.Length;
        }

        /// <summary>
        /// Decodes packets for a component and accumulates code-block data.
        /// </summary>
        private static (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[] DecodeComponentPackets(
            ReadOnlySpan<byte> packetData,
            int numCodeBlocks,
            int numLayers,
            PacketDecoder decoder,
            bool[] firstInclusion)
        {
            var results = new (ReadOnlyMemory<byte>, int, int)[numCodeBlocks];

            for (int i = 0; i < numCodeBlocks; i++)
            {
                results[i] = (ReadOnlyMemory<byte>.Empty, 0, 0);
            }

            if (!packetData.IsEmpty)
            {
                var segments = decoder.DecodePacket(packetData, numCodeBlocks, firstInclusion);

                for (int i = 0; i < numCodeBlocks; i++)
                {
                    var seg = segments[i];
                    if (seg.NumNewPasses > 0)
                    {
                        results[i] = (seg.Data, seg.NumNewPasses, seg.ZeroBitPlanes);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Applies inverse color transform (inverse RCT or ICT).
        /// </summary>
        private static void ApplyInverseColorTransform(int[][] components, int width, int height, bool lossless)
        {
            if (components.Length < 3)
            {
                return;
            }

            int[] y = components[0];
            int[] cb = components[1];
            int[] cr = components[2];
            int pixelCount = width * height;

            if (lossless)
            {
                // Inverse RCT
                for (int i = 0; i < pixelCount; i++)
                {
                    int yVal = y[i];
                    int cbVal = cb[i];
                    int crVal = cr[i];

                    // G = Y - floor((Cb + Cr) / 4)
                    // R = Cr + G
                    // B = Cb + G
                    int green = yVal - ((cbVal + crVal) >> 2);
                    int red = crVal + green;
                    int blue = cbVal + green;

                    y[i] = red;
                    cb[i] = green;
                    cr[i] = blue;
                }
            }
            else
            {
                // Inverse ICT
                for (int i = 0; i < pixelCount; i++)
                {
                    double yVal = y[i];
                    double cbVal = cb[i];
                    double crVal = cr[i];

                    // R = Y + 1.402 * Cr
                    // G = Y - 0.34413 * Cb - 0.71414 * Cr
                    // B = Y + 1.772 * Cb
                    double red = yVal + 1.402 * crVal;
                    double green = yVal - 0.34413 * cbVal - 0.71414 * crVal;
                    double blue = yVal + 1.772 * cbVal;

                    y[i] = (int)Math.Round(red);
                    cb[i] = (int)Math.Round(green);
                    cr[i] = (int)Math.Round(blue);
                }
            }
        }

        /// <summary>
        /// Writes decoded component data to output buffer.
        /// </summary>
        private static void WriteOutput(int[][] componentData, PixelDataInfo info, Span<byte> output)
        {
            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;
            int bytesPerSample = info.BytesPerSample;
            int pixelCount = width * height;

            // Clamp values to valid range
            int maxValue = (1 << info.BitsStored) - 1;
            int minValue = info.IsSigned ? -(1 << (info.BitsStored - 1)) : 0;
            int maxSigned = info.IsSigned ? (1 << (info.BitsStored - 1)) - 1 : maxValue;

            if (info.IsPlanar)
            {
                // Planar output
                for (int c = 0; c < components; c++)
                {
                    int offset = c * pixelCount * bytesPerSample;
                    for (int i = 0; i < pixelCount; i++)
                    {
                        int value = Clamp(componentData[c][i], minValue, maxSigned);
                        WriteSample(output, offset + i * bytesPerSample, value, bytesPerSample);
                    }
                }
            }
            else
            {
                // Interleaved output
                int bytesPerPixel = components * bytesPerSample;
                for (int i = 0; i < pixelCount; i++)
                {
                    int pixelOffset = i * bytesPerPixel;
                    for (int c = 0; c < components; c++)
                    {
                        int value = Clamp(componentData[c][i], minValue, maxSigned);
                        WriteSample(output, pixelOffset + c * bytesPerSample, value, bytesPerSample);
                    }
                }
            }
        }

        private static void WriteSample(Span<byte> output, int offset, int value, int bytesPerSample)
        {
            if (bytesPerSample == 1)
            {
                output[offset] = (byte)value;
            }
            else if (bytesPerSample == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(offset), (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(offset), (uint)value);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
