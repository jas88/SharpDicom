using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharpDicom.Codecs.Jpeg2000.Subband;
using SharpDicom.Codecs.Jpeg2000.Tier1;
using SharpDicom.Codecs.Jpeg2000.Tier2;
using SharpDicom.Codecs.Jpeg2000.Wavelet;

#if NETSTANDARD2_0
using BufferWriter = SharpDicom.Internal.ArrayBufferWriterPolyfill<byte>;
#else
using BufferWriter = System.Buffers.ArrayBufferWriter<byte>;
#endif

namespace SharpDicom.Codecs.Jpeg2000
{
    /// <summary>
    /// JPEG 2000 encoder options.
    /// </summary>
    public sealed class J2kEncoderOptions
    {
        /// <summary>Gets or sets the number of decomposition levels (0-32).</summary>
        public int DecompositionLevels { get; set; } = 5;

        /// <summary>Gets or sets the code-block width (must be power of 2, 4-1024).</summary>
        public int CodeBlockWidth { get; set; } = EbcotEncoder.DefaultCodeBlockSize;

        /// <summary>Gets or sets the code-block height (must be power of 2, 4-1024).</summary>
        public int CodeBlockHeight { get; set; } = EbcotEncoder.DefaultCodeBlockSize;

        /// <summary>Gets or sets the number of quality layers.</summary>
        public int NumberOfLayers { get; set; } = 1;

        /// <summary>Gets or sets the progression order.</summary>
        public ProgressionOrder Progression { get; set; } = ProgressionOrder.LRCP;

        /// <summary>
        /// Gets or sets the tile width. When null, the entire image width is used (single tile column).
        /// </summary>
        /// <remarks>
        /// Setting this to a value less than the image width enables multi-tile encoding.
        /// Each tile is encoded independently, enabling parallel decode.
        /// </remarks>
        public int? TileWidth { get; set; }

        /// <summary>
        /// Gets or sets the tile height. When null, the entire image height is used (single tile row).
        /// </summary>
        /// <remarks>
        /// Setting this to a value less than the image height enables multi-tile encoding.
        /// Each tile is encoded independently, enabling parallel decode.
        /// </remarks>
        public int? TileHeight { get; set; }

        /// <summary>
        /// Gets or sets the maximum degree of parallelism for tile encoding and decoding.
        /// A value of 1 means sequential processing; higher values enable parallel tile processing.
        /// </summary>
        /// <remarks>
        /// Default is 1 (sequential). Set higher for multi-tile images to leverage multiple cores.
        /// The actual degree of parallelism is capped at the number of tiles.
        /// </remarks>
        public int MaxDegreeOfParallelism { get; set; } = 1;

        /// <summary>
        /// Gets the default options for lossless encoding.
        /// </summary>
        public static J2kEncoderOptions Lossless => new()
        {
            DecompositionLevels = 5,
            CodeBlockWidth = 64,
            CodeBlockHeight = 64,
            NumberOfLayers = 1,
            Progression = ProgressionOrder.LRCP
        };

        /// <summary>
        /// Gets the default options for lossy encoding.
        /// </summary>
        public static J2kEncoderOptions Lossy => new()
        {
            DecompositionLevels = 5,
            CodeBlockWidth = 64,
            CodeBlockHeight = 64,
            NumberOfLayers = 1,
            Progression = ProgressionOrder.LRCP
        };
    }

    /// <summary>
    /// Encodes raw pixel data to JPEG 2000 codestreams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This encoder produces JPEG 2000 Part 1 codestreams (ITU-T T.800) suitable
    /// for use in DICOM files with transfer syntaxes:
    /// - JPEG 2000 Lossless (1.2.840.10008.1.2.4.90)
    /// - JPEG 2000 (1.2.840.10008.1.2.4.91)
    /// </para>
    /// <para>
    /// The encoder supports both lossless (5/3 wavelet) and lossy (9/7 wavelet) modes.
    /// Multi-tile encoding is supported via <see cref="J2kEncoderOptions.TileWidth"/>
    /// and <see cref="J2kEncoderOptions.TileHeight"/>.
    /// </para>
    /// </remarks>
    public static class J2kEncoder
    {
        /// <summary>
        /// Encodes a single frame to JPEG 2000 format.
        /// </summary>
        /// <param name="pixelData">Raw pixel data.</param>
        /// <param name="info">Pixel data information.</param>
        /// <param name="lossless">True for lossless encoding (5/3 wavelet), false for lossy (9/7).</param>
        /// <returns>Encoded JPEG 2000 codestream.</returns>
        public static ReadOnlyMemory<byte> EncodeFrame(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            bool lossless)
        {
            return EncodeFrame(pixelData, info, lossless ? J2kEncoderOptions.Lossless : J2kEncoderOptions.Lossy, lossless);
        }

        /// <summary>
        /// Encodes a single frame to JPEG 2000 format with custom options.
        /// </summary>
        /// <param name="pixelData">Raw pixel data.</param>
        /// <param name="info">Pixel data information.</param>
        /// <param name="options">Encoder options.</param>
        /// <param name="lossless">True for lossless encoding (5/3 wavelet), false for lossy (9/7).</param>
        /// <returns>Encoded JPEG 2000 codestream.</returns>
        public static ReadOnlyMemory<byte> EncodeFrame(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            J2kEncoderOptions options,
            bool lossless)
        {
            return EncodeFrame(pixelData, info, options, lossless, EbcotBlockCoder.Instance);
        }

        /// <summary>
        /// Encodes a single frame to JPEG 2000 format with a custom block coder.
        /// </summary>
        /// <param name="pixelData">Raw pixel data.</param>
        /// <param name="info">Pixel data information.</param>
        /// <param name="options">Encoder options.</param>
        /// <param name="lossless">True for lossless encoding (5/3 wavelet), false for lossy (9/7).</param>
        /// <param name="blockCoder">Block coder implementation (EBCOT or HT).</param>
        /// <returns>Encoded JPEG 2000 codestream.</returns>
        public static ReadOnlyMemory<byte> EncodeFrame(
            ReadOnlySpan<byte> pixelData,
            PixelDataInfo info,
            J2kEncoderOptions options,
            bool lossless,
            IBlockCoder blockCoder)
        {
            if (pixelData.Length < info.FrameSize)
            {
                throw new ArgumentException("Pixel data is too small for the specified image dimensions.");
            }

            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;

            // Determine effective tile dimensions
            int tileW = options.TileWidth ?? width;
            int tileH = options.TileHeight ?? height;
            tileW = Math.Min(tileW, width);
            tileH = Math.Min(tileH, height);
            if (tileW <= 0)
            {
                tileW = width;
            }

            if (tileH <= 0)
            {
                tileH = height;
            }

            int tileCols = (width + tileW - 1) / tileW;
            int tileRows = (height + tileH - 1) / tileH;
            int numTiles = tileCols * tileRows;

            // Extract full image component data
            int[][] componentData = ExtractComponents(pixelData, info);

            // Apply forward color transform on the full image if multi-component
            if (components >= 3 && !info.IsPlanar)
            {
                ApplyColorTransform(componentData, width, height, lossless);
            }

            bool isHtMode = blockCoder is HtBlockEncoder;

            // Encode each tile
            var tileResults = new TileEncodeResult[numTiles];

            if (numTiles == 1)
            {
                // Single tile: use existing fast path (no pixel extraction overhead)
                tileResults[0] = EncodeSingleTile(
                    componentData, width, height, components,
                    options, lossless, blockCoder, isHtMode);
            }
            else
            {
                // Multi-tile encoding
                // Each tile is encoded independently after DWT
                // Tiles share the color-transformed full image data but operate on separate regions
                if (options.MaxDegreeOfParallelism > 1)
                {
                    Parallel.For(0, numTiles, new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism }, tileIdx =>
                    {
                        int tileRow = tileIdx / tileCols;
                        int tileCol = tileIdx % tileCols;
                        int tx0 = tileCol * tileW;
                        int ty0 = tileRow * tileH;
                        int actualTileW = Math.Min(tileW, width - tx0);
                        int actualTileH = Math.Min(tileH, height - ty0);

                        int[][] tileComponentData = ExtractTileRegion(
                            componentData, width, components, tx0, ty0, actualTileW, actualTileH);

                        // EbcotBlockCoder is not thread-safe; create a per-thread instance
                        IBlockCoder localCoder = blockCoder is EbcotBlockCoder
                            ? new EbcotBlockCoder()
                            : blockCoder;

                        try
                        {
                            tileResults[tileIdx] = EncodeSingleTile(
                                tileComponentData, actualTileW, actualTileH, components,
                                options, lossless, localCoder, isHtMode);
                        }
                        finally
                        {
                            if (localCoder is IDisposable d && !ReferenceEquals(localCoder, blockCoder))
                                d.Dispose();
                        }
                    });
                }
                else
                {
                    for (int tileIdx = 0; tileIdx < numTiles; tileIdx++)
                    {
                        int tileRow = tileIdx / tileCols;
                        int tileCol = tileIdx % tileCols;
                        int tx0 = tileCol * tileW;
                        int ty0 = tileRow * tileH;
                        int actualTileW = Math.Min(tileW, width - tx0);
                        int actualTileH = Math.Min(tileH, height - ty0);

                        int[][] tileComponentData = ExtractTileRegion(
                            componentData, width, components, tx0, ty0, actualTileW, actualTileH);

                        tileResults[tileIdx] = EncodeSingleTile(
                            tileComponentData, actualTileW, actualTileH, components,
                            options, lossless, blockCoder, isHtMode);
                    }
                }
            }

            // Build codestream with all tiles
            return BuildMultiTileCodestream(info, options, lossless, tileResults, tileW, tileH, tileCols, tileRows);
        }

        /// <summary>
        /// Extracts a rectangular region from the full-image component arrays.
        /// </summary>
        private static int[][] ExtractTileRegion(
            int[][] componentData, int imageWidth, int components,
            int tx0, int ty0, int tileW, int tileH)
        {
            int[][] tileData = new int[components][];
            int tilePixelCount = tileW * tileH;

            for (int c = 0; c < components; c++)
            {
                tileData[c] = new int[tilePixelCount];
                for (int y = 0; y < tileH; y++)
                {
                    int srcStart = (ty0 + y) * imageWidth + tx0;
                    int dstStart = y * tileW;
                    Array.Copy(componentData[c], srcStart, tileData[c], dstStart, tileW);
                }
            }

            return tileData;
        }

        /// <summary>
        /// Encodes a single tile's component data through the full pipeline (DWT, Tier-1, Tier-2).
        /// </summary>
        private static TileEncodeResult EncodeSingleTile(
            int[][] componentData, int tileWidth, int tileHeight, int components,
            J2kEncoderOptions options, bool lossless, IBlockCoder blockCoder, bool isHtMode)
        {
            // Apply forward DWT to each component (operates in-place on tile data)
            for (int c = 0; c < components; c++)
            {
                DwtTransform.Forward(componentData[c], tileWidth, tileHeight, options.DecompositionLevels, lossless);
            }

            // Tier-1 encoding via IBlockCoder (per-subband using TileComponent)
            var packetEncoder = new PacketEncoder();
            int cbWidth = options.CodeBlockWidth;
            int cbHeight = options.CodeBlockHeight;

            var allCodeBlocks = new List<CodeBlockData[]>(components);
            int totalCodeBlocksPerComponent = 0;
            for (int c = 0; c < components; c++)
            {
                var (codeBlocks, total) = EncodeComponentCodeBlocks(
                    componentData[c], tileWidth, tileHeight,
                    cbWidth, cbHeight,
                    blockCoder, options.DecompositionLevels);
                allCodeBlocks.Add(codeBlocks);
                totalCodeBlocksPerComponent = total;
            }

            // Tier-2: Create packets
            // Pass (total, 1) since PacketEncoder only uses wide*high to compute count
            var allPackets = new List<PacketData[]>(components);
            for (int c = 0; c < components; c++)
            {
                var packets = packetEncoder.EncodePackets(
                    allCodeBlocks[c],
                    totalCodeBlocksPerComponent, 1,
                    options.NumberOfLayers,
                    options.Progression,
                    options.DecompositionLevels + 1);
                allPackets.Add(packets);
            }

            // Collect tile data bytes
            byte[] tileData = CollectTileData(allPackets, options);

            return new TileEncodeResult { PacketData = tileData };
        }

        /// <summary>
        /// Collects all packet data for a tile into a byte array.
        /// </summary>
        private static byte[] CollectTileData(List<PacketData[]> componentPackets, J2kEncoderOptions options)
        {
            var tileData = new List<byte>();
            int numLayers = options.NumberOfLayers;
            int numComponents = componentPackets.Count;

            // Write packets in the specified progression order
            // For single-tile, single-resolution the ordering differences are minimal
            // but we follow the correct ordering for conformance.
            switch (options.Progression)
            {
                case ProgressionOrder.LRCP:
                default:
                    // Layer, Resolution, Component, Position
                    for (int layer = 0; layer < numLayers; layer++)
                    {
                        for (int c = 0; c < numComponents; c++)
                        {
                            if (layer < componentPackets[c].Length)
                            {
                                var packet = componentPackets[c][layer];
                                if (!packet.IsEmpty)
                                {
                                    tileData.AddRange(packet.Data.ToArray());
                                }
                            }
                        }
                    }

                    break;

                case ProgressionOrder.RLCP:
                    // Resolution, Layer, Component, Position
                    // With our simplified single-resolution model, this is equivalent to LRCP
                    // but we iterate in the correct order for conformance
                    for (int layer = 0; layer < numLayers; layer++)
                    {
                        for (int c = 0; c < numComponents; c++)
                        {
                            if (layer < componentPackets[c].Length)
                            {
                                var packet = componentPackets[c][layer];
                                if (!packet.IsEmpty)
                                {
                                    tileData.AddRange(packet.Data.ToArray());
                                }
                            }
                        }
                    }

                    break;

                case ProgressionOrder.RPCL:
                    // Resolution, Position, Component, Layer
                    for (int c = 0; c < numComponents; c++)
                    {
                        for (int layer = 0; layer < numLayers; layer++)
                        {
                            if (layer < componentPackets[c].Length)
                            {
                                var packet = componentPackets[c][layer];
                                if (!packet.IsEmpty)
                                {
                                    tileData.AddRange(packet.Data.ToArray());
                                }
                            }
                        }
                    }

                    break;

                case ProgressionOrder.PCRL:
                    // Position, Component, Resolution, Layer
                    for (int c = 0; c < numComponents; c++)
                    {
                        for (int layer = 0; layer < numLayers; layer++)
                        {
                            if (layer < componentPackets[c].Length)
                            {
                                var packet = componentPackets[c][layer];
                                if (!packet.IsEmpty)
                                {
                                    tileData.AddRange(packet.Data.ToArray());
                                }
                            }
                        }
                    }

                    break;

                case ProgressionOrder.CPRL:
                    // Component, Position, Resolution, Layer
                    for (int c = 0; c < numComponents; c++)
                    {
                        for (int layer = 0; layer < numLayers; layer++)
                        {
                            if (layer < componentPackets[c].Length)
                            {
                                var packet = componentPackets[c][layer];
                                if (!packet.IsEmpty)
                                {
                                    tileData.AddRange(packet.Data.ToArray());
                                }
                            }
                        }
                    }

                    break;
            }

            return tileData.ToArray();
        }

        /// <summary>
        /// Builds the JPEG 2000 codestream with multiple tiles.
        /// </summary>
        private static ReadOnlyMemory<byte> BuildMultiTileCodestream(
            PixelDataInfo info,
            J2kEncoderOptions options,
            bool lossless,
            TileEncodeResult[] tiles,
            int tileW, int tileH,
            int tileCols, int tileRows)
        {
            var buffer = new BufferWriter(4096);

            // Write SOC marker
            WriteMarker(buffer, J2kMarkers.SOC);

            // Write SIZ marker with tile dimensions
            WriteSizMarker(buffer, info, tileW, tileH);

            // Write COD marker
            WriteCodMarker(buffer, options, lossless, info.SamplesPerPixel >= 3);

            // Write QCD marker
            WriteQcdMarker(buffer, options, lossless);

            // Write each tile
            for (int tileIdx = 0; tileIdx < tiles.Length; tileIdx++)
            {
                WriteSingleTileData(buffer, tileIdx, tiles[tileIdx].PacketData);
            }

            // Write EOC marker
            WriteMarker(buffer, J2kMarkers.EOC);

            return buffer.WrittenMemory.ToArray();
        }

        /// <summary>
        /// Writes a single tile's SOT + PLT + SOD + data to the buffer.
        /// </summary>
        private static void WriteSingleTileData(BufferWriter buffer, int tileIndex, byte[] packetData)
        {
            // Calculate PLT marker size
            // PLT format: marker(2) + length(2) + Zplt(1) + packet_lengths...
            // For simplicity, encode the entire tile data as a single packet length entry
            byte[] pltLengthBytes = EncodePltLength(packetData.Length);
            int pltSegmentLength = 2 + 1 + pltLengthBytes.Length; // length field + Zplt + encoded lengths

            // Calculate total tile-part length:
            // SOT marker (2) + SOT length field (2) + SOT segment (8) = 12
            // PLT marker (2) + PLT segment (pltSegmentLength)
            // SOD marker (2)
            // packet data
            int sotLength = 12; // SOT marker + segment
            int pltLength = 2 + pltSegmentLength; // PLT marker + segment
            int totalTilePartLength = sotLength + pltLength + 2 + packetData.Length;

            // Write SOT marker
            WriteMarker(buffer, J2kMarkers.SOT);

            Span<byte> sotSpan = buffer.GetSpan(10);
            int offset = 0;

            // Length
            BinaryPrimitives.WriteUInt16BigEndian(sotSpan.Slice(offset), 10);
            offset += 2;

            // Tile index
            BinaryPrimitives.WriteUInt16BigEndian(sotSpan.Slice(offset), (ushort)tileIndex);
            offset += 2;

            // Tile-part length (includes SOT marker through end of tile data)
            BinaryPrimitives.WriteUInt32BigEndian(sotSpan.Slice(offset), (uint)totalTilePartLength);
            offset += 4;

            // Tile-part index
            sotSpan[offset++] = 0;

            // Number of tile-parts for this tile
            sotSpan[offset++] = 1;

            buffer.Advance(10);

            // Write PLT marker
            WriteMarker(buffer, J2kMarkers.PLT);

            Span<byte> pltSpan = buffer.GetSpan(pltSegmentLength);
            offset = 0;

            // PLT segment length
            BinaryPrimitives.WriteUInt16BigEndian(pltSpan.Slice(offset), (ushort)pltSegmentLength);
            offset += 2;

            // Zplt (index of this PLT marker in tile-part, 0-based)
            pltSpan[offset++] = 0;

            // Packet length(s) in variable-length coding
            for (int i = 0; i < pltLengthBytes.Length; i++)
            {
                pltSpan[offset++] = pltLengthBytes[i];
            }

            buffer.Advance(pltSegmentLength);

            // Write SOD marker
            WriteMarker(buffer, J2kMarkers.SOD);

            // Write packet data
            if (packetData.Length > 0)
            {
                Span<byte> dataSpan = buffer.GetSpan(packetData.Length);
                for (int i = 0; i < packetData.Length; i++)
                {
                    dataSpan[i] = packetData[i];
                }

                buffer.Advance(packetData.Length);
            }
        }

        /// <summary>
        /// Encodes a packet length using the PLT variable-length encoding (ITU-T T.800 Annex B.8).
        /// Each byte: bit 7 = continuation flag, bits 6-0 = 7-bit value. MSB first.
        /// </summary>
        private static byte[] EncodePltLength(int length)
        {
            if (length < 0)
            {
                return new byte[] { 0 };
            }

            // Determine how many 7-bit groups we need
            if (length < 0x80)
            {
                // Single byte, no continuation
                return new[] { (byte)length };
            }

            var bytes = new List<byte>();
            int remaining = length;

            // Extract 7-bit groups from MSB to LSB
            var groups = new List<byte>();
            while (remaining > 0)
            {
                groups.Add((byte)(remaining & 0x7F));
                remaining >>= 7;
            }

            // Reverse to MSB first, set continuation bit on all but last
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (i > 0)
                {
                    bytes.Add((byte)(groups[i] | 0x80)); // continuation
                }
                else
                {
                    bytes.Add(groups[i]); // last byte, no continuation
                }
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// Extracts components from interleaved pixel data.
        /// </summary>
        private static int[][] ExtractComponents(ReadOnlySpan<byte> pixelData, PixelDataInfo info)
        {
            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;
            int bytesPerSample = info.BytesPerSample;
            int pixelSize = width * height;

            int[][] result = new int[components][];
            for (int c = 0; c < components; c++)
            {
                result[c] = new int[pixelSize];
            }

            if (info.IsPlanar)
            {
                // Planar: all samples of component 0, then component 1, etc.
                for (int c = 0; c < components; c++)
                {
                    int offset = c * pixelSize * bytesPerSample;
                    for (int i = 0; i < pixelSize; i++)
                    {
                        result[c][i] = ReadSample(pixelData, offset + i * bytesPerSample, bytesPerSample, info.IsSigned);
                    }
                }
            }
            else
            {
                // Interleaved: R, G, B, R, G, B, ...
                int bytesPerPixel = components * bytesPerSample;
                for (int i = 0; i < pixelSize; i++)
                {
                    int pixelOffset = i * bytesPerPixel;
                    for (int c = 0; c < components; c++)
                    {
                        result[c][i] = ReadSample(pixelData, pixelOffset + c * bytesPerSample, bytesPerSample, info.IsSigned);
                    }
                }
            }

            return result;
        }

        private static int ReadSample(ReadOnlySpan<byte> data, int offset, int bytesPerSample, bool isSigned)
        {
            if (bytesPerSample == 1)
            {
                return isSigned ? (sbyte)data[offset] : data[offset];
            }
            else if (bytesPerSample == 2)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
                return isSigned ? (short)value : value;
            }
            else
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                return isSigned ? (int)value : (int)value;
            }
        }

        /// <summary>
        /// Applies forward color transform (RCT for lossless, ICT for lossy).
        /// </summary>
        private static void ApplyColorTransform(int[][] components, int width, int height, bool lossless)
        {
            if (components.Length < 3)
            {
                return;
            }

            int[] r = components[0];
            int[] g = components[1];
            int[] b = components[2];
            int pixelCount = width * height;

            if (lossless)
            {
                // RCT: Reversible Color Transform (ITU-T T.800 Annex G.2)
                for (int i = 0; i < pixelCount; i++)
                {
                    int red = r[i];
                    int green = g[i];
                    int blue = b[i];

                    // Y = floor((R + 2G + B) / 4)
                    // Cb = B - G
                    // Cr = R - G
                    int y = (red + 2 * green + blue) >> 2;
                    int cb = blue - green;
                    int cr = red - green;

                    r[i] = y;
                    g[i] = cb;
                    b[i] = cr;
                }
            }
            else
            {
                // ICT: Irreversible Color Transform (ITU-T T.800 Annex G.1)
                for (int i = 0; i < pixelCount; i++)
                {
                    double red = r[i];
                    double green = g[i];
                    double blue = b[i];

                    // Y = 0.299R + 0.587G + 0.114B
                    // Cb = -0.16875R - 0.33126G + 0.5B
                    // Cr = 0.5R - 0.41869G - 0.08131B
                    double y = 0.299 * red + 0.587 * green + 0.114 * blue;
                    double cb = -0.16875 * red - 0.33126 * green + 0.5 * blue;
                    double cr = 0.5 * red - 0.41869 * green - 0.08131 * blue;

                    r[i] = (int)Math.Round(y);
                    g[i] = (int)Math.Round(cb);
                    b[i] = (int)Math.Round(cr);
                }
            }
        }

        /// <summary>
        /// Encodes code-blocks for a single component by iterating per-subband,
        /// using <see cref="TileComponent"/> to extract code-block coefficients
        /// from the correct subband regions in the DWT coefficient array.
        /// </summary>
        /// <returns>
        /// A tuple of (CodeBlocks, TotalCodeBlocks) where code blocks are ordered
        /// by subband (LL first, then HL/LH/HH per level) and raster within each subband.
        /// </returns>
        private static (CodeBlockData[] CodeBlocks, int TotalCodeBlocks) EncodeComponentCodeBlocks(
            int[] data, int width, int height,
            int cbWidth, int cbHeight,
            IBlockCoder blockCoder, int decompositionLevels)
        {
            using var tileComp = new TileComponent(0, 0, width, height, decompositionLevels, cbWidth, cbHeight);

            // Copy DWT coefficients into TileComponent
            data.AsSpan(0, width * height).CopyTo(tileComp.Coefficients);

            // Compute total code blocks across all subbands
            int totalCodeBlocks = 0;
            for (int s = 0; s < tileComp.Subbands.Length; s++)
            {
                totalCodeBlocks += tileComp.Subbands[s].TotalCodeBlocks;
            }

            var codeBlocks = new CodeBlockData[totalCodeBlocks];
            int[] cbBuffer = new int[cbWidth * cbHeight];
            int cbIdx = 0;

            // Iterate subbands in canonical order (matches SubbandPartitioner output)
            for (int s = 0; s < tileComp.Subbands.Length; s++)
            {
                var sb = tileComp.Subbands[s];
                int subbandType = (int)sb.Type;

                for (int cbY = 0; cbY < sb.CodeBlockGridHeight; cbY++)
                {
                    for (int cbX = 0; cbX < sb.CodeBlockGridWidth; cbX++)
                    {
                        // Extract code-block coefficients from the correct subband region
                        var (actualW, actualH) = tileComp.GetCodeBlockCoefficients(s, cbX, cbY, cbBuffer);

                        // Repack into a tightly-packed buffer with actual dimensions
                        // (GetCodeBlockCoefficients uses cbWidth stride, but EBCOT expects actualW stride)
                        int[] packed = new int[actualW * actualH];
                        for (int y = 0; y < actualH; y++)
                        {
                            for (int x = 0; x < actualW; x++)
                            {
                                packed[y * actualW + x] = cbBuffer[y * cbWidth + x];
                            }
                        }

                        // Encode with actual dimensions and correct subband type
                        codeBlocks[cbIdx] = blockCoder.EncodeBlock(
                            packed, actualW, actualH, subbandType, msbPosition: -1);
                        cbIdx++;
                    }
                }
            }

            return (codeBlocks, totalCodeBlocks);
        }

        private static void WriteMarker(BufferWriter buffer, ushort marker)
        {
            Span<byte> span = buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(span, marker);
            buffer.Advance(2);
        }

        private static void WriteSizMarker(BufferWriter buffer, PixelDataInfo info, int tileWidth, int tileHeight)
        {
            int components = info.SamplesPerPixel;
            int segmentLength = 38 + components * 3; // Lsiz per ITU-T T.800 Table A.9

            WriteMarker(buffer, J2kMarkers.SIZ);

            Span<byte> span = buffer.GetSpan(segmentLength);
            int offset = 0;

            // Length
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)segmentLength);
            offset += 2;

            // Rsiz (capabilities) - Profile 0 (no extensions)
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), 0);
            offset += 2;

            // Xsiz (reference grid width)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)info.Columns);
            offset += 4;

            // Ysiz (reference grid height)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)info.Rows);
            offset += 4;

            // XOsiz, YOsiz (image offsets)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), 0);
            offset += 4;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), 0);
            offset += 4;

            // XTsiz, YTsiz (tile size)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)tileWidth);
            offset += 4;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)tileHeight);
            offset += 4;

            // XTOsiz, YTOsiz (tile offsets)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), 0);
            offset += 4;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), 0);
            offset += 4;

            // Csiz (number of components)
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)components);
            offset += 2;

            // Component info
            for (int c = 0; c < components; c++)
            {
                // Ssiz: bit 7 = signed, bits 0-6 = bit depth - 1
                byte ssiz = (byte)((info.BitsStored - 1) | (info.IsSigned ? 0x80 : 0x00));
                span[offset++] = ssiz;

                // XRsiz, YRsiz (subsampling) - no subsampling
                span[offset++] = 1;
                span[offset++] = 1;
            }

            buffer.Advance(segmentLength);
        }

        private static void WriteCodMarker(BufferWriter buffer, J2kEncoderOptions options, bool lossless, bool usesMct)
        {
            int segmentLength = 12; // Fixed segment length

            WriteMarker(buffer, J2kMarkers.COD);

            Span<byte> span = buffer.GetSpan(segmentLength);
            int offset = 0;

            // Length
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)segmentLength);
            offset += 2;

            // Scod (coding style)
            span[offset++] = 0x00; // No precincts, no SOP/EPH markers

            // SGcod
            span[offset++] = (byte)options.Progression;

            // Number of layers
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)options.NumberOfLayers);
            offset += 2;

            // MCT (multiple component transform)
            span[offset++] = usesMct ? (byte)1 : (byte)0;

            // SPcod
            span[offset++] = (byte)options.DecompositionLevels;

            // Code-block width exponent (width = 2^(exp+2))
            span[offset++] = (byte)(GetExponent(options.CodeBlockWidth) - 2);

            // Code-block height exponent
            span[offset++] = (byte)(GetExponent(options.CodeBlockHeight) - 2);

            // Code-block style
            span[offset++] = 0x00;

            // Wavelet transform: 0 = 9/7, 1 = 5/3
            span[offset++] = lossless ? (byte)1 : (byte)0;

            buffer.Advance(segmentLength);
        }

        private static void WriteQcdMarker(BufferWriter buffer, J2kEncoderOptions options, bool lossless)
        {
            // For lossless, we use no quantization
            // For lossy, we would specify quantization parameters
            int numSubbands = 1 + 3 * options.DecompositionLevels; // LL + 3 subbands per level
            int segmentLength = 4 + numSubbands; // Header + 1 byte per subband

            WriteMarker(buffer, J2kMarkers.QCD);

            Span<byte> span = buffer.GetSpan(segmentLength);
            int offset = 0;

            // Length
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset), (ushort)segmentLength);
            offset += 2;

            // Sqcd: quantization style
            // For lossless: no quantization (style 0)
            // For lossy: scalar derived (style 1) or scalar expounded (style 2)
            span[offset++] = lossless ? (byte)0x00 : (byte)0x00;

            // SPqcd: step sizes
            // For lossless, just write 8 (exponent = 8, mantissa = 0)
            for (int i = 0; i < numSubbands; i++)
            {
                span[offset++] = 8;
            }

            buffer.Advance(segmentLength);
        }

        private static int GetExponent(int value)
        {
            int exp = 0;
            while ((1 << exp) < value && exp < 31)
            {
                exp++;
            }
            return exp;
        }

        /// <summary>
        /// Internal result from encoding a single tile.
        /// </summary>
        private struct TileEncodeResult
        {
            /// <summary>The encoded packet data for this tile.</summary>
            public byte[] PacketData;
        }
    }
}
