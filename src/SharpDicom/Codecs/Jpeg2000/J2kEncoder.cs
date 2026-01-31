using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
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

        /// <summary>Gets or sets the target compression ratio for lossy encoding (e.g., 10 = 10:1).</summary>
        /// <remarks>Only used for lossy encoding. Higher values = more compression, lower quality.</remarks>
        public int CompressionRatio { get; set; } = 10;

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
            if (pixelData.Length < info.FrameSize)
            {
                throw new ArgumentException("Pixel data is too small for the specified image dimensions.");
            }

            int width = info.Columns;
            int height = info.Rows;
            int components = info.SamplesPerPixel;

            // Convert pixel data to integer array for processing
            int[][] componentData = ExtractComponents(pixelData, info);

            // Apply forward color transform if multi-component
            if (components >= 3 && !info.IsPlanar)
            {
                ApplyColorTransform(componentData, width, height, lossless);
            }

            // Apply forward DWT to each component
            for (int c = 0; c < components; c++)
            {
                DwtTransform.Forward(componentData[c], width, height, options.DecompositionLevels, lossless);
            }

            // EBCOT tier-1 encoding
            using var ebcotEncoder = new EbcotEncoder();
            var packetEncoder = new PacketEncoder();

            // For simplicity, encode as single tile with single quality layer
            // Calculate code-block grid
            int cbWidth = options.CodeBlockWidth;
            int cbHeight = options.CodeBlockHeight;
            int cbsWide = (width + cbWidth - 1) / cbWidth;
            int cbsHigh = (height + cbHeight - 1) / cbHeight;

            // Encode each component's code-blocks
            var allCodeBlocks = new List<CodeBlockData[]>(components);

            for (int c = 0; c < components; c++)
            {
                var codeBlocks = EncodeComponentCodeBlocks(
                    componentData[c], width, height,
                    cbWidth, cbHeight, cbsWide, cbsHigh,
                    ebcotEncoder);
                allCodeBlocks.Add(codeBlocks);
            }

            // Tier-2: Create packets
            var allPackets = new List<PacketData[]>(components);
            for (int c = 0; c < components; c++)
            {
                var packets = packetEncoder.EncodePackets(
                    allCodeBlocks[c],
                    cbsWide, cbsHigh,
                    options.NumberOfLayers,
                    options.Progression,
                    options.DecompositionLevels + 1);
                allPackets.Add(packets);
            }

            // Build codestream
            return BuildCodestream(info, options, lossless, allPackets);
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
                if (isSigned)
                {
                    // Interpret bit pattern as signed int32
                    return unchecked((int)value);
                }
                else
                {
                    // Unsigned 32-bit values > int.MaxValue cannot be represented
                    if (value > int.MaxValue)
                    {
                        throw new NotSupportedException(
                            $"Unsigned 32-bit sample value {value} exceeds maximum supported value.");
                    }
                    return (int)value;
                }
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
        /// Encodes code-blocks for a single component.
        /// </summary>
        private static CodeBlockData[] EncodeComponentCodeBlocks(
            int[] data, int width, int height,
            int cbWidth, int cbHeight, int cbsWide, int cbsHigh,
            EbcotEncoder encoder)
        {
            int numCodeBlocks = cbsWide * cbsHigh;
            var codeBlocks = new CodeBlockData[numCodeBlocks];
            int[] cbBuffer = new int[cbWidth * cbHeight];

            for (int cbY = 0; cbY < cbsHigh; cbY++)
            {
                for (int cbX = 0; cbX < cbsWide; cbX++)
                {
                    int cbIdx = cbY * cbsWide + cbX;

                    // Extract code-block data
                    int startX = cbX * cbWidth;
                    int startY = cbY * cbHeight;
                    int actualWidth = Math.Min(cbWidth, width - startX);
                    int actualHeight = Math.Min(cbHeight, height - startY);

                    // Clear buffer and copy data
                    Array.Clear(cbBuffer, 0, cbBuffer.Length);
                    for (int y = 0; y < actualHeight; y++)
                    {
                        for (int x = 0; x < actualWidth; x++)
                        {
                            cbBuffer[y * cbWidth + x] = data[(startY + y) * width + (startX + x)];
                        }
                    }

                    // Encode code-block
                    codeBlocks[cbIdx] = encoder.EncodeCodeBlock(cbBuffer, cbWidth, cbHeight, subbandType: 0);
                }
            }

            return codeBlocks;
        }

        /// <summary>
        /// Builds the JPEG 2000 codestream.
        /// </summary>
        private static ReadOnlyMemory<byte> BuildCodestream(
            PixelDataInfo info,
            J2kEncoderOptions options,
            bool lossless,
            List<PacketData[]> componentPackets)
        {
            var buffer = new BufferWriter(4096);

            // Write SOC marker
            WriteMarker(buffer, J2kMarkers.SOC);

            // Write SIZ marker
            WriteSizMarker(buffer, info);

            // Write COD marker
            WriteCodMarker(buffer, options, lossless, info.SamplesPerPixel >= 3);

            // Write QCD marker
            WriteQcdMarker(buffer, options, lossless);

            // Write tile header and data
            WriteTileData(buffer, options, componentPackets);

            // Write EOC marker
            WriteMarker(buffer, J2kMarkers.EOC);

            return buffer.WrittenMemory.ToArray();
        }

        private static void WriteMarker(BufferWriter buffer, ushort marker)
        {
            Span<byte> span = buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(span, marker);
            buffer.Advance(2);
        }

        private static void WriteSizMarker(BufferWriter buffer, PixelDataInfo info)
        {
            int components = info.SamplesPerPixel;
            // SIZ segment: Lsiz(2) + Rsiz(2) + Xsiz(4) + Ysiz(4) + XOsiz(4) + YOsiz(4)
            //            + XTsiz(4) + YTsiz(4) + XTOsiz(4) + YTOsiz(4) + Csiz(2) = 38 bytes
            //            + Ssiz/XRsiz/YRsiz(3) per component
            int segmentLength = 38 + components * 3;

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

            // XTsiz, YTsiz (tile size - single tile)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)info.Columns);
            offset += 4;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset), (uint)info.Rows);
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
            span[offset] = 0x00;

            // Wavelet transform: 0 = 9/7, 1 = 5/3
            span[offset + 1] = lossless ? (byte)1 : (byte)0;

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

        private static void WriteTileData(BufferWriter buffer, J2kEncoderOptions options, List<PacketData[]> componentPackets)
        {
            // Collect all packet data
            List<byte> tileData = new List<byte>();

            // Simple single-layer case: write all component packets
            int numLayers = options.NumberOfLayers;
            int numComponents = componentPackets.Count;

            // LRCP order: layer, resolution, component, position
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

            // Calculate tile-part length
            int tileHeaderLength = 12; // SOT segment
            int totalTileLength = tileHeaderLength + 2 + tileData.Count; // +2 for SOD marker

            // Write SOT marker
            WriteMarker(buffer, J2kMarkers.SOT);

            Span<byte> sotSpan = buffer.GetSpan(10);
            int offset = 0;

            // Length
            BinaryPrimitives.WriteUInt16BigEndian(sotSpan.Slice(offset), 10);
            offset += 2;

            // Tile index
            BinaryPrimitives.WriteUInt16BigEndian(sotSpan.Slice(offset), 0);
            offset += 2;

            // Tile-part length (includes SOT and SOD)
            BinaryPrimitives.WriteUInt32BigEndian(sotSpan.Slice(offset), (uint)totalTileLength);
            offset += 4;

            // Tile-part index
            sotSpan[offset] = 0;

            // Number of tile-parts
            sotSpan[offset + 1] = 1;

            buffer.Advance(10);

            // Write SOD marker
            WriteMarker(buffer, J2kMarkers.SOD);

            // Write packet data
            if (tileData.Count > 0)
            {
                Span<byte> dataSpan = buffer.GetSpan(tileData.Count);
                for (int i = 0; i < tileData.Count; i++)
                {
                    dataSpan[i] = tileData[i];
                }
                buffer.Advance(tileData.Count);
            }
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
    }
}
