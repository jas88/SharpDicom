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

                // Inverse DC level shift for unsigned data (ITU-T T.800 D.3.1):
                // add 2^(B-1) to restore unsigned range after inverse DWT
                if (!info.IsSigned)
                {
                    int dcShift = 1 << (info.BitsStored - 1);
                    for (int c = 0; c < components; c++)
                    {
                        for (int i = 0; i < componentData[c].Length; i++)
                        {
                            componentData[c][i] += dcShift;
                        }
                    }
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

            // Inverse DC level shift for unsigned data (ITU-T T.800 D.3.1)
            if (!info.IsSigned)
            {
                int dcShift = 1 << (info.BitsStored - 1);
                for (int c = 0; c < components; c++)
                {
                    for (int i = 0; i < fullComponentData[c].Length; i++)
                    {
                        fullComponentData[c][i] += dcShift;
                    }
                }
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
        /// Packets arrive in LRCP order: for each layer, for each resolution, for each component.
        /// Within each packet, subbands are iterated with per-subband tag trees (ITU-T T.800 B.10).
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
            var subbands = SubbandPartitioner.GetSubbands(
                width, height, levels, cbWidth, cbHeight);

            int numResolutions = levels + 1;
            int numLayers = header.NumberOfLayers;

            // Compute per-subband global codeblock start indices
            int[] subbandStartCbIdx = new int[subbands.Length];
            int totalCodeBlocks = 0;
            for (int s = 0; s < subbands.Length; s++)
            {
                subbandStartCbIdx[s] = totalCodeBlocks;
                totalCodeBlocks += subbands[s].TotalCodeBlocks;
            }

            // Per-component accumulated results (indexed by global codeblock index)
            var componentResults = new (ReadOnlyMemory<byte> Data, int TotalPasses, int ZeroBitPlanes)[components][];
            for (int c = 0; c < components; c++)
            {
                componentResults[c] = new (ReadOnlyMemory<byte>, int, int)[totalCodeBlocks];
            }

            // Per-component, per-subband state: tag trees and Lblock (persistent across layers)
            var inclusionTrees = new TagTree[components, subbands.Length];
            var zeroBpTrees = new TagTree[components, subbands.Length];
            var sbLblock = new int[components][][];
            var firstInclusions = new bool[components][];
            var accumulatedData = new List<byte>[components][];
            var passCounts = new int[components][];
            var zeroBitPlanes = new int[components][];

            for (int c = 0; c < components; c++)
            {
                firstInclusions[c] = new bool[totalCodeBlocks];
                accumulatedData[c] = new List<byte>[totalCodeBlocks];
                passCounts[c] = new int[totalCodeBlocks];
                zeroBitPlanes[c] = new int[totalCodeBlocks];
                sbLblock[c] = new int[subbands.Length][];

                for (int i = 0; i < totalCodeBlocks; i++)
                {
                    firstInclusions[c][i] = true;
                    accumulatedData[c][i] = new List<byte>();
                }

                for (int s = 0; s < subbands.Length; s++)
                {
                    int numCbs = subbands[s].TotalCodeBlocks;
                    if (numCbs == 0)
                        continue;

                    int cbW = subbands[s].CodeBlockGridWidth;
                    int cbH = subbands[s].CodeBlockGridHeight;
                    inclusionTrees[c, s] = new TagTree(cbW, cbH);
                    zeroBpTrees[c, s] = new TagTree(cbW, cbH);
                    sbLblock[c][s] = new int[numCbs];
                    for (int i = 0; i < numCbs; i++)
                        sbLblock[c][s][i] = 3;
                }
            }

            // Decode packets in LRCP order
            int dataPos = 0;
            for (int layer = 0; layer < numLayers; layer++)
            {
                for (int r = 0; r < numResolutions; r++)
                {
                    for (int c = 0; c < components; c++)
                    {
                        if (dataPos >= tileData.Length)
                            continue;

                        int bytesConsumed = DecodeResolutionPacket(
                            tileData.Slice(dataPos),
                            subbands, subbandStartCbIdx,
                            inclusionTrees, zeroBpTrees, sbLblock[c],
                            firstInclusions[c],
                            accumulatedData[c], passCounts[c], zeroBitPlanes[c],
                            c, r, layer);

                        dataPos += bytesConsumed;
                    }
                }
            }

            // Build final component results
            for (int c = 0; c < components; c++)
            {
                for (int i = 0; i < totalCodeBlocks; i++)
                {
                    ReadOnlyMemory<byte> data = accumulatedData[c][i].Count > 0
                        ? accumulatedData[c][i].ToArray()
                        : ReadOnlyMemory<byte>.Empty;
                    componentResults[c][i] = (data, passCounts[c][i], zeroBitPlanes[c][i]);
                }
            }

            bool isHtMode = blockCoder is HtBlockEncoder;

            // Decode codeblocks into coefficients using TileComponent
            for (int c = 0; c < components; c++)
            {
                using var tileComp = new TileComponent(0, c, width, height, levels, cbWidth, cbHeight);
                int cbIdx = 0;
                int cbBufferSize = cbWidth * cbHeight;
                int[] cbBuffer = new int[cbBufferSize];

                for (int s = 0; s < subbands.Length; s++)
                {
                    var sb = subbands[s];
                    int subbandType = (int)sb.Type;
                    int shift = 0;

                    for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                    {
                        for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                        {
                            var (data, totalPasses, zeroBp) = componentResults[c][cbIdx];

                            if (totalPasses > 0 && !data.IsEmpty)
                            {
                                int startX = cbX * cbWidth;
                                int startY = cbY * cbHeight;
                                int actualW = Math.Min(cbWidth, sb.Width - startX);
                                int actualH = Math.Min(cbHeight, sb.Height - startY);

                                int msbPosition = Math.Max(0, 31 - zeroBp);

                                if (actualW == cbWidth && actualH == cbHeight)
                                {
                                    Array.Clear(cbBuffer, 0, cbBufferSize);
                                    blockCoder.DecodeBlock(
                                        data.Span,
                                        totalPasses,
                                        cbBuffer,
                                        actualW, actualH,
                                        msbPosition,
                                        subbandType);

                                    // HTJ2K: right-shift to undo encoder's left-shift
                                    if (shift > 0)
                                    {
                                        for (int i = 0; i < cbBufferSize; i++)
                                        {
                                            int v = cbBuffer[i];
                                            if (v >= 0)
                                                cbBuffer[i] = v >> shift;
                                            else
                                                cbBuffer[i] = -((-v) >> shift);
                                        }
                                    }
                                }
                                else
                                {
                                    int[] packed = new int[actualW * actualH];
                                    blockCoder.DecodeBlock(
                                        data.Span,
                                        totalPasses,
                                        packed,
                                        actualW, actualH,
                                        msbPosition,
                                        subbandType);

                                    // HTJ2K: right-shift to undo encoder's left-shift
                                    if (shift > 0)
                                    {
                                        int count = actualW * actualH;
                                        for (int i = 0; i < count; i++)
                                        {
                                            int v = packed[i];
                                            if (v >= 0)
                                                packed[i] = v >> shift;
                                            else
                                                packed[i] = -((-v) >> shift);
                                        }
                                    }

                                    Array.Clear(cbBuffer, 0, cbBufferSize);
                                    for (int y = 0; y < actualH; y++)
                                    {
                                        for (int x = 0; x < actualW; x++)
                                        {
                                            cbBuffer[y * cbWidth + x] = packed[y * actualW + x];
                                        }
                                    }
                                }

                                tileComp.SetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);
                            }

                            cbIdx++;
                        }
                    }
                }

                tileComp.Coefficients.CopyTo(componentData[c].AsSpan(0, width * height));
            }
        }

        /// <summary>
        /// Decodes a single resolution-level packet with per-subband tag trees from a
        /// shared bitstream, matching ITU-T T.800 B.10. Reads ONE non-empty flag, then
        /// iterates all subbands at the given resolution with per-subband tag trees.
        /// Returns number of bytes consumed.
        /// </summary>
        private static int DecodeResolutionPacket(
            ReadOnlySpan<byte> packetData,
            SubbandDescriptor[] subbands,
            int[] subbandStartCbIdx,
            TagTree[,] inclusionTrees,
            TagTree[,] zeroBpTrees,
            int[][] sbLblock,
            bool[] firstInclusion,
            List<byte>[] accumulatedData,
            int[] passCounts,
            int[] zeroBitPlanesArr,
            int component,
            int resolution,
            int layer)
        {
            if (packetData.IsEmpty)
                return 0;

            // Find subbands at this resolution
            var resSubbands = new List<int>();
            for (int s = 0; s < subbands.Length; s++)
            {
                if (subbands[s].ResolutionLevel == resolution && subbands[s].TotalCodeBlocks > 0)
                    resSubbands.Add(s);
            }

            if (resSubbands.Count == 0)
            {
                // Even resolutions with zero codeblocks have a 1-byte empty packet
                // (the non-empty flag = 0, padded to a byte). Must consume it to stay aligned.
                return packetData.Length > 0 ? 1 : 0;
            }

            // Copy to array so local functions can capture it (Span can't be captured)
            byte[] pktBytes = packetData.ToArray();

            // Inline bit reader state - shared across all subbands within this packet
            int bytePos = 0;
            int bitBuffer = 0;
            int bitsAvailable = 0;
            bool lastByteWasFF = false;

            int ReadBit()
            {
                if (bitsAvailable == 0)
                {
                    if (bytePos >= pktBytes.Length)
                        return 0;
                    bitBuffer = pktBytes[bytePos++];
                    bitsAvailable = lastByteWasFF ? 7 : 8;
                    lastByteWasFF = (bitBuffer == 0xFF);
                }
                bitsAvailable--;
                return (bitBuffer >> bitsAvailable) & 1;
            }

            int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++)
                    value = (value << 1) | ReadBit();
                return value;
            }

            int ReadNumPasses()
            {
                if (ReadBit() == 0) return 1;
                if (ReadBit() == 0) return 2;
                int next2 = ReadBits(2);
                if (next2 < 3) return 3 + next2;
                int suffix5 = ReadBits(5);
                if (suffix5 <= 30) return 6 + suffix5;
                return 37 + ReadBits(7);
            }

            int ReadLblock(int[] lblock, int cbIdx, int numPasses)
            {
                int lb = lblock[cbIdx];
                while (ReadBit() == 1)
                    lb++;
                lblock[cbIdx] = lb;
                int passContrib = 0;
                int v = numPasses;
                while (v > 1) { v >>= 1; passContrib++; }
                return ReadBits(lb + passContrib);
            }

            // Read the single non-empty flag for the entire packet
            int nonEmpty = ReadBit();
            if (nonEmpty == 0)
                return bytePos;

            // Phase 1: decode codeblock headers across all subbands (shared bitstream)
            var cbInfos = new List<(int GlobalIdx, int NumPasses, int DataLength, int ZeroBitPlanes, bool IsFirst)>();

            for (int si = 0; si < resSubbands.Count; si++)
            {
                int s = resSubbands[si];
                int numCbs = subbands[s].TotalCodeBlocks;
                int startCb = subbandStartCbIdx[s];
                int cbW = subbands[s].CodeBlockGridWidth;
                var incTree = inclusionTrees[component, s];
                var zbpTree = zeroBpTrees[component, s];
                var lblock = sbLblock[s];

                for (int cbIdx = 0; cbIdx < numCbs; cbIdx++)
                {
                    int globalIdx = startCb + cbIdx;
                    int x = cbIdx % cbW;
                    int y = cbIdx / cbW;

                    if (firstInclusion[globalIdx])
                    {
                        int inclusionValue = incTree.Decode(x, y, layer + 1, ReadBit);
                        if (inclusionValue > layer)
                        {
                            cbInfos.Add((globalIdx, 0, 0, 0, false));
                            continue;
                        }

                        int zbp = zbpTree.Decode(x, y, int.MaxValue - 1, ReadBit);
                        int numPasses = ReadNumPasses();
                        int dataLength = ReadLblock(lblock, cbIdx, numPasses);
                        cbInfos.Add((globalIdx, numPasses, dataLength, zbp, true));
                        firstInclusion[globalIdx] = false;
                    }
                    else
                    {
                        int included = ReadBit();
                        if (included == 0)
                        {
                            cbInfos.Add((globalIdx, 0, 0, 0, false));
                            continue;
                        }

                        int numPasses = ReadNumPasses();
                        int dataLength = ReadLblock(lblock, cbIdx, numPasses);
                        cbInfos.Add((globalIdx, numPasses, dataLength, 0, false));
                    }
                }
            }

            // Phase 2: extract codeblock data segments (sequential after bit-stuffed header)
            int dataOffset = bytePos;

            for (int i = 0; i < cbInfos.Count; i++)
            {
                var info = cbInfos[i];
                if (info.NumPasses > 0)
                {
                    int safeOffset = Math.Min(dataOffset, pktBytes.Length);
                    int safeLength = Math.Min(info.DataLength, pktBytes.Length - safeOffset);

                    if (safeLength > 0)
                    {
                        byte[] slice = new byte[safeLength];
                        Array.Copy(pktBytes, safeOffset, slice, 0, safeLength);

                        accumulatedData[info.GlobalIdx].AddRange(slice);
                    }

                    passCounts[info.GlobalIdx] += info.NumPasses;
                    if (info.IsFirst)
                    {
                        zeroBitPlanesArr[info.GlobalIdx] = info.ZeroBitPlanes;
                    }

                    dataOffset += info.DataLength;
                }
            }

            return dataOffset;
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
            int sotMarkerStart = -1;

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
                        // Data follows SOD marker.
                        // Use Psot (tile-part length from SOT) to compute exact data extent
                        // rather than scanning for marker-like byte patterns in the data,
                        // which can cause false marker detection (0xFF90/0xFFD9 in code block data).
                        int dataLength;
                        if (currentTilePartLength > 0 && sotMarkerStart >= 0)
                        {
                            // Psot is the total tile-part length from the start of the SOT marker.
                            // Data starts at current position (after SOD marker).
                            int tilePartEnd = sotMarkerStart + currentTilePartLength;
                            dataLength = Math.Max(0, Math.Min(tilePartEnd, data.Length) - position);
                        }
                        else
                        {
                            // Psot == 0 means tile-part extends to end of codestream (minus EOC).
                            // Scan for the EOC marker (last 2 bytes) or use remaining length.
                            if (data.Length >= 2 &&
                                data[data.Length - 2] == 0xFF &&
                                data[data.Length - 1] == 0xD9)
                            {
                                dataLength = data.Length - 2 - position;
                            }
                            else
                            {
                                dataLength = data.Length - position;
                            }
                        }

                        if (currentTileIndex >= 0 && dataLength > 0)
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
                        sotMarkerStart = -1;
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
                    // Record where the SOT marker started (position - 4: 2 for marker + 2 for length)
                    sotMarkerStart = position - 4;
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
